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
            Century,
            Other
        }

        public abstract void Apply(EventContext context);
        public abstract void Revert(EventContext context);

        public abstract NewsEventData CreateNewsData();
        public abstract EventEffect Clone();
    }
}
