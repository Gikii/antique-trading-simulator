using Mono.Cecil;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AntiqueTradingSimulator.Events
{
    [Serializable]
    public class ActiveEvent
    {
        public string EventDefinitionID { get; }
        public int StartDay { get; }
        public int EndDay { get; }

        [NonSerialized] private EventDefinition _definitionCache;
        public EventDefinition Definition => _definitionCache ??= EventDatabase.GetById(EventDefinitionID);


        public List<EventEffect> EffectInstances = new List<EventEffect>();

        public ActiveEvent(EventDefinition definition, int startDay)
        {
            EventDefinitionID = definition.Id;
            _definitionCache = definition;
            StartDay = startDay;
            EndDay = startDay + Mathf.Max(1, definition.DurationDays);
        }

        public void Begin(Market.Market market, int currentDay)
        {
            foreach (var effect in Definition.Effects)
            {
                var instance = effect.Clone();
                instance.Apply(market, currentDay);
                EffectInstances.Add(instance);
            }
        }

        public void End(Market.Market market, int currentDay)
        {
            foreach (var instance in EffectInstances)
                instance.Revert(market, currentDay);
        }

        public bool HasExpired(int day) => day >= EndDay;

        public override string ToString() => $"{Definition.DisplayName} (Day {StartDay}\u2013{EndDay})";
    }

}

