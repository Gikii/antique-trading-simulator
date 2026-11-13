using System.Text;
using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.Seasons;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>Player-facing texts about seasons, generated from SeasonSettings so they can't drift from the rules.</summary>
    public static class SeasonPresentation
    {
        /// <summary>
        /// "Winter\nSnow, ice and frozen ports — …\n\nTransport: delays 15% local · 30% domestic · 45% international,
        /// damage risk ×1.5, prices +15%."
        /// </summary>
        public static string SeasonTooltip(Season season, SeasonSettings settings,
            WeatherSeverity weather = WeatherSeverity.Normal)
        {
            if (settings == null) return season.ToDisplayString();

            var profile = settings.Get(season);
            var text = new StringBuilder();
            text.Append(UIFormat.Colorize(season.ToDisplayString(), UIFormat.AccentColor));
            if (!string.IsNullOrEmpty(profile.Description))
                text.Append('\n').Append(profile.Description);

            if (weather != WeatherSeverity.Normal)
            {
                var effect = settings.GetSeverityEffect(weather);
                text.Append("\n\n").Append(UIFormat.Colorize($"This year: {weather.Describe(season)}", UIFormat.AccentColor))
                    .Append($" — weather delays ×{effect.DelayMultiplier:0.##}, damage risk ×{effect.DamageMultiplier:0.##}.");
            }

            text.Append("\n\nTransport (Standard service):");
            AppendZone(text, "Local", profile.Local);
            AppendZone(text, "Domestic", profile.Domestic);
            AppendZone(text, "International", profile.International);

            string damage = Multiplier(profile.DamageChanceMultiplier);
            string price = PriceChange(profile.TransportCostMultiplier);
            if (damage != null) text.Append($"\nDamage risk {damage}");
            if (price != null) text.Append($"\nCarrier prices {price}");

            text.Append("\n\nMarket (at the height of the season):");
            AppendChange(text, "NPC trading", profile.NpcActivity);
            AppendChange(text, "Private collectors", profile.CollectorChance);
            AppendChange(text, "New offers", profile.NewListings);
            if (profile.Demand != null)
                foreach (var entry in profile.Demand)
                    AppendChange(text, $"Demand for {entry.Type}", entry.Multiplier);

            return text.ToString();
        }

        /// <summary>Tooltip of one transport option: the weather of the season the shipment arrives in.</summary>
        public static string TransportOutlook(TransportManager.SeasonalOutlook outlook)
        {
            var text = new StringBuilder();
            text.Append(outlook.Severity == WeatherSeverity.Normal
                ? $"Arrives in {outlook.Season.ToLowerString()}."
                : $"Arrives in {outlook.Severity.Describe(outlook.Season)}.");
            if (!string.IsNullOrEmpty(outlook.Description))
                text.Append('\n').Append(outlook.Description);

            text.Append('\n');
            if (outlook.DelayChance > 0f)
                text.Append($"\n{UIFormat.Percent(outlook.DelayChance)} chance to be held up 1 week.");
            if (outlook.EarlyChance > 0f)
                text.Append($"\n{UIFormat.Percent(outlook.EarlyChance)} chance to arrive 1 week early.");
            if (outlook.DelayChance <= 0f && outlook.EarlyChance <= 0f)
                text.Append("\nThe weather won't change the arrival week.");

            text.Append($"\n{UIFormat.Percent(outlook.DamageChance)} damage risk.");
            return text.ToString();
        }

        private static void AppendZone(StringBuilder text, string label, SeasonSettings.ZoneOdds odds)
        {
            text.Append($"\n  {label}: ");
            if (odds.DelayChance <= 0f && odds.EarlyChance <= 0f)
            {
                text.Append("no weather effects");
                return;
            }

            if (odds.DelayChance > 0f) text.Append($"{UIFormat.Percent(odds.DelayChance)} delay");
            if (odds.DelayChance > 0f && odds.EarlyChance > 0f) text.Append(", ");
            if (odds.EarlyChance > 0f) text.Append($"{UIFormat.Percent(odds.EarlyChance)} early");
        }

        /// <summary>"\n  Private collectors +25%" — skipped when there is no change.</summary>
        private static void AppendChange(StringBuilder text, string label, float multiplier)
        {
            string change = PriceChange(multiplier);
            if (change != null) text.Append($"\n  {label} {change}");
        }

        /// <summary>"×1.5", or null for ×1.</summary>
        private static string Multiplier(float value) =>
            UnityEngine.Mathf.Approximately(value, 1f) ? null : $"×{value:0.##}";

        /// <summary>"+15%" / "-10%", or null for no change.</summary>
        private static string PriceChange(float multiplier)
        {
            if (UnityEngine.Mathf.Approximately(multiplier, 1f)) return null;
            int percent = UnityEngine.Mathf.RoundToInt((multiplier - 1f) * 100f);
            return percent > 0 ? $"+{percent}%" : $"{percent}%";
        }
    }
}
