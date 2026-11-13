using System.Collections.Generic;

namespace AntiqueTradingSimulator.Events
{
    public class EventsState
    {
        public List<ScheduledEvent> ScheduledEvents = new List<ScheduledEvent>();

        public List<ActiveEventState> ActiveEvents = new List<ActiveEventState>();

        public List<ActiveEventState> EndedEvents = new List<ActiveEventState>();
    }

    public class ActiveEventState
    {
        public string EventDefinitionId;
        public int StartWeek;
        public int EndWeek;

        public static ActiveEventState Capture(ActiveEvent activeEvent)
        {
            if (activeEvent == null) return null;

            return new ActiveEventState
            {
                EventDefinitionId = activeEvent.EventDefinitionID,
                StartWeek = activeEvent.StartWeek,
                EndWeek = activeEvent.EndWeek
            };
        }

        public ActiveEvent Restore()
        {
            var activeEvent = new ActiveEvent(EventDefinitionId, StartWeek, EndWeek);
            activeEvent.RebuildEffectInstances();
            return activeEvent;
        }
    }
}
