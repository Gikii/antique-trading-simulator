using AntiqueTradingSimulator.Economy;
using System.Collections.Generic;
using System.Linq;
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

        public IReadOnlyList<Antique> Listings => _listings;
        public IReadOnlyDictionary<string, AntiqueMarketState> TypeStates => _typeStates;

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
        /// appear), then rolls random Quality/State for the new item and adds it to the
        /// market. Returns null if there are no registered types with positive supply.
        /// </summary>
        public Antique GenerateListing(int currentDay)
        {
            string definitionId = PickWeightedDefinitionId();
            if (definitionId == null)
                return null;

            float quality = Random.Range(Antique.MinQuality, Antique.MaxQuality);
            float state = Random.Range(Antique.MinState, Antique.MaxState);

            var listing = new Antique(definitionId, quality, state);
            AddListing(listing, currentDay);
            return listing;
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

        public List<Antique> GetByTimePeriod(TimePeriod period)
        {
            return _listings.FindAll(l => l.TimePeriod == period);
        }

        public List<Antique> GetByCountry(Country country)
        {
            return _listings.FindAll(l => l.Country == country);
        }

        public List<AntiqueType> GetAvailableTypes()
        {
            return _listings.Select(l => l.Type).Distinct().OrderBy(t => t.ToString()).ToList();
        }

        public  List<TimePeriod> GetAvailableTimePeriods()
        {
            return _listings.Select(l => l.TimePeriod).Distinct().OrderBy(p => (int)p).ToList();
        }

        public  List<Country> GetAvailableCountries()
        {
            return _listings.Select(l => l.Country).Distinct().OrderBy(c => c.ToString()).ToList();
        }


        /// <summary>
        /// Player/NPC buys a specific listing off the market — it's removed from the
        /// available listings, and its type's supply dips/demand rises slightly (buying pressure).
        /// </summary>
        public bool Buy(string listingId)
        {
            var listing = GetById(listingId);

            if (listing == null)
            {
                Debug.LogWarning($"Market: listing with ID {listingId} not found");
                return false;
            }

            _listings.Remove(listing);

            var typeState = GetTypeState(listing.DefinitionId);
            if (typeState != null)
            {
                typeState.Supply = Mathf.Max(0f, typeState.Supply - 1f);
                typeState.Demand += 0.1f;
            }

            return true;
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

            var typeState = GetTypeState(listing.DefinitionId);
            if (typeState != null)
            {
                typeState.Supply += 1f;
                typeState.Demand = Mathf.Max(0f, typeState.Demand - 0.1f);
            }

            AddListing(listing, currentDay); 
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
        }
    }
}
