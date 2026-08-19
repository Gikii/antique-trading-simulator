using System.Collections.Generic;
using UnityEngine;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Market;

namespace AntiqueTradingSimulator.NPC
{
    /// <summary>
    /// Minimal autonomous trader. Once per day it picks one random listing from the market
    /// and one random listing from its own inventory. If the prices are favourable it buys/sells.
    /// </summary>
    public class NPCTrader : MonoBehaviour
    {
        [SerializeField] private string npcName = "NPC Trader";
        [SerializeField] private EconomyManager economyManager;
        [SerializeField] private TimeManager timeManager;

        [Header("Behaviour")]
        [SerializeField] private float startingCash = 1000f;
        [SerializeField, Range(0f, 1f)] private float buyChance = 1.0f;
        [SerializeField, Range(0f, 1f)] private float sellChance = 1.0f;

        [SerializeField] private float buyBelowPriceRatio = 0.9f;
        [SerializeField] private float sellAbovePriceRatio = 1.1f;

        private TraderInventory inventory;

        void Awake()
        {
            inventory = new TraderInventory(startingCash);

            if (economyManager == null) economyManager = FindFirstObjectByType<EconomyManager>();
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
                    {
                        if (inventory.Buy(economyManager.Market, listing.Id))
                            Debug.Log($"{npcName} bought {listing.Name} (Q:{listing.Quality:F2} S:{listing.State:F2}) at {listing.CurrentPrice:F2} (cash: {inventory.Cash:F2})");
                    }
                }
            }

            if (Random.value <= sellChance)
            {
                var owned = new List<Antique>(inventory.Holdings.Values);
                if (owned.Count > 0)
                {
                    var listing = owned[Random.Range(0, owned.Count)];
                    float priceRatio = listing.BasePrice > 0f ? listing.CurrentPrice / listing.BasePrice : 1f;

                    //if (priceRatio >= sellAbovePriceRatio)
                    if(true)
                    {
                        if (inventory.Sell(economyManager.Market, listing.Id))
                            Debug.Log($"{npcName} sold {listing.Name} at {listing.CurrentPrice:F2} (cash: {inventory.Cash:F2})");
                    }
                }
            }
        }
    }
}
