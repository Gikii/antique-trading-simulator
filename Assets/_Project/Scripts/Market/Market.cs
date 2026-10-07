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

        // Share of an owner-set asking price kept by the market when the listing sells.
        public const float ListingFeeRate = 0.05f;

        public static float ListingFee(float askingPrice) => Mathf.Max(0f, askingPrice) * ListingFeeRate;
        public static float ListingProceeds(float askingPrice) => Mathf.Max(0f, askingPrice) - ListingFee(askingPrice);

        // Where anonymous sellers of newly generated listings are located, relative to the
        // buyer. Determines transport cost and time (TransportManager). Sum doesn't need to be 1.
        public const float LocalListingWeight = 0.30f;
        public const float DomesticListingWeight = 0.45f;
        public const float InternationalListingWeight = 0.25f;

        /// <summary>Raised when an owner-set listing is bought: antique, seller inventory, price paid, proceeds for the seller.</summary>
        public event System.Action<Antique, TraderInventory, float, float> OnOwnerListingSold;

        public IReadOnlyList<Antique> Listings => _listings;
        public IReadOnlyDictionary<string, AntiqueMarketState> TypeStates => _typeStates;

        public MarketFeed Feed { get; } = new MarketFeed();
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
        public Antique GenerateListing(int currentDay)
        {
            string definitionId = PickWeightedDefinitionId();
            if (definitionId == null)
                return null;

            float condition = Random.Range(Antique.MinCondition, Antique.MaxCondition);
            float priceFactor = Random.Range(Antique.MinPriceFactor, Antique.MaxPriceFactor);


            var listing = new Antique(definitionId, condition, priceFactor);
            listing.ShippingZone = RollShippingZone();
            AddListing(listing, currentDay);
            return listing;
        }

        private static ShippingZone RollShippingZone()
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

        public void AddListing(Antique listing, int currentDay)
        {
            listing.MarketListedOnDay = currentDay;
            RecalculatePrice(listing);
            _listings.Add(listing);
            Feed.AddListing(listing.Name, listing.SalePrice);
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

        public  List<Country> GetAvailableCountries()
        {
            return _listings.Select(l => l.Country).Distinct().OrderBy(c => c.ToString()).ToList();
        }


        /// <summary>
        /// Player/NPC buys a specific listing off the market — it's removed from the
        /// available listings, and its type's supply dips/demand rises slightly (buying pressure).
        /// </summary>
        public bool Buy(string listingId, string newOwnerId = Antique.PlayerOwnerId)
        {
            var listing = GetById(listingId);

            if (listing == null)
            {
                Debug.LogWarning($"Market: listing with ID {listingId} not found");
                return false;
            }
            // An owner-set listing: the owner still holds the antique and gets paid now.
            if (listing.IsListedForSale)
                SettleOwnerListing(listing);

            listing.OwnerId = newOwnerId;
            _listings.Remove(listing);
            ApplyBuyPressure(listing.DefinitionId);

            return true;
        }

        private void SettleOwnerListing(Antique listing)
        {
            float price = listing.AskingPrice;
            float proceeds = ListingProceeds(price);
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

            seller.CompleteListingSale(listing, proceeds, price);
            OnOwnerListingSold?.Invoke(listing, seller, price, proceeds);
        }

        /// <summary>
        /// The owner puts an antique they keep holding on the market at their own price.
        /// It stays in their inventory (and counts toward their collection) until someone
        /// buys it — then the owner receives the price minus ListingFee. Adds the same
        /// supply pressure as any other item offered for sale.
        /// </summary>
        public bool ListForSale(Antique antique, float askingPrice, int currentDay)
        {
            if (antique == null || askingPrice <= 0f) return false;

            if (_listings.Contains(antique))
            {
                Debug.LogWarning($"Market: {antique.Id} is already listed.");
                return false;
            }

            antique.AskingPrice = askingPrice;
            antique.ShippingZone = ShippingZone.Domestic; // ships from the owner's warehouse
            ApplySellPressure(antique.DefinitionId);
            AddListing(antique, currentDay);
            // The extra supply lowers this type's value everywhere, the listed item included.
            RecalculatePricesForDefinition(antique.DefinitionId);
            return true;
        }

        /// <summary>Withdraws an owner-set listing. Free of charge; reverses its supply pressure.</summary>
        public bool CancelListing(Antique antique)
        {
            if (antique == null || !antique.IsListedForSale) return false;

            _listings.Remove(antique);
            antique.AskingPrice = 0f;
            antique.MarketListedOnDay = -1;
            ApplyBuyPressure(antique.DefinitionId);
            RecalculatePricesForDefinition(antique.DefinitionId);
            return true;
        }

        // Supply/demand nudges shared by every way an antique enters or leaves the market.
        private void ApplySellPressure(string definitionId)
        {
            var typeState = GetTypeState(definitionId);
            if (typeState == null) return;

            typeState.Supply += 1f;
            typeState.Demand = Mathf.Max(0f, typeState.Demand - 0.1f);
        }

        private void ApplyBuyPressure(string definitionId)
        {
            var typeState = GetTypeState(definitionId);
            if (typeState == null) return;

            typeState.Supply = Mathf.Max(0f, typeState.Supply - 1f);
            typeState.Demand += 0.1f;
        }

        /// <summary>
        /// Puts a specific, already-existing antique listing (with its own quality/state)
        /// back onto the market for sale — its type's supply rises/demand dips slightly.
        /// </summary>
        public void Sell(Antique listing, int currentDay)
        {
            if (listing == null)
            {
                Debug.LogWarning("Market: attempted to sell a null listing");
                return;
            }

            // Relisting means relinquishing ownership — becomes an anonymous
            // market listing again until someone else buys it.
            listing.OwnerId = "";
            listing.AskingPrice = 0f;
            listing.ShippingZone = ShippingZone.Domestic;
            listing.MarkArrived();

            ApplySellPressure(listing.DefinitionId);
            AddListing(listing, currentDay);
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
                // Same pressure Sell() applies.
                ApplySellPressure(antique.DefinitionId);
                return PriceEngine.CalculatePrice(antique, typeState);
            }
            finally
            {
                typeState.Supply = supply;
                typeState.Demand = demand;
            }
        }

        public void ApplyMeanReversion(float supplyRate, float demandRate)
        {
            foreach (var typeState in _typeStates.Values)
                typeState.ApplyMeanReversion(supplyRate, demandRate);

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

        public void RecordDailyPrices(int day)
        {
            foreach (var typeState in _typeStates.Values)
            {
                var definition = AntiqueDatabase.GetById(typeState.DefinitionId);
                float basePrice = definition != null ? definition.BasePrice : 0f;
                float referencePrice = PriceEngine.CalculateReferencePrice(basePrice, typeState);
                typeState.RecordPrice(day, referencePrice);
            }

            foreach (var inventory in _inventories)
                inventory.RecordValue(day);
        }
        // ---------------------------------------------------------------- save / load

        /// <summary>
        /// Snapshot of the market. An antique listed by its owner is NOT written out here — it
        /// belongs to that owner's inventory and only its Id is kept, so the same object comes
        /// back in one place instead of being split into two copies on load.
        /// </summary>
        public MarketState CaptureState()
        {
            var state = new MarketState { FeedMessages = Feed.CaptureState() };

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

            Feed.RestoreState(state.FeedMessages);
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
