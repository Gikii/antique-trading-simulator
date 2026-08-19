using System.Collections.Generic;
using UnityEngine;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Market;

namespace AntiqueTradingSimulator.NPC
{
    /// <summary>
    /// Minimal autonomous trader. OOnce per day it picks one random antique from market and one from their inventory.
    /// If the prices are favourable it buys/sells the antiques.
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
        [SerializeField] private float tradeAmount = 1f;

        [SerializeField] private float buyBelowPriceRatio = 0.9f;
        [SerializeField] private float sellAbovePriceRatio = 1.1f;

        //public float Cash { get; private set; }

        //private readonly Dictionary<string, float> _inventory = new Dictionary<string, float>();

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
                var antiques = economyManager.Market.Antiques;
                if (antiques.Count == 0) return;

                var antique = antiques[Random.Range(0, antiques.Count)];
                float priceRatio = antique.BasePrice > 0f ? antique.CurrentPrice / antique.BasePrice : 1f;

                if (priceRatio <= buyBelowPriceRatio)
                {
                    if(inventory.Buy(economyManager.Market, antique.Id))
                        Debug.Log($"{npcName} bought {tradeAmount:F1} x {antique.Name} at {antique.CurrentPrice:F2} (cash: {inventory.Cash:F2})");
                }
            }
            if (Random.value <= sellChance)
            {
                var ownedIds = new List<string>(inventory.Holdings.Keys);
                string randomId = ownedIds[Random.Range(0, ownedIds.Count)];
                var antique = economyManager.Market.GetById(randomId);

                if (antique != null)
                {
                    float priceRatio = antique.BasePrice > 0f ? antique.CurrentPrice / antique.BasePrice : 1f;
                    if (priceRatio >= sellAbovePriceRatio)
                    {
                        if(inventory.Sell(economyManager.Market, randomId))
                            Debug.Log($"{npcName} sold {tradeAmount:F1} x {antique.Name} at {antique.CurrentPrice:F2} (cash: {inventory.Cash:F2})");

                    }
                }
            }
        }
        /*
        private void TryBuy(Antique antique)
        {
            float cost = antique.CurrentPrice * tradeAmount;
            if (cost > Cash) return;

            if (economyManager.Market.Buy(antique.Id, tradeAmount))
            {
                Cash -= cost;

                _inventory.TryGetValue(antique.Id, out float owned);
                _inventory[antique.Id] = owned + tradeAmount;

                Debug.Log($"{npcName} bought {tradeAmount:F1} x {antique.Name} at {antique.CurrentPrice:F2} (cash: {Cash:F2})");
            }
        }

        private void TrySell(Antique antique)
        {
            _inventory.TryGetValue(antique.Id, out float owned);
            if (owned < tradeAmount) return;

            economyManager.Market.Sell(antique.Id, tradeAmount);
            Cash += antique.CurrentPrice * tradeAmount;
            _inventory[antique.Id] = owned - tradeAmount;

            Debug.Log($"{npcName} sold {tradeAmount:F1} x {antique.Name} at {antique.CurrentPrice:F2} (cash: {Cash:F2})");
        }*/
    }
}
