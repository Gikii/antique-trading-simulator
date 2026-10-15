using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using AntiqueTradingSimulator.Events;

namespace AntiqueTradingSimulator.UI
{
    public class InfoBarEventsUI : InfoBarListUI
    {
        [Header("Dependencies")]
        [SerializeField] private EventManager eventManager;
        protected override string EmptyLabel => "No ongoing events";

        protected override bool HasAction => true;
        protected override string ActionTooltip => "See all active events\n<size=85%>News & Events</size>";
        protected override void OnActionClicked() => InfoBarNavigation.OpenEvents();


        protected override void Awake()
        {
            base.Awake();

            if (eventManager == null) eventManager = FindFirstObjectByType<EventManager>();
        }

        protected override void SubscribeToSources()
        {
            if (eventManager == null) return;

            eventManager.OnEventTriggered += HandleActiveEventChanged;
            eventManager.OnEventEnded += HandleActiveEventChanged;
            eventManager.OnEventsRestored += Refresh;
        }

        protected override void UnsubscribeFromSources()
        {
            if (eventManager == null) return;

            eventManager.OnEventTriggered -= HandleActiveEventChanged;
            eventManager.OnEventEnded -= HandleActiveEventChanged;
            eventManager.OnEventsRestored -= Refresh;
        }

        private void HandleActiveEventChanged(EventDefinition definition, int triggerDay) => Refresh();
        private void HandleActiveEventChanged(ActiveEvent activeEvent) => Refresh();

        protected override void BuildLines(List<RowData> lines)
        {
            if (eventManager == null)
                return;

            int today = CurrentDay;

            List<ActiveEvent> active = eventManager.ActiveEvents
                .Where(a => a != null && a.Definition != null)
                .OrderByDescending(a => a.StartDay)
                .ThenBy(a => a.EndDay)
                .Take(Mathf.Max(1, maxRows))
                .ToList();

            foreach (ActiveEvent activeEvent in active)
                lines.Add(BuildLine(activeEvent, today));
        }

        private RowData BuildLine(ActiveEvent activeEvent, int today)
        {
            int daysLeft = activeEvent.EndDay - today;
            string title = activeEvent.Definition.DisplayName;

            string tooltip = $"{title}\n" +
                             $"Started {UIFormat.GameDate(activeEvent.StartDay)} · ends {UIFormat.GameDate(activeEvent.EndDay)}";

            if (!string.IsNullOrWhiteSpace(activeEvent.Definition.Description))
                tooltip += $"\n\n{activeEvent.Definition.Description}";

            return new RowData
            {
                Label = title,
                Detail = UIFormat.Colorize(
                    NewsPresentation.DaysLeft(daysLeft).ToLowerInvariant(),
                    daysLeft <= 1 ? UIFormat.NegativeColor : UIFormat.AccentColor),
                Tooltip = tooltip
            };
        }
    }
}
