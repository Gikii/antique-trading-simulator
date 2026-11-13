using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Seasons;
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
        [SerializeField] private Logistics.TransportManager transportManager;
        [SerializeField] private Dependency.DependencyManager dependencyManager;
        [SerializeField] private Contracts.ContractManager contractManager;

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

        private EventContext BuildContext(int day) => new EventContext(economyManager.Market, day, PlayerInventory, transportManager, dependencyManager, contractManager);


        public event Action<ActiveEvent> OnEventTriggered;
        public event Action<ActiveEvent> OnEventEnded;
        public event Action<EventDefinition, int> OnEventScheduled;
        /// <summary>Fired when a Player event rolls its FailureChance and fails — its effects never apply.</summary>
        public event Action<EventDefinition, int> OnEventFailed;

        public event Action OnEventsRestored;

        void Awake()
        {
            if (economyManager == null) economyManager = FindFirstObjectByType<EconomyManager>();
            if (timeManager == null) timeManager = FindFirstObjectByType<Core.TimeManager>();
            if (playerTrader == null) playerTrader = FindFirstObjectByType<Agents.PlayerTrader>();
            if (transportManager == null) transportManager = FindFirstObjectByType<Logistics.TransportManager>();
            if(dependencyManager == null) dependencyManager = FindFirstObjectByType<Dependency.DependencyManager>();
            if (contractManager == null) contractManager = FindFirstObjectByType<Contracts.ContractManager>();
        }

        void OnEnable()
        {
            if (timeManager != null)
            {
                timeManager.OnDayChanged += HandleDayChanged;
                ScheduleAnnualEvents(timeManager.CurrentDay);
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
            ScheduleAnnualEvents(newDay);
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
            // Annual events are fixed in the calendar and always happen, so nothing can block them.
            if (definition.EventType != EventType.Player && !definition.Annual &&
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

        /// <summary>
        /// Puts every annual event on the calendar once its date is within its Max Lead weeks — every year,
        /// whatever else is scheduled that week. Safe to call every week: an occurrence that is already
        /// scheduled, running or over is never added twice.
        /// </summary>
        private void ScheduleAnnualEvents(int currentWeek)
        {
            if (timeManager == null) return;

            foreach (var definition in EventDatabase.GetAll())
            {
                if (definition == null || !definition.Annual) continue;

                int lead = Mathf.Max(1, definition.MaxLeadDays);
                int year = timeManager.DayToDate(currentWeek).Year;

                // This year's date, or next year's if this one is already behind us.
                for (int y = year; y <= year + 1; y++)
                {
                    int week = timeManager.DateToDay(AnnualDate(definition, y));
                    if (week <= currentWeek) continue;          // already happened (or happening) — handled
                    if (week - currentWeek > lead) break;       // too early to announce
                    if (IsOnCalendar(definition, week)) break;

                    ScheduleEvent(definition, week);
                    break;
                }
            }
        }

        private static DateTime AnnualDate(EventDefinition definition, int year)
        {
            int month = Mathf.Clamp(definition.AnnualMonth, 1, 12);
            int day = Mathf.Clamp(definition.AnnualDay, 1, DateTime.DaysInMonth(year, month));
            return new DateTime(year, month, day);
        }

        private bool IsOnCalendar(EventDefinition definition, int week) =>
            _scheduledEvents.Any(s => s.EventDefinitionId == definition.Id && s.TriggerDay == week) ||
            _activeEvents.Any(a => a.EventDefinitionID == definition.Id && a.StartDay == week) ||
            _endedEvents.Any(a => a.EventDefinitionID == definition.Id && a.StartDay == week);

        private void ScheduleNextMinorEvent(int afterDay)
        {
            List<EventDefinition> pool = EventDatabase.GetAllByType(EventType.Minor).Where(d => !d.Annual).ToList();
            if (pool.Count == 0) return;

            for (int attempt = 0; attempt < maxScheduleAttempts; attempt++)
            {
                EventDefinition definition = pool[UnityEngine.Random.Range(0, pool.Count)];

                int minLead = Mathf.Max(0, definition.MinLeadDays);
                int maxLead = Mathf.Max(minLead, definition.MaxLeadDays);
                int candidateDay = afterDay + UnityEngine.Random.Range(minLead, maxLead + 1);

                // Seasonal events (winter storms, the summer exhibition…) only fall in their own seasons.
                if (timeManager != null && !definition.AllowedSeasons.Contains(timeManager.SeasonOf(candidateDay))) continue;

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

        // ---------------------------------------------------------------- save / load

        public EventsState CaptureState()
        {
            var state = new EventsState { ScheduledEvents = new List<ScheduledEvent>(_scheduledEvents) };

            foreach (var active in _activeEvents)
            {
                if (active == null) continue;
                state.ActiveEvents.Add(ActiveEventState.Capture(active));
            }

            foreach (var ended in _endedEvents)
            {
                if (ended == null) continue;
                state.EndedEvents.Add(ActiveEventState.Capture(ended));
            }

            return state;
        }

        public void RestoreState(EventsState state)
        {
            _scheduledEvents.Clear();
            _activeEvents.Clear();
            _endedEvents.Clear();

            if (state == null)
            {
                OnEventsRestored?.Invoke();
                return;
            }

            if (state.ScheduledEvents != null)
            {
                foreach (var scheduled in state.ScheduledEvents)
                {
                    if (scheduled == null) continue;

                    if (scheduled.Definition == null)
                    {
                        Debug.LogWarning($"EventManager: scheduled event referenced EventDefinition '{scheduled.EventDefinitionId}', which no longer exists — dropped from the calendar.");
                        continue;
                    }

                    _scheduledEvents.Add(scheduled);
                }
            }

            RestoreInto(_activeEvents, state.ActiveEvents);
            RestoreInto(_endedEvents, state.EndedEvents);

            OnEventsRestored?.Invoke();
        }

        private static void RestoreInto(List<ActiveEvent> target, List<ActiveEventState> saved)
        {
            if (saved == null) return;

            foreach (var activeState in saved)
            {
                var active = activeState?.Restore();
                if (active != null) target.Add(active);
            }
        }
    }
}