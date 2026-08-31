using UnityEngine;
using AntiqueTradingSimulator.Economy;

namespace AntiqueTradingSimulator.Agents
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

            if (string.IsNullOrWhiteSpace(traderName) || traderName == "Trader")
                traderName = gameObject.name;

            if (economyManager == null)
                economyManager = FindFirstObjectByType<EconomyManager>();
        }

        public bool BuyListing(string listingId)
        {
            var market = economyManager != null ? economyManager.Market : null;
            return TraderHelper.BuyListing(Inventory, market, listingId, traderName);
        }

        public bool SellListing(string listingId)
        {
            var market = economyManager != null ? economyManager.Market : null;
            return TraderHelper.SellListing(Inventory, market, listingId, traderName, economyManager.TimeManager.CurrentDay);
        }

    }
}