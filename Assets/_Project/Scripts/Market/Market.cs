using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Logistics;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.Market
{
    /// <summary>
    /// Holds the type-level market state (supply/demand per AntiqueDefinition) and the
    /// list of individual AntiqueListing instances currently available to buy, and
    /// handles basic transactions.
    /// </summary>
    public class Market
    {
        private readonly List<Antique> _listings = new List<Antique>();
        private readonly Dictionary<string, AntiqueMarketState> _typeStates = new Dictionary<string, AntiqueMarketState>();

        // Inventories (player + NPCs) whose holdings are re-priced together with the
        // listings. Owned by EconomyManager so traders can register before the Market exists.
        private readonly IReadOnlyList<TraderInventory> _inventories;

        // Base share of an owner-set asking price kept by the market when the listing sells.
        // The rate actually charged is the seller's (TraderInventory.Modifiers.ListingFeeRate),
        // which starts at this value; the player's can be lowered by the Staff upgrade.
        public const float ListingFeeRate = 0.05f;

        public static float ListingFee(float askingPrice) =>
            Core.Money.RoundToShilling(Mathf.Max(0f, askingPrice) * ListingFeeRate);
        public static float ListingProceeds(float askingPrice) => Mathf.Max(0f, askingPrice) - ListingFee(askingPrice);

        /// <summary>
        /// What the market keeps when a seller takes the instant price instead of listing. The
        /// market buys at wholesale, so an instant sale is always worse than a patient listing.
        /// </summary>
        public static float InstantSaleSpread = 0.15f;

        /// <summary>
        /// What a seller receives for an instant sale at this price. The single definition of the
        /// spread: the preview and the charge both go through it, for every trader alike.
        /// </summary>
        public static float InstantSaleProceeds(float postPressurePrice) =>
            Core.Money.RoundToShilling(Mathf.Max(0f, postPressurePrice) * (1f - Mathf.Clamp01(InstantSaleSpread)));

        // Where anonymous sellers of newly generated listings are located, relative to the
        // buyer. Determines transport cost and time (TransportManager). Sum doesn't need to be 1.
        public const float LocalListingWeight = 0.30f;
        public const float DomesticListingWeight = 0.45f;
        public const float InternationalListingWeight = 0.25f;

        /// <summary>Raised when an owner-set listing is bought: antique, seller inventory, price paid, proceeds for the seller.</summary>
        public event System.Action<Antique, TraderInventory, float, float> OnOwnerListingSold;
        /// <summary>Raised when a listing is added.</summary>
        public event System.Action<Antique> OnListingAdded;
        /// <summary> Raissed when a listing is removed for any reason.</summary>
        public event System.Action<Antique> OnListingRemoved;
        /// <summary>Raised when supply or demand is affected. </summary>
        public event System.Action<string, float, float> OnPressureApplied;

        public IReadOnlyList<Antique> Listings => _listings;
        public IReadOnlyDictionary<string, AntiqueMarketState> TypeStates => _typeStates;

        public MarketFeed Feed { get; } = new MarketFeed();

        /// <summary>
        /// Long-term demand memory parameters (AntiqueMarketState.UpdateLongTermDemand), set by
        /// EconomyManager. Cap 0 turns the memory off: Demand then reverts to BaselineDemand as before.
        /// </summary>
        public float LongTermDemandRate { get; private set; }
        public float LongTermDemandDecay { get; private set; }
        public float LongTermDemandCap { get; private set; }

        public void SetLongTermDemand(float rate, float decay, float cap)
        {
            LongTermDemandRate = Mathf.Clamp01(rate);
            LongTermDemandDecay = Mathf.Clamp01(decay);
            LongTermDemandCap = Mathf.Max(0f, cap);
        }

        /// <summary>
        /// Weight of the antique's own price trend in <see cref="PriceTrend"/>; the rest is the trend of
        /// its whole category (AntiqueType). One trade moves a single type's price a lot, so the
        /// category part smooths that out and lets a buy-up of a category spread to its other types.
        /// </summary>
        public static float OwnTrendWeight
        {
            get => _ownTrendWeight;
            set => _ownTrendWeight = Mathf.Clamp01(value);
        }
        private static float _ownTrendWeight = 0.2f;

        // Category trends per (type, window), valid until the next weekly price record.
        private readonly Dictionary<(AntiqueType, int), float> _categoryTrendCache = new Dictionary<(AntiqueType, int), float>();

        /// <summary>
        /// Recent price trend for an antique type, seasons removed: OwnTrendWeight × its own trend +
        /// the rest × the average trend of its category, over <paramref name="windowWeeks"/> weeks.
        /// 0.1 = prices up 10%. Read by trend-following NPCs.
        /// </summary>
        public float PriceTrend(string definitionId, int windowWeeks)
        {
            var typeState = GetTypeState(definitionId);
            var definition = AntiqueDatabase.GetById(definitionId);
            if (typeState == null || definition == null || windowWeeks <= 0) return 0f;

            return OwnTrendWeight * typeState.PriceTrend(windowWeeks) +
                   (1f - OwnTrendWeight) * CategoryTrend(definition.Type, windowWeeks);
        }

        // ---------------------------------------------------------------- rival asking prices

        /// <summary>
        /// Cheapest asking price on the board for this type, ignoring one listing. 0 when it is
        /// the only one. Lets a seller price against the rest of the board.
        /// </summary>
        public float LowestAskFor(string definitionId, string excludeAntiqueId)
        {
            float lowest = 0f;

            for (int i = 0; i < _listings.Count; i++)
            {
                var listing = _listings[i];
                if (listing == null || listing.DefinitionId != definitionId) continue;
                if (!listing.IsListedForSale || listing.Id == excludeAntiqueId) continue;

                if (lowest <= 0f || listing.AskingPrice < lowest) lowest = listing.AskingPrice;
            }

            return lowest;
        }

        /// <summary>Middle asking price on the board for this type, ignoring one listing. 0 when none.</summary>
        public float MedianAskFor(string definitionId, string excludeAntiqueId)
        {
            var asks = new List<float>();

            for (int i = 0; i < _listings.Count; i++)
            {
                var listing = _listings[i];
                if (listing == null || listing.DefinitionId != definitionId) continue;
                if (!listing.IsListedForSale || listing.Id == excludeAntiqueId) continue;

                asks.Add(listing.AskingPrice);
            }

            if (asks.Count == 0) return 0f;

            asks.Sort();
            int middle = asks.Count / 2;
            return asks.Count % 2 == 1 ? asks[middle] : 0.5f * (asks[middle - 1] + asks[middle]);
        }

        /// <summary>Average PriceTrend of every registered antique type in a category.</summary>
        public float CategoryTrend(AntiqueType type, int windowWeeks)
        {
            if (windowWeeks <= 0) return 0f;
            if (_categoryTrendCache.TryGetValue((type, windowWeeks), out float cached)) return cached;

            float sum = 0f;
            int count = 0;
            foreach (var definition in AntiqueDatabase.GetByType(type))
            {
                var typeState = definition != null ? GetTypeState(definition.Id) : null;
                if (typeState == null) continue;

                sum += typeState.PriceTrend(windowWeeks);
                count++;
            }

            float trend = count > 0 ? sum / count : 0f;
            _categoryTrendCache[(type, windowWeeks)] = trend;
            return trend;
        }

        public Market(IReadOnlyList<TraderInventory> inventories = null)
        {
            _inventories = inventories ?? new List<TraderInventory>();
        }

        /// <summary>
        /// Registers an AntiqueDefinition with the market so it has Supply/Demand tracked
        /// and can start appearing as listings. Must be called once per definition before
        /// GenerateListing can produce listings of that type.
        /// </summary>
        public void RegisterType(string definitionId, float initialSupply, float initialDemand)
        {
            if (_typeStates.ContainsKey(definitionId))
                return;

            _typeStates[definitionId] = new AntiqueMarketState(definitionId, initialSupply, initialDemand);
        }

        public AntiqueMarketState GetTypeState(string definitionId)
        {
            _typeStates.TryGetValue(definitionId, out var state);
            return state;
        }

        /// <summary>
        /// Rolls a new individual listing into existence: picks a definition at random,
        /// weighted by that definition's current Supply (higher supply = more likely to
        /// appear), then rolls a random Condition for the new item and adds it to the
        /// market. Returns null if there are no registered types with positive supply.
        /// </summary>
        /// <param name="addSupply">
        /// True for offers that appear during the game: a new item for sale is more supply, like any
        /// item put up for sale (and RemoveExpiredListings takes it back out). False for the listings
        /// the market starts with — they are the baseline the initial supply describes.
        /// </param>
        public Antique GenerateListing(int currentWeek, bool addSupply = true)
        {
            string definitionId = PickWeightedDefinitionId();
            if (definitionId == null)
                return null;

            float condition = Antique.RandomCondition();
            float priceFactor = Random.Range(Antique.MinPriceFactor, Antique.MaxPriceFactor);

            return CreateListing(definitionId, condition, priceFactor, RollShippingZone(), currentWeek, addSupply);
        }

        public Antique CreateListing(string definitionId, float condition, float priceFactor,
                                     ShippingZone shippingZone, int currentWeek, bool addSupply = true)
        {
            if (string.IsNullOrEmpty(definitionId))
            {
                Debug.LogWarning("Market: CreateListing was called without a definition Id.");
                return null;
            }

            if (GetTypeState(definitionId) == null)
            {
                Debug.LogWarning($"Market: '{definitionId}' is not a registered type.");
                return null;
            }

            var listing = new Antique(definitionId, condition, priceFactor);
            listing.ShippingZone = shippingZone;
            AddListing(listing, currentWeek);

            if (addSupply)
            {
                ApplySellPressure(definitionId);
                RecalculatePricesForDefinition(definitionId);
            }
            return listing;
        }

        public static ShippingZone RollShippingZone()
        {
            float total = LocalListingWeight + DomesticListingWeight + InternationalListingWeight;
            float roll = Random.value * total;

            if (roll < LocalListingWeight) return ShippingZone.Local;
            if (roll < LocalListingWeight + DomesticListingWeight) return ShippingZone.Domestic;
            return ShippingZone.International;
        }

        private string PickWeightedDefinitionId()
        {
            float totalWeight = 0f;
            foreach (var typeState in _typeStates.Values)
                totalWeight += Mathf.Max(0f, typeState.Supply);

            if (totalWeight <= 0f)
                return null;

            float roll = Random.value * totalWeight;
            float cumulative = 0f;

            foreach (var typeState in _typeStates.Values)
            {
                cumulative += Mathf.Max(0f, typeState.Supply);
                if (roll <= cumulative)
                    return typeState.DefinitionId;
            }

            return null;
        }

        public void AddListing(Antique listing, int currentWeek)
        {
            listing.MarketListedOnWeek = currentWeek;
            RecalculatePrice(listing);
            _listings.Add(listing);
            Feed.AddListed();
            OnListingAdded?.Invoke(listing);
        }

        public Antique GetById(string listingId)
        {
            return _listings.Find(l => l.Id == listingId);
        }

        public List<Antique> GetListingsByDefinition(string definitionId)
        {
            return _listings.FindAll(l => l.DefinitionId == definitionId);
        }

        public List<Antique> GetByType(AntiqueType type)
        {
            return _listings.FindAll(l => l.Type == type);
        }

        public List<Antique> GetByCentury(Century century)
        {
            return _listings.FindAll(l => l.Century == century);
        }

        public List<Antique> GetByCountry(Country country)
        {
            return _listings.FindAll(l => l.Country == country);
        }

        public List<AntiqueType> GetAvailableTypes()
        {
            return _listings.Select(l => l.Type).Distinct().OrderBy(t => t.ToString()).ToList();
        }

        public List<Century> GetAvailableCenturies()
        {
            return _listings.Select(l => l.Century).Distinct().OrderBy(c => (int)c).ToList();
        }

        public List<Country> GetAvailableCountries()
        {
            return _listings.Select(l => l.Country).Distinct().OrderBy(c => c.ToString()).ToList();
        }


        /// <summary>
        /// Player/NPC buys a specific listing off the market — it's removed from the
        /// available listings, and its type's supply dips/demand rises slightly (buying pressure).
        /// </summary>
        public bool Buy(string listingId, string newOwnerId)
        {
            var listing = GetById(listingId);

            if (listing == null)
            {
                Debug.LogWarning($"Market: listing with ID {listingId} not found");
                return false;
            }
            // Read before settling: settling clears the asking price, and the owner changes below.
            float price = listing.SalePrice;
            string sellerId = listing.IsListedForSale ? listing.OwnerId : "";

            // An owner-set listing: the owner still holds the antique and gets paid now.
            if (listing.IsListedForSale)
                SettleOwnerListing(listing);

            listing.OwnerId = newOwnerId;
            _listings.Remove(listing);
            ApplyBuyPressure(listing.DefinitionId);
            OnListingRemoved?.Invoke(listing);

            Feed.AddPurchase(listing.Name, newOwnerId, sellerId, price);
            return true;
        }

        /// <summary>
        /// Withdraws anonymous listings (no owner, no asking price) that have been on the market for
        /// more than <paramref name="lifetimeDays"/> days — their sellers gave up — so the market
        /// keeps refreshing instead of filling with stale offers. Owner-set listings never expire.
        /// The supply the offer added when it appeared is taken back out (same as cancelling a listing).
        /// The demand it took away is NOT given back (unless ExpiredListingsRestoreDemand): nobody bought
        /// it, which is a sign of weak interest. That is also what makes a purchase a demand signal —
        /// if expiring gave the demand back too, buying an offer and letting it expire would leave the
        /// market in the same state, and trades (the player's included) could never leave a trace.
        /// Returns how many were removed.
        /// </summary>
        public int RemoveExpiredListings(int currentWeek, int lifetimeWeeks)
        {
            if (lifetimeWeeks <= 0) return 0;

            var expired = _listings.Where(l =>
                !l.IsListedForSale &&
                string.IsNullOrEmpty(l.OwnerId) &&
                l.MarketListedOnWeek >= 0 &&
                currentWeek - l.MarketListedOnWeek > lifetimeWeeks).ToList();

            foreach (var listing in expired)
            {
                _listings.Remove(listing);
                if (ExpiredListingsRestoreDemand) ApplyBuyPressure(listing.DefinitionId);
                else ApplyWithdrawal(listing.DefinitionId);
                OnListingRemoved?.Invoke(listing);
            }

            foreach (var definitionId in expired.Select(l => l.DefinitionId).Distinct())
                RecalculatePricesForDefinition(definitionId);

            return expired.Count;
        }

        /// <summary>Feed name for buyers from outside the simulated traders (CollectorDemand).</summary>
        public const string CollectorBuyerName = "A private collector";

        /// <summary>
        /// A private collector buys an owner-set listing: the owner is paid (price minus their fee,
        /// exactly as when a trader buys it) and the antique leaves the game with the collector.
        /// Same buying pressure as any purchase. False if it isn't an owner-set listing on the market.
        /// </summary>
        public bool SellToCollector(Antique listing)
        {
            if (listing == null || !listing.IsListedForSale || !_listings.Contains(listing))
                return false;

            string name = listing.Name;
            float price = listing.AskingPrice;
            string sellerId = listing.OwnerId;

            SettleOwnerListing(listing);
            listing.OwnerId = "";
            _listings.Remove(listing);
            ApplyBuyPressure(listing.DefinitionId);
            OnListingRemoved?.Invoke(listing);

            Feed.AddCollectorPurchase(name, sellerId, price);
            return true;
        }

        /// <summary>
        /// An antique handed over to a contract's client leaves the game with them — a sale outside the
        /// market. It was in a warehouse, not on offer, so supply is untouched; the sale is a signal of
        /// interest like any purchase (the same demand nudge as buying pressure), and it goes into the
        /// market history. The client's own waiting demand is removed separately by ContractManager.
        /// </summary>
        /// <param name="pricePaid">What the client paid for this antique (the contract's reward per unit).</param>
        public void RecordContractDelivery(Antique antique, string supplierId, float pricePaid)
        {
            if (antique == null) return;

            var typeState = GetTypeState(antique.DefinitionId);
            if (typeState != null)
            {
                float demandBefore = typeState.Demand;
                typeState.Demand += TradeDemandImpulse;
                ReportPressure(antique.DefinitionId, typeState.Demand - demandBefore, 0f);
                RecalculatePricesForDefinition(antique.DefinitionId);
            }

            Feed.AddContractDelivery(antique.Name, supplierId, pricePaid);
        }

        private void SettleOwnerListing(Antique listing)
        {
            float price = listing.AskingPrice;
            listing.AskingPrice = 0f;

            TraderInventory seller = null;
            foreach (var inventory in _inventories)
            {
                if (inventory.Owns(listing.Id))
                {
                    seller = inventory;
                    break;
                }
            }

            if (seller == null)
            {
                Debug.LogWarning($"Market: seller of owner listing {listing.Id} not found — nobody was paid.");
                return;
            }

            // The seller's fee rate at the moment of the sale (the player's depends on Staff).
            float proceeds = seller.Modifiers.ListingProceeds(price);
            seller.CompleteListingSale(listing, proceeds, price);
            OnOwnerListingSold?.Invoke(listing, seller, price, proceeds);
        }

        /// <summary>
        /// The owner puts an antique they keep holding on the market at their own price.
        /// It stays in their inventory (and counts toward their collection) until someone
        /// buys it — then the owner receives the price minus ListingFee. Adds the same
        /// supply pressure as any other item offered for sale.
        /// </summary>
        public bool ListForSale(Antique antique, float askingPrice, int currentWeek)
        {
            if (antique == null || askingPrice <= 0f) return false;

            if (_listings.Contains(antique))
            {
                Debug.LogWarning($"Market: {antique.Id} is already listed.");
                return false;
            }

            antique.AskingPrice = Core.Money.RoundToShilling(askingPrice); // NPC prices are markups, so not round
            antique.ShippingZone = ShippingZone.Domestic; // ships from the owner's warehouse
            ApplySellPressure(antique.DefinitionId);
            AddListing(antique, currentWeek);
            // The extra supply lowers this type's value everywhere, the listed item included.
            RecalculatePricesForDefinition(antique.DefinitionId);
            return true;
        }

        /// <summary> Changes the asking price on a listing already on the market</summary>
        public bool RepriceListing(Antique antique, float askingPrice)
        {
            if (antique == null || askingPrice <= 0f) return false;
            if (!antique.IsListedForSale) return false;

            antique.AskingPrice = Core.Money.RoundToShilling(askingPrice);
            return true;
        }

        /// <summary>Withdraws an owner-set listing. Free of charge; reverses its supply pressure.</summary>
        public bool CancelListing(Antique antique)
        {
            if (antique == null || !antique.IsListedForSale) return false;

            _listings.Remove(antique);
            antique.AskingPrice = 0f;
            antique.MarketListedOnWeek = -1;
            ApplyBuyPressure(antique.DefinitionId);
            OnListingRemoved?.Invoke(antique);
            RecalculatePricesForDefinition(antique.DefinitionId);
            return true;
        }

        // Supply/demand nudges shared by every way an antique enters or leaves the market.
        /// <summary>
        /// How much one trade moves its type's Demand: + on a purchase (and a contract delivery),
        /// − when a piece is put on offer. Supply always moves by one piece. Set by EconomyManager
        /// (tradeDemandImpulse); 0.1 until it is.
        /// </summary>
        public float TradeDemandImpulse { get; private set; } = 0.1f;

        public void SetTradeDemandImpulse(float impulse) => TradeDemandImpulse = Mathf.Max(0f, impulse);

        /// <summary>
        /// Old behaviour switch: when true an expired offer gives back the demand it took away, like a
        /// purchase (see RemoveExpiredListings). Set by EconomyManager; false by default.
        /// </summary>
        public bool ExpiredListingsRestoreDemand { get; set; }

        /// <summary>An offer leaves the market unsold: its piece of supply goes, demand stays where it is.</summary>
        private void ApplyWithdrawal(string definitionId)
        {
            var typeState = GetTypeState(definitionId);
            if (typeState == null) return;

            float supplyBefore = typeState.Supply;
            typeState.Supply = Mathf.Max(0f, typeState.Supply - 1f);
            ReportPressure(definitionId, 0f, typeState.Supply - supplyBefore);
        }

        private void ApplySellPressure(string definitionId) => ApplySellPressure(definitionId, report: true);

        private void ApplySellPressure(string definitionId, bool report)
        {
            var typeState = GetTypeState(definitionId);
            if (typeState == null) return;

            float supplyBefore = typeState.Supply;
            float demandBefore = typeState.Demand;

            typeState.Supply += 1f;
            typeState.Demand = Mathf.Max(0f, typeState.Demand - TradeDemandImpulse);

            if (report)
                ReportPressure(definitionId, typeState.Demand - demandBefore, typeState.Supply - supplyBefore);
        }

        private void ApplyBuyPressure(string definitionId)
        {
            var typeState = GetTypeState(definitionId);
            if (typeState == null) return;

            float supplyBefore = typeState.Supply;
            float demandBefore = typeState.Demand;

            typeState.Supply = Mathf.Max(0f, typeState.Supply - 1f);
            typeState.Demand += TradeDemandImpulse;

            ReportPressure(definitionId, typeState.Demand - demandBefore, typeState.Supply - supplyBefore);
        }

        public void ReportPressure(string definitionId, float demandDelta, float supplyDelta)
        {
            if (string.IsNullOrEmpty(definitionId)) return;
            if (Mathf.Approximately(demandDelta, 0f) && Mathf.Approximately(supplyDelta, 0f)) return;

            OnPressureApplied?.Invoke(definitionId, demandDelta, supplyDelta);
        }

        /// <summary>
        /// Takes in the outcome of an auction lot. A sale is remembered as the type's last auction price.
        /// The result is a signal about interest in the type, so it moves demand by
        /// <paramref name="demandDelta"/> (the AuctionManager derives it from hammer price vs. market value)
        /// — it never sets a price. The change is reported like any other pressure, so related categories
        /// follow through the dependency graph.
        /// </summary>
        /// <param name="hammerPrice">Price the lot sold for; 0 if it went unsold.</param>
        public void ApplyAuctionResult(string definitionId, float hammerPrice, int week, float demandDelta)
        {
            var typeState = GetTypeState(definitionId);
            if (typeState == null) return;

            if (hammerPrice > 0f)
            {
                typeState.LastAuctionPrice = hammerPrice;
                typeState.LastAuctionWeek = week;
            }

            if (Mathf.Approximately(demandDelta, 0f)) return;

            float demandBefore = typeState.Demand;
            typeState.Demand = Mathf.Max(0f, typeState.Demand + demandDelta);

            ReportPressure(definitionId, typeState.Demand - demandBefore, 0f);
            RecalculatePricesForDefinition(definitionId);
        }


        /// <summary>
        /// Puts a specific, already-existing antique listing (with its own quality/state)
        /// back onto the market for sale — its type's supply rises/demand dips slightly.
        /// </summary>
        public void Sell(Antique listing, int currentWeek)
        {
            if (listing == null)
            {
                Debug.LogWarning("Market: attempted to sell a null listing");
                return;
            }

            string sellerId = listing.OwnerId;

            // Relisting means relinquishing ownership — becomes an anonymous
            // market listing again until someone else buys it.
            listing.OwnerId = "";
            listing.AskingPrice = 0f;
            listing.ShippingZone = ShippingZone.Domestic;
            listing.MarkArrived();

            ApplySellPressure(listing.DefinitionId);
            AddListing(listing, currentWeek);

            // The seller is paid the proceeds, not the gross price, so the feed shows the same.
            Feed.AddSoldToMarket(listing.Name, sellerId, InstantSaleProceeds(listing.CurrentPrice));
        }

        /// <summary>
        /// What the owner would get right now for selling this antique instantly via Sell():
        /// the price after the sale's own supply/demand impact, exactly as Sell() computes it.
        /// Does not change any state.
        /// </summary>
        public float EstimateInstantSalePrice(Antique antique)
        {
            if (antique == null) return 0f;

            var typeState = GetTypeState(antique.DefinitionId);
            if (typeState == null) return antique.CurrentPrice;

            float supply = typeState.Supply;
            float demand = typeState.Demand;
            try
            {
                // Same pressure and the same spread the charge applies.
                ApplySellPressure(antique.DefinitionId, false);
                return InstantSaleProceeds(PriceEngine.CalculatePrice(antique, typeState));
            }
            finally
            {
                typeState.Supply = supply;
                typeState.Demand = demand;
            }
        }

        /// <summary>
        /// Sets every type's seasonal demand from a multiplier per antique type (1 = normal). Prices are
        /// not recalculated here — call RecalculateAllPrices (ApplyMeanReversion does) afterwards.
        /// </summary>
        public void ApplySeasonalDemand(System.Func<AntiqueType, float> multiplierByType)
        {
            if (multiplierByType == null) return;

            foreach (var typeState in _typeStates.Values)
            {
                var definition = AntiqueDatabase.GetById(typeState.DefinitionId);
                float multiplier = definition != null ? multiplierByType(definition.Type) : 1f;
                typeState.SeasonalDemandMod = typeState.BaselineDemand * (multiplier - 1f);
            }
        }

        public void ApplyMeanReversion(float supplyRate, float demandRate)
        {
            foreach (var typeState in _typeStates.Values)
            {
                typeState.ApplyMeanReversion(supplyRate, demandRate);
                typeState.UpdateLongTermDemand(LongTermDemandRate, LongTermDemandDecay, LongTermDemandCap);
            }

            RecalculateAllPrices();
        }

        public void RecalculatePrice(Antique listing)
        {
            var typeState = GetTypeState(listing.DefinitionId);
            listing.CurrentPrice = PriceEngine.CalculatePrice(listing, typeState);
        }

        public void RecalculateAllPrices()
        {
            foreach (var listing in _listings)
                RecalculatePrice(listing);

            foreach (var inventory in _inventories)
                inventory.RevalueHoldings(this);
        }

        public void RecalculatePricesForDefinition(string definitionId)
        {
            foreach (var listing in _listings)
                if (listing.DefinitionId == definitionId)
                    RecalculatePrice(listing);

            foreach (var inventory in _inventories)
                inventory.RevalueHoldings(this, definitionId);
        }

        public void RecordWeeklyPrices(int week)
        {
            foreach (var typeState in _typeStates.Values)
            {
                var definition = AntiqueDatabase.GetById(typeState.DefinitionId);
                float basePrice = definition != null ? definition.BasePrice : 0f;
                float referencePrice = PriceEngine.CalculateReferencePrice(basePrice, typeState);
                // The same price as if the season didn't move demand: what trend followers compare.
                float trendPrice = referencePrice *
                    PriceEngine.PriceRatioForDemand(typeState, typeState.EffectiveDemand - typeState.SeasonalDemandMod);
                typeState.RecordPrice(week, referencePrice, trendPrice);
            }

            _categoryTrendCache.Clear();

            foreach (var inventory in _inventories)
                inventory.RecordValue(week);
        }
        // ---------------------------------------------------------------- save / load

        /// <summary>
        /// Snapshot of the market. An antique listed by its owner is NOT written out here — it
        /// belongs to that owner's inventory and only its Id is kept, so the same object comes
        /// back in one place instead of being split into two copies on load.
        /// </summary>
        public MarketState CaptureState()
        {
            var state = new MarketState
            {
                FeedEntries = Feed.CaptureEntries(),
                FeedPlayerTotals = Feed.CaptureTotals(),
                FeedWeekStats = Feed.CaptureWeekStats()
            };

            foreach (var typeState in _typeStates.Values)
            {
                if (typeState == null) continue;
                state.TypeStates.Add(MarketTypeState.Capture(typeState));
            }

            foreach (var listing in _listings)
            {
                if (listing == null) continue;

                state.ListingIds.Add(listing.Id);

                // IsListedForSale means an owner set the asking price, so an inventory holds it.
                if (!listing.IsListedForSale)
                    state.UnownedListings.Add(AntiqueState.Capture(listing));
            }

            return state;
        }

        /// <summary>
        /// Replaces the market with the saved state. Call after every TraderInventory has been restored
        /// </summary>
        public void RestoreState(MarketState state)
        {
            if (state == null) return;

            _typeStates.Clear();
            _categoryTrendCache.Clear();
            if (state.TypeStates != null)
            {
                foreach (var typeStateData in state.TypeStates)
                {
                    var typeState = typeStateData?.Restore();
                    if (typeState == null || string.IsNullOrEmpty(typeState.DefinitionId)) continue;

                    _typeStates[typeState.DefinitionId] = typeState;
                }
            }

            // Unowned stock is rebuilt from the save; owned listings are looked up by Id.
            var unownedById = new Dictionary<string, Antique>();
            if (state.UnownedListings != null)
            {
                foreach (var antiqueState in state.UnownedListings)
                {
                    var antique = antiqueState?.Restore();
                    if (antique == null) continue;

                    unownedById[antique.Id] = antique;
                }
            }

            _listings.Clear();
            if (state.ListingIds != null)
            {
                foreach (var listingId in state.ListingIds)
                {
                    if (unownedById.TryGetValue(listingId, out var unowned))
                    {
                        _listings.Add(unowned);
                        continue;
                    }

                    var owned = FindHeldAntique(listingId);
                    if (owned == null)
                    {
                        Debug.LogWarning($"Market: listing '{listingId}' was listed by an owner, but no restored inventory holds it. Dropped from the market.");
                        continue;
                    }

                    _listings.Add(owned);
                }
            }

            Feed.RestoreState(state.FeedEntries, state.FeedPlayerTotals, state.FeedWeekStats);
        }

        private Antique FindHeldAntique(string listingId)
        {
            foreach (var inventory in _inventories)
            {
                var antique = inventory?.GetHolding(listingId);
                if (antique != null) return antique;
            }
            return null;
        }
    }
}
