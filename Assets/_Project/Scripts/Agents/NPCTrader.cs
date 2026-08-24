using System.Collections.Generic;
using UnityEngine;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Market;

namespace AntiqueTradingSimulator.Agents
{
    /// <summary>
    /// Minimal autonomous trader. Once per day it picks one random listing from the market
    /// and one random listing from its own inventory, and if the price looks favourable,
    /// buys/sells through the shared TraderAgent plumbing.
    /// </summary>
    public class NPCTrader : TraderAgent
    {
        [SerializeField] private TimeManager timeManager;

        [Header("Behaviour")]
        [SerializeField, Range(0f, 1f)] private float buyChance = 1.0f;
        [SerializeField, Range(0f, 1f)] private float sellChance = 1.0f;

        [SerializeField] private float buyBelowPriceRatio = 0.9f;
        [SerializeField] private float sellAbovePriceRatio = 1.1f;

        protected override void Awake()
        {
            base.Awake();
            if (timeManager == null) timeManager = FindFirstObjectByType<TimeManager>();
        }

        void OnEnable()
        {
            if (timeManager != null)
                timeManager.OnDayChanged += HandleDayChanged;
        }

        void OnDisable()
        {
            if (timeManager != null)
                timeManager.OnDayChanged -= HandleDayChanged;
        }

        private void HandleDayChanged(int newDay)
        {
            if (economyManager == null || economyManager.Market == null) return;

            if (Random.value <= buyChance)
            {
                var listings = economyManager.Market.Listings;
                if (listings.Count > 0)
                {
                    var listing = listings[Random.Range(0, listings.Count)];
                    float priceRatio = listing.BasePrice > 0f ? listing.CurrentPrice / listing.BasePrice : 1f;

                    if (priceRatio <= buyBelowPriceRatio)
                        BuyListing(listing.Id);
                }
            }

            if (Random.value <= sellChance)
            {
                var owned = new List<Antique>(Inventory.Holdings.Values);
                if (owned.Count > 0)
                {
                    var listing = owned[Random.Range(0, owned.Count)];
                    float priceRatio = listing.BasePrice > 0f ? listing.CurrentPrice / listing.BasePrice : 1f;
                    //if (priceRatio >= sellAbovePriceRatio)
                    if(true)
                    {
                        SellListing(listing.Id);
                    }

                }
            }
        }
    }
}