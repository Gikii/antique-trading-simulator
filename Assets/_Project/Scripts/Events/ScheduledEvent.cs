using System;
using UnityEngine;

namespace AntiqueTradingSimulator.Events
{
    [Serializable]
    public class ScheduledEvent
    {
        public int TriggerDay;
        public string EventDefinitionId;

        [NonSerialized] private EventDefinition _definitionCache;
        public EventDefinition Definition => _definitionCache ??= EventDatabase.GetById(EventDefinitionId);

        public ScheduledEvent() { }

        public ScheduledEvent(int triggerDay, EventDefinition eventDefinition)
        {
            TriggerDay = triggerDay;
            EventDefinitionId = eventDefinition.Id;
            _definitionCache = eventDefinition;
        }

    }
}
