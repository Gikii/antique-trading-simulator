using UnityEngine;
using AntiqueTradingSimulator.Economy;

namespace AntiqueTradingSimulator.Trading
{
    /// <summary>
    /// Common base for anything that owns a TraderInventory and trades on the Market —
    /// the player and NPCs alike. Provides shared buy/sell plumbing so both agents
    /// go through the exact same code path; subclasses only decide WHEN to call it
    /// (NPC: daily decision logic, Player: UI clicks).
    /// </summary>
    public abstract class TraderAgent : MonoBehaviour
    {
        [SerializeField] protected string traderName = "Trader";
        [SerializeField] protected EconomyManager economyManager;
        [SerializeField] protected float startingCash = 1000f;

        public string TraderName => traderName;
        public TraderInventory Inventory { get; private set; }

        protected virtual void Awake()
        {
            Inventory = new TraderInventory(startingCash);

            if (economyManager == null)
                economyManager = FindFirstObjectByType<EconomyManager>();
        }

        public bool BuyListing(string listingId)
        {
            if (!HasMarket()) return false;

            bool success = Inventory.Buy(economyManager.Market, listingId);
            LogResult("buy", listingId, success);
            return success;
        }

        public bool SellListing(string listingId)
        {
            if (!HasMarket()) return false;

            bool success = Inventory.Sell(economyManager.Market, listingId);
            LogResult("sell", listingId, success);
            return success;
        }

        private bool HasMarket()
        {
            if (economyManager != null && economyManager.Market != null)
                return true;

            Debug.LogWarning($"{traderName}: no Market available yet.");
            return false;
        }

        private void LogResult(string action, string listingId, bool success)
        {
            if (success)
                Debug.Log($"{traderName} {action} succeeded — listing {listingId}. Cash: {Inventory.Cash:F2}");
            else
                Debug.Log($"{traderName} {action} failed — listing {listingId}.");
        }
    }
}