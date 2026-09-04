using System.Collections.Generic;
using UnityEngine;

namespace AntiqueTradingSimulator.Events
{
    public class ActiveEvent
    {
        public EventDefinition Definition { get; }
        public int StartDay { get; }
        public int EndDay { get; }

        private readonly List<EventEffect> _effectInstances = new List<EventEffect>();

        public ActiveEvent(EventDefinition definition, int startDay)
        {
            Definition = definition;
            StartDay = startDay;
            EndDay = startDay + Mathf.Max(1, definition.DurationDays);
        }

        public void Begin(Market.Market market, int currentDay)
        {
            foreach (var effect in Definition.Effects)
            {
                var instance = effect.Clone();
                instance.Apply(market, currentDay);
                _effectInstances.Add(instance);
            }
        }

        public void End(Market.Market market, int currentDay)
        {
            foreach (var instance in _effectInstances)
                instance.Revert(market, currentDay);
        }

        public bool HasExpired(int day) => day >= EndDay;

        public override string ToString() => $"{Definition.DisplayName} (Day {StartDay}\u2013{EndDay})";
    }

}

