using AntiqueTradingSimulator.Auctions;
using AntiqueTradingSimulator.Company;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Events;
using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.News;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static AntiqueTradingSimulator.Events.EventEffect;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.Agents
{
    /// <summary>
    /// Runtime instance of an NPC trader — mirrors the Antique/AntiqueDefinition split.
    /// Holds only what's dynamic (cash, holdings, pending reactions) plus a string
    /// ProfileId, never a direct ScriptableObject reference, so instances stay
    /// serializable for save/load. Plain C# on purpose: NPCs have no scene presence.
    /// </summary>
    [Serializable]
    public class NPCTrader : IInformationReceiver
    {
        public string Id { get; private set; }
        public string ProfileId { get; }
        public string TraderName { get; }
        public TraderInventory Inventory { get; }

        private readonly EconomyManager _economyManager;
        private readonly ContractManager _contractManager;
        private readonly TransportManager _transportManager;

        // NPCs always ship with the standard service.
        private const TransportOption NpcTransportOption = TransportOption.Standard;
        private NpcBehaviorProfile _profileCache;
        public NpcBehaviorProfile Profile => _profileCache ??= NpcProfileDatabase.GetById(ProfileId);

        public InfoAccessLevel AccessLevel => Profile != null ? Profile.AccessLevel : InfoAccessLevel.LocalPress;

        private const int NewsActionWindowWeeks = 2;

        /// <summary>Exclusive contracts must leave at least this share of the reward as profit (they carry a penalty).</summary>
        private const float ExclusiveMinProfitShare = 0.15f;

        /// <summary>Share of cash an NPC may tie up in contract purchases at once.</summary>
        private const float ContractCashShare = 0.5f;

        /// <summary>A discretionary purchase may not take cash below the contract reserve times this variable.</summary>
        private const float ReserveSafetyMargin = 1.25f;

        private const float CommittedBargainPenalty = 2f;

        /// <summary>Expected seasonal rise (fall) beyond which a far-sighted NPC holds (sells) an antique.</summary>
        private const float SeasonalHoldThreshold = 0.03f;

        public float ActivityScale { get; set; } = 1f;

        private struct PendingReaction
        {
            public NewsItem News;
            public int ReactionWeek;
        }

        private struct Acquisition
        {
            public float PurchasePrice;
            public int Week;
        }

        private readonly struct Offer
        {
            public readonly Antique Listing;
            public readonly TransportOption Option;
            public readonly float Cost;
            public readonly int ArrivalWeek;
            public readonly int DurationWeeks;

            public Offer(Antique listing, TransportOption option, float cost, int arrivalWeek, int durationWeeks)
            {
                Listing = listing;
                Option = option;
                Cost = cost;
                ArrivalWeek = arrivalWeek;
                DurationWeeks = durationWeeks;
            }
        }

        private readonly List<PendingReaction> _pendingReactions = new();
        private readonly Dictionary<string, Acquisition> _acquisitions = new();

        private int _planWeek = -1;
        private readonly List<int> _slotTicks = new();
        private float _weekBudget;
        private float _contractReserve;
        private int _contractSlotsUsed;
        private int _lastObligationTick = -1;
        private System.Random _rng;

        private readonly List<Contract> _contractScratch = new();
        private readonly List<Offer> _offerScratch = new();

        /// <summary>Contracts this NPC has taken — kept by its inventory (saved with the economy).</summary>
        public IReadOnlyCollection<string> CommittedContractIds => Inventory.CommittedContractIds;

        /// <summary>
        /// Reputation, credibility, contract counts and daily history — kept by the same rules as
        /// the player's company. Reputation decides which contracts this NPC may take.
        /// </summary>
        public TraderRecord Record { get; }

        /// <param name="reputationSettings">The reputation rules; the player's are passed in so both play by the same ones.</param>
        public NPCTrader(string traderName, string profileId, float startingCash, EconomyManager economyManager,
            ContractManager contractManager = null, TransportManager transportManager = null,
            ReputationSettings reputationSettings = null)
        {
            Id = Guid.NewGuid().ToString("N");
            TraderName = traderName;
            ProfileId = profileId;
            Inventory = new TraderInventory(startingCash);
            _economyManager = economyManager;
            _contractManager = contractManager;
            _transportManager = transportManager;

            Record = new TraderRecord(reputationSettings != null
                ? reputationSettings
                : ScriptableObject.CreateInstance<ReputationSettings>());

            // The inventory lives exactly as long as this NPC, so the subscription never needs removing.
            Inventory.Ledger.OnEntryAdded += entry =>
            {
                if (entry.Category == LedgerCategory.Sale) Record.RecordSale(entry.Amount);
            };
        }

        public void Detach()
        {
            if (_transportManager != null)
                _transportManager.OnShipmentDelivered -= HandleShipmentDelivered;
        }

        private int CurrentWeek => _economyManager != null && _economyManager.TimeManager != null
            ? _economyManager.TimeManager.CurrentWeek
            : 0;

        private int TicksPerPeriod => _economyManager != null && _economyManager.TimeManager != null
            ? _economyManager.TimeManager.TicksPerPeriod
            : Core.TimeManager.DefaultTicksPerPeriod;

        // ---------------------------------------------------------------- actions on tick

        public void RunScheduledActions(int week, int tick)
        {
            var profile = Profile;
            if (profile == null || _economyManager.Market == null) return;

            if (_planWeek != week) PlanPeriod(week, tick, profile);

            if (_lastObligationTick != tick)
            {
                _lastObligationTick = tick;
                RunObligations(week, profile);
            }

            while (_slotTicks.Count > 0 && _slotTicks[0] <= tick)
            {
                _slotTicks.RemoveAt(0);
                RunOneBudgetedAction(week, profile);
            }
        }

        public void FlushRemainingSlots()
        {
            var profile = Profile;
            if (profile == null || _economyManager.Market == null || _planWeek < 0) return;

            while (_slotTicks.Count > 0)
            {
                _slotTicks.RemoveAt(0);
                RunOneBudgetedAction(_planWeek, profile);
            }
        }

        public void EvaluateWeek(int currentWeek)
        {
            var profile = Profile;
            if (profile == null || _economyManager.Market == null) return;

            PlanPeriod(currentWeek, 0, profile);
            RunObligations(currentWeek, profile);
            FlushRemainingSlots();
        }

        // ---------------------------------------------------------------- planning

        private void PlanPeriod(int week, int fromTick, NpcBehaviorProfile profile)
        {
            _planWeek = week;
            _contractSlotsUsed = 0;
            _lastObligationTick = -1;
            _rng = new System.Random(SeedFor(week));

            DropInactiveContracts();
            DropExpiredReactions(week);

            _contractReserve = CalculateContractReserve();
            _weekBudget = Mathf.Max(0f, Inventory.Cash - _contractReserve) * profile.WeeklyBudgetFraction;

            RollSlotTicks(fromTick, profile);
        }

        private void RollSlotTicks(int fromTick, NpcBehaviorProfile profile)
        {
            _slotTicks.Clear();

            int ticks = Mathf.Max(1, TicksPerPeriod);
            fromTick = Mathf.Clamp(fromTick, 0, ticks - 1);
            int available = ticks - fromTick;

            int full = Mathf.Clamp(Mathf.RoundToInt(profile.ActionsPerPeriod * Mathf.Max(0f, ActivityScale)), 1, ticks);

            int slots = fromTick == 0
                ? full
                : Mathf.Clamp(Mathf.RoundToInt(full * available / (float)ticks), 1, available);

            var taken = new bool[ticks];
            int placed = 0;
            int guard = 0;

            while (placed < slots && guard++ < ticks * 4)
            {
                int t = _rng.Next(fromTick, ticks);
                if (taken[t]) continue;
                taken[t] = true;
                placed++;
            }

            for (int t = fromTick; t < ticks && placed < slots; t++)
            {
                if (taken[t]) continue;
                taken[t] = true;
                placed++;
            }

            for (int t = 0; t < ticks; t++)
                if (taken[t]) _slotTicks.Add(t);
        }

        private int SeedFor(int week)
        {
            unchecked
            {
                int hash = (int)2166136261;
                string id = Id ?? TraderName ?? "npc";
                for (int i = 0; i < id.Length; i++)
                    hash = (hash ^ id[i]) * 16777619;
                return hash ^ (week * 397);
            }
        }

        private float CalculateContractReserve()
        {
            if (_contractManager == null) return 0f;

            var market = _economyManager.Market;
            float reserve = 0f;

            foreach (var contractId in Inventory.CommittedContractIds)
            {
                var contract = _contractManager.GetById(contractId);
                if (contract == null || contract.Status != ContractStatus.Active) continue;

                int missing = MissingFor(contract);
                if (missing <= 0) continue;

                reserve += contract.Requirement.AverageReferenceUnitPrice(market) * missing;
            }

            return reserve;
        }

        // ---------------------------------------------------------------- non-tick actions

        private void RunObligations(int week, NpcBehaviorProfile profile)
        {
            DropInactiveContracts();
            ReserveMatchingHoldings();
            DropUnfullfillableOpenContracts(week, profile);
            FulfillReadyContracts(week);
            RunStopLoss(profile);
            WithdrawStaleListings(week, profile);
        }

        private void DropInactiveContracts()
        {
            if (_contractManager == null) return;

            string dead = null;
            foreach (var contractId in Inventory.CommittedContractIds)
            {
                var contract = _contractManager.GetById(contractId);
                if (contract != null && contract.Status == ContractStatus.Active) continue;
                dead = contractId;
                break;
            }

            if (dead == null) return;

            Inventory.ReleaseAllReservationsForContract(dead);
            Inventory.ReleaseCommittedContract(dead);
        }

        private void DropExpiredReactions(int week)
        {
            for (int i = _pendingReactions.Count - 1; i >= 0; i--)
                if (week > _pendingReactions[i].ReactionWeek + NewsActionWindowWeeks)
                    _pendingReactions.RemoveAt(i);
        }

        private void ReserveMatchingHoldings()
        {
            if (_contractManager == null) return;

            foreach (var contractId in Inventory.CommittedContractIds)
            {
                var contract = _contractManager.GetById(contractId);
                if (contract == null || contract.Status != ContractStatus.Active) continue;

                int missing = MissingFor(contract);
                if (missing <= 0) continue;

                string pick = null;
                foreach (var pair in Inventory.Holdings)
                {
                    var holding = pair.Value;
                    if (holding.IsReservedForContract || holding.IsListedForSale || holding.IsInRestoration) continue;
                    if (!contract.Requirement.IsSatisfiedBy(holding)) continue;
                    pick = holding.Id;
                    break;
                }

                if (pick != null) Inventory.ReserveForContract(pick, contractId);
            }
        }

        private void FulfillReadyContracts(int week)
        {
            if (_contractManager == null || Inventory.CommittedContractIds.Count == 0) return;

            Contract ready = null;
            foreach (var contractId in Inventory.CommittedContractIds)
            {
                var contract = _contractManager.GetById(contractId);
                if (contract == null || contract.Status != ContractStatus.Active) continue;
                if (ReadyCountFor(contract) < contract.Requirement.Quantity) continue;

                ready = contract;
                break;
            }

            if (ready != null) TryFulfillContract(ready, week);
        }

        private void TryFulfillContract(Contract contract, int week)
        {
            var listingIds = new List<string>(contract.Requirement.Quantity);

            foreach (var pair in Inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding.ReservedForContractId != contract.ContractId) continue;
                if (!holding.IsAvailable) continue;
                if (!contract.Requirement.IsSatisfiedBy(holding)) continue;

                listingIds.Add(holding.Id);
                if (listingIds.Count >= contract.Requirement.Quantity) break;
            }

            if (listingIds.Count < contract.Requirement.Quantity) return;

            if (_contractManager.FulfillContract(contract.ContractId, Id, Inventory, listingIds))
            {
                foreach (var listingId in listingIds)
                    _acquisitions.Remove(listingId);

                Inventory.ReleaseCommittedContract(contract.ContractId);
                Record.ContractFulfilled(contract, week);
            }
        }

        private void DropUnfullfillableOpenContracts(int week, NpcBehaviorProfile profile)
        {
            if (_contractManager == null || Inventory.CommittedContractIds.Count == 0) return;

            Contract hopeless = null;
            foreach (var contractId in Inventory.CommittedContractIds)
            {
                var contract = _contractManager.GetById(contractId);
                if (contract == null || contract.Status != ContractStatus.Active) continue;
                if (contract.Type != ContractType.Open) continue;

                int missing = MissingFor(contract);
                if (missing <= 0) continue;
                if (UrgencyOf(contract, week, profile) != ContractUrgency.Critical) continue;
                if (IsStillReachable(contract, missing, week, profile)) continue;

                hopeless = contract;
                break;
            }

            if (hopeless == null) return;

            Debug.Log($"{TraderName} dropped open contract {hopeless.ContractId}. They cannot aquire missing antiques in remaining time.");
            Inventory.ReleaseAllReservationsForContract(hopeless.ContractId);
            Inventory.ReleaseCommittedContract(hopeless.ContractId);
            Record.ContractFailed(hopeless, week);
        }

        private void RunStopLoss(NpcBehaviorProfile profile)
        {
            if (profile.NeverSells || profile.StopLossMultiplier <= 0f) return;

            string victim = null;
            float paid = 0f;

            foreach (var pair in Inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding.IsReservedForContract || holding.IsListedForSale) continue;
                if (holding.IsInTransit || holding.IsInRestoration) continue;
                if (!_acquisitions.TryGetValue(holding.Id, out var acquisition)) continue;
                if (holding.CurrentPrice >= acquisition.PurchasePrice * profile.StopLossMultiplier) continue;

                victim = holding.Id;
                paid = acquisition.PurchasePrice;
                break;
            }

            if (victim == null) return;

            var sold = Inventory.GetHolding(victim);
            Debug.Log($"{TraderName} panic sells {sold?.Name}: worth {sold?.CurrentPrice:F0} £, paid {paid:F0} £.");
            if (SellListing(victim)) _acquisitions.Remove(victim);
        }

        /// <summary>Withdraws one of its own listings that has sat unsold past RelistAfterWeeks.</summary>
        private void WithdrawStaleListings(int week, NpcBehaviorProfile profile)
        {
            if (!profile.ListsOnMarket) return;

            string stale = null;
            foreach (var pair in Inventory.Holdings)
            {
                var holding = pair.Value;
                if (!holding.IsListedForSale) continue;
                if (holding.MarketListedOnWeek < 0) continue;
                if (week - holding.MarketListedOnWeek < Mathf.Max(1, profile.RelistAfterWeeks)) continue;

                stale = holding.Id;
                break;
            }

            if (stale != null) Inventory.CancelListing(_economyManager.Market, stale);
        }

        // ---------------------------------------------------------------- tick action priority ladder

        private void RunOneBudgetedAction(int week, NpcBehaviorProfile profile)
        {
            if (TierContractRescue(week, profile)) return;
            if (TierDamageControl(week, profile)) return;
            if (TierNewsBuy(week, profile)) return;
            if (TierContractWork(week, profile)) return;
            if (TierReviewHoldings(week, profile)) return;
            if (TierTakeContract(week, profile)) return;
            if (TierBrowseMarket(week, profile)) return;
        }

        private int ContractSlotsAllowed(NpcBehaviorProfile profile)
        {
            int full = Mathf.Clamp(Mathf.RoundToInt(profile.ActionsPerPeriod * Mathf.Max(0f, ActivityScale)), 1, Mathf.Max(1, TicksPerPeriod));
            return Mathf.Max(1, Mathf.CeilToInt(full * profile.ContractSlotShare));
        }

        // ---------------------------------------------------------------- contract tick actions

        private bool TierContractRescue(int week, NpcBehaviorProfile profile) =>
            WorkOnContracts(week, profile, urgentOnly: true);

        private bool TierContractWork(int week, NpcBehaviorProfile profile)
        {
            if (_contractSlotsUsed >= ContractSlotsAllowed(profile)) return false;
            return WorkOnContracts(week, profile, urgentOnly: false);
        }

        private bool WorkOnContracts(int week, NpcBehaviorProfile profile, bool urgentOnly)
        {
            if (_contractManager == null) return false;

            CollectActiveContracts(week, profile);
            if (_contractScratch.Count == 0) return false;

            foreach (var contract in _contractScratch)
            {
                int missing = MissingFor(contract);
                if (missing <= 0) continue;

                var urgency = UrgencyOf(contract, week, profile);
                if (urgentOnly && urgency == ContractUrgency.Normal) continue;
                if (!urgentOnly && urgency != ContractUrgency.Normal) continue;

                var offer = BestOfferFor(contract, week, profile, urgency);
                if (offer.Listing == null) continue;

                float cost = offer.Cost;
                if (cost > Inventory.Cash) continue;

                if (!BuyListing(offer.Listing.Id, offer.Option)) continue;

                _acquisitions[offer.Listing.Id] = new Acquisition { PurchasePrice = offer.Listing.PurchasePrice, Week = week };
                Inventory.ReserveForContract(offer.Listing.Id, contract.ContractId);

                _contractReserve = CalculateContractReserve();
                if (!urgentOnly) _contractSlotsUsed++;

                Debug.Log($"{TraderName} bought {offer.Listing.Name} for contract {contract.ContractId} ({urgency}, {offer.Option} transport, arrives week {offer.ArrivalWeek}).");
                return true;
            }

            return false;
        }

        private void CollectActiveContracts(int week, NpcBehaviorProfile profile)
        {
            _contractScratch.Clear();
            if (_contractManager == null) return;

            foreach (var contractId in Inventory.CommittedContractIds)
            {
                var contract = _contractManager.GetById(contractId);
                if (contract == null || contract.Status != ContractStatus.Active) continue;
                _contractScratch.Add(contract);
            }

            // Tightest deadline first.
            _contractScratch.Sort((a, b) => a.DeadlineWeek.CompareTo(b.DeadlineWeek));
        }

        private bool TierTakeContract(int week, NpcBehaviorProfile profile)
        {
            if (!profile.TakesContracts || _contractManager == null) return false;
            return ConsiderNewContract(week, profile);
        }

        // ---------------------------------------------------------------- news tick actions

        private bool TierDamageControl(int week, NpcBehaviorProfile profile)
        {
            if (profile.NeverSells) return false;

            for (int i = _pendingReactions.Count - 1; i >= 0; i--)
            {
                var pending = _pendingReactions[i];
                if (pending.ReactionWeek > week) continue;
                if (SellOnNews(pending.News, week, profile)) return true;
            }

            return false;
        }

        private bool TierNewsBuy(int week, NpcBehaviorProfile profile)
        {
            for (int i = _pendingReactions.Count - 1; i >= 0; i--)
            {
                var pending = _pendingReactions[i];
                if (pending.ReactionWeek > week) continue;
                if (BuyOnNews(pending.News, week, profile)) return true;
            }

            return false;
        }

        private bool SellOnNews(NewsItem news, int week, NpcBehaviorProfile profile)
        {
            foreach (var effect in news.NewsData)
            {
                if (effect.targetScope == TargetScope.Other || effect.affectsPriceUp) continue;

                string victim = null;
                string victimName = null;
                bool wasListed = false;

                foreach (var pair in Inventory.Holdings)
                {
                    var holding = pair.Value;
                    if (holding.IsReservedForContract || holding.IsInTransit || holding.IsInRestoration) continue;
                    if (!MatchesScope(holding, effect)) continue;

                    victim = holding.Id;
                    victimName = holding.Name;
                    wasListed = holding.IsListedForSale;
                    break;
                }

                if (victim == null) continue;

                if (wasListed) Inventory.CancelListing(_economyManager.Market, victim);

                if (!SellListing(victim)) continue;

                _acquisitions.Remove(victim);
                Debug.Log($"{TraderName} sold {victimName} ahead of {news.Type} news from week {news.WeekPublished}.");
                return true;
            }

            return false;
        }

        private bool BuyOnNews(NewsItem news, int week, NpcBehaviorProfile profile)
        {
            foreach (var effect in news.NewsData)
            {
                if (effect.targetScope == TargetScope.Other || !effect.affectsPriceUp) continue;

                var listings = ListingsInScope(effect);
                if (listings == null) continue;

                for (int i = 0; i < listings.Count; i++)
                {
                    var listing = listings[i];
                    if (!CanConsider(listing, profile)) continue;

                    float cost = EstimateTotalCost(listing, NpcTransportOption);
                    if (cost > _weekBudget || !IsAcceptablePrice(listing, profile)) continue;
                    if (!PassesCashFloor(cost)) continue;

                    if (!TryBuyForStock(listing, week, cost)) continue;

                    Debug.Log($"{TraderName} bought {listing.Name} on {news.Type} news from week {news.WeekPublished}.");
                    return true;
                }
            }

            return false;
        }

        private List<Antique> ListingsInScope(NewsEventData effect)
        {
            var market = _economyManager.Market;
            if (market == null) return null;

            return effect.targetScope switch
            {
                TargetScope.AntiqueType => market.GetByType(effect.AntiqueType),
                TargetScope.Country => market.GetByCountry(effect.Country),
                TargetScope.Century => market.GetByCentury(effect.Century),
                _ => null
            };
        }

        private static bool MatchesScope(Antique antique, NewsEventData effect) => effect.targetScope switch
        {
            TargetScope.AntiqueType => antique.Definition != null && antique.Definition.Type == effect.AntiqueType,
            TargetScope.Country => antique.Definition != null && antique.Definition.Country == effect.Country,
            TargetScope.Century => antique.Definition != null && antique.Definition.Century == effect.Century,
            _ => false
        };

        // ---------------------------------------------------------------- sell holdings tick actions

        private bool TierReviewHoldings(int week, NpcBehaviorProfile profile)
        {
            if (profile.NeverSells) return false;

            string chosen = null;
            string chosenName = null;
            float askingPrice = 0f;
            float estimatedValue = 0f;

            foreach (var pair in Inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding.IsReservedForContract || holding.IsListedForSale) continue;
                if (holding.IsInTransit || holding.IsInRestoration) continue;
                if (!_acquisitions.TryGetValue(holding.Id, out var acquisition)) continue;

                int held = week - acquisition.Week;
                if (held < profile.MinHoldingWeeksBeforeSell) continue;

                float value = holding.CurrentPrice;
                bool patient = profile.PatientSellAfterWeeks > 0 && held >= profile.PatientSellAfterWeeks;
                float target = patient
                    ? Mathf.Min(profile.PatientProfitMultiplier, profile.ProfitTargetMultiplier)
                    : profile.ProfitTargetMultiplier;

                // A far-sighted trader waits for a coming season and cashes in before one ends.
                float seasonal = SeasonalOutlook(holding.DefinitionId, profile);
                if (seasonal >= SeasonalHoldThreshold && !patient) continue;
                if (seasonal <= -SeasonalHoldThreshold)
                    target = Mathf.Min(target, profile.PatientProfitMultiplier);

                if (value < acquisition.PurchasePrice * target) continue;

                chosen = holding.Id;
                chosenName = holding.Name;
                estimatedValue = value;
                askingPrice = Mathf.Max(value * profile.ListingMarkup, acquisition.PurchasePrice * target);
                break;
            }

            if (chosen == null) return false;

            if (profile.ListsOnMarket)
            {
                if (!Inventory.ListForSale(_economyManager.Market, chosen, askingPrice, week)) return false;

                Debug.Log($"{TraderName} listed {chosenName} for {askingPrice:F0} £ (value {estimatedValue:F0} £).");
                return true;
            }

            if (!SellListing(chosen)) return false;

            _acquisitions.Remove(chosen);
            return true;
        }

        // ---------------------------------------------------------------- buy from market tick actions

        private bool TierBrowseMarket(int week, NpcBehaviorProfile profile)
        {
            if (_weekBudget <= 0f) return false;

            var market = _economyManager.Market;
            if (market == null) return false;

            var listings = market.Listings;
            float threshold = BargainThreshold(profile);

            Antique best = null;
            float bestScore = threshold;
            float bestCost = 0f;

            for (int i = 0; i < listings.Count; i++)
            {
                var listing = listings[i];
                if (!CanConsider(listing, profile)) continue;
                if (!IsInterestedIn(listing.Definition, profile)) continue;

                float cost = EstimateTotalCost(listing, NpcTransportOption);
                if (cost <= 0f || cost > _weekBudget) continue;
                if (!IsAcceptablePrice(listing, profile)) continue;
                if (!PassesCashFloor(cost)) continue;

                float score = BargainScore(listing, cost, profile);
                if (score < bestScore) continue;

                best = listing;
                bestScore = score;
                bestCost = cost;
            }

            if (best == null) return false;

            if (!TryBuyForStock(best, week, bestCost)) return false;

            Debug.Log($"{TraderName} bought {best.Name} on the open market (score {bestScore:P0}, cost {bestCost:F0} £).");
            return true;
        }

        // ---------------------------------------------------------------- valuation

        private float BargainScore(Antique listing, float totalCost, NpcBehaviorProfile profile)
        {
            if (totalCost <= 0f) return 0f;

            float fair = FairValue(listing, profile);
            int holdPeriods = profile.PatientSellAfterWeeks > 0
                ? profile.PatientSellAfterWeeks
                : Mathf.Max(1, profile.MinHoldingWeeksBeforeSell);

            float carry = holdPeriods * MarginalUpkeepPerItem();
            return (fair - totalCost - carry) / totalCost;
        }

        private float BargainThreshold(NpcBehaviorProfile profile)
        {
            float bar = profile.MinBargainScore;
            return HasActiveCommitments() ? bar * CommittedBargainPenalty : bar;
        }

        /// <summary>
        /// What one more item adds to this period's warehouse upkeep. Zero for NPCs until they are
        /// given warehouses; written against the real curve so it starts working when they are.
        /// </summary>
        private float MarginalUpkeepPerItem()
        {
            var warehouse = Inventory.Warehouse;
            if (warehouse == null) return 0f;

            int used = Inventory.UsedSlots;
            return Mathf.Max(0f, warehouse.CalculateWeeklyUpkeep(used + 1) - warehouse.CalculateWeeklyUpkeep(used));
        }

        /// <summary>
        /// What the antique is worth in its condition, with the profile's condition premium — and, for a
        /// far-sighted trader, the part of the coming seasonal price change it believes in.
        /// </summary>
        private float FairValue(Antique listing, NpcBehaviorProfile profile)
        {
            var typeState = _economyManager.Market?.GetTypeState(listing.DefinitionId);
            float baseValue = PriceEngine.CalculateReferencePrice(listing.BasePrice, typeState) * listing.Condition;
            return baseValue * (1f + profile.ConditionPremium * Mathf.Max(0f, listing.Condition - 0.5f))
                             * (1f + SeasonalOutlook(listing.DefinitionId, profile));
        }

        /// <summary>
        /// The seasonal price change this trader expects for a type over its horizon, weighted by its
        /// foresight: 0.06 = expects (and prices in) +6%. 0 for traders who ignore the calendar.
        /// </summary>
        private float SeasonalOutlook(string definitionId, NpcBehaviorProfile profile)
        {
            if (profile.SeasonalForesight <= 0f || profile.SeasonalHorizonWeeks <= 0 || _economyManager == null) return 0f;

            float ratio = _economyManager.SeasonalPriceOutlook(definitionId, profile.SeasonalHorizonWeeks);
            return profile.SeasonalForesight * (ratio - 1f);
        }
        private bool IsAcceptablePrice(Antique listing, NpcBehaviorProfile profile) =>
            listing.SalePrice <= FairValue(listing, profile) * profile.MaxPriceMultiplierWillingToPay;

        /// <summary>A regular purchase may never eat into the cash a taken contract needs.</summary>
        private bool PassesCashFloor(float cost) =>
            Inventory.Cash - cost >= _contractReserve * ReserveSafetyMargin;

        private bool HasActiveCommitments()
        {
            if (_contractManager == null) return false;

            foreach (var contractId in Inventory.CommittedContractIds)
            {
                var contract = _contractManager.GetById(contractId);
                if (contract != null && contract.Status == ContractStatus.Active && MissingFor(contract) > 0)
                    return true;
            }

            return false;
        }

        private bool CanConsider(Antique listing, NpcBehaviorProfile profile)
        {
            if (listing == null) return false;
            if (Inventory.Owns(listing.Id)) return false;              // its own listing
            if (listing.Condition < profile.MinConditionToBuy) return false;
            return true;
        }

        private bool IsInterestedIn(AntiqueDefinition def, NpcBehaviorProfile profile)
        {
            if (def == null) return false;
            bool typeOk = profile.PreferredTypes.Count == 0 || profile.PreferredTypes.Contains(def.Type);
            bool countryOk = profile.PreferredCountries.Count == 0 || profile.PreferredCountries.Contains(def.Country);
            bool periodOk = profile.PreferredCenturies.Count == 0 || profile.PreferredCenturies.Contains(def.Century);
            return typeOk && countryOk && periodOk;
        }

        // ---------------------------------------------------------------- contracts

        /// <summary>
        /// Latest day an item may arrive and still be handed in.
        /// </summary>
        private static int LatestUsefulArrivalWeek(Contract contract) => contract.DeadlineWeek - 1;

        /// <summary>Items still needed: quantity minus what is reserved and still qualifies.</summary>
        private int MissingFor(Contract contract) =>
            Mathf.Max(0, contract.Requirement.Quantity - ReservedCountFor(contract));

        /// <summary>Reserved for this contract and still matching In transit included.</summary>
        private int ReservedCountFor(Contract contract)
        {
            int count = 0;
            foreach (var pair in Inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding.ReservedForContractId != contract.ContractId) continue;
                if (!contract.Requirement.IsSatisfiedBy(holding)) continue;
                count++;
            }
            return count;
        }

        /// <summary>Reserved, arrived and still matching. Only items that can be currently handed in.</summary>
        private int ReadyCountFor(Contract contract)
        {
            int count = 0;
            foreach (var pair in Inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding.ReservedForContractId != contract.ContractId) continue;
                if (!holding.IsAvailable) continue;
                if (!contract.Requirement.IsSatisfiedBy(holding)) continue;
                count++;
            }
            return count;
        }

        /// <summary>
        /// Slack is the deadline minus today minus the worst transit still ahead of this contract —
        /// both the shipments already flying and the trips the missing items have yet to make.
        /// </summary>
        private ContractUrgency UrgencyOf(Contract contract, int week, NpcBehaviorProfile profile)
        {
            if (MissingFor(contract) <= 0) return ContractUrgency.Normal;

            int slack = LatestUsefulArrivalWeek(contract) - week - WorstTransitAhead(contract);

            if (slack <= 0) return ContractUrgency.Critical;
            if (slack <= profile.ContractPanicWeeks) return ContractUrgency.Pressing;
            return ContractUrgency.Normal;
        }

        private int WorstTransitAhead(Contract contract)
        {
            int worst = 1;

            foreach (var pair in Inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding.ReservedForContractId != contract.ContractId) continue;
                if (!holding.IsInTransit) continue;
                worst = Mathf.Max(worst, holding.ArrivalWeek - CurrentWeek);
            }

            if (MissingFor(contract) > 0 && _transportManager != null && _transportManager.Settings != null)
                worst = Mathf.Max(worst, _transportManager.Settings.GetBaseDurationWeeks(ShippingZone.Domestic, NpcTransportOption));

            return Mathf.Max(1, worst);
        }

        /// <summary>
        /// Cheapest matching listing that can still land in time, re-quoted now. The transport option
        /// escalates with urgency and with how little condition margin the item has to lose.
        /// </summary>
        private Offer BestOfferFor(Contract contract, int week, NpcBehaviorProfile profile, ContractUrgency urgency)
        {
            var market = _economyManager.Market;
            if (market == null) return default;

            int latestArrival = LatestUsefulArrivalWeek(contract);
            var listings = market.Listings;

            Offer best = default;
            float bestCost = float.MaxValue;

            for (int i = 0; i < listings.Count; i++)
            {
                var listing = listings[i];
                if (Inventory.Owns(listing.Id)) continue;
                if (!contract.Requirement.IsSatisfiedBy(listing)) continue;
                if (!IsAcceptablePrice(listing, profile)) continue;

                var option = TransportOptionFor(listing, contract, urgency);
                var quote = QuoteFor(listing, option);
                int arrival = quote != null ? quote.ArrivalWeek : week;
                int duration = quote != null ? quote.DurationWeeks : 0;
                if (arrival > latestArrival) continue;

                float cost = listing.SalePrice + (quote != null ? quote.Cost : 0f);
                if (cost > contract.RewardPerUnit) continue;
                if (cost >= bestCost) continue;

                best = new Offer(listing, option, cost, arrival, duration);
                bestCost = cost;
            }

            return best;
        }

        private TransportOption TransportOptionFor(Antique listing, Contract contract, ContractUrgency urgency)
        {
            if (contract == null) return NpcTransportOption;
            if (urgency != ContractUrgency.Normal) return TransportOption.Express;

            var requirement = contract.Requirement;
            if (!requirement.HasMinCondition) return NpcTransportOption;

            float maxLoss = _transportManager != null && _transportManager.Settings != null
                ? _transportManager.Settings.MaxConditionLoss
                : 0f;

            float margin = listing.Condition - requirement.MinCondition;
            return margin < maxLoss ? TransportOption.Express : NpcTransportOption;
        }

        private TransportQuote QuoteFor(Antique listing, TransportOption option) =>
            _transportManager != null ? _transportManager.Quote(listing, option, buyer: Inventory) : null;


        private bool IsStillReachable(Contract contract, int missing, int week, NpcBehaviorProfile profile)
        {
            int perPeriod = Mathf.Max(1, ContractSlotsAllowed(profile));
            int latestArrival = LatestUsefulArrivalWeek(contract);

            BuildOfferPlan(contract, week, profile, missing);
            if (_offerScratch.Count < missing) return false;

            for (int k = 0; k < missing; k++)
            {
                int buyWeek = week + k / perPeriod;
                if (buyWeek + _offerScratch[k].DurationWeeks > latestArrival) return false;
            }

            return true;
        }

        private void BuildOfferPlan(Contract contract, int week, NpcBehaviorProfile profile, int missing)
        {
            _offerScratch.Clear();

            var market = _economyManager.Market;
            if (market == null || missing <= 0) return;

            int latestArrival = LatestUsefulArrivalWeek(contract);
            var listings = market.Listings;
            var urgency = ContractUrgency.Normal;

            for (int i = 0; i < listings.Count; i++)
            {
                var listing = listings[i];
                if (Inventory.Owns(listing.Id)) continue;
                if (!contract.Requirement.IsSatisfiedBy(listing)) continue;
                if (!IsAcceptablePrice(listing, profile)) continue;

                var option = TransportOptionFor(listing, contract, urgency);
                var quote = QuoteFor(listing, option);
                int arrival = quote != null ? quote.ArrivalWeek : week;
                if (arrival > latestArrival) continue;

                float cost = listing.SalePrice + (quote != null ? quote.Cost : 0f);
                if (cost > contract.RewardPerUnit) continue;

                _offerScratch.Add(new Offer(listing, option, cost, arrival, quote != null ? quote.DurationWeeks : 0));
            }

            _offerScratch.Sort((a, b) => a.Cost.CompareTo(b.Cost));
            if (_offerScratch.Count > missing) _offerScratch.RemoveRange(missing, _offerScratch.Count - missing);

            _offerScratch.Sort((a, b) => b.DurationWeeks.CompareTo(a.DurationWeeks));
        }

        /// <summary>
        /// Takes the most profitable contract the NPC can actually finish.
        /// </summary>
        private bool ConsiderNewContract(int week, NpcBehaviorProfile profile)
        {
            int reputation = Record.Reputation.Reputation;

            var candidates = _contractManager.OpenContracts
                .Where(c => c != null && !Inventory.IsCommittedToContract(c.ContractId))
                .Concat(_contractManager.ExclusiveContracts.Where(c => c != null && c.CanBeClaimed))
                .Where(c => c.RequiredReputation <= reputation && c.Status == ContractStatus.Active)
                .ToList();

            if (candidates.Count == 0) return false;

            float cashLimit = Inventory.Cash * ContractCashShare - _contractReserve;
            if (cashLimit <= 0f) return false;

            Contract chosen = null;
            float bestProfit = 0f;

            foreach (var contract in candidates)
            {
                int missing = contract.Requirement.Quantity - ReservedCountFor(contract);
                if (missing <= 0) continue;

                BuildOfferPlan(contract, week, profile, missing);
                if (_offerScratch.Count < missing) continue;

                float purchaseCost = 0f;
                for (int k = 0; k < _offerScratch.Count; k++) purchaseCost += _offerScratch[k].Cost;
                if (purchaseCost > cashLimit) continue;

                if (!IsStillReachable(contract, missing, week, profile)) continue;

                float minProfit = contract.Type == ContractType.Exclusive
                    ? contract.TotalReward * ExclusiveMinProfitShare
                    : 0f;

                float profit = contract.TotalReward - purchaseCost;
                if (profit <= minProfit) continue;

                if (chosen == null || profit > bestProfit)
                {
                    chosen = contract;
                    bestProfit = profit;
                }
            }

            if (chosen == null) return false;

            if (chosen.Type == ContractType.Exclusive && !_contractManager.ClaimContract(chosen.ContractId, Id))
                return false;

            Inventory.CommitToContract(chosen.ContractId);
            Record.ContractAccepted();

            // Exclusive contracts are recorded by ClaimContract above; open ones are never claimed.
            if (chosen.Type != ContractType.Exclusive)
                _contractManager.RecordTakenOn(chosen, Id);

            _contractReserve = CalculateContractReserve();
            _weekBudget = Mathf.Min(_weekBudget, Mathf.Max(0f, Inventory.Cash - _contractReserve));

            Debug.Log($"{TraderName} picked up {chosen.Type} contract {chosen.ContractId} — needs {chosen.Requirement.Quantity}, due week {chosen.DeadlineWeek}, expected profit {bestProfit:F0}.");
            return true;
        }

        // ---------------------------------------------------------------- deliveries 

        private void HandleShipmentDelivered(Shipment shipment, Antique antique, float conditionLost)
        {
            if (shipment == null || antique == null) return;
            if (!ReferenceEquals(shipment.Recipient, Inventory)) return;
            if (conditionLost <= 0f) return;

            string contractId = antique.ReservedForContractId;
            if (string.IsNullOrEmpty(contractId) || _contractManager == null) return;

            var contract = _contractManager.GetById(contractId);
            if (contract == null || contract.Requirement.IsSatisfiedBy(antique)) return;

            Inventory.ReleaseReservation(antique.Id);
            Debug.Log($"{TraderName}: {antique.Name} arrived damaged (-{conditionLost:F2}) and no longer meets contract {contractId} — reservation released, looking for a replacement.");
        }

        // ---------------------------------------------------------------- receiving nes

        public void ReceiveNews(NewsItem news)
        {
            var profile = Profile;
            if (profile == null || _economyManager == null || _economyManager.TimeManager == null) return;

            // Nothing to trade on (e.g. a weather forecast) — no reaction to plan.
            if (news?.NewsData == null || news.NewsData.Count == 0) return;

            float trust = news.Type switch
            {
                NewsType.Official => 1f,
                NewsType.Leak => profile.LeakTrust,
                NewsType.Rumor => profile.RumorTrust,
                NewsType.Fake => profile.RumorTrust * 0.5f,
                _ => 0f
            };

            float actionChance = trust * news.Credibility * (0.5f + profile.RiskTolerance);

            if (UnityEngine.Random.value > actionChance)
            {
                Debug.Log($"Trader {TraderName} dismissed the {news.Type} news from Week {news.WeekPublished} based on actionChance");
                return;
            }

            int delay = UnityEngine.Random.Range(profile.MinReactionDelayWeeks, profile.MaxReactionDelayWeeks + 1);
            int reactionWeek = _economyManager.TimeManager.CurrentWeek + delay;

            if (news.EventTriggerWeek <= reactionWeek)
            {
                Debug.Log($"Trader {TraderName} dismissed the {news.Type} news from Week {news.WeekPublished} because the week it would react ({reactionWeek}) is later than the week the event starts ({news.EventTriggerWeek})");
                return;
            }

            _pendingReactions.Add(new PendingReaction { News = news, ReactionWeek = reactionWeek });
        }

        // ---------------------------------------------------------------- transactions

        private bool TryBuyForStock(Antique listing, int week, float cost)
        {
            if (!BuyListing(listing.Id, NpcTransportOption)) return false;

            _acquisitions[listing.Id] = new Acquisition { PurchasePrice = listing.PurchasePrice, Week = week };
            _weekBudget = Mathf.Max(0f, _weekBudget - cost);
            return true;
        }

        private float EstimateTotalCost(Antique listing, TransportOption option) =>
            TraderHelper.EstimateTotalCost(listing, _transportManager, option, Inventory);

        public bool BuyListing(string listingId) => BuyListing(listingId, NpcTransportOption);

        public bool BuyListing(string listingId, TransportOption option) =>
            TraderHelper.BuyListing(Inventory, _economyManager.Market, listingId, TraderName, CurrentWeek, _transportManager, option);

        public bool SellListing(string listingId) =>
            TraderHelper.SellListing(Inventory, _economyManager.Market, listingId, TraderName, CurrentWeek);

        // ---------------------------------------------------------------- auctions

        /// <summary>
        /// Most this NPC can bid on one lot (hammer price): its free cash after the contract reserve,
        /// times the profile's auction budget share, leaving <paramref name="costAllowance"/> for the
        /// buyer's premium and transport. Recomputed for every lot, so winning one lowers the next.
        /// </summary>
        public float AuctionBudget(float costAllowance)
        {
            var profile = Profile;
            if (profile == null) return 0f;

            float free = Mathf.Max(0f, Inventory.Cash - CalculateContractReserve() * ReserveSafetyMargin);
            return free * profile.AuctionBudgetShare / (1f + Mathf.Max(0f, costAllowance));
        }

        /// <summary>
        /// This NPC's paddle for a lot, or null if it doesn't want it (outside its interests, below its
        /// condition standard, no budget). The limit starts from the same fair value used on the market,
        /// scaled by the profile's auction multiplier, the lot's extra appeal (<paramref name="valuePremium"/>,
        /// e.g. provenance) and the news it believes about this kind of antique — capped by its budget.
        /// </summary>
        public AuctionBidder CreateAuctionBidder(Antique antique, float valuePremium, float costAllowance, System.Random rng)
        {
            var profile = Profile;
            if (profile == null || antique == null || antique.Definition == null || _economyManager.Market == null) return null;
            if (!IsInterestedIn(antique.Definition, profile)) return null;
            if (antique.Condition < profile.MinConditionToBuy) return null;

            float cap = AuctionBudget(costAllowance);
            if (cap <= 0f) return null;

            float jitter = rng != null ? 0.9f + 0.2f * (float)rng.NextDouble() : 1f;
            float value = FairValue(antique, profile)
                          * profile.AuctionValueMultiplier
                          * (1f + Mathf.Max(0f, valuePremium))
                          * (1f + AuctionNewsBias(antique, profile))
                          * jitter;

            float limit = Mathf.Min(value, cap);
            if (limit <= 0f) return null;

            return new AuctionBidder
            {
                Id = Id,
                Name = TraderName,
                Kind = BidderKind.Npc,
                Limit = limit,
                Cap = cap,
                FeverChance = profile.AuctionFeverChance,
                FeverRaise = profile.AuctionFeverRaise,
                Hesitation = profile.AuctionHesitation
            };
        }

        /// <summary>Remembers a won lot like a market purchase, so the usual selling rules apply to it.</summary>
        public void RecordAuctionPurchase(Antique antique, int week)
        {
            if (antique == null) return;
            _acquisitions[antique.Id] = new Acquisition { PurchasePrice = antique.PurchasePrice, Week = week };
        }

        /// <summary>
        /// +premium per believed piece of good news matching the antique, −premium per bad one. Only news
        /// the NPC already reacts to (passed its trust roll, reaction day reached) counts.
        /// </summary>
        private float AuctionNewsBias(Antique antique, NpcBehaviorProfile profile)
        {
            if (profile.AuctionNewsPremium <= 0f) return 0f;

            int week = CurrentWeek;
            float bias = 0f;

            foreach (var pending in _pendingReactions)
            {
                if (pending.ReactionWeek > week || pending.News?.NewsData == null) continue;

                foreach (var effect in pending.News.NewsData)
                {
                    if (effect == null || effect.targetScope == TargetScope.Other) continue;
                    if (!MatchesScope(antique, effect)) continue;

                    bias += effect.affectsPriceUp ? profile.AuctionNewsPremium : -profile.AuctionNewsPremium;
                }
            }

            return Mathf.Clamp(bias, -0.5f, 0.5f);
        }

        // ---------------------------------------------------------------- save / load

        public NPCTraderState CaptureState()
        {
            var state = new NPCTraderState
            {
                Id = Id,
                ProfileId = ProfileId,
                TraderName = TraderName,
                // Informational only: the inventory saves and restores the commitments (EconomyState).
                CommittedContractIds = new List<string>(Inventory.CommittedContractIds),
                Record = Record.CaptureState(),
                PlanWeek = _planWeek,
                SlotTicks = new List<int>(_slotTicks),
                WeekBudget = _weekBudget,
                ContractReserve = _contractReserve,
                ContractSlotsUsed = _contractSlotsUsed
            };

            foreach (var pair in _acquisitions)
            {
                state.Acquisitions.Add(new AcquisitionState
                {
                    ListingId = pair.Key,
                    PurchasePrice = pair.Value.PurchasePrice,
                    Week = pair.Value.Week
                });
            }

            foreach (var pending in _pendingReactions)
            {
                state.PendingReactions.Add(new PendingReactionState
                {
                    ReactionWeek = pending.ReactionWeek,
                    News = NewsItemState.Capture(pending.News)
                });
            }

            return state;
        }

        public void RestoreState(NPCTraderState state)
        {
            if (state == null) return;

            if (!string.IsNullOrEmpty(state.Id))
                Id = state.Id;

            if (!string.IsNullOrEmpty(state.ProfileId) && state.ProfileId != ProfileId)
                Debug.LogWarning($"NPCTrader: '{TraderName}' was constructed with profile '{ProfileId}' but the save says '{state.ProfileId}'. Construct the NPC from the saved ProfileId.");

            // Saves from before NPC records start them fresh.
            Record.RestoreState(state.Record);

            _acquisitions.Clear();
            if (state.Acquisitions != null)
            {
                foreach (var acquisition in state.Acquisitions)
                {
                    if (acquisition == null || string.IsNullOrEmpty(acquisition.ListingId)) continue;

                    _acquisitions[acquisition.ListingId] = new Acquisition
                    {
                        PurchasePrice = acquisition.PurchasePrice,
                        Week = acquisition.Week
                    };
                }
            }

            _pendingReactions.Clear();
            if (state.PendingReactions != null)
            {
                foreach (var pending in state.PendingReactions)
                {
                    var news = pending?.News?.Restore();
                    if (news == null) continue;

                    _pendingReactions.Add(new PendingReaction { News = news, ReactionWeek = pending.ReactionWeek });
                }
            }

            // The period in progress. A save from before intra-period acting carries PlanWeek 0,
            // which leaves _planWeek at -1 and makes the next tick plan the period from scratch.
            _planWeek = state.PlanWeek > 0 ? state.PlanWeek : -1;
            _weekBudget = state.WeekBudget;
            _contractReserve = state.ContractReserve;
            _contractSlotsUsed = state.ContractSlotsUsed;
            _lastObligationTick = -1;
            _rng = new System.Random(SeedFor(Mathf.Max(1, _planWeek)));

            _slotTicks.Clear();
            if (state.SlotTicks != null)
            {
                _slotTicks.AddRange(state.SlotTicks);
                _slotTicks.Sort();
            }

            // CommittedContractIds is not restored here: the inventory restores its own commitments
            // when the economy is loaded (after the NPCs).
        }

    }

    public enum ContractUrgency
    {
        /// <summary> There are more days left than ContractPanicWeeks.</summary>
        Normal,
        /// <summary>Equal or fewer days than ContractPanicWeeks. Handling contracts is done by every slot.</summary>
        Pressing,
        /// <summary>The contract may already be unfinishable.</summary>
        Critical
    }
}