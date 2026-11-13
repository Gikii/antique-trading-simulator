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

        private const int NewsActionWindowDays = 2;

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
            public int ReactionDay;
        }

        private struct Acquisition
        {
            public float PurchasePrice;
            public int Day;
        }

        private readonly struct Offer
        {
            public readonly Antique Listing;
            public readonly TransportOption Option;
            public readonly float Cost;
            public readonly int ArrivalDay;
            public readonly int DurationDays;

            public Offer(Antique listing, TransportOption option, float cost, int arrivalDay, int durationDays)
            {
                Listing = listing;
                Option = option;
                Cost = cost;
                ArrivalDay = arrivalDay;
                DurationDays = durationDays;
            }
        }

        private readonly List<PendingReaction> _pendingReactions = new();
        private readonly Dictionary<string, Acquisition> _acquisitions = new();

        private int _planDay = -1;
        private readonly List<int> _slotTicks = new();
        private float _dayBudget;
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

        private int CurrentDay => _economyManager != null && _economyManager.TimeManager != null
            ? _economyManager.TimeManager.CurrentDay
            : 0;

        private int TicksPerPeriod => _economyManager != null && _economyManager.TimeManager != null
            ? _economyManager.TimeManager.TicksPerPeriod
            : Core.TimeManager.DefaultTicksPerPeriod;

        // ---------------------------------------------------------------- actions on tick

        public void RunScheduledActions(int day, int tick)
        {
            var profile = Profile;
            if (profile == null || _economyManager.Market == null) return;

            if (_planDay != day) PlanPeriod(day, tick, profile);

            if (_lastObligationTick != tick)
            {
                _lastObligationTick = tick;
                RunObligations(day, profile);
            }

            while (_slotTicks.Count > 0 && _slotTicks[0] <= tick)
            {
                _slotTicks.RemoveAt(0);
                RunOneBudgetedAction(day, profile);
            }
        }

        public void FlushRemainingSlots()
        {
            var profile = Profile;
            if (profile == null || _economyManager.Market == null || _planDay < 0) return;

            while (_slotTicks.Count > 0)
            {
                _slotTicks.RemoveAt(0);
                RunOneBudgetedAction(_planDay, profile);
            }
        }

        public void EvaluateDay(int currentDay)
        {
            var profile = Profile;
            if (profile == null || _economyManager.Market == null) return;

            PlanPeriod(currentDay, 0, profile);
            RunObligations(currentDay, profile);
            FlushRemainingSlots();
        }

        // ---------------------------------------------------------------- planning

        private void PlanPeriod(int day, int fromTick, NpcBehaviorProfile profile)
        {
            _planDay = day;
            _contractSlotsUsed = 0;
            _lastObligationTick = -1;
            _rng = new System.Random(SeedFor(day));

            DropInactiveContracts();
            DropExpiredReactions(day);

            _contractReserve = CalculateContractReserve();
            _dayBudget = Mathf.Max(0f, Inventory.Cash - _contractReserve) * profile.DailyBudgetFraction;

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

        private int SeedFor(int day)
        {
            unchecked
            {
                int hash = (int)2166136261;
                string id = Id ?? TraderName ?? "npc";
                for (int i = 0; i < id.Length; i++)
                    hash = (hash ^ id[i]) * 16777619;
                return hash ^ (day * 397);
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

        private void RunObligations(int day, NpcBehaviorProfile profile)
        {
            DropInactiveContracts();
            ReserveMatchingHoldings();
            DropUnfullfillableOpenContracts(day, profile);
            FulfillReadyContracts(day);
            RunStopLoss(profile);
            WithdrawStaleListings(day, profile);
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

        private void DropExpiredReactions(int day)
        {
            for (int i = _pendingReactions.Count - 1; i >= 0; i--)
                if (day > _pendingReactions[i].ReactionDay + NewsActionWindowDays)
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

        private void FulfillReadyContracts(int day)
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

            if (ready != null) TryFulfillContract(ready, day);
        }

        private void TryFulfillContract(Contract contract, int day)
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
                Record.ContractFulfilled(contract, day);
            }
        }

        private void DropUnfullfillableOpenContracts(int day, NpcBehaviorProfile profile)
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
                if (UrgencyOf(contract, day, profile) != ContractUrgency.Critical) continue;
                if (IsStillReachable(contract, missing, day, profile)) continue;

                hopeless = contract;
                break;
            }

            if (hopeless == null) return;

            Debug.Log($"{TraderName} dropped open contract {hopeless.ContractId}. They cannot aquire missing antiques in remaining time.");
            Inventory.ReleaseAllReservationsForContract(hopeless.ContractId);
            Inventory.ReleaseCommittedContract(hopeless.ContractId);
            Record.ContractFailed(hopeless, day);
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

        /// <summary>Withdraws one of its own listings that has sat unsold past RelistAfterDays.</summary>
        private void WithdrawStaleListings(int day, NpcBehaviorProfile profile)
        {
            if (!profile.ListsOnMarket) return;

            string stale = null;
            foreach (var pair in Inventory.Holdings)
            {
                var holding = pair.Value;
                if (!holding.IsListedForSale) continue;
                if (holding.MarketListedOnDay < 0) continue;
                if (day - holding.MarketListedOnDay < Mathf.Max(1, profile.RelistAfterDays)) continue;

                stale = holding.Id;
                break;
            }

            if (stale != null) Inventory.CancelListing(_economyManager.Market, stale);
        }

        // ---------------------------------------------------------------- tick action priority ladder

        private void RunOneBudgetedAction(int day, NpcBehaviorProfile profile)
        {
            if (TierContractRescue(day, profile)) return;
            if (TierDamageControl(day, profile)) return;
            if (TierNewsBuy(day, profile)) return;
            if (TierContractWork(day, profile)) return;
            if (TierReviewHoldings(day, profile)) return;
            if (TierTakeContract(day, profile)) return;
            if (TierBrowseMarket(day, profile)) return;
        }

        private int ContractSlotsAllowed(NpcBehaviorProfile profile)
        {
            int full = Mathf.Clamp(Mathf.RoundToInt(profile.ActionsPerPeriod * Mathf.Max(0f, ActivityScale)), 1, Mathf.Max(1, TicksPerPeriod));
            return Mathf.Max(1, Mathf.CeilToInt(full * profile.ContractSlotShare));
        }

        // ---------------------------------------------------------------- contract tick actions

        private bool TierContractRescue(int day, NpcBehaviorProfile profile) =>
            WorkOnContracts(day, profile, urgentOnly: true);

        private bool TierContractWork(int day, NpcBehaviorProfile profile)
        {
            if (_contractSlotsUsed >= ContractSlotsAllowed(profile)) return false;
            return WorkOnContracts(day, profile, urgentOnly: false);
        }

        private bool WorkOnContracts(int day, NpcBehaviorProfile profile, bool urgentOnly)
        {
            if (_contractManager == null) return false;

            CollectActiveContracts(day, profile);
            if (_contractScratch.Count == 0) return false;

            foreach (var contract in _contractScratch)
            {
                int missing = MissingFor(contract);
                if (missing <= 0) continue;

                var urgency = UrgencyOf(contract, day, profile);
                if (urgentOnly && urgency == ContractUrgency.Normal) continue;
                if (!urgentOnly && urgency != ContractUrgency.Normal) continue;

                var offer = BestOfferFor(contract, day, profile, urgency);
                if (offer.Listing == null) continue;

                float cost = offer.Cost;
                if (cost > Inventory.Cash) continue;

                if (!BuyListing(offer.Listing.Id, offer.Option)) continue;

                _acquisitions[offer.Listing.Id] = new Acquisition { PurchasePrice = offer.Listing.PurchasePrice, Day = day };
                Inventory.ReserveForContract(offer.Listing.Id, contract.ContractId);

                _contractReserve = CalculateContractReserve();
                if (!urgentOnly) _contractSlotsUsed++;

                Debug.Log($"{TraderName} bought {offer.Listing.Name} for contract {contract.ContractId} ({urgency}, {offer.Option} transport, arrives day {offer.ArrivalDay}).");
                return true;
            }

            return false;
        }

        private void CollectActiveContracts(int day, NpcBehaviorProfile profile)
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
            _contractScratch.Sort((a, b) => a.DeadlineDay.CompareTo(b.DeadlineDay));
        }

        private bool TierTakeContract(int day, NpcBehaviorProfile profile)
        {
            if (!profile.TakesContracts || _contractManager == null) return false;
            return ConsiderNewContract(day, profile);
        }

        // ---------------------------------------------------------------- news tick actions

        private bool TierDamageControl(int day, NpcBehaviorProfile profile)
        {
            if (profile.NeverSells) return false;

            for (int i = _pendingReactions.Count - 1; i >= 0; i--)
            {
                var pending = _pendingReactions[i];
                if (pending.ReactionDay > day) continue;
                if (SellOnNews(pending.News, day, profile)) return true;
            }

            return false;
        }

        private bool TierNewsBuy(int day, NpcBehaviorProfile profile)
        {
            for (int i = _pendingReactions.Count - 1; i >= 0; i--)
            {
                var pending = _pendingReactions[i];
                if (pending.ReactionDay > day) continue;
                if (BuyOnNews(pending.News, day, profile)) return true;
            }

            return false;
        }

        private bool SellOnNews(NewsItem news, int day, NpcBehaviorProfile profile)
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
                Debug.Log($"{TraderName} sold {victimName} ahead of {news.Type} news from day {news.DayPublished}.");
                return true;
            }

            return false;
        }

        private bool BuyOnNews(NewsItem news, int day, NpcBehaviorProfile profile)
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
                    if (cost > _dayBudget || !IsAcceptablePrice(listing, profile)) continue;
                    if (!PassesCashFloor(cost)) continue;

                    if (!TryBuyForStock(listing, day, cost)) continue;

                    Debug.Log($"{TraderName} bought {listing.Name} on {news.Type} news from day {news.DayPublished}.");
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

        private bool TierReviewHoldings(int day, NpcBehaviorProfile profile)
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

                int held = day - acquisition.Day;
                if (held < profile.MinHoldingDaysBeforeSell) continue;

                float value = holding.CurrentPrice;
                bool patient = profile.PatientSellAfterDays > 0 && held >= profile.PatientSellAfterDays;
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
                if (!Inventory.ListForSale(_economyManager.Market, chosen, askingPrice, day)) return false;

                Debug.Log($"{TraderName} listed {chosenName} for {askingPrice:F0} £ (value {estimatedValue:F0} £).");
                return true;
            }

            if (!SellListing(chosen)) return false;

            _acquisitions.Remove(chosen);
            return true;
        }

        // ---------------------------------------------------------------- buy from market tick actions

        private bool TierBrowseMarket(int day, NpcBehaviorProfile profile)
        {
            if (_dayBudget <= 0f) return false;

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
                if (cost <= 0f || cost > _dayBudget) continue;
                if (!IsAcceptablePrice(listing, profile)) continue;
                if (!PassesCashFloor(cost)) continue;

                float score = BargainScore(listing, cost, profile);
                if (score < bestScore) continue;

                best = listing;
                bestScore = score;
                bestCost = cost;
            }

            if (best == null) return false;

            if (!TryBuyForStock(best, day, bestCost)) return false;

            Debug.Log($"{TraderName} bought {best.Name} on the open market (score {bestScore:P0}, cost {bestCost:F0} £).");
            return true;
        }

        // ---------------------------------------------------------------- valuation

        private float BargainScore(Antique listing, float totalCost, NpcBehaviorProfile profile)
        {
            if (totalCost <= 0f) return 0f;

            float fair = FairValue(listing, profile);
            int holdPeriods = profile.PatientSellAfterDays > 0
                ? profile.PatientSellAfterDays
                : Mathf.Max(1, profile.MinHoldingDaysBeforeSell);

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
            return Mathf.Max(0f, warehouse.CalculateDailyUpkeep(used + 1) - warehouse.CalculateDailyUpkeep(used));
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
            if (profile.SeasonalForesight <= 0f || profile.SeasonalHorizonDays <= 0 || _economyManager == null) return 0f;

            float ratio = _economyManager.SeasonalPriceOutlook(definitionId, profile.SeasonalHorizonDays);
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
        private static int LatestUsefulArrivalDay(Contract contract) => contract.DeadlineDay - 1;

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
        private ContractUrgency UrgencyOf(Contract contract, int day, NpcBehaviorProfile profile)
        {
            if (MissingFor(contract) <= 0) return ContractUrgency.Normal;

            int slack = LatestUsefulArrivalDay(contract) - day - WorstTransitAhead(contract);

            if (slack <= 0) return ContractUrgency.Critical;
            if (slack <= profile.ContractPanicDays) return ContractUrgency.Pressing;
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
                worst = Mathf.Max(worst, holding.ArrivalDay - CurrentDay);
            }

            if (MissingFor(contract) > 0 && _transportManager != null && _transportManager.Settings != null)
                worst = Mathf.Max(worst, _transportManager.Settings.GetBaseDurationDays(ShippingZone.Domestic, NpcTransportOption));

            return Mathf.Max(1, worst);
        }

        /// <summary>
        /// Cheapest matching listing that can still land in time, re-quoted now. The transport option
        /// escalates with urgency and with how little condition margin the item has to lose.
        /// </summary>
        private Offer BestOfferFor(Contract contract, int day, NpcBehaviorProfile profile, ContractUrgency urgency)
        {
            var market = _economyManager.Market;
            if (market == null) return default;

            int latestArrival = LatestUsefulArrivalDay(contract);
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
                int arrival = quote != null ? quote.ArrivalDay : day;
                int duration = quote != null ? quote.DurationDays : 0;
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


        private bool IsStillReachable(Contract contract, int missing, int day, NpcBehaviorProfile profile)
        {
            int perPeriod = Mathf.Max(1, ContractSlotsAllowed(profile));
            int latestArrival = LatestUsefulArrivalDay(contract);

            BuildOfferPlan(contract, day, profile, missing);
            if (_offerScratch.Count < missing) return false;

            for (int k = 0; k < missing; k++)
            {
                int buyDay = day + k / perPeriod;
                if (buyDay + _offerScratch[k].DurationDays > latestArrival) return false;
            }

            return true;
        }

        private void BuildOfferPlan(Contract contract, int day, NpcBehaviorProfile profile, int missing)
        {
            _offerScratch.Clear();

            var market = _economyManager.Market;
            if (market == null || missing <= 0) return;

            int latestArrival = LatestUsefulArrivalDay(contract);
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
                int arrival = quote != null ? quote.ArrivalDay : day;
                if (arrival > latestArrival) continue;

                float cost = listing.SalePrice + (quote != null ? quote.Cost : 0f);
                if (cost > contract.RewardPerUnit) continue;

                _offerScratch.Add(new Offer(listing, option, cost, arrival, quote != null ? quote.DurationDays : 0));
            }

            _offerScratch.Sort((a, b) => a.Cost.CompareTo(b.Cost));
            if (_offerScratch.Count > missing) _offerScratch.RemoveRange(missing, _offerScratch.Count - missing);

            _offerScratch.Sort((a, b) => b.DurationDays.CompareTo(a.DurationDays));
        }

        /// <summary>
        /// Takes the most profitable contract the NPC can actually finish.
        /// </summary>
        private bool ConsiderNewContract(int day, NpcBehaviorProfile profile)
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

                BuildOfferPlan(contract, day, profile, missing);
                if (_offerScratch.Count < missing) continue;

                float purchaseCost = 0f;
                for (int k = 0; k < _offerScratch.Count; k++) purchaseCost += _offerScratch[k].Cost;
                if (purchaseCost > cashLimit) continue;

                if (!IsStillReachable(contract, missing, day, profile)) continue;

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

            _contractReserve = CalculateContractReserve();
            _dayBudget = Mathf.Min(_dayBudget, Mathf.Max(0f, Inventory.Cash - _contractReserve));

            Debug.Log($"{TraderName} picked up {chosen.Type} contract {chosen.ContractId} — needs {chosen.Requirement.Quantity}, due day {chosen.DeadlineDay}, expected profit {bestProfit:F0}.");
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
                Debug.Log($"Trader {TraderName} dismissed the {news.Type} news from Day {news.DayPublished} based on actionChance");
                return;
            }

            int delay = UnityEngine.Random.Range(profile.MinReactionDelayDays, profile.MaxReactionDelayDays + 1);
            int reactionDay = _economyManager.TimeManager.CurrentDay + delay;

            if (news.EventTriggerDay <= reactionDay)
            {
                Debug.Log($"Trader {TraderName} dismissed the {news.Type} news from Day {news.DayPublished} because the day it would react ({reactionDay}) is later than the day the event starts ({news.EventTriggerDay})");
                return;
            }

            _pendingReactions.Add(new PendingReaction { News = news, ReactionDay = reactionDay });
        }

        // ---------------------------------------------------------------- transactions

        private bool TryBuyForStock(Antique listing, int day, float cost)
        {
            if (!BuyListing(listing.Id, NpcTransportOption)) return false;

            _acquisitions[listing.Id] = new Acquisition { PurchasePrice = listing.PurchasePrice, Day = day };
            _dayBudget = Mathf.Max(0f, _dayBudget - cost);
            return true;
        }

        private float EstimateTotalCost(Antique listing, TransportOption option) =>
            TraderHelper.EstimateTotalCost(listing, _transportManager, option, Inventory);

        public bool BuyListing(string listingId) => BuyListing(listingId, NpcTransportOption);

        public bool BuyListing(string listingId, TransportOption option) =>
            TraderHelper.BuyListing(Inventory, _economyManager.Market, listingId, TraderName, CurrentDay, _transportManager, option);

        public bool SellListing(string listingId) =>
            TraderHelper.SellListing(Inventory, _economyManager.Market, listingId, TraderName, CurrentDay);

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
        public void RecordAuctionPurchase(Antique antique, int day)
        {
            if (antique == null) return;
            _acquisitions[antique.Id] = new Acquisition { PurchasePrice = antique.PurchasePrice, Day = day };
        }

        /// <summary>
        /// +premium per believed piece of good news matching the antique, −premium per bad one. Only news
        /// the NPC already reacts to (passed its trust roll, reaction day reached) counts.
        /// </summary>
        private float AuctionNewsBias(Antique antique, NpcBehaviorProfile profile)
        {
            if (profile.AuctionNewsPremium <= 0f) return 0f;

            int day = CurrentDay;
            float bias = 0f;

            foreach (var pending in _pendingReactions)
            {
                if (pending.ReactionDay > day || pending.News?.NewsData == null) continue;

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
                PlanDay = _planDay,
                SlotTicks = new List<int>(_slotTicks),
                DayBudget = _dayBudget,
                ContractReserve = _contractReserve,
                ContractSlotsUsed = _contractSlotsUsed
            };

            foreach (var pair in _acquisitions)
            {
                state.Acquisitions.Add(new AcquisitionState
                {
                    ListingId = pair.Key,
                    PurchasePrice = pair.Value.PurchasePrice,
                    Day = pair.Value.Day
                });
            }

            foreach (var pending in _pendingReactions)
            {
                state.PendingReactions.Add(new PendingReactionState
                {
                    ReactionDay = pending.ReactionDay,
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
                        Day = acquisition.Day
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

                    _pendingReactions.Add(new PendingReaction { News = news, ReactionDay = pending.ReactionDay });
                }
            }

            // The period in progress. A save from before intra-period acting carries PlanDay 0,
            // which leaves _planDay at -1 and makes the next tick plan the period from scratch.
            _planDay = state.PlanDay > 0 ? state.PlanDay : -1;
            _dayBudget = state.DayBudget;
            _contractReserve = state.ContractReserve;
            _contractSlotsUsed = state.ContractSlotsUsed;
            _lastObligationTick = -1;
            _rng = new System.Random(SeedFor(Mathf.Max(1, _planDay)));

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
        /// <summary> There are more days left than ContractPanicDays.</summary>
        Normal,
        /// <summary>Equal or fewer days than ContractPanicDays. Handling contracts is done by every slot.</summary>
        Pressing,
        /// <summary>The contract may already be unfinishable.</summary>
        Critical
    }
}