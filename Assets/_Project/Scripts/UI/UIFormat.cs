using System.Globalization;
using UnityEngine;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Shared number/label formatting for the collection (inventory) screens, so the list,
    /// summary and details panel show money, percentages and condition the same way.
    /// </summary>
    public static class UIFormat
    {
        public static readonly Color PositiveColor = NewsPresentation.OfficialColor;
        public static readonly Color NegativeColor = NewsPresentation.EventColor;
        public static readonly Color MutedColor = new Color32(0xB8, 0xB8, 0xB8, 0xFF);

        private static readonly NumberFormatInfo MoneyFormat = new NumberFormatInfo
        {
            NumberGroupSeparator = " ",
            NumberDecimalSeparator = ".",
            NumberGroupSizes = new[] { 3 }
        };

        /// <summary>"128 450 €"</summary>
        public static string Money(float value) =>
            Mathf.RoundToInt(value).ToString("#,0", MoneyFormat) + " €";

        /// <summary>"+30 220 €" / "-1 200 €" / "0 €"</summary>
        public static string SignedMoney(float value)
        {
            int rounded = Mathf.RoundToInt(value);
            string sign = rounded > 0 ? "+" : rounded < 0 ? "-" : "";
            return sign + Mathf.Abs(rounded).ToString("#,0", MoneyFormat) + " €";
        }

        /// <summary>"+30.8%" for 0.308</summary>
        public static string SignedPercent(float ratio)
        {
            float percent = ratio * 100f;
            string sign = percent > 0.05f ? "+" : "";
            return sign + percent.ToString("0.0", CultureInfo.InvariantCulture) + "%";
        }

        public static string Percent(float ratio) =>
            Mathf.RoundToInt(ratio * 100f).ToString(CultureInfo.InvariantCulture) + "%";

        /// <summary>Wraps text in a TMP rich-text color tag picked by the sign of <paramref name="value"/>.</summary>
        public static string ColorBySign(string text, float value)
        {
            if (Mathf.Approximately(value, 0f))
                return text;

            return Colorize(text, value > 0f ? PositiveColor : NegativeColor);
        }

        public static string Colorize(string text, Color color) =>
            $"<color=#{ColorUtility.ToHtmlStringRGB(color)}>{text}</color>";

        public static string TrendArrow(float value) =>
            value > 0f ? "↑" : value < 0f ? "↓" : "→";

        // ------------------------------------------------------------------
        // Condition
        // ------------------------------------------------------------------

        public readonly struct ConditionBucket
        {
            public readonly string Label;
            public readonly float Min;
            public readonly float Max;

            public ConditionBucket(string label, float min, float max)
            {
                Label = label;
                Min = min;
                Max = max;
            }

            public bool Contains(float condition) => condition >= Min && condition < Max;
        }

        // Best first. Max of the top bucket is slightly above 1 so Condition = 1 is included.
        public static readonly ConditionBucket[] ConditionBuckets =
        {
            new ConditionBucket("Very Good", 0.9f, 1.01f),
            new ConditionBucket("Good", 0.7f, 0.9f),
            new ConditionBucket("Average", 0.5f, 0.7f),
            new ConditionBucket("Poor", 0.3f, 0.5f),
            new ConditionBucket("Very Poor", 0f, 0.3f),
        };

        public static string ConditionLabel(float condition)
        {
            foreach (var bucket in ConditionBuckets)
                if (bucket.Contains(condition))
                    return bucket.Label;

            return ConditionBuckets[ConditionBuckets.Length - 1].Label;
        }

        // ------------------------------------------------------------------
        // Category colors (stable per type, used by the donut chart and legend)
        // ------------------------------------------------------------------

        private static readonly Color[] CategoryPalette =
        {
            new Color32(0x4E, 0x79, 0xA7, 0xFF),
            new Color32(0xE1, 0x8F, 0x3C, 0xFF),
            new Color32(0x59, 0xA1, 0x4F, 0xFF),
            new Color32(0xB0, 0x7A, 0xA1, 0xFF),
            new Color32(0xC9, 0xA2, 0x27, 0xFF),
            new Color32(0x76, 0xB7, 0xB2, 0xFF),
            new Color32(0xD3, 0x6B, 0x5C, 0xFF),
            new Color32(0x9C, 0x75, 0x5F, 0xFF),
        };

        public static readonly Color OtherCategoryColor = new Color32(0x8C, 0x8C, 0x8C, 0xFF);

        public static int CategoryPaletteSize => CategoryPalette.Length;

        // Colored by rank in the breakdown (largest share first) rather than by type,
        // so the categories shown next to each other never share a color.
        public static Color CategoryColorByRank(int rank) =>
            CategoryPalette[rank % CategoryPalette.Length];
    }
}
