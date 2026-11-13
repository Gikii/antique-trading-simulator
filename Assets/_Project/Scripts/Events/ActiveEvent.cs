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
        public int StartWeek { get; }
        public int EndWeek { get; }

        [JsonIgnore]
        [NonSerialized] private EventDefinition _definitionCache;
        public EventDefinition Definition => _definitionCache ??= EventDatabase.GetById(EventDefinitionID);


        public List<EventEffect> EffectInstances = new List<EventEffect>();

        public ActiveEvent(EventDefinition definition, int startWeek)
        {
            EventDefinitionID = definition.Id;
            _definitionCache = definition;
            StartWeek = startWeek;
            EndWeek = startWeek + Mathf.Max(1, definition.DurationWeeks);
        }

        public ActiveEvent(string eventDefinitionId, int startWeek, int endWeek)
        {
            EventDefinitionID = eventDefinitionId;
            StartWeek = startWeek;
            EndWeek = endWeek;
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

        public bool HasExpired(int week) => week >= EndWeek;

        public override string ToString() => $"{Definition.DisplayName} (Week {StartWeek}\u2013{EndWeek})";

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

