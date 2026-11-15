using System.Collections.Generic;
using AntiqueTradingSimulator.Events;
using AntiqueTradingSimulator.Logistics;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.News
{
    public enum NewsDirection
    {
        None,
        Up,
        Down,
        /// <summary>Both directions named.</summary>
        Mixed,
        /// <summary>Transport only, with no price claim.</summary>
        Delay
    }

    /// <summary>How much room a slot has for text; each call site declares its own.</summary>
    public enum TextBudget
    {
        /// <summary>One narrow line: calendar cell, info bar row, inventory's related news.</summary>
        Tight,
        /// <summary>One normal line: news list, rumour composer.</summary>
        Normal,
        /// <summary>Several lines: tooltips, details panel.</summary>
        Wide
    }

    /// <summary>
    /// What a news item names, folded out of its effect list once so every surface renders the
    /// same facts at its own length.
    /// </summary>
    public sealed class NewsSummary
    {
        /// <summary>One named value and the way it is claimed to move.</summary>
        public readonly struct Token
        {
            public readonly string Label;
            public readonly string ProseLabel;
            public readonly bool Up;

            public Token(string label, string proseLabel, bool up)
            {
                Label = label;
                ProseLabel = proseLabel;
                Up = up;
            }
        }

        public NewsDirection Direction { get; private set; } = NewsDirection.None;

        public readonly List<Token> Types = new List<Token>();
        public readonly List<Token> Regions = new List<Token>();
        public readonly List<Token> Periods = new List<Token>();

        /// <summary>Transport disruptions, which carry no price direction.</summary>
        public readonly List<Token> Routes = new List<Token>();

        public int UpCount { get; private set; }

        public int DownCount { get; private set; }

        public int NamedCount => Types.Count + Regions.Count + Periods.Count + Routes.Count;

        public bool IsEmpty => NamedCount == 0;

        public bool HasPriceClaim => UpCount + DownCount > 0;

        public bool HasRoutes => Routes.Count > 0;

        /// <summary>The side a headline leads with: the direction behind more values, ties going up.</summary>
        public bool DominantUp => UpCount >= DownCount;

        public static NewsSummary Build(IReadOnlyList<NewsEventData> effects)
        {
            var summary = new NewsSummary();
            if (effects == null) return summary;

            foreach (NewsEventData data in effects)
            {
                if (data == null) continue;

                if (data.IsTransportDisruption)
                {
                    summary.Add(summary.Routes, NewsVocabulary.Chip(data.Zone), NewsVocabulary.Prose(data.Zone), up: false, counts: false);
                    continue;
                }

                switch (data.targetScope)
                {
                    case EventEffect.TargetScope.AntiqueType:
                        summary.Add(summary.Types, NewsVocabulary.Chip(data.AntiqueType), NewsVocabulary.Prose(data.AntiqueType), data.affectsPriceUp);
                        break;

                    case EventEffect.TargetScope.Country:
                        summary.Add(summary.Regions, NewsVocabulary.Chip(data.Country), NewsVocabulary.Prose(data.Country), data.affectsPriceUp);
                        break;

                    case EventEffect.TargetScope.Century:
                        summary.Add(summary.Periods, NewsVocabulary.Chip(data.Century), NewsVocabulary.Prose(data.Century), data.affectsPriceUp);
                        break;
                }
            }

            summary.ResolveDirection();
            return summary;
        }

        /// <summary>Adds a value unless the list already holds it, so paired effects name it once.</summary>
        private void Add(List<Token> into, string label, string proseLabel, bool up, bool counts = true)
        {
            for (int i = 0; i < into.Count; i++)
                if (into[i].Label == label)
                    return;

            into.Add(new Token(label, proseLabel, up));

            if (!counts) return;

            if (up) UpCount++;
            else DownCount++;
        }

        private void ResolveDirection()
        {
            if (NamedCount == 0) Direction = NewsDirection.None;
            else if (!HasPriceClaim) Direction = NewsDirection.Delay;
            else if (UpCount > 0 && DownCount > 0) Direction = NewsDirection.Mixed;
            else if (UpCount > 0) Direction = NewsDirection.Up;
            else Direction = NewsDirection.Down;
        }

        /// <summary>The values moving the way <see cref="DominantUp"/> says, grouped by attribute.</summary>
        public List<Token> DominantTokens()
        {
            bool up = DominantUp;
            var ordered = new List<Token>();

            foreach (Token token in Types) if (token.Up == up) ordered.Add(token);
            foreach (Token token in Regions) if (token.Up == up) ordered.Add(token);
            foreach (Token token in Periods) if (token.Up == up) ordered.Add(token);

            return ordered;
        }

        /// <summary>Price values moving against the headlined direction.</summary>
        public int OffsideCount => DominantUp ? DownCount : UpCount;

        /// <summary>Every named value, in the order the details panel's rows run.</summary>
        public List<Token> GroupedTokens()
        {
            var ordered = new List<Token>(NamedCount);
            ordered.AddRange(Types);
            ordered.AddRange(Regions);
            ordered.AddRange(Periods);
            ordered.AddRange(Routes);
            return ordered;
        }
    }
}
