using System.Collections.Generic;
using System.Text;
using AntiqueTradingSimulator.News;
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
            NewsType.Rumor => RumourColor,
            NewsType.Leak => LeakedColor,
            _ => OfficialColor
        };

        // ------------------------------------------------------------------
        // Labels
        // ------------------------------------------------------------------

        /// <summary>"Official" / "Rumour" / "Leaked"</summary>
        public static string TypeLabel(NewsType type) => type switch
        {
            NewsType.Official => "Official",
            NewsType.Rumor => "Rumour",
            NewsType.Leak => "Leaked",
            _ => type.ToString()
        };

        /// <summary>e.g. "Clock gaining market interest"</summary>
        public static string GetTitle(NewsItem item)
        {
            if (item.NewsData == null || item.NewsData.Count == 0)
                return "Market information";

            NewsEventData data = item.NewsData[0];
            string subject = GetSubject(data);

            return data.affectsPriceUp
                ? $"{subject} gaining market interest"
                : $"{subject} facing market pressure";
        }

        /// <summary>e.g. "Clock ↑ • France ↓"</summary>
        public static string GetPreview(NewsItem item)
        {
            if (item.NewsData == null || item.NewsData.Count == 0)
                return "No additional information available.";

            var builder = new StringBuilder();

            for (int i = 0; i < item.NewsData.Count; i++)
            {
                if (i > 0)
                    builder.Append(" • ");

                builder.Append(GetSubjectWithArrow(item.NewsData[i]));
            }

            return builder.ToString();
        }

        /// <summary>What the news is about: antique type, country, century or the market in general.</summary>
        public static string GetSubject(NewsEventData data)
        {
            switch (data.targetScope)
            {
                case Events.EventEffect.TargetScope.AntiqueType:
                    return data.AntiqueType.ToString();

                case Events.EventEffect.TargetScope.Country:
                    return data.Country.ToString();

                case Events.EventEffect.TargetScope.Century:
                    return data.Century.ToString();

                default:
                    return "Antique market";
            }
        }

        /// <summary>"Clock ↑"</summary>
        public static string GetSubjectWithArrow(NewsEventData data) =>
            GetSubject(data) + (data.affectsPriceUp ? " ↑" : " ↓");

        // ------------------------------------------------------------------
        // Longer texts (details panel)
        // ------------------------------------------------------------------

        public static string GetDescription(NewsItem item)
        {
            string intro = item.Type switch
            {
                NewsType.Official => "Official sources have announced an upcoming market event.",
                NewsType.Rumor => "Market circles whisper about an upcoming event.",
                NewsType.Leak => "An inside source has revealed an event that has not been announced yet.",
                _ => "New market information is available."
            };

            return intro + " " + DescribeEffects(item.NewsData);
        }

        public static string GetEventDescription(string eventName, IReadOnlyList<NewsEventData> effects)
        {
            return $"{eventName} is currently in progress. " + DescribeEffects(effects);
        }

        /// <summary>"Growing interest is expected in Clock and France. Weaker demand is expected for Porcelain."</summary>
        public static string DescribeEffects(IReadOnlyList<NewsEventData> effects)
        {
            var up = new List<string>();
            var down = new List<string>();

            if (effects != null)
            {
                foreach (NewsEventData data in effects)
                    (data.affectsPriceUp ? up : down).Add(GetSubject(data));
            }

            if (up.Count == 0 && down.Count == 0)
                return "No details are known yet.";

            var builder = new StringBuilder();

            if (up.Count > 0)
                builder.Append($"Growing interest is expected in {JoinNice(up)}.");

            if (down.Count > 0)
            {
                if (builder.Length > 0) builder.Append(' ');
                builder.Append($"Weaker demand is expected for {JoinNice(down)}.");
            }

            return builder.ToString();
        }

        public static string GetNote(NewsType type) => type switch
        {
            NewsType.Official => "This is official information. It is confirmed and publicly available to all market participants.",
            NewsType.Rumor => "This is a rumour. Its credibility is uncertain and it may turn out to be false. The actual market effects depend on how other market participants react to this information.",
            NewsType.Leak => "This is a leak. The information is likely true, but it has not been officially announced yet, so most of the market doesn't know about it.",
            _ => ""
        };

        public const string EventNote =
            "This event is in progress. Its effects will influence the market until it ends.";

        // ------------------------------------------------------------------
        // Dates
        // ------------------------------------------------------------------

        /// <summary>"14 May 1884" when a TimeManager is available, otherwise "Day 23".</summary>
        public static string FormatDay(int day, Core.TimeManager timeManager)
        {
            return timeManager != null
                ? Core.TimeManager.FormatLong(timeManager.DayToDate(day))
                : $"Day {day}";
        }

        /// <summary>daysFromToday: 5 → "In 5 days", 1 → "Tomorrow", 0 → "Today", -2 → "2 days ago"</summary>
        public static string RelativeDays(int daysFromToday) => daysFromToday switch
        {
            > 1 => $"In {daysFromToday} days",
            1 => "Tomorrow",
            0 => "Today",
            -1 => "Yesterday",
            _ => $"{-daysFromToday} days ago"
        };

        /// <summary>daysLeft: 3 → "3 days left", 1 → "Last day", 0 or less → "Ended"</summary>
        public static string DaysLeft(int daysLeft) => daysLeft switch
        {
            > 1 => $"{daysLeft} days left",
            1 => "Last day",
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

        /// <summary>1 → "1 day", 3 → "3 days"</summary>
        public static string DaysLabel(int days) => days == 1 ? "1 day" : $"{days} days";

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

        /// <summary>Same rule NewsManager uses when delivering news to receivers.</summary>
        public static bool CanSee(NewsItem news, Events.InfoAccessLevel accessLevel) =>
            news != null && accessLevel >= news.RequiredAccessLevel;

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

        /// <summary>"A" / "A and B" / "A, B and C"</summary>
        public static string JoinNice(IReadOnlyList<string> items)
        {
            if (items == null || items.Count == 0) return "";
            if (items.Count == 1) return items[0];

            var builder = new StringBuilder();
            for (int i = 0; i < items.Count; i++)
            {
                if (i > 0) builder.Append(i == items.Count - 1 ? " and " : ", ");
                builder.Append(items[i]);
            }
            return builder.ToString();
        }
    }
}