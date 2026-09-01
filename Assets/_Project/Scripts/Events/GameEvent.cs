using System;

namespace AntiqueTradingSimulator.Events
{
    /// <summary>
    /// A concrete occurrence of an EventDefinition in the running game — the "ground truth",
    /// existing whether or not anyone knows about it yet. Holds only a string
    /// EventDefinitionId, not a direct reference, so instances stay serializable for save/load.
    /// </summary>
    public class GameEvent
    {
        public string InstanceId { get; }
        public string EventDefinitionId { get; }
        public int DayOccurred { get; }
        public string SpecificAntiqueTypeId { get; }

        private EventDefinition _definitionCache;
        public EventDefinition Definition => _definitionCache ??= EventDatabase.GetById(EventDefinitionId);

        public GameEvent(EventDefinition definition, int dayOccurred, string specificAntiqueTypeId = null)
        {
            InstanceId = Guid.NewGuid().ToString();
            EventDefinitionId = definition.Id;
            DayOccurred = dayOccurred;
            SpecificAntiqueTypeId = specificAntiqueTypeId;
            _definitionCache = definition; 
        }
    }
}