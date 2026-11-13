using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using AntiqueTradingSimulator.Auctions;
using AntiqueTradingSimulator.Events;

namespace AntiqueTradingSimulator.UI
{
    public class InfoBarEventsUI : InfoBarListUI
    {
        [Header("Dependencies")]
        [SerializeField] private EventManager eventManager;
        [Tooltip("Auto-found if empty. The next auction (and today's result) is listed above the events.")]
        [SerializeField] private AuctionManager auctionManager;
        protected override string EmptyLabel => "No ongoing events";

        protected override bool HasAction => true;
        protected override string ActionTooltip => "See all active events\n<size=85%>News & Events</size>";
        protected override void OnActionClicked() => InfoBarNavigation.OpenEvents();


        protected override void Awake()
        {
            base.Awake();

            if (eventManager == null) eventManager = FindFirstObjectByType<EventManager>();
            if (auctionManager == null) auctionManager = FindFirstObjectByType<AuctionManager>();
        }

        protected override void SubscribeToSources()
        {
            if (auctionManager != null)
            {
                auctionManager.OnAuctionAnnounced += HandleAuctionChanged;
                auctionManager.OnJoinWindowOpened += HandleAuctionChanged;
                auctionManager.OnJoinWindowClosed += HandleJoinWindowClosed;
                auctionManager.OnAuctionFinished += HandleAuctionChanged;
                auctionManager.OnAuctionsRestored += Refresh;
            }

            if (eventManager == null) return;

            eventManager.OnEventTriggered += HandleActiveEventChanged;
            eventManager.OnEventEnded += HandleActiveEventChanged;
            eventManager.OnEventsRestored += Refresh;
        }

        protected override void UnsubscribeFromSources()
        {
            if (auctionManager != null)
            {
                auctionManager.OnAuctionAnnounced -= HandleAuctionChanged;
                auctionManager.OnJoinWindowOpened -= HandleAuctionChanged;
                auctionManager.OnJoinWindowClosed -= HandleJoinWindowClosed;
                auctionManager.OnAuctionFinished -= HandleAuctionChanged;
                auctionManager.OnAuctionsRestored -= Refresh;
            }

            if (eventManager == null) return;

            eventManager.OnEventTriggered -= HandleActiveEventChanged;
            eventManager.OnEventEnded -= HandleActiveEventChanged;
            eventManager.OnEventsRestored -= Refresh;
        }

        private void HandleActiveEventChanged(EventDefinition definition, int triggerWeek) => Refresh();
        private void HandleActiveEventChanged(ActiveEvent activeEvent) => Refresh();
        private void HandleAuctionChanged(Auction auction) => Refresh();
        private void HandleJoinWindowClosed(Auction auction, bool joined) => Refresh();

        protected override void BuildLines(List<RowData> lines)
        {
            int thisWeek = CurrentWeek;

            AddAuctionLines(lines, thisWeek);

            if (eventManager == null)
                return;

            List<ActiveEvent> active = eventManager.ActiveEvents
                .Where(a => a != null && a.Definition != null)
                .OrderByDescending(a => a.StartWeek)
                .ThenBy(a => a.EndWeek)
                .Take(Mathf.Max(1, maxRows))
                .ToList();

            foreach (ActiveEvent activeEvent in active)
                lines.Add(BuildLine(activeEvent, thisWeek));
        }

        /// <summary>Today's result (if an auction was held today), then the next auction.</summary>
        private void AddAuctionLines(List<RowData> lines, int thisWeek)
        {
            if (auctionManager == null) return;

            var last = auctionManager.History.Count > 0 ? auctionManager.History[0] : null;
            if (last != null && last.Week == thisWeek)
            {
                int won = last.Lots.Count(l => l.IsSold && AuctionPresentation.IsPlayer(l.WinnerId));
                string detail = won > 0
                    ? UIFormat.Colorize(won == 1 ? "you won 1 lot" : $"you won {won} lots", UIFormat.PositiveColor)
                    : UIFormat.Colorize($"{last.SoldCount}/{last.Lots.Count} sold", UIFormat.MutedColor);

                lines.Add(new RowData
                {
                    Label = $"{AuctionPresentation.Title(last)} results",
                    Detail = detail,
                    Tooltip = string.Join("\n", last.Lots.Select(l => l.IsSold
                        ? $"{AuctionPresentation.LotTitle(l)} — {AuctionPresentation.Winner(l)}, {UIFormat.Money(l.HammerPrice)}"
                        : $"{AuctionPresentation.LotTitle(l)} — unsold"))
                });
            }

            var next = auctionManager.Upcoming.FirstOrDefault(a => !a.IsFinished);
            if (next == null) return;

            string when;
            Color color = UIFormat.AccentColor;
            switch (next.Status)
            {
                case AuctionStatus.JoinWindow:
                    when = "starting · join now";
                    color = UIFormat.PositiveColor;
                    break;
                case AuctionStatus.Live:
                    when = "in progress";
                    color = UIFormat.PositiveColor;
                    break;
                default:
                    when = AuctionPresentation.When(next, thisWeek);
                    break;
            }

            lines.Add(new RowData
            {
                Label = $"{AuctionPresentation.Title(next)} · {next.Lots.Count} lots",
                Detail = UIFormat.Colorize(when, color),
                Tooltip = $"{AuctionPresentation.Title(next)} · {AuctionPresentation.Date(next)}\n" +
                          string.Join("\n", next.Lots.Select(l => $"{AuctionPresentation.LotTitle(l)} · est. {AuctionPresentation.Estimate(l)}")) +
                          "\n\nSee the catalog in the Auctions tab."
            });
        }

        private RowData BuildLine(ActiveEvent activeEvent, int thisWeek)
        {
            int weeksLeft = activeEvent.EndWeek - thisWeek;
            string title = activeEvent.Definition.DisplayName;

            string tooltip = $"{title}\n" +
                             $"Started {UIFormat.GameDate(activeEvent.StartWeek)} · ends {UIFormat.WeekEndDate(activeEvent.EndWeek - 1)}";

            if (!string.IsNullOrWhiteSpace(activeEvent.Definition.Description))
                tooltip += $"\n\n{Helpers.TextHelper.Resolve(activeEvent.Definition.Description, future: false)}";

            return new RowData
            {
                Label = title,
                Detail = UIFormat.Colorize(
                    NewsPresentation.WeeksLeft(weeksLeft).ToLowerInvariant(),
                    weeksLeft <= 1 ? UIFormat.NegativeColor : UIFormat.AccentColor),
                Tooltip = tooltip
            };
        }
    }
}
