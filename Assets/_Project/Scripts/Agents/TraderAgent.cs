using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Events;
using AntiqueTradingSimulator.Logistics;
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
    public abstract class TraderAgent : MonoBehaviour, IInformationReceiver
    {
        [SerializeField] protected string traderName = "Trader";
        [SerializeField] protected EconomyManager economyManager;
        [SerializeField] protected TransportManager transportManager;
        [SerializeField] protected float startingCash = 1000f;
        [Tooltip("Which news this trader receives. Ignored by PlayerTrader — the player's access " +
                 "comes from the Information Network upgrade (CompanyManager).")]
        [SerializeField] protected InfoAccessLevel accessLevel = InfoAccessLevel.LocalPress;

        public string TraderName => traderName;
        public TraderInventory Inventory { get; private set; }
        public virtual InfoAccessLevel AccessLevel => accessLevel;

        public virtual string OwnerId => traderName;

        protected virtual void Awake()
        {
            Inventory = new TraderInventory(startingCash);

            if (string.IsNullOrWhiteSpace(traderName) || traderName == "Trader")
                traderName = gameObject.name;

            if (economyManager == null)
                economyManager = FindFirstObjectByType<EconomyManager>();

            if (transportManager == null)
                transportManager = FindFirstObjectByType<TransportManager>();

            // Lets the market re-value this trader's holdings together with its own listings.
            if (economyManager != null)
                economyManager.RegisterInventory(Inventory, OwnerId);
        }

        protected virtual void OnDestroy()
        {
            if (economyManager != null)
                economyManager.UnregisterInventory(Inventory);
        }

        public bool BuyListing(string listingId) => BuyListing(listingId, TransportOption.Standard);

        public bool BuyListing(string listingId, TransportOption option)
        {
            var market = economyManager != null ? economyManager.Market : null;
            int currentDay = economyManager != null ? economyManager.TimeManager.CurrentDay : -1;
            return TraderHelper.BuyListing(Inventory, market, listingId, traderName, currentDay, transportManager, option);
        }

        /// <summary>Shipping price/time for a market listing — null if there's no such listing or no TransportManager.</summary>
        public TransportQuote QuoteTransport(string listingId, TransportOption option)
        {
            var market = economyManager != null ? economyManager.Market : null;
            var listing = market?.GetById(listingId);
            return listing != null && transportManager != null ? transportManager.Quote(listing, option, buyer: Inventory) : null;
        }

        public bool ListOnMarket(string listingId, float askingPrice)
        {
            var market = economyManager != null ? economyManager.Market : null;
            int currentDay = economyManager != null ? economyManager.TimeManager.CurrentDay : 0;
            return Inventory.ListForSale(market, listingId, askingPrice, currentDay);
        }

        public bool CancelMarketListing(string listingId)
        {
            var market = economyManager != null ? economyManager.Market : null;
            return Inventory.CancelListing(market, listingId);
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