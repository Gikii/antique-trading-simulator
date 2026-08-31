using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Market;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AntiqueTradingSimulator.Agents
{
    /// <summary>
    /// Minimal autonomous trader. Once per day it picks one random listing from the market
    /// and one random listing from its own inventory, and if the price looks favourable,
    /// buys/sells through the shared TraderAgent plumbing.
    /// </summary>
    [Serializable]
    public class NPCTrader
    {
        public string Id { get; private set; }
        public string TraderName { get; private set; }
        public TraderInventory Inventory { get; private set; }
        public EconomyManager economyManager;

        [Header("Behaviour")]
        [Range(0f, 1f)] public float BuyChance = 1f;
        [Range(0f, 1f)] public float SellChance = 1f;
        public float BuyBelowPriceRatio = 0.9f;
        public float SellAbovePriceRatio = 1.1f;

        public NPCTrader(string traderName, float startingCash, EconomyManager economyManager)
        {
            Id = Guid.NewGuid().ToString("N");
            TraderName = traderName;
            Inventory = new TraderInventory(startingCash);
            this.economyManager = economyManager;
        }


        public void DecideTrade(Market.Market market)
        {
            if (market == null) return;

            if (UnityEngine.Random.value <= SellChance)
            {
                var owned = new List<Antique>(Inventory.Holdings.Values);
                if (owned.Count > 0)
                {
                    var listing = owned[UnityEngine.Random.Range(0, owned.Count)];
                    float priceRatio = listing.BasePrice > 0f ? listing.CurrentPrice / listing.BasePrice : 1f;
                    //if (priceRatio >= SellAbovePriceRatio)
                    if (true)
                    {
                        SellListing(market, listing.Id);
                    }

                }
            }

            if (UnityEngine.Random.value <= BuyChance)
            {
                var listings = market.Listings;
                if (listings.Count > 0)
                {
                    var listing = listings[UnityEngine.Random.Range(0, listings.Count)];
                    float priceRatio = listing.BasePrice > 0f ? listing.CurrentPrice / listing.BasePrice : 1f;

                    if (priceRatio <= BuyBelowPriceRatio)
                        BuyListing(market, listing.Id);
                }
            }
        }
        public bool BuyListing(Market.Market market, string listingId)
        {
            return TraderHelper.BuyListing(Inventory, market, listingId, TraderName);
        }

        public bool SellListing(Market.Market market, string listingId)
        {
            return TraderHelper.SellListing(Inventory, market, listingId, TraderName, economyManager.TimeManager.CurrentDay);
        }

    }
}