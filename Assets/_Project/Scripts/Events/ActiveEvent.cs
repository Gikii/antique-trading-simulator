using Mono.Cecil;
using Newtonsoft.Json;
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

        [JsonIgnore]
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

        public ActiveEvent(string eventDefinitionId, int startDay, int endDay)
        {
            EventDefinitionID = eventDefinitionId;
            StartDay = startDay;
            EndDay = endDay;
        }


        public void Begin(EventContext context)
        {
            foreach (var effect in Definition.Effects)
            {
                var instance = effect.Clone();
                instance.Apply(context);
                EffectInstances.Add(instance);
            }
        }

        public void End(EventContext context)
        {
            foreach (var instance in EffectInstances)
                instance.Revert(context);
        }

        public bool HasExpired(int day) => day >= EndDay;

        public override string ToString() => $"{Definition.DisplayName} (Day {StartDay}\u2013{EndDay})";

        public void RebuildEffectInstances()
        {
            EffectInstances.Clear();

            var definition = Definition;
            if (definition == null)
            {
                Debug.LogWarning($"ActiveEvent: no EventDefinition with Id '{EventDefinitionID}'. The running event cannot be reverted when it ends.");
                return;
            }

            if (definition.Effects == null) return;

            foreach (var effect in definition.Effects)
            {
                if (effect == null) continue;
                EffectInstances.Add(effect.Clone());
            }
        }

    }

}

