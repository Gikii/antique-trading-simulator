using System.Collections.Generic;
using System.Linq;
using AntiqueTradingSimulator.Market;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.Economy
{
    /// <summary>
    /// Read-only calculations over a set of owned antiques (a trader's collection).
    /// Pure functions with no Unity dependencies, so the same numbers can later feed
    /// the end-of-campaign score, NPC evaluation or reports, not just the UI.
    /// </summary>
    public static class CollectionAnalytics
    {
        public readonly struct CategoryShare
        {
            public readonly AntiqueType Type;
            public readonly int Count;
            public readonly float Value;
            public readonly float ValueShare; // 0..1 of the collection's total value

            public CategoryShare(AntiqueType type, int count, float value, float valueShare)
            {
                Type = type;
                Count = count;
                Value = value;
                ValueShare = valueShare;
            }
        }

        public static float TotalValue(IEnumerable<Antique> antiques) =>
            antiques.Sum(a => a.CurrentPrice);

        /// <summary>
        /// Sum of what was paid for the antiques that have an acquisition record.
        /// Items without a record (should not happen for owned items) count as 0.
        /// </summary>
        public static float TotalCost(IEnumerable<Antique> antiques) =>
            antiques.Where(a => a.HasPurchaseRecord).Sum(a => a.PurchasePrice);

        public static float Profit(Antique antique) =>
            antique.CurrentPrice - antique.PurchasePrice;

        /// <summary>Relative profit (0.1 = +10%). Returns null when there is no cost basis.</summary>
        public static float? ReturnOnInvestment(float value, float cost) =>
            cost > 0f ? (value - cost) / cost : (float?)null;

        public static int LimitedEditionCount(IEnumerable<Antique> antiques) =>
            antiques.Count(a => a.IsLimitedEdition);

        public static Antique MostValuable(IEnumerable<Antique> antiques) =>
            antiques.OrderByDescending(a => a.CurrentPrice).FirstOrDefault();

        /// <summary>Collection split by antique type, largest value share first.</summary>
        public static List<CategoryShare> CategoryBreakdown(IReadOnlyCollection<Antique> antiques)
        {
            float total = TotalValue(antiques);

            return antiques
                .GroupBy(a => a.Type)
                .Select(g =>
                {
                    float value = g.Sum(a => a.CurrentPrice);
                    float share = total > 0f ? value / total : 0f;
                    return new CategoryShare(g.Key, g.Count(), value, share);
                })
                .OrderByDescending(c => c.Value)
                .ThenByDescending(c => c.Count)
                .ToList();
        }

        /// <summary>
        /// Reference price of the antique's type as it was <paramref name="daysAgo"/> days
        /// before the latest recorded day, or null if the type has no history yet.
        /// </summary>
        public static float? ReferencePriceDaysAgo(AntiqueMarketState typeState, int daysAgo)
        {
            if (typeState == null || typeState.PriceHistory.Count == 0)
                return null;

            var history = typeState.PriceHistory;
            int targetDay = history[history.Count - 1].Day - daysAgo;

            // Latest point on or before the target day; fall back to the oldest point
            // when history is shorter than the requested window.
            PricePoint match = history[0];
            foreach (var point in history)
            {
                if (point.Day > targetDay) break;
                match = point;
            }

            return match.Price;
        }

        /// <summary>
        /// How much of the antique's current value is due to market movement over the
        /// last <paramref name="days"/> days (not purchases or sales). Positive = gained value.
        /// </summary>
        public static float MarketValueChange(Antique antique, Market.Market market, int days)
        {
            if (antique == null || market == null)
                return 0f;

            var typeState = market.GetTypeState(antique.DefinitionId);
            float? past = ReferencePriceDaysAgo(typeState, days);
            if (past == null || past.Value <= 0f)
                return 0f;

            float now = PriceEngine.CalculateReferencePrice(antique.BasePrice, typeState);
            if (now <= 0f)
                return 0f;

            // Same item, same condition — only the market multiplier moved.
            float pastValue = antique.CurrentPrice * (past.Value / now);
            return antique.CurrentPrice - pastValue;
        }

        public static float MarketValueChange(IEnumerable<Antique> antiques, Market.Market market, int days) =>
            antiques.Sum(a => MarketValueChange(a, market, days));

        /// <summary>
        /// The antique's own value history: its type's reference price history scaled by
        /// this item's individual condition and price factor, oldest first.
        /// </summary>
        public static List<float> ItemValueHistory(Antique antique, Market.Market market, int days)
        {
            var result = new List<float>();
            if (antique == null || market == null)
                return result;

            var typeState = market.GetTypeState(antique.DefinitionId);
            if (typeState == null)
                return result;

            var history = typeState.PriceHistory;
            if (history.Count == 0)
                return result;

            int fromDay = history[history.Count - 1].Day - days;
            float itemMultiplier = antique.Condition * antique.PriceFactor;

            foreach (var point in history)
            {
                if (point.Day < fromDay) continue;
                result.Add(point.Price * itemMultiplier);
            }

            return result;
        }
    }
}
