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

        [Tooltip("Used when Scope = Century.")]
        public Century Century = Century.Unknown;

        [Tooltip("Temporarily added to Demand for every matching antique type. Use a negative value to lower demand.")]
        public float tempDemandChange = 1f;
        [Tooltip("Permamently added to Demand for every matching antique type.")]
        public float permDemandChange = 0f;

        public override void Apply(EventContext context)
        {
            var market = context.Market;
            var affectedDefinitionIds = ResolveTargetDefinitionIds();

            foreach (var definitionId in affectedDefinitionIds)
            {
                var typeState = market.GetTypeState(definitionId);
                if (typeState == null) continue;

                typeState.Demand = Mathf.Max(0f, typeState.Demand + permDemandChange);
                typeState.TempDemandMod += tempDemandChange;
                RecalculatePricesForDefinition(market, definitionId);
            }
        }

        public override void Revert(EventContext context)
        {
            var market = context.Market;
            var affectedDefinitionIds = ResolveTargetDefinitionIds();

            foreach (var definitionId in affectedDefinitionIds)
            {
                var typeState = market.GetTypeState(definitionId);
                if (typeState == null) continue;

                typeState.TempDemandMod -= tempDemandChange;
                RecalculatePricesForDefinition(market, definitionId);
            }

        }

        public override NewsEventData CreateNewsData()
        {
            return new NewsEventData(Scope, AntiqueType, Country, Century, (permDemandChange + tempDemandChange > 0) ? true : false);
        }

        public override EventEffect Clone()
        {
            return new ChangeDemandEffect
            {
                Scope = Scope,
                AntiqueType = AntiqueType,
                Country = Country,
                Century = Century,
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
                TargetScope.Century => AntiqueDatabase.GetByCentury(Century),
                _ => new List<AntiqueDefinition>()
            };

            return matches.Select(def => def.Id).ToList();
        }

        private static void RecalculatePricesForDefinition(Market.Market market, string definitionId)
        {
            // Covers both open listings and antiques already owned by traders.
            market.RecalculatePricesForDefinition(definitionId);
        }

    }
}
