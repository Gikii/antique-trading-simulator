using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Events;
using AntiqueTradingSimulator.News;
using UnityEngine;

namespace AntiqueTradingSimulator.Agents
{
    /// <summary>
    /// Common base for anything that owns a TraderInventory and trades on the Market —
    /// the player and NPCs alike. Delegates the actual buy/sell logic to TraderHelper
    /// so both agents go through the exact same code path; subclasses only decide WHEN
    /// to call it (NPC: daily decision logic, Player: UI clicks).
    /// Also implements IInformationReceiver so both Player and NPCs can be targeted by NewsManager
    /// </summary>
    public abstract class TraderAgent : MonoBehaviour
    {
        [SerializeField] protected string traderName = "Trader";
        [SerializeField] protected EconomyManager economyManager;
        [SerializeField] protected float startingCash = 1000f;
        [SerializeField] protected InfoAccessLevel accessLevel = InfoAccessLevel.LocalPress;

        public string TraderName => traderName;
        public TraderInventory Inventory { get; private set; }
        public InfoAccessLevel AccessLevel => accessLevel;

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
            int currentDay = economyManager != null ? economyManager.TimeManager.CurrentDay : 0;
            return TraderHelper.SellListing(Inventory, market, listingId, traderName, currentDay);
        }

        // Default: do nothing. NPCTrader overrides this with actual decision logic;
        // PlayerTrader can override it later to push a UI notification instead.
        public virtual void ReceiveNews(NewsItem news)
        {
        }
    }
}