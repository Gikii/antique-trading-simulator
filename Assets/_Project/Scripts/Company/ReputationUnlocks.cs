using System.Collections.Generic;
using System.Globalization;
using System.Text;
using AntiqueTradingSimulator.Contracts;

namespace AntiqueTradingSimulator.Company
{
    /// <summary>One line of a tier's unlock list.</summary>
    public readonly struct ReputationUnlock
    {
        public readonly string Text;

        /// <summary>False for unlocks no game system enforces yet (shown as "coming soon").</summary>
        public readonly bool Active;

        public ReputationUnlock(string text, bool active)
        {
            Text = text;
            Active = active;
        }
    }

    /// <summary>
    /// Builds what a reputation or credibility tier gives access to, from the data the game
    /// actually enforces — contract classes (ContractManager) and upgrade-level reputation
    /// requirements (CompanyUpgradeSettings) — followed by the tier's hand-written Unlocks,
    /// which describe systems that don't exist yet. Reputation gives access; it never changes prices.
    /// </summary>
    public static class ReputationUnlocks
    {
        public static List<ReputationUnlock> For(ReputationKind kind, int tierIndex, ReputationSettings settings,
            CompanyUpgrades upgrades, IReadOnlyList<ContractClassDefinition> contractClasses)
        {
            var result = new List<ReputationUnlock>();
            if (settings == null) return result;

            var tiers = kind == ReputationKind.Reputation ? settings.ReputationTiers : settings.CredibilityTiers;
            if (tiers == null || tierIndex < 0 || tierIndex >= tiers.Count) return result;

            var tier = tiers[tierIndex];

            if (kind == ReputationKind.Reputation)
            {
                float from = tier.MinValue;
                float to = tierIndex + 1 < tiers.Count ? tiers[tierIndex + 1].MinValue : float.MaxValue;
                bool InTier(int required) => required > 0 && required >= from && required < to;

                if (tierIndex == 0)
                    result.Add(new ReputationUnlock("Standard contracts from local clients", true));

                if (contractClasses != null)
                    foreach (var c in contractClasses)
                        if (c != null && InTier(c.RequiredReputation))
                            result.Add(new ReputationUnlock(ContractClassLine(c), true));

                string upgradeLine = UpgradeLevelsLine(upgrades, InTier);
                if (upgradeLine.Length > 0)
                    result.Add(new ReputationUnlock(upgradeLine, true));
            }

            if (tier.Unlocks != null)
                foreach (var text in tier.Unlocks)
                    if (!string.IsNullOrWhiteSpace(text))
                        result.Add(new ReputationUnlock(text.Trim(), false));

            return result;
        }

        private static string ContractClassLine(ContractClassDefinition c)
        {
            string min = c.MinRewardMultiplier.ToString("0.0#", CultureInfo.InvariantCulture);
            string max = c.MaxRewardMultiplier.ToString("0.0#", CultureInfo.InvariantCulture);
            string client = string.IsNullOrWhiteSpace(c.ClientDescription) ? "" : $" from {c.ClientDescription.Trim().ToLowerInvariant()}";
            return $"{c.Name} contracts{client} (pay ×{min}–{max} of market value)";
        }

        /// <summary>"Upgrade levels: Warehouse 4, Information Network 3" for levels whose requirement falls in the tier.</summary>
        private static string UpgradeLevelsLine(CompanyUpgrades upgrades, System.Func<int, bool> inTier)
        {
            if (upgrades == null) return "";

            var sb = new StringBuilder();
            foreach (var type in CompanyUpgrades.AllTypes)
            {
                int count = upgrades.LevelCount(type);
                for (int level = 1; level < count; level++)
                {
                    if (!inTier(upgrades.RequiredReputation(type, level))) continue;

                    var definition = upgrades.Settings.Get(type);
                    string name = definition != null && !string.IsNullOrEmpty(definition.DisplayName) ? definition.DisplayName : type.ToString();

                    sb.Append(sb.Length == 0 ? "Upgrade levels: " : ", ").Append(name).Append(' ').Append(level + 1);
                    break; // the lowest level per upgrade is enough — higher ones in the same tier follow from it
                }
            }
            return sb.ToString();
        }
    }
}
