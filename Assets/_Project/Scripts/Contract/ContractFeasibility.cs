using System;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.Market;
using UnityEngine;

namespace AntiqueTradingSimulator.Contracts
{
    /// <summary>
    /// Rough estimate of how many antiques that qualify for a contract the market will offer before its
    /// deadline, so ContractManager doesn't put up contracts nobody can fulfil (or ask for more pieces
    /// than will ever be offered). Two parts:
    ///
    ///  • listed now — market listings that already qualify and can still arrive in time by Express;
    ///  • expected new — the market's new listings per week × the weeks in which a new one could still be
    ///    bought and delivered × the chance a new listing qualifies (it is of a matching type — types are
    ///    drawn by supply, as in Market.GenerateListing — and rolls a good enough condition and value).
    ///
    /// Optimistic on purpose (Express everywhere, nobody else buying); ContractManager takes only a share
    /// of the estimate to leave room for NPC competition and distant listings. Plain C#, no side effects:
    /// it uses its own random generator, so estimating never changes the game's random sequence.
    /// </summary>
    public static class ContractFeasibility
    {
        /// <summary>Simulated new listings per matching type when estimating the chance one qualifies.</summary>
        private const int SamplesPerType = 48;

        public static float ExpectedSupply(ContractRequirement requirement, Market.Market market,
            int currentWeek, int deadlineWeek, float newListingsPerWeek, Func<ShippingZone, int> expressWeeks)
        {
            if (requirement == null || market == null || expressWeeks == null) return 0f;

            int latestArrival = deadlineWeek - 1; // the contract expires at the start of its deadline week

            return ListedNow(requirement, market, currentWeek, latestArrival, expressWeeks)
                   + ExpectedNewListings(requirement, market, currentWeek, latestArrival, newListingsPerWeek, expressWeeks);
        }

        /// <summary>Qualifying listings already on the market that can still arrive in time (the player's own excluded).</summary>
        public static int ListedNow(ContractRequirement requirement, Market.Market market,
            int currentWeek, int latestArrival, Func<ShippingZone, int> expressWeeks)
        {
            int count = 0;
            var listings = market.Listings;

            for (int i = 0; i < listings.Count; i++)
            {
                var listing = listings[i];
                if (listing == null || listing.OwnerId == Antique.PlayerOwnerId) continue;
                if (currentWeek + expressWeeks(listing.ShippingZone) > latestArrival) continue;
                if (!requirement.IsSatisfiedBy(listing)) continue;
                count++;
            }

            return count;
        }

        /// <summary>
        /// New listings appear at each week change. One appearing in week w can be bought then and, coming from
        /// nearby by Express, arrive in w + the local Express time — so the buying window ends that much before the deadline.
        /// </summary>
        public static float ExpectedNewListings(ContractRequirement requirement, Market.Market market,
            int currentWeek, int latestArrival, float newListingsPerWeek, Func<ShippingZone, int> expressWeeks)
        {
            int lastBuyWeek = latestArrival - expressWeeks(ShippingZone.Local);
            int windowWeeks = lastBuyWeek - currentWeek;
            if (windowWeeks <= 0 || newListingsPerWeek <= 0f) return 0f;

            return newListingsPerWeek * windowWeeks * ChanceNewListingQualifies(requirement, market);
        }

        /// <summary>
        /// Chance that one new market listing qualifies: Σ over matching types of (its share of all supply,
        /// as Market.PickWeightedDefinitionId draws by supply) × (share of simulated condition/price rolls of
        /// that type that meet the condition and value floors).
        /// </summary>
        public static float ChanceNewListingQualifies(ContractRequirement requirement, Market.Market market)
        {
            var all = AntiqueDatabase.GetAll();
            if (all == null || all.Count == 0) return 0f;

            float totalSupply = 0f;
            foreach (var def in all)
            {
                var state = def != null ? market.GetTypeState(def.Id) : null;
                if (state != null) totalSupply += Mathf.Max(0f, state.Supply);
            }

            if (totalSupply <= 0f) return 0f;

            // Fixed seed: the same market gives the same estimate, and the game's own Random is untouched.
            var rng = new System.Random(12345);
            float chance = 0f;

            foreach (var def in requirement.MatchingDefinitions())
            {
                if (def.BasePrice <= 0f) continue;

                var state = market.GetTypeState(def.Id);
                if (state == null) continue;

                float typeShare = Mathf.Max(0f, state.Supply) / totalSupply;
                if (typeShare <= 0f) continue;

                // Market multiplier of the type now (reference price at condition 1, price factor 1, over base).
                float marketMultiplier = PriceEngine.CalculateReferencePrice(def.BasePrice, state) / def.BasePrice;

                int qualifying = 0;
                for (int i = 0; i < SamplesPerType; i++)
                {
                    float condition = Lerp(rng, Antique.MinCondition, Antique.MaxCondition);
                    float priceFactor = Lerp(rng, Antique.MinPriceFactor, Antique.MaxPriceFactor);

                    if (condition + 0.0005f < requirement.MinCondition) continue;

                    float value = def.BasePrice * condition * priceFactor * marketMultiplier;
                    if (requirement.HasMinValue &&
                        value + Core.Money.DisplayTolerance(requirement.MinAcceptedValue) < requirement.MinAcceptedValue) continue;

                    qualifying++;
                }

                chance += typeShare * qualifying / SamplesPerType;
            }

            return chance;
        }

        private static float Lerp(System.Random rng, float min, float max) =>
            min + (float)rng.NextDouble() * (max - min);
        /// <summary>Shipping zones listings come from, weighted as in Market.PickShippingZone.</summary>
        private static readonly ShippingZone[] Zones =
        {
            ShippingZone.Local, ShippingZone.Domestic, ShippingZone.International
        };

        /// <summary>Every transport service, in no particular order.</summary>
        private static readonly TransportOption[] Services =
        {
            TransportOption.Economy, TransportOption.Standard, TransportOption.Express
        };

        private static float ZoneWeight(ShippingZone zone) => zone switch
        {
            ShippingZone.Local => AntiqueTradingSimulator.Market.Market.LocalListingWeight,
            ShippingZone.Domestic => AntiqueTradingSimulator.Market.Market.DomesticListingWeight,
            ShippingZone.International => AntiqueTradingSimulator.Market.Market.InternationalListingWeight,
            _ => 0f
        };

        /// <summary>Weeks of transit this duration allows a buyer acting the week the contract appears.</summary>
        public static int TransitWeeksAvailable(int durationWeeks) => durationWeeks - 1;

        /// <summary>
        /// Cheapest service delivering from <paramref name="zone"/> within <paramref name="transitWeeks"/>,
        /// and its cost for an item at this price; false when no service is fast enough.
        /// </summary>
        public static bool TryCheapestServiceWithin(TransportSettings settings, ShippingZone zone,
            int transitWeeks, float itemPrice, out TransportOption option, out float cost)
        {
            option = TransportOption.Standard;
            cost = 0f;
            if (settings == null || transitWeeks < 1) return false;

            bool found = false;
            for (int i = 0; i < Services.Length; i++)
            {
                var candidate = Services[i];
                if (settings.GetBaseDurationWeeks(zone, candidate) > transitWeeks) continue;

                float candidateCost = settings.GetCost(itemPrice, zone, candidate);
                if (found && candidateCost >= cost) continue;

                option = candidate;
                cost = candidateCost;
                found = true;
            }

            return found;
        }

        /// <summary>
        /// Cost of delivering one piece: per zone, the cheapest service that meets the deadline, weighted
        /// by zone frequency, with zones nothing reaches in time left out of the average.
        /// </summary>
        /// <param name="safetyWeeks">Slack required of the service, given up on zones too tight for it.</param>
        public static float ExpectedShippingCost(TransportSettings settings, float itemPrice,
            int durationWeeks, int safetyWeeks)
        {
            if (settings == null || itemPrice <= 0f) return 0f;

            int transit = TransitWeeksAvailable(durationWeeks);
            if (transit < 1) return 0f;

            int relaxed = Mathf.Max(1, transit - Mathf.Max(0, safetyWeeks));

            float weighted = 0f;
            float total = 0f;

            for (int i = 0; i < Zones.Length; i++)
            {
                var zone = Zones[i];
                float weight = ZoneWeight(zone);
                if (weight <= 0f) continue;

                bool reachable = TryCheapestServiceWithin(settings, zone, relaxed, itemPrice, out _, out float cost);
                if (!reachable)
                    reachable = TryCheapestServiceWithin(settings, zone, transit, itemPrice, out _, out cost);
                if (!reachable) continue;

                weighted += weight * cost;
                total += weight;
            }

            return total > 0f ? weighted / total : 0f;
        }

        /// <summary>Share of listings, by zone weight, that Standard delivery reaches within this deadline.</summary>
        public static float StandardDeliveryReach(TransportSettings settings, int durationWeeks)
        {
            if (settings == null) return 1f;

            int transit = TransitWeeksAvailable(durationWeeks);
            if (transit < 1) return 0f;

            float reached = 0f;
            float total = 0f;

            for (int i = 0; i < Zones.Length; i++)
            {
                var zone = Zones[i];
                float weight = ZoneWeight(zone);
                if (weight <= 0f) continue;

                total += weight;
                if (settings.GetBaseDurationWeeks(zone, TransportOption.Standard) <= transit) reached += weight;
            }

            return total > 0f ? reached / total : 1f;
        }

        /// <summary>
        /// Sale price of the cheapest qualifying listing able to arrive by <paramref name="latestArrival"/>
        /// </summary>
        public static float CheapestQualifyingSalePrice(ContractRequirement requirement, Market.Market market,
            int currentWeek, int latestArrival, Func<ShippingZone, int> expressWeeks)
        {
            if (requirement == null || market == null || expressWeeks == null) return 0f;

            float cheapest = 0f;
            var listings = market.Listings;

            for (int i = 0; i < listings.Count; i++)
            {
                var listing = listings[i];
                if (listing == null || listing.OwnerId == Antique.PlayerOwnerId) continue;
                if (currentWeek + expressWeeks(listing.ShippingZone) > latestArrival) continue;
                if (!requirement.IsSatisfiedBy(listing)) continue;

                if (cheapest <= 0f || listing.SalePrice < cheapest) cheapest = listing.SalePrice;
            }

            return cheapest;
        }

    public static float MarginalQualifyingSalePrice(ContractRequirement requirement, Market.Market market,
            int currentWeek, int latestArrival, Func<ShippingZone, int> expressWeeks, int quantity)
        {
            if (requirement == null || market == null || expressWeeks == null) return 0f;

            int wanted = Mathf.Max(1, quantity);

            var cheapest = new float[wanted];
            int found = 0;

            var listings = market.Listings;
            for (int i = 0; i < listings.Count; i++)
            {
                var listing = listings[i];
                if (listing == null || listing.OwnerId == Antique.PlayerOwnerId) continue;
                if (currentWeek + expressWeeks(listing.ShippingZone) > latestArrival) continue;
                if (!requirement.IsSatisfiedBy(listing)) continue;

                float price = listing.SalePrice;
                if (found == wanted && price >= cheapest[wanted - 1]) continue;

                int at = found < wanted ? found : wanted - 1;
                while (at > 0 && cheapest[at - 1] > price)
                {
                    cheapest[at] = cheapest[at - 1];
                    at--;
                }

                cheapest[at] = price;
                if (found < wanted) found++;
            }

            return found > 0 ? cheapest[found - 1] : 0f;
        }
    }
}
