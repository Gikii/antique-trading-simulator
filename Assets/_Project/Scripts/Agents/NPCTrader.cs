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
        private NpcBehaviorProfile _profileCache;
        public NpcBehaviorProfile Profile => _profileCache ??= NpcProfileDatabase.GetById(ProfileId);

        public InfoAccessLevel AccessLevel => Profile != null ? Profile.AccessLevel : InfoAccessLevel.LocalPress;

        private struct PendingReaction { public NewsItem News; public int TriggerDay; }
        private struct Acquisition { public float PurchasePrice; public int Day; }

        private readonly List<PendingReaction> _pendingReactions = new();
        private readonly Dictionary<string, Acquisition> _acquisitions = new();

        public NPCTrader(string traderName, string profileId, float startingCash, EconomyManager economyManager)
        {
            Id = Guid.NewGuid().ToString("N");
            TraderName = traderName;
            ProfileId = profileId;
            Inventory = new TraderInventory(startingCash);
            _economyManager = economyManager;
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
            if (UnityEngine.Random.value > actionChance) return;

            int delay = UnityEngine.Random.Range(profile.MinReactionDelayDays, profile.MaxReactionDelayDays + 1);
            _pendingReactions.Add(new PendingReaction
            {
                News = news,
                TriggerDay = _economyManager.TimeManager.CurrentDay + delay
            });
        }

        public void EvaluateDay(int currentDay)
        {
            var profile = Profile;
            if (profile == null) return;

            float budget = Inventory.Cash * profile.DailyBudgetFraction;
            budget = ProcessPendingReactions(currentDay, budget, profile);
            ConsiderSellingHoldings(currentDay, profile);
            ConsiderBuyingFromMarket(budget, profile);
        }

        private float ProcessPendingReactions(int currentDay, float budget, NpcBehaviorProfile profile)
        {
            for (int i = _pendingReactions.Count - 1; i >= 0; i--)
            {
                var pending = _pendingReactions[i];
                if (pending.TriggerDay > currentDay) continue;

                budget = TryActOnNews(pending.News, budget, currentDay, profile);
                _pendingReactions.RemoveAt(i);
            }
            return budget;
        }

        private float TryActOnNews(NewsItem news, float budget, int currentDay, NpcBehaviorProfile profile)
        {
            foreach (var eventEffect in news.NewsData)
            {
                if (eventEffect.targetScope != EventEffect.TargetScope.Other)
                {
                    if (eventEffect.affectsPriceUp) {
                        var listings = eventEffect.targetScope switch
                        {
                            TargetScope.AntiqueType => _economyManager.Market.GetByType(eventEffect.AntiqueType),
                            TargetScope.Country => _economyManager.Market.GetByCountry(eventEffect.Country),
                            TargetScope.TimePeriod => _economyManager.Market.GetByTimePeriod(eventEffect.TimePeriod),
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
                            TargetScope.AntiqueType => Inventory.Holdings.Values.Where(h => h.Definition.Type == eventEffect.AntiqueType).ToList(),
                            TargetScope.Country => Inventory.Holdings.Values.Where(h => h.Definition.Country == eventEffect.Country).ToList(),
                            TargetScope.TimePeriod => Inventory.Holdings.Values.Where(h => h.Definition.TimePeriod == eventEffect.TimePeriod).ToList()

                        };
                        foreach (var holding in holdings) {
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
            if (budget <= 0f) return;
            int currentDay = _economyManager.TimeManager.CurrentDay;

            foreach (var listing in _economyManager.Market.Listings)
            {
                if (budget <= 0f) break;
                if (!IsInterestedIn(listing.Definition, profile)) continue;
                if (!IsAcceptablePrice(listing, profile) || listing.CurrentPrice > budget) continue;

                if (TryBuy(listing, currentDay)) budget -= listing.CurrentPrice;
            }

            Debug.Log(TraderName + " considered buying from market");
        }

        private void ConsiderSellingHoldings(int currentDay, NpcBehaviorProfile profile)
        {
            var listingIds = new List<string>(_acquisitions.Keys);
            foreach (var listingId in listingIds)
            {
                var listing = Inventory.GetHolding(listingId);
                if (listing == null) { _acquisitions.Remove(listingId); continue; }

                var acquisition = _acquisitions[listingId];
                if (currentDay - acquisition.Day < profile.MinHoldingDaysBeforeSell) continue;

                var typeState = _economyManager.Market.GetTypeState(listing.DefinitionId);
                float estimatedValue = PriceEngine.CalculatePrice(listing, typeState);
                if (estimatedValue < acquisition.PurchasePrice * profile.ProfitTargetMultiplier) continue;

                if (SellListing(listing.Id)) _acquisitions.Remove(listingId);
            }

            Debug.Log(TraderName + " considered selling holdings");
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
            bool periodOk = profile.PreferredPeriods.Count == 0 || profile.PreferredPeriods.Contains(def.TimePeriod);
            return typeOk && countryOk && periodOk;
        }
    }
}