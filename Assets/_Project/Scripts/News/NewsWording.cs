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
        // Budgets
        // ------------------------------------------------------------------

        /// <summary>Values a slot names before the overflow count takes over.</summary>
        private static int MaxTokens(TextBudget budget) => budget switch
        {
            TextBudget.Tight => 1,
            TextBudget.Wide => 6,
            _ => 3
        };

        /// <summary>Character ceiling a slot fits, applied after the value count.</summary>
        private static int MaxChars(TextBudget budget) => budget switch
        {
            TextBudget.Tight => 30,
            TextBudget.Wide => 80,
            _ => 46
        };

        // ------------------------------------------------------------------
        // Subjects
        // ------------------------------------------------------------------

        /// <summary>What the news is about: antique type, country, century or the market in general.</summary>
        public static string Subject(NewsEventData data)
        {
            if (data == null) return "Antique market";

            if (data.IsTransportDisruption)
                return NewsVocabulary.Prose(data.Zone);

            return data.targetScope switch
            {
                Events.EventEffect.TargetScope.AntiqueType => NewsVocabulary.Chip(data.AntiqueType),
                Events.EventEffect.TargetScope.Country => NewsVocabulary.Chip(data.Country),
                Events.EventEffect.TargetScope.Century => NewsVocabulary.Chip(data.Century),
                _ => "Antique market"
            };
        }

        /// <summary>True when the first clause counts more than one value, so it takes "are".</summary>
        private static bool StartsWithPluralCount(NewsSummary summary)
        {
            if (summary.Types.Count > 0) return summary.Types.Count > 1;
            if (summary.Regions.Count > 0) return summary.Regions.Count > 1;
            if (summary.Periods.Count > 0) return summary.Periods.Count > 1;
            return summary.Routes.Count > 1;
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
        public static string Headline(IReadOnlyList<NewsEventData> effects, TextBudget budget = TextBudget.Normal) =>
            Headline(NewsSummary.Build(effects), budget);

        public static string Headline(NewsSummary summary, TextBudget budget = TextBudget.Normal)
        {
            if (summary == null || summary.IsEmpty) return "Market information";

            bool terse = budget == TextBudget.Tight;

            if (summary.Direction == NewsDirection.Delay)
                return Compose(terse ? "Delays" : "Shipping delays", summary.Routes, budget);

            // Every item leads with its dominant side, so planted and genuine rumours share grammar.
            string lead = summary.DominantUp
                ? (terse ? "Rising" : "Prices to rise")
                : (terse ? "Falling" : "Prices to fall");

            // Offside values and routes are counted here and named in the chip line and rows.
            int hidden = summary.OffsideCount + summary.Routes.Count;

            return Compose(lead, summary.DominantTokens(), budget, extraHidden: hidden);
        }

        private static string Compose(string lead, List<NewsSummary.Token> tokens, TextBudget budget, int extraHidden = 0)
        {
            int room = System.Math.Min(MaxTokens(budget), tokens.Count);
            int ceiling = MaxChars(budget);

            while (room > 0)
            {
                string candidate = lead + ": " + ListWithOverflow(tokens, room, tokens.Count - room + extraHidden);
                if (candidate.Length <= ceiling || room == 1) return candidate;
                room--;
            }

            return lead;
        }

        /// <summary>"a, b and c" when nothing is hidden, "a, b +2" when something is.</summary>
        private static string ListWithOverflow(List<NewsSummary.Token> tokens, int shown, int hidden)
        {
            var labels = new List<string>(shown);
            for (int i = 0; i < shown && i < tokens.Count; i++)
                labels.Add(tokens[i].Label);

            return hidden > 0
                ? string.Join(", ", labels) + " +" + hidden
                : JoinNice(labels);
        }

        /// <summary>"clocks ↑  vases ↑ · France ↑ · +2 more", grouped by attribute.</summary>
        public static string Chips(IReadOnlyList<NewsEventData> effects, TextBudget budget = TextBudget.Normal) =>
            Chips(NewsSummary.Build(effects), budget);

        public static string Chips(NewsSummary summary, TextBudget budget = TextBudget.Normal)
        {
            if (summary == null || summary.IsEmpty) return "";

            int room = MaxTokens(budget) + 1;
            int ceiling = MaxChars(budget);

            while (room > 0)
            {
                string candidate = BuildChips(summary, room);
                if (candidate.Length <= ceiling || room == 1) return candidate;
                room--;
            }

            return "";
        }

        private static string BuildChips(NewsSummary summary, int room)
        {
            var groups = new List<List<NewsSummary.Token>>
            {
                summary.Types, summary.Regions, summary.Periods, summary.Routes
            };

            var builder = new StringBuilder();
            int shown = 0;
            int total = summary.NamedCount;

            foreach (List<NewsSummary.Token> group in groups)
            {
                if (group.Count == 0 || shown >= room) continue;

                bool groupStarted = false;

                foreach (NewsSummary.Token token in group)
                {
                    if (shown >= room) break;

                    if (!groupStarted && builder.Length > 0) builder.Append(" · ");
                    else if (groupStarted) builder.Append("  ");

                    groupStarted = true;

                    builder.Append(token.Label);

                    if (group != summary.Routes)
                        builder.Append(token.Up ? " ↑" : " ↓");

                    shown++;
                }
            }

            int hidden = total - shown;
            if (hidden > 0) builder.Append(" · +").Append(hidden).Append(" more");

            return builder.ToString();
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
        public static string DescribeEffects(IReadOnlyList<NewsEventData> effects) =>
            DescribeEffects(NewsSummary.Build(effects));

        public static string DescribeEffects(NewsSummary summary)
        {
            if (summary == null || summary.IsEmpty) return NoDetails;

            var builder = new StringBuilder();
            var parts = new List<string>();

            if (summary.Types.Count > 0)
                parts.Add($"{NewsVocabulary.Spell(summary.Types.Count)} {NewsVocabulary.Categories(summary.Types.Count)}");

            if (summary.Regions.Count > 0)
                parts.Add($"{NewsVocabulary.Spell(summary.Regions.Count)} {NewsVocabulary.Regions(summary.Regions.Count)}");

            if (summary.Periods.Count > 0)
                parts.Add($"{NewsVocabulary.Spell(summary.Periods.Count)} {NewsVocabulary.Periods(summary.Periods.Count)}");

            if (summary.Routes.Count > 0)
                parts.Add($"{NewsVocabulary.Spell(summary.Routes.Count)} shipping {NewsVocabulary.Routes(summary.Routes.Count)}");

            string named = Capitalize(JoinNice(parts));
            string verb = parts.Count == 1 && !StartsWithPluralCount(summary) ? "is" : "are";

            builder.Append(named).Append(' ').Append(verb).Append(" named");

            switch (summary.Direction)
            {
                case NewsDirection.Up:
                    builder.Append(", and prices there are expected to rise.");
                    break;

                case NewsDirection.Down:
                    builder.Append(", and prices there are expected to fall.");
                    break;

                case NewsDirection.Mixed:
                    builder.Append(summary.DominantUp
                        ? ", with prices expected to rise across most of them and fall on the rest."
                        : ", with prices expected to fall across most of them and rise on the rest.");
                    break;

                case NewsDirection.Delay:
                    builder.Append(", with shipments expected to be held up.");
                    break;

                default:
                    builder.Append('.');
                    break;
            }

            if (summary.HasPriceClaim && summary.HasRoutes)
                builder.Append(" Shipments on the named routes are expected to be held up.");

            return builder.ToString();
        }

        /// <summary>
        /// The details panel's rows as text, for surfaces with vertical room but no row widgets:
        /// "Categories: clocks ↑\nRegions: France ↑\nPeriods: —".
        /// </summary>
        public static string Lines(IReadOnlyList<NewsEventData> effects, string emptyValue = "—") =>
            Lines(NewsSummary.Build(effects), emptyValue);

        public static string Lines(NewsSummary summary, string emptyValue = "—")
        {
            if (summary == null || summary.IsEmpty) return "";

            var builder = new StringBuilder();

            AppendLine(builder, "Categories", summary.Types, emptyValue, arrows: true);
            AppendLine(builder, "Regions", summary.Regions, emptyValue, arrows: true);
            AppendLine(builder, "Periods", summary.Periods, emptyValue, arrows: true);

            if (summary.HasRoutes)
                AppendLine(builder, "Routes", summary.Routes, emptyValue, arrows: false);

            return builder.ToString();
        }

        private static void AppendLine(StringBuilder builder, string label, List<NewsSummary.Token> tokens,
            string emptyValue, bool arrows)
        {
            if (builder.Length > 0) builder.Append('\n');

            builder.Append(label).Append(": ");

            if (tokens.Count == 0)
            {
                builder.Append(emptyValue);
                return;
            }

            for (int i = 0; i < tokens.Count; i++)
            {
                if (i > 0) builder.Append(", ");
                builder.Append(tokens[i].Label);
                if (arrows) builder.Append(tokens[i].Up ? " ↑" : " ↓");
            }
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

        private static string Capitalize(string value) =>
            string.IsNullOrEmpty(value) ? value : char.ToUpperInvariant(value[0]) + value.Substring(1);
    }
}
