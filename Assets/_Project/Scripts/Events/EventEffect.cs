using UnityEngine;
using System;
using AntiqueTradingSimulator.News;

namespace AntiqueTradingSimulator.Events
{
    [Serializable]
    public abstract class EventEffect
    {
        public enum TargetScope
        {
            AntiqueType,
            Country,
            TimePeriod,
            Other
        }
        public abstract void Apply(Market.Market market, int currentDay);
        public abstract void Revert(Market.Market market, int currentDay);

        public abstract NewsEventData CreateNewsData();
        public abstract EventEffect Clone();
    }
}
