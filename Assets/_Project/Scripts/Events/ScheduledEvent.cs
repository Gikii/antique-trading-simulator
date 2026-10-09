using Newtonsoft.Json;
using System;
using UnityEngine;

namespace AntiqueTradingSimulator.Events
{
    [Serializable]
    public class ScheduledEvent
    {
        public int TriggerDay;
        public string EventDefinitionId;
        public EventType EventType;

        [NonSerialized] private EventDefinition _definitionCache;

        [JsonIgnore]
        public EventDefinition Definition => _definitionCache ??= EventDatabase.GetById(EventDefinitionId);

        public ScheduledEvent() { }

        public ScheduledEvent(int triggerDay, EventDefinition eventDefinition)
        {
            TriggerDay = triggerDay;
            EventDefinitionId = eventDefinition.Id;
            _definitionCache = eventDefinition;
            EventType = eventDefinition.EventType;
        }

    }
}
