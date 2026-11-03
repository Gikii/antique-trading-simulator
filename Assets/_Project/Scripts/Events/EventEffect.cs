using AntiqueTradingSimulator.Dependency;
using AntiqueTradingSimulator.News;
using System;
using UnityEngine;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.Events
{
    [Serializable]
    public abstract class EventEffect
    {
        public enum TargetScope
        {
            AntiqueType,
            Country,
            Century,
            Other
        }

        protected static bool TryResolveTargetFacet(TargetScope scope, AntiqueType antiqueType,
                                            Country country, Century century, out MarketFacet facet)
        {
            switch (scope)
            {
                case TargetScope.AntiqueType: facet = MarketFacet.Of(antiqueType); return true;
                case TargetScope.Country: facet = MarketFacet.Of(country); return true;
                case TargetScope.Century: facet = MarketFacet.Of(century); return true;
                default: facet = default; return false;
            }
        }

        public abstract void Apply(EventContext context);
        public abstract void Revert(EventContext context);

        public abstract NewsEventData CreateNewsData();
        public abstract EventEffect Clone();
    }
}
