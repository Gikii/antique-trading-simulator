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
        public int StartDay;
        public int EndDay;

        public static ActiveEventState Capture(ActiveEvent activeEvent)
        {
            if (activeEvent == null) return null;

            return new ActiveEventState
            {
                EventDefinitionId = activeEvent.EventDefinitionID,
                StartDay = activeEvent.StartDay,
                EndDay = activeEvent.EndDay
            };
        }

        public ActiveEvent Restore()
        {
            var activeEvent = new ActiveEvent(EventDefinitionId, StartDay, EndDay);
            activeEvent.RebuildEffectInstances();
            return activeEvent;
        }
    }
}
