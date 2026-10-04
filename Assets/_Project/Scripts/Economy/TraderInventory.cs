using AntiqueTradingSimulator.Logistics;
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

        // Fired after the market re-values the holdings (daily tick, events), so UI
        // showing collection value can refresh without polling.
        public event Action OnHoldingsRevalued;

        // (antique, proceeds) — an antique this trader listed on the market was bought.
        public event Action<Antique, float> OnListingSold;

        // Daily snapshot of the total market value of all holdings — feeds the
        // "collection value over time" chart. Recorded by Market.RecordDailyPrices.
        private const int MaxValueHistoryDays = 90;
        private readonly List<PricePoint> _valueHistory = new List<PricePoint>();
        public IReadOnlyList<PricePoint> ValueHistory => _valueHistory;

        public float TotalHoldingsValue => _holdings.Values.Sum(h => h.CurrentPrice);

        /// <summary>
        /// Total worth of this trader: cash plus the market value of every owned antique
        /// (including items in transit and items listed on the market, at market value).
        /// </summary>
        public float Wealth => Cash + TotalHoldingsValue;

        // ---------------------------------------------------------------- warehouse

        // Null = unlimited storage (NPCs). The player gets one from PlayerTrader.
        public Warehouse Warehouse { get; private set; }

        /// <summary>Fired when the warehouse is attached or upgraded.</summary>
        public event Action OnWarehouseChanged;

        /// <summary>(amount charged) — daily warehouse upkeep was deducted.</summary>
        public event Action<float> OnWarehouseUpkeepCharged;

        // Every owned antique takes a slot: stored, in transit (space reserved at
        // purchase) and listed on the market (still physically in the warehouse).
        public int UsedSlots => _holdings.Count;
        public bool HasWarehouseLimit => Warehouse != null;
        public int Capacity => Warehouse != null ? Warehouse.Capacity : int.MaxValue;
        public int FreeSlots => Mathf.Max(0, Capacity - UsedSlots);
        public bool HasFreeSlot => UsedSlots < Capacity;

        // Possible after an event reward overflows the warehouse — buying is blocked
        // until enough items are sold.
        public bool IsOverCapacity => UsedSlots > Capacity;

        public void AttachWarehouse(Warehouse warehouse)
        {
            Warehouse = warehouse;
            OnWarehouseChanged?.Invoke();
        }

        public bool TryUpgradeWarehouseCapacity()
        {
            if (Warehouse == null || !Warehouse.CanUpgradeCapacity) return false;

            float cost = Warehouse.NextCapacityUpgradeCost;
            if (cost > Cash) return false;

            RemoveCash(cost);
            Warehouse.RaiseCapacityLevel();
            OnWarehouseChanged?.Invoke();
            return true;
        }

        public bool TryUpgradeWarehouseSecurity()
        {
            if (Warehouse == null || !Warehouse.CanUpgradeSecurity) return false;

            float cost = Warehouse.NextSecurityUpgradeCost;
            if (cost > Cash) return false;

            RemoveCash(cost);
            Warehouse.RaiseSecurityLevel();
            OnWarehouseChanged?.Invoke();
            return true;
        }

        /// <summary>
        /// Deducts one day of warehouse upkeep. Returns the amount that was due.
        /// Cash is clamped at 0 for now (no debt mechanic yet).
        /// </summary>
        public float ChargeWarehouseUpkeep()
        {
            if (Warehouse == null) return 0f;

            float upkeep = Warehouse.CalculateDailyUpkeep(UsedSlots);
            if (upkeep <= 0f) return 0f;

            RemoveCash(upkeep);
            OnWarehouseUpkeepCharged?.Invoke(upkeep);
            return upkeep;
        }

        public List<Antique> GetAvailableHoldings() => _holdings.Values.Where(h => h.IsAvailable).ToList();

        public List<Antique> GetInTransitHoldings() => _holdings.Values.Where(h => h.IsInTransit).ToList();

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

        /// <summary>
        /// Adds an antique straight into the holdings, bypassing the Market entirely — e.g. an
        /// event reward (a financed expedition's finds), not a purchase off a listing. The antique
        /// is never added to Market.Listings, so it never appears for sale and its
        /// MarketListedOnDay stays -1 until the owner lists it themselves.
        /// </summary>
        /// <param name="transport">If given, the antique arrives via TransportManager (InTransit until
        /// the quote's ArrivalDay); the caller dispatches the shipment. Grants ignore warehouse
        /// capacity on purpose — a reward may overflow the warehouse (see IsOverCapacity).</param>
        public bool GrantHolding(Antique antique, int currentDay = -1, TransportQuote transport = null)
        {
            if (antique == null) return false;

            // Free acquisition — cost basis 0, but remember when it arrived.
            if (currentDay >= 0 && !antique.HasPurchaseRecord)
                antique.RecordAcquisition(0f, currentDay);

            if (transport != null)
                antique.MarkInTransit(transport.ArrivalDay);

            _holdings[antique.Id] = antique;

            OnHoldingChanged?.Invoke(antique.Id, antique);
            return true;
        }

        /// <summary>
        /// Buys a listing off the market. With a transport quote the shipping cost is paid
        /// together with the price and the antique enters the holdings as InTransit — the
        /// caller then hands the same quote to TransportManager.Dispatch. Without a quote
        /// (no TransportManager in the scene) the antique is available immediately.
        /// </summary>
        public bool Buy(Market.Market market, string listingId, int currentDay = -1, TransportQuote transport = null)
        {
            var listing = market.GetById(listingId);
            if (listing == null) return false;

            // Can't buy your own listing — cancel it instead.
            if (Owns(listingId)) return false;

            if (!HasFreeSlot)
            {
                Debug.Log($"TraderInventory: cannot buy {listing.Name} — warehouse full ({UsedSlots}/{Capacity}).");
                return false;
            }

            float price = listing.SalePrice;
            float shippingCost = transport != null ? transport.Cost : 0f;
            float cost = price + shippingCost;
            if (cost > Cash) return false;

            if (!market.Buy(listingId)) return false;

            Cash -= cost;
            // Cost basis includes shipping, so "profit" everywhere (UI, NPC sell targets)
            // is already net of transport — as the design doc requires.
            listing.RecordAcquisition(cost, currentDay);
            listing.TransportCost = shippingCost;
            if (transport != null)
                listing.MarkInTransit(transport.ArrivalDay);
            _holdings[listing.Id] = listing;

            OnCashChanged?.Invoke(Cash);
            OnHoldingChanged?.Invoke(listing.Id, listing);
            return true;
        }

        public bool Sell(Market.Market market, string listingId, int currentDay)
        {
            if (!_holdings.TryGetValue(listingId, out var listing)) return false;

            if (listing.IsInTransit)
            {
                Debug.LogWarning($"TraderInventory: refused to sell {listingId} — still in transit (arrives day {listing.ArrivalDay}).");
                return false;
            }

            if (listing.IsReservedForContract)
            {
                Debug.LogWarning($"TraderInventory: refused to sell {listingId} — reserved for contract {listing.ReservedForContractId}.");
                return false;
            }

            if (listing.IsListedForSale)
            {
                Debug.LogWarning($"TraderInventory: refused to sell {listingId} — it is listed on the market; cancel the listing first.");
                return false;
            }

            _holdings.Remove(listingId);
            listing.ClearAcquisition();
            market.Sell(listing, currentDay);
            Cash += listing.CurrentPrice;

            OnCashChanged?.Invoke(Cash);
            OnHoldingChanged?.Invoke(listingId, null);
            return true;
        }

        /// <summary>Called by TransportManager when a shipment reaches this trader.</summary>
        public bool CompleteDelivery(string listingId)
        {
            if (!_holdings.TryGetValue(listingId, out var antique)) return false;

            antique.MarkArrived();
            OnHoldingChanged?.Invoke(listingId, antique);
            return true;
        }

        /// <summary>Re-raises OnHoldingChanged for a holding whose data changed outside this class
        /// (e.g. a transport delay moved its ArrivalDay).</summary>
        public void NotifyHoldingUpdated(string listingId)
        {
            if (_holdings.TryGetValue(listingId, out var antique))
                OnHoldingChanged?.Invoke(listingId, antique);
        }

        public bool RemoveHolding(string listingId)
        {
            if (!_holdings.Remove(listingId)) return false;

            OnHoldingChanged?.Invoke(listingId, null);
            return true;

        }

        /// <summary>
        /// Lists an owned antique on the market at the given price. The antique stays in
        /// the holdings until a buyer takes it (see CompleteListingSale).
        /// </summary>
        public bool ListForSale(Market.Market market, string listingId, float askingPrice, int currentDay)
        {
            if (market == null || askingPrice <= 0f) return false;
            if (!_holdings.TryGetValue(listingId, out var antique)) return false;
            if (antique.IsListedForSale || antique.IsReservedForContract || antique.IsInTransit) return false;

            if (!market.ListForSale(antique, askingPrice, currentDay)) return false;

            OnHoldingChanged?.Invoke(listingId, antique);
            return true;
        }

        public bool CancelListing(Market.Market market, string listingId)
        {
            if (market == null) return false;
            if (!_holdings.TryGetValue(listingId, out var antique)) return false;

            if (!market.CancelListing(antique)) return false;

            OnHoldingChanged?.Invoke(listingId, antique);
            return true;
        }

        /// <summary>
        /// Called by the Market when someone buys an antique this trader listed:
        /// hands the item over and pays the proceeds (price minus the market fee).
        /// </summary>
        public void CompleteListingSale(Antique antique, float proceeds)
        {
            if (antique == null || !_holdings.Remove(antique.Id)) return;

            antique.ClearAcquisition();
            Cash += Mathf.Max(0f, proceeds);

            OnCashChanged?.Invoke(Cash);
            OnHoldingChanged?.Invoke(antique.Id, null);
            OnListingSold?.Invoke(antique, proceeds);
        }

        /// <summary>
        /// Re-prices every holding against the current market state. Called by the Market
        /// whenever it re-prices its own listings, so owned items don't keep the price
        /// they had on the day they were bought.
        /// </summary>
        public void RevalueHoldings(Market.Market market, string definitionId = null)
        {
            if (market == null) return;

            bool any = false;
            foreach (var holding in _holdings.Values)
            {
                if (definitionId != null && holding.DefinitionId != definitionId) continue;
                market.RecalculatePrice(holding);
                any = true;
            }

            if (any)
                OnHoldingsRevalued?.Invoke();
        }

        public void RecordValue(int day)
        {
            _valueHistory.Add(new PricePoint(day, TotalHoldingsValue));
            if (_valueHistory.Count > MaxValueHistoryDays)
                _valueHistory.RemoveAt(0);
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
        /// Tags an owned listing as being gathered for a specific contract. Returns false if the listing isn't
        /// owned or is already reserved for something else.
        /// </summary>
        public bool ReserveForContract(string listingId, string contractId)
        {
            if (string.IsNullOrEmpty(contractId)) return false;
            if (!_holdings.TryGetValue(listingId, out var listing)) return false;
            if (listing.IsReservedForContract) return false;
            if (listing.IsListedForSale) return false;

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

        private readonly HashSet<string> _committedContractIds = new();
        public IReadOnlyCollection<string> CommittedContractIds => _committedContractIds;

        public bool IsCommittedToContract(string contractId) =>
            !string.IsNullOrEmpty(contractId) && _committedContractIds.Contains(contractId);

        public bool CommitToContract(string contractId)
        {
            if (string.IsNullOrEmpty(contractId)) return false;
            return _committedContractIds.Add(contractId);
        }

        public bool ReleaseCommittedContract(string contractId) => _committedContractIds.Remove(contractId);

    }
}