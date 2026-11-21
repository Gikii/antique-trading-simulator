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
        [Tooltip("Lasting change: added to Demand and to the type's long-term demand memory, so it stays " +
                 "(capped, fading very slowly) instead of reverting to the baseline within months.")]
        public float permDemandChange = 0f;

        public override void Apply(EventContext context)
        {
            var market = context.Market;
            var affectedDefinitionIds = ResolveTargetDefinitionIds();
            bool anyApplied = false;

            foreach (var definitionId in affectedDefinitionIds)
            {
                var typeState = market.GetTypeState(definitionId);
                if (typeState == null) continue;
                
                float demandBefore = typeState.Demand;

                // A lasting change moves Demand now and its anchor with it (the long-term memory), so mean
                // reversion keeps it instead of erasing it within a few months. The memory is capped and
                // fades very slowly (EconomyManager → Long-term demand memory).
                typeState.Demand = Mathf.Max(0f, typeState.Demand + permDemandChange);
                typeState.AddLongTermDemand(permDemandChange, market.LongTermDemandCap);
                typeState.TempDemandMod += tempDemandChange;
                RecalculatePricesForDefinition(market, definitionId);

                anyApplied = true;
            }

            if(anyApplied && TryResolveTargetFacet(Scope, AntiqueType, Country, Century, out var facet))
                context.Dependencies?.ReportCategoryPressure(facet, permDemandChange + tempDemandChange, 0f);

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
