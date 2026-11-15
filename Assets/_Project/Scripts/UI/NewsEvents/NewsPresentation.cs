using AntiqueTradingSimulator.Events;
using AntiqueTradingSimulator.Helpers;
using AntiqueTradingSimulator.News;
using System.Collections.Generic;
using UnityEngine;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Shared UI wording and colors for news and events, so the news list,
    /// calendar and details panel describe the same thing in the same way.
    /// </summary>
    public static class NewsPresentation
    {
        // ------------------------------------------------------------------
        // Colors (same as the calendar legend)
        // ------------------------------------------------------------------

        public static readonly Color OfficialColor = new Color32(0x59, 0xB8, 0x73, 0xFF);
        public static readonly Color RumourColor = new Color32(0xE6, 0xB2, 0x40, 0xFF);
        public static readonly Color LeakedColor = new Color32(0x99, 0x6B, 0xD9, 0xFF);
        public static readonly Color EventColor = new Color32(0xD1, 0x4C, 0x4C, 0xFF);

        public static Color TypeColor(NewsType type) => type switch
        {
            NewsType.Rumor or NewsType.Fake => RumourColor,
            NewsType.Leak => LeakedColor,
            _ => OfficialColor
        };

        /// <summary>Colour of the type the player sees.</summary>
        public static Color TypeColor(NewsItem item) => TypeColor(DisplayType(item));

        // ------------------------------------------------------------------
        // Labels
        // ------------------------------------------------------------------

        /// <summary>"Official" / "Rumour" / "Leaked" / "Planted"</summary>
        public static string TypeLabel(NewsType type) => type switch
        {
            NewsType.Official => "Official",
            NewsType.Rumor => "Rumour",
            NewsType.Leak => "Leaked",
            NewsType.Fake => "Planted",
            _ => type.ToString()
        };

        /// <summary>
        /// The type the player sees: a fake planted by someone else shows as a rumour;
        /// only the player's own fakes show as Fake ("Planted").
        /// </summary>
        public static NewsType DisplayType(NewsItem item)
        {
            if (item == null) return NewsType.Official;
            return item.Type == NewsType.Fake && !item.PlantedByPlayer ? NewsType.Rumor : item.Type;
        }

        /// <summary>Type label for the player, with " · Debunked" once a fake's claimed date has passed.</summary>
        public static string TypeLabel(NewsItem item)
        {
            string label = TypeLabel(DisplayType(item));
            return item != null && item.Debunked ? label + " · Debunked" : label;
        }

        /// <summary>Rumours filter: rumours, NPC fakes and the player's planted rumours.</summary>
        public static bool IsRumour(NewsItem item)
        {
            var type = DisplayType(item);
            return type == NewsType.Rumor || type == NewsType.Fake;
        }

        /// <summary>e.g. "Clock gaining market interest". Rumour wording lives in NewsWording.</summary>
        public static string GetTitle(NewsItem item, TextBudget budget = TextBudget.Normal)
        {
            if (item == null) return "Market information";

            if (item.NewsData == null || item.NewsData.Count == 0)
                return string.IsNullOrWhiteSpace(item.OfficialName) ? "Market information" : item.OfficialName;

            if (item.Type == NewsType.Official)
                return budget == TextBudget.Tight
                    ? item.OfficialName
                    : $"{item.OfficialName} official announcement";

            return NewsWording.Headline(item.NewsData, budget);
        }

        /// <summary>e.g. "Clock ↑ • France ↓"</summary>
        public static string GetPreview(NewsItem item, TextBudget budget = TextBudget.Normal)
        {
            if (item == null) return "";

            if (item.NewsData == null || item.NewsData.Count == 0)
                return string.IsNullOrWhiteSpace(item.EventDescription)
                    ? "No additional information available."
                    : item.EventDescription;

            return ColorizeArrows(NewsWording.Chips(item.NewsData, budget));
        }

        /// <summary>The details panel's rows as text, for surfaces without row widgets.</summary>
        public static string GetLines(NewsItem item) =>
            item == null || item.NewsData == null || item.NewsData.Count == 0
                ? ""
                : ColorizeArrows(NewsWording.Lines(item.NewsData));

        public static string ColorizeArrows(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;

            return text
                .Replace("↑", UIFormat.Colorize("↑", UIFormat.PositiveColor))
                .Replace("↓", UIFormat.Colorize("↓", UIFormat.NegativeColor));
        }


        /// <summary>What the news is about: antique type, country, century or the market in general.</summary>
        public static string GetSubject(NewsEventData data) => NewsWording.Subject(data);

        /// <summary>"Clock ↑"</summary>
        public static string GetSubjectWithArrow(NewsEventData data) => NewsWording.SubjectWithArrow(data);

        // ------------------------------------------------------------------
        // Longer texts (details panel)
        // ------------------------------------------------------------------

        public static string GetNewsDescription(NewsItem item)
        {
            // News that isn't about an event's market effects (e.g. a weather forecast) carries its own text.
            if ((item.NewsData == null || item.NewsData.Count == 0) && !string.IsNullOrWhiteSpace(item.EventDescription))
                return item.Type == NewsType.Official
                    ? item.EventDescription
                    : $"{item.EventDescription} Credibility: {UIFormat.Percent(item.Credibility)}.";

            if (item.Type == NewsType.Official)
            {
                return string.IsNullOrWhiteSpace(item.EventDescription)
                    ? DescribeEffects(item.NewsData)
                    : TextHelper.Resolve(item.EventDescription, future: true);
            }

            // A planted rumour reads exactly like a genuine one: no branch on Fake here. What marks
            // the player's own is the badge and the note, not the words of the news itself.
            string intro = item.Type switch
            {
                NewsType.Official => NewsWording.OfficialIntro,
                NewsType.Rumor or NewsType.Fake => NewsWording.RumourIntro,
                NewsType.Leak => NewsWording.LeakIntro,
                _ => "New market information is available."
            };

            return intro + " " + DescribeEffects(item.NewsData);
        }

        public static string GetEventDescription(EventDefinition definition, IReadOnlyList<NewsEventData> effects)
        {
            return $"{definition.DisplayName} is currently in progress. {TextHelper.Resolve(definition.Description, future: false)}";
        }

        /// <summary>"Increase in prices is expected in Clock and France." Wording lives in NewsWording.</summary>
        public static string DescribeEffects(IReadOnlyList<NewsEventData> effects) =>
            NewsWording.DescribeEffects(effects);

        public static string GetNote(NewsType type) => type switch
        {
            NewsType.Official => "This is official information. It is confirmed and publicly available to all market participants.",
            NewsType.Rumor => "This is a rumour. Its credibility is uncertain and it may turn out to be false. The actual market effects depend on how other market participants react to this information.",
            NewsType.Leak => "This is a leak. The information is likely true, but it has not been officially announced yet, so most of the market doesn't know about it.",
            _ => ""
        };

        /// <summary>Note for one news item: debunked, planted by the player, or flagged by the player's experts.</summary>
        public static string GetNote(NewsItem item)
        {
            if (item == null) return "";

            if (item.Debunked)
                return "This rumour was false. Its claimed date passed and nothing happened.";

            if (item.IsDebunkNotice)
                return "This notice reached every trader. Traders who believed the rumour have dropped it.";

            // The news text is identical to a genuine rumour's, so what the player knows about their
            // own rumour is said here, outside the wording.
            if (item.Type == NewsType.Fake && item.PlantedByPlayer)
                return $"You planted this rumour (credibility {UIFormat.Percent(item.Credibility)}). " +
                       "Every trader receives it in the week it spreads. It is debunked when the claimed date passes.";

            string note = GetNote(DisplayType(item));
            return item.PlayerSuspectsFake ? note + " Your experts suspect this rumour is fabricated." : note;
        }

        public const string EventNote =
            "This event is in progress. Its effects will influence the market until it ends.";

        // ------------------------------------------------------------------
        // Dates
        // ------------------------------------------------------------------

        /// <summary>
        /// "14 May 1884" (the Monday the week starts on — news and events happen at the start of a
        /// week) when a TimeManager is available, otherwise "Week 23".
        /// </summary>
        public static string FormatWeek(int week, Core.TimeManager timeManager)
        {
            return timeManager != null
                ? Core.TimeManager.FormatLong(timeManager.WeekToDate(week))
                : $"Week {week}";
        }

        /// <summary>weeksFromNow: 5 → "In 5 weeks", 1 → "Next week", 0 → "This week", -2 → "2 weeks ago"</summary>
        public static string RelativeWeeks(int weeksFromNow) => weeksFromNow switch
        {
            > 1 => $"In {weeksFromNow} weeks",
            1 => "Next week",
            0 => "This week",
            -1 => "Last week",
            _ => $"{-weeksFromNow} weeks ago"
        };

        /// <summary>weeksLeft: 3 → "3 weeks left", 1 → "Last week", 0 or less → "Ended"</summary>
        public static string WeeksLeft(int weeksLeft) => weeksLeft switch
        {
            > 1 => $"{weeksLeft} weeks left",
            1 => "Last week",
            _ => "Ended"
        };

        // ------------------------------------------------------------------
        // Event definitions (details panel, create-event modal)
        // ------------------------------------------------------------------

        /// <summary>Effects of an event definition in the same form news use (subject + direction).</summary>
        public static List<NewsEventData> BuildEventEffects(Events.EventDefinition definition)
        {
            var result = new List<NewsEventData>();
            if (definition == null || definition.Effects == null) return result;

            foreach (Events.EventEffect effect in definition.Effects)
                if (effect != null) result.Add(effect.CreateNewsData());

            return result;
        }

        /// <summary>All effects of one scope as "Clock ↑, Porcelain ↓", or emptyValue if there are none.</summary>
        public static string JoinTargets(IReadOnlyList<NewsEventData> effects,
            Events.EventEffect.TargetScope scope, string emptyValue)
        {
            var parts = new List<string>();

            if (effects != null)
                foreach (NewsEventData data in effects)
                    if (data.targetScope == scope)
                        parts.Add(GetSubjectWithArrow(data));

            return parts.Count > 0 ? string.Join(", ", parts) : emptyValue;
        }

        /// <summary>Which kinds of news the event generates, e.g. "Rumour and Official".</summary>
        public static string GeneratedNewsLabel(Events.EventDefinition definition)
        {
            var kinds = new List<string>();
            if (definition.CreateRumour) kinds.Add(TypeLabel(NewsType.Rumor));
            if (definition.CreateLeak) kinds.Add(TypeLabel(NewsType.Leak));
            if (definition.CreateOfficialNews) kinds.Add(TypeLabel(NewsType.Official));

            return kinds.Count > 0 ? JoinNice(kinds) : "None";
        }

        /// <summary>1 → "1 week", 3 → "3 weeks"</summary>
        public static string WeeksLabel(int weeks) => weeks == 1 ? "1 week" : $"{weeks} weeks";

        // ------------------------------------------------------------------
        // Player information access
        // ------------------------------------------------------------------

        /// <summary>Player's access level. Falls back to the lowest level if the player can't be read.</summary>
        public static Events.InfoAccessLevel GetAccessLevel(Agents.PlayerTrader player)
        {
            return player is Agents.IInformationReceiver receiver
                ? receiver.AccessLevel
                : Events.InfoAccessLevel.LocalPress;
        }

        public static int PlayerWeek(NewsItem news)
        {
            if (news == null) return 0;
            return news.PlayerDeliveryWeek != NewsItem.NotDelivered ? news.PlayerDeliveryWeek : news.WeekPublished;
        }

        /// <summary>Weeks after publication this reached the player, 0 if it arrived as it broke.</summary>
        public static int WeeksLate(NewsItem news) =>
            news == null || news.PlayerDeliveryWeek == NewsItem.NotDelivered
                ? 0
                : Mathf.Max(0, news.PlayerDeliveryWeek - news.WeekPublished);

        /// <summary>"rumours 58% · leaks 37%", the share of each kind this tier receives.</summary>
        public static string AccessChanceLabel(Events.InfoAccessLevel level)
        {
            var settings = InformationSettings.Active;
            if (settings == null) return "";

            return $"rumours {settings.ChanceFor(NewsType.Rumor, level):P0} · " +
                   $"leaks {settings.ChanceFor(NewsType.Leak, level):P0}";
        }

        /// <summary>"Informant Network  (Lv. 3) · rumours 58% · leaks 37% · reaches you in 0–2 weeks"</summary>
        public static string AccessLevelSummary(Events.InfoAccessLevel level)
        {
            string chances = AccessChanceLabel(level);
            string label = AccessLevelLabel(level);
            if (string.IsNullOrEmpty(chances)) return label;

            var settings = InformationSettings.Active;
            var delay = settings.DelayFor(NewsType.Leak, level);

            string timing = delay.y <= 0
                ? "as it breaks"
                : delay.x == delay.y
                    ? $"after {WeeksLabel(delay.x)}"
                    : $"in {delay.x}–{delay.y} weeks";

            return $"{label} · {chances} · reaches you {timing}";
        }
        /// <summary>Same rule NewsManager uses when delivering news to receivers.</summary>
        public static bool CanSee(NewsItem news, int currentWeek) =>
            news != null && news.PlayerKnows(currentWeek);

        /// <summary>"Industry Sources"</summary>
        public static string AccessLevelName(Events.InfoAccessLevel level) => level switch
        {
            Events.InfoAccessLevel.LocalPress => "Local Press",
            Events.InfoAccessLevel.IndustrySources => "Industry Sources",
            Events.InfoAccessLevel.InformantNetwork => "Informant Network",
            Events.InfoAccessLevel.Expert => "Expert",
            Events.InfoAccessLevel.InternationalNetwork => "International Network",
            _ => level.ToString()
        };

        /// <summary>"Industry Sources  (Lv. 2)"</summary>
        public static string AccessLevelLabel(Events.InfoAccessLevel level) =>
            $"{AccessLevelName(level)}  (Lv. {(int)level})";

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>"A" / "A and B" / "A, B and C". One implementation, in NewsWording.</summary>
        public static string JoinNice(IReadOnlyList<string> items) => NewsWording.JoinNice(items);
    }
}