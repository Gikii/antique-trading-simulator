using System.Collections.Generic;
using System.Text;

namespace AntiqueTradingSimulator.News
{
    public static class NewsWording
    {
        /// <summary>Opens the body of any rumour, genuine or planted.</summary>
        public const string RumourIntro = "Market circles whisper about an upcoming event.";

        public const string LeakIntro = "An inside source has revealed an event that has not been announced yet.";

        public const string OfficialIntro = "Official sources have announced an upcoming market event.";

        public const string NoDetails = "No details are known yet.";

        // ------------------------------------------------------------------
        // Subjects
        // ------------------------------------------------------------------

        /// <summary>What the news is about: antique type, country, century or the market in general.</summary>
        public static string Subject(NewsEventData data)
        {
            if (data.IsTransportDisruption)
                return $"{data.Zone} shipping";

            return data.targetScope switch
            {
                Events.EventEffect.TargetScope.AntiqueType => data.AntiqueType.ToString(),
                Events.EventEffect.TargetScope.Country => data.Country.ToString(),
                Events.EventEffect.TargetScope.Century => data.Century.ToString(),
                _ => "Antique market"
            };
        }

        /// <summary>"Clock ↑"</summary>
        public static string SubjectWithArrow(NewsEventData data) =>
            Subject(data) + (data.affectsPriceUp ? " ↑" : " ↓");

        // ------------------------------------------------------------------
        // Headline, preview, body
        // ------------------------------------------------------------------

        /// <summary>
        /// "Clock gaining market interest". Taken from the first thing the news names, so a planted
        /// rumour and a genuine one that happens to reveal the same scope read the same.
        /// </summary>
        public static string Headline(IReadOnlyList<NewsEventData> effects)
        {
            if (effects == null || effects.Count == 0) return "Market information";

            NewsEventData data = effects[0];
            string subject = Subject(data);

            if (data.IsTransportDisruption)
                return $"{subject} facing delays";

            return data.affectsPriceUp
                ? $"{subject} gaining market interest"
                : $"{subject} facing market pressure";
        }

        /// <summary>"Clock ↑ • France ↓"</summary>
        public static string Preview(IReadOnlyList<NewsEventData> effects)
        {
            if (effects == null || effects.Count == 0) return "";

            var builder = new StringBuilder();

            for (int i = 0; i < effects.Count; i++)
            {
                if (i > 0) builder.Append(" • ");
                builder.Append(SubjectWithArrow(effects[i]));
            }

            return builder.ToString();
        }

        /// <summary>The whole body of a rumour: the opening line plus what it says is moving.</summary>
        public static string RumourBody(IReadOnlyList<NewsEventData> effects) =>
            RumourIntro + " " + DescribeEffects(effects);

        /// <summary>"Increase in prices is expected in Clock and France. Decrease in prices is expected for Porcelain."</summary>
        public static string DescribeEffects(IReadOnlyList<NewsEventData> effects)
        {
            var up = new List<string>();
            var down = new List<string>();
            var delayed = new List<string>();

            if (effects != null)
            {
                foreach (NewsEventData data in effects)
                {
                    if (data.IsTransportDisruption) delayed.Add(Subject(data).ToLowerInvariant());
                    else (data.affectsPriceUp ? up : down).Add(Subject(data));
                }
            }

            if (up.Count == 0 && down.Count == 0 && delayed.Count == 0)
                return NoDetails;

            var builder = new StringBuilder();

            if (up.Count > 0)
                builder.Append($"Increase in prices is expected in {JoinNice(up)}.");

            if (down.Count > 0)
            {
                if (builder.Length > 0) builder.Append(' ');
                builder.Append($"Decrease in prices is expected for {JoinNice(down)}.");
            }

            if (delayed.Count > 0)
            {
                if (builder.Length > 0) builder.Append(' ');
                builder.Append($"Delays are expected in {JoinNice(delayed)}.");
            }

            return builder.ToString();
        }

        /// <summary>"Clock", "Clock and France", "Clock, France and Porcelain"</summary>
        public static string JoinNice(IReadOnlyList<string> values)
        {
            if (values == null || values.Count == 0) return "";
            if (values.Count == 1) return values[0];

            var builder = new StringBuilder();

            for (int i = 0; i < values.Count - 1; i++)
            {
                if (i > 0) builder.Append(", ");
                builder.Append(values[i]);
            }

            return builder.Append(" and ").Append(values[values.Count - 1]).ToString();
        }
    }
}
