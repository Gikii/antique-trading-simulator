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

        private readonly List<PendingReaction> _pendingReactions = new();
        private readonly Dictionary<string, Acquisition> _acquisitions = new();

        /// <summary>Contracts this NPC has taken — kept by its inventory (saved with the economy).</summary>
        public IReadOnlyCollection<string> CommittedContractIds => Inventory.CommittedContractIds;

        public NPCTrader(string traderName, string profileId, float startingCash, EconomyManager economyManager,
            ContractManager contractManager = null, TransportManager transportManager = null)
        {
            Id = Guid.NewGuid().ToString("N");
            TraderName = traderName;
            ProfileId = profileId;
            Inventory = new TraderInventory(startingCash);
            _economyManager = economyManager;
            _contractManager = contractManager;
            _transportManager = transportManager;
        }

        public void ReceiveNews(NewsItem news)
        {
            var profile = Profile;
            if (profile == null) return;

            float trust = news.Type switch
            {
                NewsType.Official => 1f,
                NewsType.Leak => profile.LeakTrust,
                NewsType.Rumor => profile.RumorTrust,
                NewsType.Fake => profile.RumorTrust * 0.5f,
                _ => 0f
            };

            float actionChance = trust * news.Credibility * (0.5f + profile.RiskTolerance);

            Debug.Log($"Trader {TraderName} received {news.Type} news on Day {news.DayPublished} ({news.NewsData.Count} effects, trust {trust:F2}, credibility {news.Credibility:F2}, action chance {actionChance:F2})");

            if (UnityEngine.Random.value > actionChance)
            {
                Debug.Log($"Trader {TraderName} dismissed the {news.Type} news from Day {news.DayPublished} based on actionChance");
                return;
            }

            int delay = UnityEngine.Random.Range(profile.MinReactionDelayDays, profile.MaxReactionDelayDays + 1);
            int reactionDay = _economyManager.TimeManager.CurrentDay + delay;
            if (news.EventTriggerDay <= reactionDay)
            {
                Debug.Log($"Trader {TraderName} dismissed the {news.Type} news from Day {news.DayPublished} because the day it would react ({reactionDay}) is later than the day the event starts({news.EventTriggerDay})");
                return;
            }
            _pendingReactions.Add(new PendingReaction
            {
                News = news,
                ReactionDay = _economyManager.TimeManager.CurrentDay + delay
            });
        }

        public void EvaluateDay(int currentDay)
        {
            var profile = Profile;
            if (profile == null) return;

            float budget = Inventory.Cash * profile.DailyBudgetFraction;
            budget = ProcessPendingReactions(currentDay, budget, profile);
            ConsiderSellingHoldings(currentDay, profile);

            RemoveInactiveContracts();
            budget = PursueCommittedContracts(budget, profile, currentDay);
            if (profile.TakesContracts)
                budget -= ConsiderNewContract(profile, currentDay);

            ConsiderBuyingFromMarket(budget, profile);
        }

        private float ProcessPendingReactions(int currentDay, float budget, NpcBehaviorProfile profile)
        {
            Debug.Log($"Trader {TraderName} processing news. Day {_economyManager.TimeManager.CurrentDay}");
            for (int i = _pendingReactions.Count - 1; i >= 0; i--)
            {
                var pending = _pendingReactions[i];
                if (pending.ReactionDay >= currentDay) continue;

                budget = TryActOnNews(pending.News, budget, currentDay, profile);
                _pendingReactions.RemoveAt(i);
            }
            return budget;
        }

        private float TryActOnNews(NewsItem news, float budget, int currentDay, NpcBehaviorProfile profile)
        {
            Debug.Log($"Trader {TraderName} acting on {news.Type} from day {news.DayPublished}");
            foreach (var eventEffect in news.NewsData)
            {
                if (eventEffect.targetScope != EventEffect.TargetScope.Other)
                {
                    if (eventEffect.affectsPriceUp)
                    {
                        var listings = eventEffect.targetScope switch
                        {
                            TargetScope.AntiqueType => _economyManager.Market.GetByType(eventEffect.AntiqueType),
                            TargetScope.Country => _economyManager.Market.GetByCountry(eventEffect.Country),
                            TargetScope.Century => _economyManager.Market.GetByCentury(eventEffect.Century),
                            _ => new List<Antique>()
                        };

                        foreach (var listing in listings)
                        {
                            if (budget <= 0f) break;
                            if (Inventory.Owns(listing.Id) || listing.Condition < profile.MinConditionToBuy) continue;
                            float totalCost = EstimateTotalCost(listing);
                            if (!IsAcceptablePrice(listing, profile) || totalCost > budget) continue;

                            if (TryBuy(listing, currentDay)) budget -= totalCost;
                        }
                    }
                    else if (!profile.NeverSells)
                    {
                        var holdings = eventEffect.targetScope switch
                        {
                            TargetScope.AntiqueType => Inventory.Holdings.Values.Where(h => h.Definition.Type == eventEffect.AntiqueType && !h.IsReservedForContract && h.IsAvailable).ToList(),
                            TargetScope.Country => Inventory.Holdings.Values.Where(h => h.Definition.Country == eventEffect.Country && !h.IsReservedForContract && h.IsAvailable).ToList(),
                            TargetScope.Century => Inventory.Holdings.Values.Where(h => h.Definition.Century == eventEffect.Century && !h.IsReservedForContract && h.IsAvailable).ToList()

                        };
                        foreach (var holding in holdings)
                        {
                            var typeState = _economyManager.Market.GetTypeState(holding.DefinitionId);
                            float sellPrice = PriceEngine.CalculatePrice(holding, typeState);

                            // Bad news beats waiting for a buyer: withdraw its own listing and sell now.
                            if (holding.IsListedForSale)
                                Inventory.CancelListing(_economyManager.Market, holding.Id);

                            if (SellListing(holding.ListingId)) budget += sellPrice;
                        }
                    }
                }
            }

            return budget;
        }

        private void ConsiderBuyingFromMarket(float budget, NpcBehaviorProfile profile)
        {
            Debug.Log($"{TraderName} considered buying from market. Day {_economyManager.TimeManager.CurrentDay}");
            if (budget <= 0f) return;
            int currentDay = _economyManager.TimeManager.CurrentDay;

            foreach (var listing in _economyManager.Market.Listings.ToList())
            {
                if (budget <= 0f) break;
                if (Inventory.Owns(listing.Id)) continue; // its own listing
                if (!IsInterestedIn(listing.Definition, profile)) continue;
                if (listing.Condition < profile.MinConditionToBuy) continue;
                float totalCost = EstimateTotalCost(listing);
                if (!IsAcceptablePrice(listing, profile) || totalCost > budget) continue;

                if (TryBuy(listing, currentDay)) budget -= totalCost;
            }
        }

        /// <summary>
        /// Decides what to do with each antique the NPC bought: keep it, sell it, or (ListsOnMarket
        /// profiles) put it on the market at a markup, withdrawing listings that didn't sell in
        /// RelistAfterDays. Aims for the profit target; after PatientSellAfterDays settles for a small
        /// profit; below the stop-loss sells at once.
        /// </summary>
        private void ConsiderSellingHoldings(int currentDay, NpcBehaviorProfile profile)
        {
            if (profile.NeverSells) return;

            Debug.Log($"{TraderName} considered selling holdings. Day {_economyManager.TimeManager.CurrentDay}");
            var market = _economyManager.Market;
            var listingIds = new List<string>(_acquisitions.Keys);
            foreach (var listingId in listingIds)
            {
                var listing = Inventory.GetHolding(listingId);
                if (listing == null) { _acquisitions.Remove(listingId); continue; } // sold (also from a listing) or handed in
                if (listing.IsReservedForContract) continue; // being gathered for a contract — not for sale
                if (listing.IsInTransit || listing.IsInRestoration) continue; // can't sell what isn't here

                // Its own listing: wait for a buyer, or withdraw it to decide again tomorrow.
                if (listing.IsListedForSale)
                {
                    if (currentDay - listing.MarketListedOnDay >= profile.RelistAfterDays)
                        Inventory.CancelListing(market, listingId);
                    continue;
                }

                var acquisition = _acquisitions[listingId];
                int held = currentDay - acquisition.Day;

                var typeState = market.GetTypeState(listing.DefinitionId);
                float estimatedValue = PriceEngine.CalculatePrice(listing, typeState);

                bool stopLoss = profile.StopLossMultiplier > 0f && estimatedValue < acquisition.PurchasePrice * profile.StopLossMultiplier;
                if (held < profile.MinHoldingDaysBeforeSell) continue;

                bool patient = profile.PatientSellAfterDays > 0 && held >= profile.PatientSellAfterDays;
                float target = patient ? Mathf.Min(profile.PatientProfitMultiplier, profile.ProfitTargetMultiplier) : profile.ProfitTargetMultiplier;
                if (!stopLoss && estimatedValue < acquisition.PurchasePrice * target) continue;

                if (profile.ListsOnMarket && !stopLoss)
                {
                    float askingPrice = Mathf.Max(estimatedValue * profile.ListingMarkup, acquisition.PurchasePrice * target);
                    if (Inventory.ListForSale(market, listingId, askingPrice, currentDay))
                        Debug.Log($"{TraderName} listed {listing.Name} for {askingPrice:F0} € (value {estimatedValue:F0} €).");
                    continue; // the acquisition stays — it's still ours until someone buys it
                }

                if (stopLoss)
                    Debug.Log($"{TraderName} panic-sells {listing.Name}: worth {estimatedValue:F0} €, paid {acquisition.PurchasePrice:F0} €.");

                if (SellListing(listing.Id)) _acquisitions.Remove(listingId);
            }
        }


        // ---------------------------------------------------------------- contracts
        //
        // The trader's inventory is the single record of which contracts it has taken
        // (TraderInventory.CommittedContractIds) — the player uses the same one.
        //
        // An NPC only takes a contract it can actually finish: every missing item must be
        // available to buy now (or already owned) and arrive in time, and the whole purchase must
        // fit its cash. Contract purchases are paid from cash, not the daily browsing budget —
        // the cash was checked when the contract was taken.

        /// <summary>
        /// Latest day an item may arrive and still be handed in. Contracts expire on the tick of
        /// their deadline day and NPCs act once per tick, possibly before that day's deliveries —
        /// so an item must arrive at least one tick earlier.
        /// </summary>
        private static int LatestUsefulArrivalDay(Contract contract) => contract.DeadlineDay - 2;

        /// <summary>Exclusive contracts must leave at least this share of the reward as profit (they carry a penalty).</summary>
        private const float ExclusiveMinProfitShare = 0.15f;

        /// <summary>Share of cash an NPC may tie up in contract purchases at once.</summary>
        private const float ContractCashShare = 0.5f;

        private sealed class ContractPlan
        {
            public Contract Contract;
            public readonly List<Antique> FromHoldings = new();
            public readonly List<Antique> ToBuy = new();
            public float PurchaseCost;
            public float HoldingsValue;
            public int Missing;

            public bool IsComplete => Missing <= 0;
            public float Profit => Contract.TotalReward - PurchaseCost - HoldingsValue;
        }

        private void RemoveInactiveContracts()
        {
            if (_contractManager == null) return;

            foreach (var contractId in Inventory.CommittedContractIds.ToList())
            {
                var contract = _contractManager.GetById(contractId);
                if (contract != null && contract.Status == ContractStatus.Active) continue;

                // Fulfilled by someone else, expired or gone — free the antiques and the cash it held.
                Inventory.ReleaseAllReservationsForContract(contractId);
                Inventory.ReleaseCommittedContract(contractId);
            }
        }

        /// <summary>
        /// What it would take to finish a contract from now: matching antiques the NPC already owns
        /// (not reserved, not listed, here or arriving in time) first, then the cheapest matching
        /// market listings that arrive in time and don't cost more than the reward per unit.
        /// </summary>
        private ContractPlan PlanContract(Contract contract, int currentDay)
        {
            var plan = new ContractPlan { Contract = contract };
            int latestArrival = LatestUsefulArrivalDay(contract);

            int needed = contract.Requirement.Quantity - Inventory.GetReservedForContract(contract.ContractId).Count;

            foreach (var holding in Inventory.Holdings.Values
                         .Where(h => !h.IsReservedForContract && !h.IsListedForSale && !h.IsInRestoration)
                         .Where(h => h.IsAvailable || (h.IsInTransit && h.ArrivalDay <= latestArrival))
                         .Where(h => contract.Requirement.IsSatisfiedBy(h))
                         .OrderBy(h => h.CurrentPrice))
            {
                if (plan.FromHoldings.Count >= needed) break;
                plan.FromHoldings.Add(holding);
                plan.HoldingsValue += holding.CurrentPrice;
            }

            int toBuy = needed - plan.FromHoldings.Count;
            if (toBuy > 0 && _economyManager.Market != null)
            {
                var offers = _economyManager.Market.Listings
                    .Where(l => contract.Requirement.IsSatisfiedBy(l) && !Inventory.Owns(l.Id))
                    .Select(l => (listing: l, quote: QuoteFor(l)))
                    .Where(o => o.quote == null || o.quote.ArrivalDay <= latestArrival)
                    .Select(o => (o.listing, cost: o.listing.SalePrice + (o.quote != null ? o.quote.Cost : 0f)))
                    .Where(o => o.cost <= contract.RewardPerUnit && IsAcceptablePrice(o.listing, Profile))
                    .OrderBy(o => o.cost);

                foreach (var (listing, cost) in offers)
                {
                    if (plan.ToBuy.Count >= toBuy) break;
                    plan.ToBuy.Add(listing);
                    plan.PurchaseCost += cost;
                }
            }

            plan.Missing = Mathf.Max(0, needed - plan.FromHoldings.Count - plan.ToBuy.Count);
            return plan;
        }

        private TransportQuote QuoteFor(Antique listing) =>
            _transportManager != null ? _transportManager.Quote(listing, NpcTransportOption, buyer: Inventory) : null;

        /// <summary>Reserves the planned holdings and buys the planned listings. Returns what was spent.</summary>
        private float ExecutePlan(ContractPlan plan, int currentDay)
        {
            string contractId = plan.Contract.ContractId;

            foreach (var holding in plan.FromHoldings)
                Inventory.ReserveForContract(holding.Id, contractId);

            float spent = 0f;
            foreach (var listing in plan.ToBuy)
            {
                float cost = EstimateTotalCost(listing);
                if (cost > Inventory.Cash) break;
                if (!TryBuy(listing, currentDay)) continue;

                // Reserved right away, even while still in transit — handed in once it arrives.
                Inventory.ReserveForContract(listing.Id, contractId);
                spent += cost;
            }
            return spent;
        }

        /// <summary>
        /// Keeps every taken contract on track: buys what is still missing (from cash) and hands the
        /// contract in once everything has arrived. An Open contract that can no longer be finished
        /// is dropped — that costs nothing; an Exclusive one is pursued to the end to avoid the penalty.
        /// Returns the browsing budget left after contract purchases.
        /// </summary>
        private float PursueCommittedContracts(float budget, NpcBehaviorProfile profile, int currentDay)
        {
            if (_contractManager == null) return budget;

            var contracts = Inventory.CommittedContractIds
                .Select(id => _contractManager.GetById(id))
                .Where(c => c != null && c.Status == ContractStatus.Active)
                .OrderBy(c => c.DeadlineDay)
                .ToList();

            foreach (var contract in contracts)
            {
                int reserved = Inventory.GetReservedForContract(contract.ContractId).Count;
                if (reserved < contract.Requirement.Quantity)
                {
                    var plan = PlanContract(contract, currentDay);

                    if (!plan.IsComplete && contract.Type == ContractType.Open)
                    {
                        Debug.Log($"{TraderName} dropped open contract {contract.ContractId} — the missing antiques can't arrive before day {contract.DeadlineDay}.");
                        Inventory.ReleaseAllReservationsForContract(contract.ContractId);
                        Inventory.ReleaseCommittedContract(contract.ContractId);
                        continue;
                    }

                    budget -= ExecutePlan(plan, currentDay);
                }

                if (Inventory.GetReservedForContract(contract.ContractId).Count >= contract.Requirement.Quantity)
                    TryFulfillContract(contract);
            }

            return budget;
        }

        private void TryFulfillContract(Contract contract)
        {
            var listingIds = Inventory.GetReservedForContract(contract.ContractId)
                .Where(a => a.IsAvailable) // items still in transit can't be handed in yet
                .Take(contract.Requirement.Quantity)
                .Select(a => a.Id)
                .ToList();

            if (listingIds.Count < contract.Requirement.Quantity) return;

            if (_contractManager.FulfillContract(contract.ContractId, Id, Inventory, listingIds))
                Inventory.ReleaseCommittedContract(contract.ContractId);
        }

        /// <summary>
        /// Takes the most profitable contract the NPC can finish in time and afford, if any, and
        /// starts on it right away (before others buy the antiques it planned on).
        /// Returns what was spent.
        /// </summary>
        private float ConsiderNewContract(NpcBehaviorProfile profile, int currentDay)
        {
            if (_contractManager == null) return 0f;

            // NPCs have no reputation, so contracts that require some are the player's alone.
            var candidates = _contractManager.OpenContracts.Where(c => !Inventory.IsCommittedToContract(c.ContractId))
                .Concat(_contractManager.ExclusiveContracts.Where(c => c.CanBeClaimed))
                .Where(c => !c.RequiresReputation)
                .ToList();
            if (candidates.Count == 0) return 0f;

            // Cash still needed by contracts already taken (only those still running).
            var market = _economyManager.Market;
            float committedValue = Inventory.CommittedContractIds
                .Select(id => _contractManager.GetById(id))
                .Where(c => c != null && c.Status == ContractStatus.Active)
                .Sum(c => c.Requirement.AverageReferenceUnitPrice(market) *
                          Mathf.Max(0, c.Requirement.Quantity - Inventory.GetReservedForContract(c.ContractId).Count));
            float cashLimit = Inventory.Cash * ContractCashShare - committedValue;

            ContractPlan best = null;
            foreach (var contract in candidates)
            {
                var plan = PlanContract(contract, currentDay);
                if (!plan.IsComplete || plan.PurchaseCost > cashLimit) continue;

                float minProfit = contract.Type == ContractType.Exclusive ? contract.TotalReward * ExclusiveMinProfitShare : 0f;
                if (plan.Profit <= minProfit) continue;

                if (best == null || plan.Profit > best.Profit)
                    best = plan;
            }

            if (best == null)
            {
                Debug.Log($"{TraderName} found no contract it can finish in time and afford.");
                return 0f;
            }

            var chosen = best.Contract;
            if (chosen.Type == ContractType.Exclusive && !_contractManager.ClaimContract(chosen.ContractId, Id))
                return 0f;

            Inventory.CommitToContract(chosen.ContractId);
            Debug.Log($"{TraderName} picked up {chosen.Type} contract {chosen.ContractId} — needs {chosen.Requirement.Quantity}, due day {chosen.DeadlineDay}, expected profit {best.Profit:F0}.");

            float spent = ExecutePlan(best, currentDay);
            if (Inventory.GetReservedForContract(chosen.ContractId).Count >= chosen.Requirement.Quantity)
                TryFulfillContract(chosen);
            return spent;
        }


        private bool TryBuy(Antique listing, int currentDay)
        {
            if (!BuyListing(listing.Id)) return false;

            // PurchasePrice is the cost basis recorded by TraderInventory.Buy — price + shipping.
            _acquisitions[listing.Id] = new Acquisition { PurchasePrice = listing.PurchasePrice, Day = currentDay };
            return true;
        }

        private float EstimateTotalCost(Antique listing) =>
            TraderHelper.EstimateTotalCost(listing, _transportManager, NpcTransportOption);

        public bool BuyListing(string listingId) =>
            TraderHelper.BuyListing(Inventory, _economyManager.Market, listingId, TraderName, _economyManager.TimeManager.CurrentDay,
                _transportManager, NpcTransportOption);

        public bool SellListing(string listingId) =>
            TraderHelper.SellListing(Inventory, _economyManager.Market, listingId, TraderName, _economyManager.TimeManager.CurrentDay);

        /// <summary>
        /// The price is acceptable if it isn't more than the profile's multiplier over what the
        /// antique is worth in its condition (reference price of its type × condition). The
        /// listing's own price noise (PriceFactor) is what makes some offers bargains.
        /// </summary>
        private bool IsAcceptablePrice(Antique listing, NpcBehaviorProfile profile)
        {
            var typeState = _economyManager.Market.GetTypeState(listing.DefinitionId);
            float fairValue = PriceEngine.CalculateReferencePrice(listing.BasePrice, typeState) * listing.Condition;

            // Collectors (ConditionPremium > 0) stretch their limit for antiques in good condition.
            float multiplier = profile.MaxPriceMultiplierWillingToPay *
                               (1f + profile.ConditionPremium * Mathf.Max(0f, listing.Condition - 0.5f));
            return listing.SalePrice <= fairValue * multiplier;
        }

        private bool IsInterestedIn(AntiqueDefinition def, NpcBehaviorProfile profile)
        {
            if (def == null) return false;
            bool typeOk = profile.PreferredTypes.Count == 0 || profile.PreferredTypes.Contains(def.Type);
            bool countryOk = profile.PreferredCountries.Count == 0 || profile.PreferredCountries.Contains(def.Country);
            bool periodOk = profile.PreferredCenturies.Count == 0 || profile.PreferredCenturies.Contains(def.Century);
            return typeOk && countryOk && periodOk;
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
                CommittedContractIds = new List<string>(Inventory.CommittedContractIds)
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

            // CommittedContractIds is not restored here: the inventory restores its own commitments
            // when the economy is loaded (after the NPCs).
        }

    }
}