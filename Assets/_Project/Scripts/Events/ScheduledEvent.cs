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
        public string InstanceId;

        [NonSerialized] private EventDefinition _definitionCache;
        public EventDefinition Definition => _definitionCache ??= EventDatabase.GetById(EventDefinitionId);

        public ScheduledEvent() { }

        public ScheduledEvent(int triggerDay, EventDefinition eventDefinition)
        {
            TriggerDay = triggerDay;
            EventDefinitionId = eventDefinition.Id;
            _definitionCache = eventDefinition;
            EventType = eventDefinition.EventType;
            InstanceId = NewInstanceId();
        }

        /// <summary>A fresh occurrence id. "N" format: 32 hex digits, no braces or dashes.</summary>
        public static string NewInstanceId() => Guid.NewGuid().ToString("N");
    }
}
