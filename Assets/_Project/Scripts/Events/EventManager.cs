using AntiqueTradingSimulator.Economy;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AntiqueTradingSimulator.Events
{
    public class EventManager : MonoBehaviour
    {
        [SerializeField] private EconomyManager economyManager;
        [SerializeField] private Core.TimeManager timeManager;
        [SerializeField] private Agents.PlayerTrader playerTrader;

        private readonly List<ActiveEvent> _activeEvents = new List<ActiveEvent>();
        public IReadOnlyList<ActiveEvent> ActiveEvents => _activeEvents;
        private readonly List<ScheduledEvent> _scheduledEvents = new List<ScheduledEvent>();
        public IReadOnlyList<ScheduledEvent> ScheduledEvents => _scheduledEvents;

        /// <summary>History of events that already finished (e.g. for the calendar).</summary>
        private readonly List<ActiveEvent> _endedEvents = new List<ActiveEvent>();
        public IReadOnlyList<ActiveEvent> EndedEvents => _endedEvents;

        [SerializeField] private int maxScheduleAttempts = 20;

        /// <summary>Null-safe accessor — effects that need it (e.g. GrantAntiquesEffect) check for null themselves.</summary>
        private Economy.TraderInventory PlayerInventory => playerTrader != null ? playerTrader.Inventory : null;

        private EventContext BuildContext(int day) => new EventContext(economyManager.Market, day, PlayerInventory);


        public event Action<ActiveEvent> OnEventTriggered;
        public event Action<ActiveEvent> OnEventEnded;
        public event Action<EventDefinition, int> OnEventScheduled;
        /// <summary>Fired when a Player event rolls its FailureChance and fails — its effects never apply.</summary>
        public event Action<EventDefinition, int> OnEventFailed;

        void Awake()
        {
            if (economyManager == null) economyManager = FindFirstObjectByType<EconomyManager>();
            if (timeManager == null) timeManager = FindFirstObjectByType<Core.TimeManager>();
            if (playerTrader == null) playerTrader = FindFirstObjectByType<Agents.PlayerTrader>();

        }

        void OnEnable()
        {
            if (timeManager != null)
            {
                timeManager.OnDayChanged += HandleDayChanged;
                ScheduleNextMinorEvent(timeManager.CurrentDay);
            }

        }

        void OnDisable()
        {
            if (timeManager != null)
                timeManager.OnDayChanged -= HandleDayChanged;

        }
        private void HandleDayChanged(int newDay)
        {
            ExpireFinishedEvents(newDay);
            TriggerScheduledEvent(newDay);
            ScheduleNextMinorEvent(newDay);
        }

        private void ExpireFinishedEvents(int day)
        {
            for (int i = _activeEvents.Count - 1; i >= 0; i--)
            {
                var active = _activeEvents[i];
                if (!active.HasExpired(day)) continue;

                active.End(BuildContext(day));
                _activeEvents.RemoveAt(i);
                _endedEvents.Add(active);

                Debug.Log($"EventManager: event ended — {active}");
                OnEventEnded?.Invoke(active);
            }
        }

        private void TriggerScheduledEvent(int day)
        {
            for (int i = _scheduledEvents.Count - 1; i >= 0; i--)
            {
                var scheduled = _scheduledEvents[i];
                if (scheduled.TriggerDay != day) continue;

                _scheduledEvents.RemoveAt(i);

                var definition = scheduled.Definition;
                if (definition == null)
                {
                    Debug.Log($"EventManager: Failed to scheduled trigger event for day {day}. No EventDefinition with Id {scheduled.EventDefinitionId}");
                    continue;
                }

                if (definition.EventType == EventType.Player && UnityEngine.Random.value < definition.FailureChance)
                {
                    Debug.Log($"EventManager: player event failed — {definition.DisplayName} did not occur (Day {day}).");
                    OnEventFailed?.Invoke(definition, day);
                    continue;
                }

                var active = new ActiveEvent(definition, day);
                active.Begin(BuildContext(day));

                _activeEvents.Add(active);

                Debug.Log($"EventManager: event triggered — {active.Definition.name}");
                OnEventTriggered?.Invoke(active);
            }
        }


        public bool ScheduleEvent(EventDefinition definition, int triggerDay)
        {
            if (definition == null)
            {
                Debug.LogWarning("EventManager: tried to schedule a null EventDefinition.");
                return false;
            }

            // Different event types don't interfere with each other (a Minor and a Player event can
            // share a day), but non-player types are still limited to one of their own type per day
            // (e.g. only one Minor event per day). Player-created events are never blocked by what's
            // already on the calendar that day, including other Player events.
            if (definition.EventType != EventType.Player &&
                _scheduledEvents.Any(s => s.EventType == definition.EventType && s.TriggerDay == triggerDay))
            {
                Debug.LogWarning($"EventManager: day {triggerDay} already has a {definition.EventType} event scheduled. '{definition.name}' was not scheduled.");
                return false;
            }

            _scheduledEvents.Add(new ScheduledEvent(triggerDay, definition));

            Debug.Log($"EventManager: event scheduled — {definition.name} (Day {triggerDay})");
            OnEventScheduled?.Invoke(definition, triggerDay);
            return true;
        }

        private void ScheduleNextMinorEvent(int afterDay)
        {
            List<EventDefinition> pool = EventDatabase.GetAllByType(EventType.Minor);
            if (pool.Count == 0) return;

            for (int attempt = 0; attempt < maxScheduleAttempts; attempt++)
            {
                EventDefinition definition = pool[UnityEngine.Random.Range(0, pool.Count)];

                int minLead = Mathf.Max(0, definition.MinLeadDays);
                int maxLead = Mathf.Max(minLead, definition.MaxLeadDays);
                int candidateDay = afterDay + UnityEngine.Random.Range(minLead, maxLead + 1);

                if (_scheduledEvents.Any(s => s.EventType == EventType.Minor && s.TriggerDay == candidateDay)) continue;

                ScheduleEvent(definition, candidateDay);
                return;
            }

            Debug.LogWarning("EventManager: could not find a free day to schedule the next random event.");
        }


        public bool CancelScheduledEvent(int triggerDay)
        {
            int index = _scheduledEvents.FindIndex(s => s.TriggerDay == triggerDay);
            if (index < 0) return false;

            _scheduledEvents.RemoveAt(index);
            return true;

        }

    }
}