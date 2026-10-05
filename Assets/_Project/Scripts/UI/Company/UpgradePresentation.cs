using System.Collections.Generic;
using System.Text;
using AntiqueTradingSimulator.Company;
using UnityEngine;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>Shared wording for upgrade cards and the upgrade details panel.</summary>
    public static class UpgradePresentation
    {
        public const string EffectComingSoon = "Effect coming soon";

        public static string LevelLabel(UpgradeInfo info) => $"Level {info.DisplayLevel} / {info.LevelCount}";

        public static string PerDay(float amount) => $"{UIFormat.Money(amount)} / day";

        public static string CostValue(UpgradeInfo info) => info.IsMaxed ? "—" : UIFormat.Money(info.NextCost);

        /// <summary>"Requires 500 reputation", red while unmet; empty when there's no requirement.</summary>
        public static string RequirementLine(UpgradeInfo info)
        {
            if (info.IsMaxed || info.RequiredReputation <= 0) return "";

            string text = $"Requires {UIFormat.Number(info.RequiredReputation)} reputation";
            return UIFormat.Colorize(text, info.MeetsReputation ? UIFormat.MutedColor : UIFormat.NegativeColor);
        }

        /// <summary>Tooltip for an upgrade button: why it's disabled, or what clicking does.</summary>
        public static string ButtonTooltip(UpgradeInfo info)
        {
            string text = info.CanUpgrade
                ? $"Pay {UIFormat.Money(info.NextCost)} to reach level {info.DisplayLevel + 1}."
                : info.BlockReason;

            if (!info.CanAfford && !info.IsMaxed && info.MeetsReputation)
                text += $"\n<size=85%>You need {UIFormat.Money(info.NextCost - info.Cash)} more.</size>";

            if (!info.EffectActive && !info.IsMaxed)
                text += "\n<size=85%>The level is kept, but no game system uses it yet.</size>";

            return text;
        }

        /// <summary>
        /// Splits an effect text ("Capacity: 12 items\nBasic protection") into rows.
        /// "Label: value" lines become (label, value); other lines become (line, "").
        /// </summary>
        public static List<(string label, string value)> EffectRows(string effect)
        {
            var rows = new List<(string, string)>();
            if (string.IsNullOrWhiteSpace(effect)) return rows;

            foreach (var raw in effect.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;

                int colon = line.IndexOf(':');
                rows.Add(colon > 0
                    ? (line.Substring(0, colon).Trim(), line.Substring(colon + 1).Trim())
                    : (line, ""));
            }
            return rows;
        }

        /// <summary>
        /// Multi-line rich text for a card column: small muted header, the effect lines
        /// (optionally coloured) and the daily upkeep.
        /// </summary>
        public static string EffectBlock(string header, string effect, float upkeep, Color? color = null)
        {
            var sb = new StringBuilder();
            sb.Append("<size=80%>").Append(UIFormat.Colorize(header, UIFormat.MutedColor)).Append("</size>");

            foreach (var (label, value) in EffectRows(effect))
            {
                string line = value.Length > 0 ? $"{label}: {value}" : label;
                sb.Append('\n').Append(color.HasValue ? UIFormat.Colorize(line, color.Value) : line);
            }

            sb.Append('\n').Append(UIFormat.Colorize($"Upkeep: {PerDay(upkeep)}", UIFormat.MutedColor));
            return sb.ToString();
        }

        public static string MaxedBlock(string header) =>
            $"<size=80%>{UIFormat.Colorize(header, UIFormat.MutedColor)}</size>\n" +
            UIFormat.Colorize("Maximum level reached", UIFormat.MutedColor);
    }
}
