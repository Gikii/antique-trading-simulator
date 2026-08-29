using AntiqueTradingSimulator.Market;
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
        public enum TargetScope
        {
            AntiqueType,
            Country,
            TimePeriod
        }

        [Tooltip("Which category this effect targets. Only the matching field below is used.")]
        public TargetScope Scope = TargetScope.AntiqueType;

        [Tooltip("Used when Scope = AntiqueType.")]
        public AntiqueType AntiqueType = AntiqueType.Other;

        [Tooltip("Used when Scope = Country.")]
        public Country Country = Country.Other;

        [Tooltip("Used when Scope = TimePeriod.")]
        public TimePeriod TimePeriod = TimePeriod.Unknown;

        [Tooltip("Added to Demand for every matching antique type. Use a negative value to lower demand.")]
        public float DemandChange = 1f;

        // Runtime-only: which definitions this specific instance actually touched,
        // resolved once at Apply time so Revert undoes exactly the same set even if
        // the database changes in between (it won't at runtime, but this is cheap and safe).
        [NonSerialized] private List<string> _affectedDefinitionIds;

        public override void Apply(Market.Market market, int currentDay)
        {
            _affectedDefinitionIds = ResolveTargetDefinitionIds();

            foreach (var definitionId in _affectedDefinitionIds)
            {
                var typeState = market.GetTypeState(definitionId);
                if (typeState == null) continue;

                typeState.Demand = Mathf.Max(0f, typeState.Demand + DemandChange);
            }
        }

        public override void Revert(Market.Market market, int currentDay)
        {
            if (_affectedDefinitionIds == null) return;

            foreach (var definitionId in _affectedDefinitionIds)
            {
                var typeState = market.GetTypeState(definitionId);
                if (typeState == null) continue;

                typeState.Demand = Mathf.Max(0f, typeState.Demand - DemandChange);
            }
        }

        public override EventEffect Clone()
        {
            return new ChangeDemandEffect
            {
                Scope = Scope,
                AntiqueType = AntiqueType,
                Country = Country,
                TimePeriod = TimePeriod,
                DemandChange = DemandChange
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

    }
}
