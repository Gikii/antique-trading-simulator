using AntiqueTradingSimulator.Market;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.Economy
{
    /// <summary>
    /// Tracks a trader's (player or NPC) cash and antique holdings, and wraps
    /// Market.Buy/Sell so cash and inventory only ever change together with a successful trade.
    /// Holdings are keyed by ListingId rather than definition Id, since each owned antique
    /// is a distinct individual item with its own Condition/price.
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
            //var result = new List<Antique>();
            //foreach (var listing in _holdings.Values)
            //{
            //    if (listing.DefinitionId == definitionId)
            //        result.Add(listing);
            //}
            //return result;
            return _holdings.Values.Where(h => h.DefinitionId == definitionId).ToList();
        }

        public List<Antique> GetByType(AntiqueType type)
        {
            return _holdings.Values.Where(l => l.Type == type).ToList();
        }

        public List<Antique> GetByCentury(Century century)
        {
            return _holdings.Values.Where(l => l.Century == century).ToList();
        }

        public List<Antique> GetByCountry(Country country)
        {
            return _holdings.Values.Where(l => l.Country == country).ToList();
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

        public bool Sell(Market.Market market, string listingId, int currentDay)
        {
            if (!_holdings.TryGetValue(listingId, out var listing)) return false;

            if (listing.IsReservedForContract)
            {
                Debug.LogWarning($"TraderInventory: refused to sell {listingId} — reserved for contract {listing.ReservedForContractId}.");
                return false;
            }

            _holdings.Remove(listingId);
            market.Sell(listing, currentDay);
            Cash += listing.CurrentPrice;

            OnCashChanged?.Invoke(Cash);
            OnHoldingChanged?.Invoke(listingId, null);
            return true;
        }

        public bool RemoveHolding(string listingId)
        {
            if (!_holdings.Remove(listingId)) return false;

            OnHoldingChanged?.Invoke(listingId, null);
            return true;

        }

        /// <summary>
        /// Adds cash not tied to a market transaction — e.g. a contract reward payout.
        /// </summary>
        public void AddCash(float amount)
        {
            if (amount <= 0f) return;

            Cash += amount;
            OnCashChanged?.Invoke(Cash);
        }

        /// <summary>
        /// Deducts cash not tied to a market transaction — e.g. a contract penalty.
        /// Clamped so Cash never goes negative; logs if the full amount couldn't be taken.
        /// </summary>
        public void RemoveCash(float amount)
        {
            if (amount <= 0f) return;

            if (amount > Cash)
                Debug.LogWarning($"TraderInventory: tried to deduct {amount:F2} but only {Cash:F2} cash available — clamping to 0.");

            Cash = Mathf.Max(0f, Cash - amount);
            OnCashChanged?.Invoke(Cash);
        }

        /// <summary>
        /// Tags an owned listing as being gathered for a specific contract, so normal
        /// selling logic (NPC or player) leaves it alone. Fails if the listing isn't
        /// owned or is already reserved for something else.
        /// </summary>
        public bool ReserveForContract(string listingId, string contractId)
        {
            if (string.IsNullOrEmpty(contractId)) return false;
            if (!_holdings.TryGetValue(listingId, out var listing)) return false;
            if (listing.IsReservedForContract) return false;

            listing.ReservedForContractId = contractId;
            OnHoldingChanged?.Invoke(listingId, listing);
            return true;
        }

        public void ReleaseReservation(string listingId)
        {
            if (!_holdings.TryGetValue(listingId, out var listing)) return;
            if (!listing.IsReservedForContract) return;

            listing.ReservedForContractId = null;
            OnHoldingChanged?.Invoke(listingId, listing);
        }

        public void ReleaseAllReservationsForContract(string contractId)
        {
            foreach (var listing in _holdings.Values)
            {
                if (listing.ReservedForContractId == contractId)
                    listing.ReservedForContractId = null;
            }
        }

        public List<Antique> GetReservedForContract(string contractId)
        {
            return _holdings.Values.Where(h => h.ReservedForContractId == contractId).ToList();
        }

        public List<Antique> GetUnreservedHoldings()
        {
            return _holdings.Values.Where(h => !h.IsReservedForContract).ToList();
        }
    }
}