using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Events;
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
        public string Id { get; }
        public string ProfileId { get; }
        public string TraderName { get; }
        public TraderInventory Inventory { get; }

        private readonly EconomyManager _economyManager;
        private readonly ContractManager _contractManager;
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

        private readonly List<string> _committedContractIds = new();
        public IReadOnlyList<string> CommittedContractIds => _committedContractIds;

        public NPCTrader(string traderName, string profileId, float startingCash, EconomyManager economyManager, ContractManager contractManager = null)
        {
            Id = Guid.NewGuid().ToString("N");
            TraderName = traderName;
            ProfileId = profileId;
            Inventory = new TraderInventory(startingCash);
            _economyManager = economyManager;
            _contractManager = contractManager;
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
            ConsiderNewContract(profile);

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
                            if (!IsAcceptablePrice(listing, profile) || listing.CurrentPrice > budget) continue;

                            if (TryBuy(listing, currentDay)) budget -= listing.CurrentPrice;
                        }
                    }
                    else
                    {
                        var holdings = eventEffect.targetScope switch
                        {
                            TargetScope.AntiqueType => Inventory.Holdings.Values.Where(h => h.Definition.Type == eventEffect.AntiqueType && !h.IsReservedForContract).ToList(),
                            TargetScope.Country => Inventory.Holdings.Values.Where(h => h.Definition.Country == eventEffect.Country && !h.IsReservedForContract).ToList(),
                            TargetScope.Century => Inventory.Holdings.Values.Where(h => h.Definition.Century == eventEffect.Century && !h.IsReservedForContract).ToList()

                        };
                        foreach (var holding in holdings)
                        {
                            var typeState = _economyManager.Market.GetTypeState(holding.DefinitionId);
                            float sellPrice = PriceEngine.CalculatePrice(holding, typeState);
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
                if (!IsInterestedIn(listing.Definition, profile)) continue;
                if (!IsAcceptablePrice(listing, profile) || listing.CurrentPrice > budget) continue;

                if (TryBuy(listing, currentDay)) budget -= listing.CurrentPrice;
            }
        }

        private void ConsiderSellingHoldings(int currentDay, NpcBehaviorProfile profile)
        {
            Debug.Log($"{TraderName} considered selling holdings. Day {_economyManager.TimeManager.CurrentDay}");
            var listingIds = new List<string>(_acquisitions.Keys);
            foreach (var listingId in listingIds)
            {
                var listing = Inventory.GetHolding(listingId);
                if (listing == null) { _acquisitions.Remove(listingId); continue; }
                if (listing.IsReservedForContract) continue; // being gathered for a contract — not for sale

                var acquisition = _acquisitions[listingId];
                if (currentDay - acquisition.Day < profile.MinHoldingDaysBeforeSell) continue;

                var typeState = _economyManager.Market.GetTypeState(listing.DefinitionId);
                float estimatedValue = PriceEngine.CalculatePrice(listing, typeState);
                if (estimatedValue < acquisition.PurchasePrice * profile.ProfitTargetMultiplier) continue;

                if (SellListing(listing.Id)) _acquisitions.Remove(listingId);
            }
        }


        private void RemoveInactiveContracts()
        {
            if (_contractManager == null) return;

            foreach (var contractId in _committedContractIds.ToList())
            {
                var contract = _contractManager.GetById(contractId);
                if (contract != null && contract.Status == ContractStatus.Active) continue;

                Inventory.ReleaseAllReservationsForContract(contractId);
                _committedContractIds.Remove(contractId);
            }
        }

        /// <summary>
        /// Buys antiques for each contract
        /// </summary>
        private float PursueCommittedContracts(float budget, NpcBehaviorProfile profile, int currentDay)
        {
            var orderedContractIds = Inventory.CommittedContractIds
                .OrderBy(id => _contractManager.GetById(id)?.DeadlineDay ?? int.MaxValue)
                .ToList();

            foreach (var contractId in orderedContractIds)
            {
                var contract = _contractManager.GetById(contractId);
                if (contract == null || contract.Status != ContractStatus.Active) continue;

                int needed = contract.Requirement.Quantity - Inventory.GetReservedForContract(contractId).Count;

                if (needed > 0 && budget > 0f)
                {
                    var candidates = _economyManager.Market.Listings.Where(l => contract.Requirement.IsSatisfiedBy(l)).ToList();
                    foreach (var listing in candidates)
                    {
                        if (needed <= 0 || budget <= 0f) break;
                        if (!IsAcceptablePrice(listing, profile) || listing.CurrentPrice > budget) continue;

                        if (TryBuy(listing, currentDay))
                        {
                            budget -= listing.CurrentPrice;
                            Inventory.ReserveForContract(listing.Id, contractId);
                            needed--;
                        }
                    }
                }

                if (Inventory.GetReservedForContract(contractId).Count >= contract.Requirement.Quantity)
                    TryFulfillContract(contract);
            }

            return budget;
        }

        private void TryFulfillContract(Contract contract)
        {
            var listingIds = Inventory.GetReservedForContract(contract.ContractId)
                .Take(contract.Requirement.Quantity)
                .Select(a => a.Id)
                .ToList();

            if (listingIds.Count < contract.Requirement.Quantity) return;

            if (_contractManager.FulfillContract(contract.ContractId, Id, Inventory, listingIds))
                Inventory.ReleaseCommittedContract(contract.ContractId);
        }

        private void ConsiderNewContract(NpcBehaviorProfile profile)
        {
            if (_contractManager == null) return;

            var candidates = _contractManager.OpenContracts.Where(c => !Inventory.IsCommittedToContract(c.ContractId))
                .Concat(_contractManager.ExclusiveContracts.Where(c => c.CanBeClaimed))
                .ToList();
            if (candidates.Count == 0) return;

            var market = _economyManager.Market;
            float cashLimit = Inventory.Cash * 0.5f;

            float committedValue = Inventory.CommittedContractIds
                .Select(id => _contractManager.GetById(id))
                .Where(c => c != null)
                .Sum(c => c.ReferenceValue(market));

            var affordable = candidates.Where(c => committedValue + c.ReferenceValue(market) <= cashLimit).ToList();
            if (affordable.Count == 0)
            {
                Debug.Log($"{TraderName} found no contract affordable without exceeding half of {Inventory.Cash:F2} cash ({committedValue:F2} already committed).");
                return;
            }

            var candidate = affordable[UnityEngine.Random.Range(0, affordable.Count)];

            if (candidate.Type == ContractType.Exclusive && !_contractManager.ClaimContract(candidate.ContractId, Id))
                return;

            Inventory.CommitToContract(candidate.ContractId);
            Debug.Log($"{TraderName} picked up {candidate.Type} contract {candidate.ContractId} — needs {candidate.Requirement.Quantity}, due day {candidate.DeadlineDay}.");
        }


        private bool TryBuy(Antique listing, int currentDay)
        {
            float price = listing.CurrentPrice;
            if (!BuyListing(listing.Id)) return false;

            _acquisitions[listing.Id] = new Acquisition { PurchasePrice = price, Day = currentDay };
            return true;
        }

        public bool BuyListing(string listingId) =>
            TraderHelper.BuyListing(Inventory, _economyManager.Market, listingId, TraderName);

        public bool SellListing(string listingId) =>
            TraderHelper.SellListing(Inventory, _economyManager.Market, listingId, TraderName, _economyManager.TimeManager.CurrentDay);

        private bool IsAcceptablePrice(Antique listing, NpcBehaviorProfile profile)
        {
            var typeState = _economyManager.Market.GetTypeState(listing.DefinitionId);
            float referencePrice = PriceEngine.CalculateReferencePrice(listing.BasePrice, typeState);
            return listing.CurrentPrice <= referencePrice * profile.MaxPriceMultiplierWillingToPay;
        }

        private bool IsInterestedIn(AntiqueDefinition def, NpcBehaviorProfile profile)
        {
            if (def == null) return false;
            bool typeOk = profile.PreferredTypes.Count == 0 || profile.PreferredTypes.Contains(def.Type);
            bool countryOk = profile.PreferredCountries.Count == 0 || profile.PreferredCountries.Contains(def.Country);
            bool periodOk = profile.PreferredCenturies.Count == 0 || profile.PreferredCenturies.Contains(def.Century);
            return typeOk && countryOk && periodOk;
        }
    }
}