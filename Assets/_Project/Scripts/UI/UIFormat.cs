using System.Globalization;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Market;
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
        public static readonly Color InTransitColor = new Color32(0x7F, 0xB3, 0xD5, 0xFF);
        public static readonly Color InRestorationColor = new Color32(0xC3, 0xA6, 0xE0, 0xFF);
        /// <summary>Highlight colour of the greybox UI (active tab, player row, progress bars).</summary>
        public static readonly Color AccentColor = new Color32(0xC9, 0x9A, 0x3C, 0xFF);

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

        private static TimeManager _timeManager;

        private static TimeManager GameClock
        {
            get
            {
                if (_timeManager == null)
                    _timeManager = Object.FindFirstObjectByType<TimeManager>();
                return _timeManager;
            }
        }

        private static NPCManager _npcManager;

        private static NPCManager Npcs
        {
            get
            {
                if (_npcManager == null)
                    _npcManager = Object.FindFirstObjectByType<NPCManager>();
                return _npcManager;
            }
        }

        /// <summary>
        /// Who owns or sells an antique, in words: "Private seller" for anonymous market stock,
        /// "You" for the player, the trader's name for NPCs (their owner id is an internal GUID).
        /// </summary>
        public static string OwnerName(string ownerId, string anonymousLabel = "Private seller", string playerLabel = "You")
        {
            if (string.IsNullOrEmpty(ownerId))
                return anonymousLabel;

            if (ownerId == Antique.PlayerOwnerId)
                return playerLabel;

            var npc = Npcs != null ? Npcs.GetById(ownerId) : null;
            return npc != null && !string.IsNullOrEmpty(npc.TraderName) ? npc.TraderName : ownerId;
        }

        /// <summary>"7 October" for a game day, or "day 37" without a TimeManager.</summary>
        public static string GameDate(int day) =>
            GameClock != null ? TimeManager.FormatDayMonth(GameClock.DayToDate(day)) : $"day {day}";

        /// <summary>"arrives 7 October (in 2 days)" / "arrives tomorrow" / "arriving today".</summary>
        public static string ArrivalLabel(int arrivalDay)
        {
            int today = GameClock != null ? GameClock.CurrentDay : arrivalDay;
            int daysLeft = arrivalDay - today;

            if (daysLeft <= 0) return "arriving today";
            if (daysLeft == 1) return $"arrives tomorrow ({GameDate(arrivalDay)})";
            return $"arrives {GameDate(arrivalDay)} (in {Days(daysLeft)})";
        }

        /// <summary>"ready 7 October (in 2 days)" / "ready tomorrow (8 October)" / "ready today" — for restorations.</summary>
        public static string ReadyLabel(int readyDay)
        {
            int today = GameClock != null ? GameClock.CurrentDay : readyDay;
            int daysLeft = readyDay - today;

            if (daysLeft <= 0) return "ready today";
            if (daysLeft == 1) return $"ready tomorrow ({GameDate(readyDay)})";
            return $"ready {GameDate(readyDay)} (in {Days(daysLeft)})";
        }

        /// <summary>"1 day" / "5 days"</summary>
        public static string Days(int days) =>
            days == 1 ? "1 day" : $"{days} days";

        public static string Percent(float ratio) =>
            Mathf.RoundToInt(ratio * 100f).ToString(CultureInfo.InvariantCulture) + "%";

        /// <summary>"6.4%" for 0.064</summary>
        public static string PercentOneDecimal(float ratio) =>
            (ratio * 100f).ToString("0.0", CultureInfo.InvariantCulture) + "%";

        /// <summary>"5%" for 0.05, "4.5%" for 0.045 — one decimal only when it isn't zero.</summary>
        public static string PercentCompact(float ratio) =>
            (Mathf.Round(ratio * 1000f) / 10f).ToString("0.#", CultureInfo.InvariantCulture) + "%";

        /// <summary>"+50" / "-20" / "0"</summary>
        public static string SignedNumber(float value)
        {
            int rounded = Mathf.RoundToInt(value);
            string sign = rounded > 0 ? "+" : rounded < 0 ? "-" : "";
            return sign + Mathf.Abs(rounded).ToString("#,0", MoneyFormat);
        }

        /// <summary>"1 250" — whole number with a space as thousands separator.</summary>
        public static string Number(float value) =>
            Mathf.RoundToInt(value).ToString("#,0", MoneyFormat);

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

        // Warm, muted colours that sit on the dark theme (antique gold first), still far enough
        // apart to tell neighbouring donut segments apart.
        private static readonly Color[] CategoryPalette =
        {
            new Color32(0xC9, 0x9A, 0x3C, 0xFF), // antique gold
            new Color32(0x7E, 0x9C, 0x5C, 0xFF), // sage green
            new Color32(0xB8, 0x64, 0x42, 0xFF), // terracotta
            new Color32(0x5F, 0x8E, 0x8A, 0xFF), // verdigris
            new Color32(0x9A, 0x6A, 0x8C, 0xFF), // faded plum
            new Color32(0xD9, 0xC0, 0x8C, 0xFF), // parchment
            new Color32(0x6E, 0x80, 0x9A, 0xFF), // slate
            new Color32(0x8C, 0x5E, 0x3C, 0xFF), // umber
        };

        public static readonly Color OtherCategoryColor = new Color32(0x6E, 0x60, 0x4A, 0xFF);

        public static int CategoryPaletteSize => CategoryPalette.Length;

        // Colored by rank in the breakdown (largest share first) rather than by type,
        // so the categories shown next to each other never share a color.
        public static Color CategoryColorByRank(int rank) =>
            CategoryPalette[rank % CategoryPalette.Length];
    }
}
