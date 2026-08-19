using System;
using System.Collections.Generic;
using AntiqueTradingSimulator.Market;

namespace AntiqueTradingSimulator.Economy
{
    /// <summary>
    /// Tracks a trader's (player or NPC) cash and antique holdings, and wraps
    /// Market.Buy/Sell so cash and inventory only ever change together with a successful trade.
    /// Holdings are keyed by ListingId rather than definition Id, since each owned antique
    /// is a distinct individual item with its own Quality/State/price.
    /// </summary>
    [Serializable]
    public class TraderInventory
    {
        public float Cash { get; private set; }

        private readonly Dictionary<string, Antique> _holdings = new Dictionary<string, Antique>();
        public IReadOnlyDictionary<string, Antique> Holdings => _holdings;

        public event Action<float> OnCashChanged;
        public event Action<string, Antique> OnHoldingChanged; // (listingId, listing — null if it was just removed)

        public TraderInventory(float startingCash = 0f)
        {
            Cash = startingCash;
        }

        public bool Owns(string listingId) => _holdings.ContainsKey(listingId);

        public Antique GetHolding(string listingId)
        {
            _holdings.TryGetValue(listingId, out var listing);
            return listing;
        }

        /// <summary>
        /// Returns every individually-owned listing that matches a given antique definition
        /// (e.g. all the "Chinese Vase" items this trader owns, each with its own quality/state).
        /// </summary>
        public List<Antique> GetHoldingsByDefinition(string definitionId)
        {
            var result = new List<Antique>();
            foreach (var listing in _holdings.Values)
            {
                if (listing.DefinitionId == definitionId)
                    result.Add(listing);
            }
            return result;
        }

        public bool Buy(Market.Market market, string listingId)
        {
            var listing = market.GetById(listingId);
            if (listing == null) return false;

            float cost = listing.CurrentPrice;
            if (cost > Cash) return false;

            if (!market.Buy(listingId)) return false;

            Cash -= cost;
            _holdings[listing.Id] = listing;

            OnCashChanged?.Invoke(Cash);
            OnHoldingChanged?.Invoke(listing.Id, listing);
            return true;
        }

        public bool Sell(Market.Market market, string listingId)
        {
            if (!_holdings.TryGetValue(listingId, out var listing)) return false;

            _holdings.Remove(listingId);
            market.Sell(listing);

            Cash += listing.CurrentPrice;

            OnCashChanged?.Invoke(Cash);
            OnHoldingChanged?.Invoke(listingId, null);
            return true;
        }
    }
}
