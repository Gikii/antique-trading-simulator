using System;
using System.Globalization;
using UnityEngine;

namespace AntiqueTradingSimulator.Core
{
    /// <summary>
    /// How money is written in the game: pounds sterling and shillings, as in 1884 (£1 = 20s).
    /// Amounts are stored as plain float pounds everywhere; this class only decides how they are shown,
    /// so both the UI (through UIFormat) and game logic (e.g. "Not enough cash" reasons) read the same.
    ///
    ///   0.75    → "15s"
    ///   4.6     → "£4 12s"
    ///   42      → "£42"
    ///   1250.4  → "£1,250"   (from WholePoundsFrom up, shillings are dropped)
    ///
    /// Pence are not shown: nothing in the game is priced finely enough for them to matter.
    /// </summary>
    public static class Money
    {
        public const string Symbol = "£";
        public const int ShillingsPerPound = 20;

        /// <summary>Amounts of at least this many pounds are shown in whole pounds only.</summary>
        public const float WholePoundsFrom = 100f;

        /// <summary>Shown for a positive amount too small to reach a whole shilling (plain text: TMP would read "<1s" as a tag).</summary>
        public const string UnderOneShilling = "under 1s";

        /// <summary>One-line explanation of the currency, for tooltips and the first-week notice.</summary>
        public const string CurrencyExplanation = "Pounds sterling (£). £1 = 20 shillings (s).";

        /// <summary>What money is worth in 1884, for the same places (a London office clerk's pay).</summary>
        public const string ScaleReference = "For scale: a clerk earns about 25s a week.";

        private static readonly NumberFormatInfo Grouping = new NumberFormatInfo
        {
            NumberGroupSeparator = ",",
            NumberDecimalSeparator = ".",
            NumberGroupSizes = new[] { 3 }
        };

        /// <summary>"£4 12s", "15s", "£1,250"; negative amounts get a leading "-" ("-£4 12s").</summary>
        public static string Format(float pounds)
        {
            if (float.IsNaN(pounds) || float.IsInfinity(pounds)) return "—";

            string body = FormatAbsolute(Mathf.Abs(pounds), tinyAsZero: false);
            return pounds < 0f && !RoundsToZero(Mathf.Abs(pounds)) ? "-" + body : body;
        }

        /// <summary>"+£4 12s" / "-£1,250" / "£0" — the sign follows the amount as it is shown.</summary>
        public static string FormatSigned(float pounds)
        {
            if (float.IsNaN(pounds) || float.IsInfinity(pounds)) return "—";

            float absolute = Mathf.Abs(pounds);
            if (RoundsToZero(absolute)) return Symbol + "0";

            return (pounds > 0f ? "+" : "-") + FormatAbsolute(absolute, tinyAsZero: true);
        }

        /// <summary>"£4 10s–£6", "£850–£1,300".</summary>
        public static string FormatRange(float low, float high, string separator = "–") =>
            Format(low) + separator + Format(high);

        /// <summary>The amount as it is displayed, back in pounds (e.g. 4.63 → 4.6, i.e. £4 12s).</summary>
        public static float RoundForDisplay(float pounds)
        {
            float absolute = Mathf.Abs(pounds);
            float rounded = absolute >= WholePoundsFrom
                ? (float)Math.Round(absolute, MidpointRounding.AwayFromZero)
                : (float)(Math.Round(absolute * (double)ShillingsPerPound, MidpointRounding.AwayFromZero) / ShillingsPerPound);
            return pounds < 0f ? -rounded : rounded;
        }

        /// <summary>One shilling in pounds — the smallest amount the game shows.</summary>
        public const float OneShilling = 1f / ShillingsPerPound;

        /// <summary>
        /// Nearest whole shilling (4.637 → 4.65, i.e. £4 13s). Every amount the game charges or pays is
        /// rounded like this where it is worked out (prices, transport, fees, restoration, rewards) and
        /// cash is kept in whole shillings — so the parts shown on screen always add up to the total.
        /// </summary>
        public static float RoundToShilling(float pounds) =>
            (float)(Math.Round(pounds * (double)ShillingsPerPound, MidpointRounding.AwayFromZero) / ShillingsPerPound);

        // Amounts are whole shillings, but 0.05 has no exact float: £2 4s + 3s can come out a hair above
        // £2 7s. Far below a shilling, so it only absorbs that — never lets anyone pay with money they lack.
        private const float CashEpsilon = 0.001f;

        /// <summary>True if <paramref name="cash"/> pays for <paramref name="cost"/> (see CashEpsilon).</summary>
        public static bool Covers(float cash, float cost) => cost <= cash + CashEpsilon;

        /// <summary>
        /// Slack for "is this at least that much" checks between two amounts the player compares on screen:
        /// half of what the display rounds to (half a shilling below £100, half a pound from £100 up),
        /// so an antique shown as "£4 12s" meets a "£4 12s" requirement.
        /// </summary>
        public static float DisplayTolerance(float pounds) =>
            Mathf.Abs(pounds) >= WholePoundsFrom ? 0.5f : OneShilling * 0.5f;

        /// <summary>
        /// A round step for an amount of this size, as a dealer would quote it:
        /// 1s below £2, 5s below £10, £1 below £50, £5 below £200, £10 below £1,000, £50 above.
        /// </summary>
        public static float NiceStep(float pounds)
        {
            float absolute = Mathf.Abs(pounds);
            if (absolute < 2f) return OneShilling;
            if (absolute < 10f) return 5f * OneShilling;
            if (absolute < 50f) return 1f;
            if (absolute < 200f) return 5f;
            if (absolute < 1000f) return 10f;
            return 50f;
        }

        /// <summary>Nearest round amount (see NiceStep): 4.63 → £4 15s, 37.4 → £37, 1234 → £1,250.</summary>
        public static float RoundToNice(float pounds)
        {
            float step = NiceStep(pounds);
            return Mathf.Round(pounds / step) * step;
        }

        /// <summary>Round amount at or below this one, never under one shilling: 4.63 → £4 10s, 537 → £530.</summary>
        public static float RoundDownToNice(float pounds)
        {
            if (pounds <= OneShilling) return OneShilling;
            float step = NiceStep(pounds);
            // The epsilon keeps an amount that is already round (e.g. 4.5 → 4.4999…) from dropping a step.
            return Mathf.Max(OneShilling, Mathf.Floor(pounds / step + 0.0001f) * step);
        }

        /// <summary>
        /// Reads an amount typed by the player. Accepts what Format writes and the usual shorthands:
        /// "£4 12s", "4 12s", "4 12", "4/12", "15s", "£1,250", "1250", and decimal pounds ("4.6").
        /// </summary>
        public static bool TryParse(string text, out float pounds)
        {
            pounds = 0f;
            if (string.IsNullOrWhiteSpace(text)) return false;

            string s = text.Trim().Replace(Symbol, "").Replace(",", "").Trim().ToLowerInvariant();
            if (s.Length == 0) return false;

            int poundsPart = 0;
            int shillingsPart = 0;

            if (s[s.Length - 1] == 's')
            {
                // "4 12s" / "12s"
                string[] parts = s.Substring(0, s.Length - 1).Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 1)
                {
                    if (!TryWhole(parts[0], out shillingsPart)) return false;
                }
                else if (parts.Length == 2)
                {
                    if (!TryWhole(parts[0], out poundsPart) || !TryWhole(parts[1], out shillingsPart)) return false;
                }
                else return false;
            }
            else if (s.Contains("/") || s.Contains(" "))
            {
                // "4/12" / "4 12"
                string[] parts = s.Split(new[] { '/', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 2 || !TryWhole(parts[0], out poundsPart) || !TryWhole(parts[1], out shillingsPart))
                    return false;
            }
            else
            {
                // "1250" / "4.6"
                if (!float.TryParse(s, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out float decimalPounds))
                    return false;
                pounds = decimalPounds;
                return pounds > 0f && !float.IsInfinity(pounds);
            }

            if (shillingsPart >= ShillingsPerPound && poundsPart > 0) return false; // "4 25s" is a typo, not £5 5s
            pounds = poundsPart + shillingsPart / (float)ShillingsPerPound;
            return pounds > 0f;
        }

        private static bool TryWhole(string text, out int value) =>
            int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value);

        private static bool RoundsToZero(float absolute) => RoundForDisplay(absolute) <= 0f;

        private static string FormatAbsolute(float absolute, bool tinyAsZero)
        {
            if (absolute >= WholePoundsFrom)
                return Symbol + ((long)Math.Round(absolute, MidpointRounding.AwayFromZero)).ToString("#,0", Grouping);

            long totalShillings = (long)Math.Round(absolute * (double)ShillingsPerPound, MidpointRounding.AwayFromZero);
            if (totalShillings == 0)
                return absolute > 0f && !tinyAsZero ? UnderOneShilling : Symbol + "0";

            long pounds = totalShillings / ShillingsPerPound;
            long shillings = totalShillings % ShillingsPerPound;

            if (pounds == 0) return shillings + "s";
            if (shillings == 0) return Symbol + pounds.ToString("#,0", Grouping);
            return Symbol + pounds.ToString("#,0", Grouping) + " " + shillings + "s";
        }
    }
}
