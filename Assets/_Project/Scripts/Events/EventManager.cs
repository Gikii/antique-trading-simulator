using AntiqueTradingSimulator.Economy;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AntiqueTradingSimulator.Events
{
    public class EventManager : MonoBehaviour
    {
        [SerializeField] private EconomyManager economyManager;
        [SerializeField] private Core.TimeManager timeManager;

        private readonly List<ActiveEvent> _activeEvents = new List<ActiveEvent>();
        public IReadOnlyList<ActiveEvent> ActiveEvents => _activeEvents;
        private readonly Dictionary<int, EventDefinition> _scheduledByDay = new Dictionary<int, EventDefinition>();
        public IReadOnlyDictionary<int, EventDefinition> ScheduledEvents => _scheduledByDay;
        [SerializeField] private int maxScheduleAttempts = 20;


        public event Action<ActiveEvent> OnEventTriggered;
        public event Action<ActiveEvent> OnEventEnded;
        public event Action<EventDefinition, int> OnEventScheduled;

        void Awake()
        {
            if (economyManager == null) economyManager = FindFirstObjectByType<EconomyManager>();
            if (timeManager == null) timeManager = FindFirstObjectByType<Core.TimeManager>();

        }

        void OnEnable()
        {
            if (timeManager != null)
            {
                timeManager.OnDayChanged += HandleDayChanged;
                ScheduleNextRandomEvent(timeManager.CurrentDay);
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
            ScheduleNextRandomEvent(newDay);
        }

        private void ExpireFinishedEvents(int day)
        {
            for (int i = _activeEvents.Count - 1; i >= 0; i--)
            {
                var active = _activeEvents[i];
                if (!active.HasExpired(day)) continue;

                active.End(economyManager.Market, day);
                _activeEvents.RemoveAt(i);

                Debug.Log($"EventManager: event ended — {active}");
                OnEventEnded?.Invoke(active);
            }
        }

        private void TriggerScheduledEvent(int day)
        {
            if (!_scheduledByDay.TryGetValue(day, out EventDefinition definition)) return;
            _scheduledByDay.Remove(day);

            var active = new ActiveEvent(definition, day);
            active.Begin(economyManager.Market, day);

            _activeEvents.Add(active);

            Debug.Log($"EventManager: event triggered — {active.Definition.name}");
            OnEventTriggered?.Invoke(active);
        }


        public bool ScheduleEvent(EventDefinition definition, int triggerDay)
        {
            if (definition == null)
            {
                Debug.LogWarning("EventManager: tried to schedule a null EventDefinition.");
                return false;
            }

            if (_scheduledByDay.ContainsKey(triggerDay))
            {
                Debug.LogWarning($"EventManager: day {triggerDay} already has an event scheduled. '{definition.name}' was not scheduled.");
                return false;
            }

            _scheduledByDay.Add(triggerDay, definition);

            Debug.Log($"EventManager: event scheduled — {definition.name} (Day {triggerDay})");
            OnEventScheduled?.Invoke(definition, triggerDay);
            return true;
        }

        private void ScheduleNextRandomEvent(int afterDay)
        {
            List<EventDefinition> pool = EventDatabase.GetAll();
            if (pool.Count == 0) return;

            for (int attempt = 0; attempt < maxScheduleAttempts; attempt++)
            {
                EventDefinition definition = pool[UnityEngine.Random.Range(0, pool.Count)];

                int minLead = Mathf.Max(0, definition.MinLeadDays);
                int maxLead = Mathf.Max(minLead, definition.MaxLeadDays);
                int candidateDay = afterDay + UnityEngine.Random.Range(minLead, maxLead + 1);

                if (_scheduledByDay.ContainsKey(candidateDay)) continue;

                ScheduleEvent(definition, candidateDay);
                return;
            }

            Debug.LogWarning("EventManager: could not find a free day to schedule the next random event.");
        }


        public bool CancelScheduledEvent(int triggerDay)
        {
            return _scheduledByDay.Remove(triggerDay);
        }

    }
}
