using System.Collections.Generic;
using AntiqueTradingSimulator.Events;
using AntiqueTradingSimulator.News;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>Kind of a week entry. The order is the calendar legend order.</summary>
    public enum WeekEntryKind { Official, Rumour, Leaked, Event }

    /// <summary>One thing the player knows about a week: a published news item or a running event.</summary>
    public readonly struct WeekEntry
    {
        public readonly WeekEntryKind Kind;

        /// <summary>Set for news entries, null for events.</summary>
        public readonly NewsItem News;

        /// <summary>Set for event entries, null for news.</summary>
        public readonly ActiveEvent Event;

        private WeekEntry(WeekEntryKind kind, NewsItem news, ActiveEvent activeEvent)
        {
            Kind = kind;
            News = news;
            Event = activeEvent;
        }

        public static WeekEntry ForNews(NewsItem news) => new WeekEntry(WeekEntries.KindOf(news.Type), news, null);

        public static WeekEntry ForEvent(ActiveEvent activeEvent) => new WeekEntry(WeekEntryKind.Event, null, activeEvent);

        public bool IsEvent => Event != null;

        public string Title => Event != null
            ? (Event.Definition != null ? Event.Definition.DisplayName : "Unknown event")
            : NewsPresentation.GetTitle(News);
    }

    /// <summary>
    /// Collects what the player knows about a game week. Shared by the calendar (markers and the
    /// list under it) and the week view of the news list, so both always show the same entries.
    /// </summary>
    public static class WeekEntries
    {
        /// <summary>
        /// Events running in that week (active or already ended), then news published in that week
        /// which the player can see with their access level. News stay listed even after their event
        /// started – this is the history of the week, not the list of upcoming events.
        /// Scheduled (future) events are intentionally left out – the player learns about them only through news.
        /// </summary>
        public static void Collect(
            int week,
            EventManager eventManager,
            NewsManager newsManager,
            int currentWeek,
            List<WeekEntry> result)
        {
            result.Clear();

            if (eventManager != null)
            {
                AddEvents(eventManager.ActiveEvents, week, result);
                AddEvents(eventManager.EndedEvents, week, result);
            }

            if (newsManager != null)
            {
                foreach (NewsItem news in newsManager.PublishedNews)
                {
                    if (news == null || news.WeekPublished != week || !NewsPresentation.CanSee(news, currentWeek))
                        continue;

                    result.Add(WeekEntry.ForNews(news));
                }
            }
        }

        private static void AddEvents(IReadOnlyList<ActiveEvent> events, int week, List<WeekEntry> result)
        {
            if (events == null) return;

            foreach (ActiveEvent activeEvent in events)
            {
                // EndWeek is exclusive (the event expires at the start of EndWeek).
                if (activeEvent == null || week < activeEvent.StartWeek || week >= activeEvent.EndWeek) continue;

                result.Add(WeekEntry.ForEvent(activeEvent));
            }
        }

        /// <summary>Fake news look like official news to the player.</summary>
        public static WeekEntryKind KindOf(NewsType type) => type switch
        {
            NewsType.Rumor => WeekEntryKind.Rumour,
            NewsType.Leak => WeekEntryKind.Leaked,
            _ => WeekEntryKind.Official
        };

        public static string LabelOf(WeekEntryKind kind) => kind switch
        {
            WeekEntryKind.Official => NewsPresentation.TypeLabel(NewsType.Official),
            WeekEntryKind.Rumour => NewsPresentation.TypeLabel(NewsType.Rumor),
            WeekEntryKind.Leaked => NewsPresentation.TypeLabel(NewsType.Leak),
            _ => "Event"
        };
    }
}
