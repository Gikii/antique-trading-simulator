using System;
using System.Globalization;
using AntiqueTradingSimulator.Saving;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>The words a save slot is shown with. Shared by the slot list and the main menu's Continue button.</summary>
    public static class SaveSlotFormat
    {
        private static readonly CultureInfo English = CultureInfo.InvariantCulture;

        /// <summary>"Week 34 / 104 · 3 February 1885" (no "/ 104" for saves that don't know the campaign length).</summary>
        public static string DayAndDate(SaveHeader header)
        {
            if (header == null) return string.Empty;

            string day = header.CampaignFinished
                ? $"Week {header.SavedOnDay} · Sandbox"
                : header.CampaignLength > 0
                    ? $"Week {header.SavedOnDay} / {header.CampaignLength}"
                    : $"Week {header.SavedOnDay}";

            return string.IsNullOrEmpty(header.InGameDate) ? day : $"{day} · {header.InGameDate}";
        }

        /// <summary>"Cash 12,400 · Net worth 48,900 · Played 2 h 15 min".</summary>
        public static string Figures(SaveHeader header)
        {
            if (header == null) return string.Empty;

            string figures = $"Cash {UIFormat.Money(header.Cash)} · Net worth {UIFormat.Money(header.NetWorth)}";
            return header.PlaytimeSeconds > 0d ? $"{figures} · Played {Playtime(header.PlaytimeSeconds)}" : figures;
        }

        /// <summary>"45 min", "2 h 15 min".</summary>
        public static string Playtime(double seconds)
        {
            int minutes = Math.Max(1, (int)Math.Round(seconds / 60d));
            int hours = minutes / 60;
            minutes %= 60;
            return hours > 0 ? $"{hours} h {minutes} min" : $"{minutes} min";
        }

        /// <summary>"Today, 19:42", "Yesterday, 08:10", "2 Nov, 21:10", "2 Nov 2025" — in the player's local time.</summary>
        public static string SavedAt(DateTime savedAtUtc) => SavedAt(savedAtUtc, DateTime.Now);

        public static string SavedAt(DateTime savedAtUtc, DateTime now)
        {
            if (savedAtUtc == default) return string.Empty;

            DateTime local = savedAtUtc.ToLocalTime();
            string time = local.ToString("HH:mm", English);

            if (local.Date == now.Date) return $"Today, {time}";
            if (local.Date == now.Date.AddDays(-1)) return $"Yesterday, {time}";
            if (local.Year == now.Year) return $"{local.ToString("d MMM", English)}, {time}";
            return local.ToString("d MMM yyyy", English);
        }

        /// <summary>What an unusable or empty slot says instead of its details.</summary>
        public static string Problem(SaveSlotInfo info)
        {
            if (info == null || !info.Exists) return "Empty slot";
            if (info.IsCorrupt) return "This save can't be read";
            if (info.IsIncompatible) return "Saved by a different version of the game";
            return string.Empty;
        }
    }
}
