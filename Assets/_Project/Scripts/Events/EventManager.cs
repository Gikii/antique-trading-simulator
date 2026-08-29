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

        public event Action<ActiveEvent> OnEventTriggered;
        public event Action<ActiveEvent> OnEventEnded;

        void Awake()
        {
            if (economyManager == null) economyManager = FindFirstObjectByType<EconomyManager>();
            if (timeManager == null) timeManager = FindFirstObjectByType<Core.TimeManager>();

        }

        void OnEnable()
        {
            if (timeManager != null)
                timeManager.OnDayChanged += HandleDayChanged;

        }

        void OnDisable()
        {
            if (timeManager != null)
                timeManager.OnDayChanged -= HandleDayChanged;

        }
        private void HandleDayChanged(int newDay)
        {
            ExpireFinishedEvents(newDay);
            TriggerRandomEvent(newDay);
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

        private void TriggerRandomEvent(int day)
        {
            List<EventDefinition> pool = EventDatabase.GetAll();
            if (pool.Count == 0) return;

            EventDefinition definition = pool[UnityEngine.Random.Range(0, pool.Count)];

            var active = new ActiveEvent(definition, day);
            active.Begin(economyManager.Market, day);

            _activeEvents.Add(active);

            Debug.Log($"EventManager: event triggered — {active}");
            OnEventTriggered?.Invoke(active);
        }

    }
}
