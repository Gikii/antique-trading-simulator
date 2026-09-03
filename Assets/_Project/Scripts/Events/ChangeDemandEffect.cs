using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.News;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.Events
{
    [Serializable]
    public class ChangeDemandEffect : EventEffect
    {
        [Tooltip("Which category this effect targets. Only the matching field below is used.")]
        public TargetScope Scope = TargetScope.AntiqueType;

        [Tooltip("Used when Scope = AntiqueType.")]
        public AntiqueType AntiqueType = AntiqueType.Other;

        [Tooltip("Used when Scope = Country.")]
        public Country Country = Country.Other;

        [Tooltip("Used when Scope = TimePeriod.")]
        public TimePeriod TimePeriod = TimePeriod.Unknown;

        [Tooltip("Temporarily added to Demand for every matching antique type. Use a negative value to lower demand.")]
        public float tempDemandChange = 1f;
        [Tooltip("Permamently added to Demand for every matching antique type.")]
        public float permDemandChange = 0f;

        public override void Apply(Market.Market market, int currentDay)
        {
            var affectedDefinitionIds = ResolveTargetDefinitionIds();

            foreach (var definitionId in affectedDefinitionIds)
            {
                var typeState = market.GetTypeState(definitionId);
                if (typeState == null) continue;

                typeState.Demand = Mathf.Max(0f, typeState.Demand + tempDemandChange + permDemandChange);
                RecalculatePricesForDefinition(market, definitionId);
            }
        }

        public override void Revert(Market.Market market, int currentDay)
        {
            var affectedDefinitionIds = ResolveTargetDefinitionIds();

            foreach (var definitionId in affectedDefinitionIds)
            {
                var typeState = market.GetTypeState(definitionId);
                if (typeState == null) continue;

                typeState.Demand = Mathf.Max(0f, typeState.Demand - tempDemandChange);
                RecalculatePricesForDefinition(market, definitionId);
            }

        }

        public override NewsEventData CreateNewsData()
        {
            return new NewsEventData(Scope, AntiqueType, Country, TimePeriod, (permDemandChange + tempDemandChange > 0) ? true : false);
        }

        public override EventEffect Clone()
        {
            return new ChangeDemandEffect
            {
                Scope = Scope,
                AntiqueType = AntiqueType,
                Country = Country,
                TimePeriod = TimePeriod,
                tempDemandChange = tempDemandChange,
                permDemandChange = permDemandChange
            };
        }

        private List<string> ResolveTargetDefinitionIds()
        {
            List<AntiqueDefinition> matches = Scope switch
            {
                TargetScope.AntiqueType => AntiqueDatabase.GetByType(AntiqueType),
                TargetScope.Country => AntiqueDatabase.GetByCountry(Country),
                TargetScope.TimePeriod => AntiqueDatabase.GetByTimePeriod(TimePeriod),
                _ => new List<AntiqueDefinition>()
            };

            return matches.Select(def => def.Id).ToList();
        }

        private static void RecalculatePricesForDefinition(Market.Market market, string definitionId)
        {
            foreach (var listing in market.GetListingsByDefinition(definitionId))
                market.RecalculatePrice(listing);
        }

    }
}
