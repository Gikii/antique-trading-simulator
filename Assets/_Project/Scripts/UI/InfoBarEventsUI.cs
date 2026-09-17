using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using AntiqueTradingSimulator.Events;

namespace AntiqueTradingSimulator.UI
{
    public class InfoBarEventsUI : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private EventManager eventManager;
        [SerializeField] private Core.TimeManager timeManager;

        [Header("List")]
        [SerializeField] private RectTransform listContainer;
        [SerializeField] private GameObject listItemPrefab;
        [SerializeField] private int maxEvents = 3;

        private readonly List<UpcomingEventListItemUI> _spawnedRows = new();

        void Awake()
        {
            if (eventManager == null) eventManager = FindFirstObjectByType<EventManager>();
            if (timeManager == null) timeManager = FindFirstObjectByType<Core.TimeManager>();
        }

        void OnEnable()
        {
            if (eventManager != null)
            {
                eventManager.OnEventScheduled += HandleScheduleChanged;
                eventManager.OnEventTriggered += HandleScheduleChanged;
            }

            if (timeManager != null)
                timeManager.OnDayChanged += HandleDayChanged;

            Refresh();
        }

        void OnDisable()
        {
            if (eventManager != null)
            {
                eventManager.OnEventScheduled -= HandleScheduleChanged;
                eventManager.OnEventTriggered -= HandleScheduleChanged;
            }

            if (timeManager != null)
                timeManager.OnDayChanged -= HandleDayChanged;
        }

        private void HandleScheduleChanged(EventDefinition definition, int triggerDay) => Refresh();
        private void HandleScheduleChanged(ActiveEvent activeEvent) => Refresh();
        private void HandleDayChanged(int day) => Refresh();

        public void Refresh()
        {
            ClearRows();

            if (eventManager == null || listContainer == null || listItemPrefab == null)
                return;

            List<ScheduledEvent> upcoming = eventManager.ScheduledEvents
                .Where(scheduled => scheduled.Definition != null)
                .OrderBy(scheduled => scheduled.TriggerDay)
                .Take(maxEvents)
                .ToList();

            foreach (ScheduledEvent scheduled in upcoming)
            {
                GameObject rowObject = Instantiate(listItemPrefab, listContainer);
                UpcomingEventListItemUI row = rowObject.GetComponent<UpcomingEventListItemUI>();

                if (row == null)
                {
                    Debug.LogError("InfoBarEventsUI: list item prefab is missing UpcomingEventListItemUI.", rowObject);
                    Destroy(rowObject);
                    continue;
                }

                row.Setup(scheduled.Definition.DisplayName);
                _spawnedRows.Add(row);
            }
        }

        private void ClearRows()
        {
            foreach (UpcomingEventListItemUI row in _spawnedRows)
            {
                if (row != null)
                    Destroy(row.gameObject);
            }

            _spawnedRows.Clear();
        }
    }
}
