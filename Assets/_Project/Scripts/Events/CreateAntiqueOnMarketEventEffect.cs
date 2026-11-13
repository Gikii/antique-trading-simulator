using UnityEngine;
using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.News;
using System;
using System.Collections.Generic;
using System.Linq;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.Events
{

    [Serializable]
    public class CreateAntiqueOnMarketEventEffect : EventEffect
    {
        [Tooltip("Minimum number of antiques put on the market.")]
        public int MinCount = 1;
        [Tooltip("Maximum number of antiques put on the market.")]
        public int MaxCount = 1;

        [Tooltip("Type of the antique. Leave at Other to pick a type at random for each one.")]
        public AntiqueType AntiqueType = AntiqueType.Other;

        [Tooltip("Century the antique comes from. Leave at Unknown to pick a century at random for each one.")]
        public Century Century = Century.Unknown;

        [Tooltip("Country the antique comes from. Leave at Other to pick a country at random for each one.")]
        public Country Country = Country.Other;

        [Tooltip("Condition every created antique gets. 0 = roll it within the normal range.")]
        [Range(0f, Antique.MaxCondition)]
        public float Condition = 0f;

        [Tooltip("Price variance unrelated to condition. 0 = roll it within the normal range.")]
        [Range(0f, Antique.MaxPriceFactor)]
        public float PriceFactor = 0f;

        [Header("Seller")]
        [Tooltip("If true, each listing's shipping zone is rolled with the market's usual weights. If false, every one of them ships from the zone below.")]
        public bool RandomizeShippingZone = true;
        public ShippingZone ShippingZone = ShippingZone.Domestic;

        [Tooltip("If true, each new offer raises its type's supply like any other item put up for sale.")]
        public bool AddSupplyPressure = false;

        public override void Apply(EventContext context)
        {
            var market = context.Market;
            if (market == null)
            {
                Debug.LogWarning("CreateAntiqueOnMarketEventEffect: no market available — nothing was listed.");
                return;
            }

            int min = Mathf.Max(0, Mathf.Min(MinCount, MaxCount));
            int max = Mathf.Max(min, MaxCount);
            int count = UnityEngine.Random.Range(min, max + 1);

            int created = 0;
            for (int i = 0; i < count; i++)
            {
                if (CreateOne(market, context.CurrentWeek))
                    created++;
            }

            Debug.Log($"CreateAntiqueOnMarketEventEffect: put {created}/{count} antique(s) on the market.");
        }

        public override void Revert(EventContext context)
        {
        }

        public override NewsEventData CreateNewsData()
        {
            return new NewsEventData(NewsScope(), AntiqueType, Country, Century, false);
        }

        public override EventEffect Clone()
        {
            return new CreateAntiqueOnMarketEventEffect
            {
                MinCount = MinCount,
                MaxCount = MaxCount,
                AntiqueType = AntiqueType,
                Century = Century,
                Country = Country,
                Condition = Condition,
                PriceFactor = PriceFactor,
                RandomizeShippingZone = RandomizeShippingZone,
                ShippingZone = ShippingZone,
                AddSupplyPressure = AddSupplyPressure
            };
        }

        private bool CreateOne(Market.Market market, int currentWeek)
        {
            AntiqueDefinition definition = PickDefinition();
            if (definition == null) return false;

            float condition = Condition > 0f
                ? Antique.QuantizeCondition(Condition)
                : Antique.RandomCondition();

            float priceFactor = PriceFactor > 0f
                ? Mathf.Clamp(PriceFactor, Antique.MinPriceFactor, Antique.MaxPriceFactor)
                : UnityEngine.Random.Range(Antique.MinPriceFactor, Antique.MaxPriceFactor);

            Logistics.ShippingZone zone = RandomizeShippingZone
                ? Market.Market.RollShippingZone()
                : ShippingZone;

            return market.CreateListing(definition.Id, condition, priceFactor, zone, currentWeek, AddSupplyPressure) != null;
        }

        private AntiqueDefinition PickDefinition()
        {
            List<AntiqueDefinition> pool = AntiqueDatabase.GetAll();

            if (AntiqueType != AntiqueType.Other)
                pool = pool.Where(d => d.Type == AntiqueType).ToList();
            if (Century != Century.Unknown)
                pool = pool.Where(d => d.Century == Century).ToList();
            if (Country != Country.Other)
                pool = pool.Where(d => d.Country == Country).ToList();

            if (pool.Count == 0)
            {
                Debug.LogWarning("CreateAntiqueOnMarketEventEffect: no AntiqueDefinition matches the configured " +
                                 "attributes — skipping one item.");
                return null;
            }

            return pool[UnityEngine.Random.Range(0, pool.Count)];
        }

        private TargetScope NewsScope()
        {
            if (AntiqueType != AntiqueType.Other) return TargetScope.AntiqueType;
            if (Country != Country.Other) return TargetScope.Country;
            if (Century != Century.Unknown) return TargetScope.Century;
            return TargetScope.Other;
        }
    }
}
