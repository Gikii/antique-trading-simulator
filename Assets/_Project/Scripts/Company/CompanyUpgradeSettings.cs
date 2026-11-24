using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace AntiqueTradingSimulator.Company
{
    public enum CompanyUpgradeType
    {
        Warehouse,
        // Removed from the game (no theft/damage system to protect against). The value stays so the
        // numbers serialized for the upgrades after it (assets, saves) don't shift; nothing offers it.
        Security,
        InformationNetwork,
        Logistics,
        Experts,
        Conservators,
        Staff
    }

    /// <summary>
    /// A number an upgrade level gives to a game system. Game logic reads these values
    /// (CompanyUpgrades.GetStat) and the UI turns the same values into text
    /// (UpgradePresentation.FormatEffect), so the two can't drift apart.
    /// </summary>
    public enum UpgradeStat
    {
        /// <summary>InfoAccessLevel the player gets (1 = Local Press … 5 = International Network).</summary>
        InfoAccessLevel,

        /// <summary>Fraction taken off the player's transport cost (0.1 = -10%).</summary>
        TransportCostReduction,

        /// <summary>Whole days taken off the player's delivery time (never below 1 day).</summary>
        TransportWeeksReduction,

        /// <summary>Market fee rate on the player's own listings (0.05 = 5% of the price).</summary>
        MarketFeeRate,

        /// <summary>How many contracts the player can have accepted at once.</summary>
        MaxActiveContracts,

        /// <summary>
        /// Warehouse slots. Comes from WarehouseSettings.CapacityPerLevel — CompanyUpgrades fills
        /// it in for the UI; don't author it in Levels.
        /// </summary>
        WarehouseCapacity,

        // Appended (not inserted) so values serialized in a settings asset keep their meaning.

        /// <summary>Fraction taken off restoration fees (0.1 = -10%).</summary>
        RestorationCostReduction,

        /// <summary>Fraction taken off restoration time (0.1 = -10%, rounded, never below 1 day).</summary>
        RestorationTimeReduction,

        /// <summary>Chance the player's experts flag a fake rumour planted by an NPC (0.15 = 15%).</summary>
        NewsReliability
    }

    [Serializable]
    public struct UpgradeEffect
    {
        public UpgradeStat Stat;
        public float Value;

        public UpgradeEffect(UpgradeStat stat, float value)
        {
            Stat = stat;
            Value = value;
        }
    }

    [Serializable]
    public class CompanyUpgradeLevel
    {
        [Tooltip("Price of upgrading TO this level. Ignored for the first level (starting level).")]
        [Min(0f)] public float Cost;

        [Tooltip("Weekly upkeep while at this level (charged once per period).")]
        [FormerlySerializedAs("DailyUpkeep")]
        [Min(0f)] public float WeeklyUpkeep;

        [Tooltip("Reputation needed to upgrade TO this level (0 = none).")]
        [Min(0)] public int RequiredReputation;

        [Tooltip("Optional descriptive lines shown above the effects (e.g. the name of the information source). " +
                 "Not read by game logic — put numbers in Effects so the UI and the systems stay in sync.")]
        [FormerlySerializedAs("Effect")]
        [TextArea(1, 3)] public string Note;

        [Tooltip("Values this level gives to game systems. Shown in the UI automatically.")]
        public List<UpgradeEffect> Effects = new List<UpgradeEffect>();

        public CompanyUpgradeLevel() { }

        public CompanyUpgradeLevel(float cost, float weeklyUpkeep, int requiredReputation, string note,
            params UpgradeEffect[] effects)
        {
            Cost = cost;
            WeeklyUpkeep = weeklyUpkeep;
            RequiredReputation = requiredReputation;
            Note = note;
            Effects = new List<UpgradeEffect>(effects);
        }

        public bool TryGetEffect(UpgradeStat stat, out float value)
        {
            if (Effects != null)
                foreach (var effect in Effects)
                    if (effect.Stat == stat)
                    {
                        value = effect.Value;
                        return true;
                    }

            value = 0f;
            return false;
        }
    }

    [Serializable]
    public class CompanyUpgradeDefinition
    {
        public CompanyUpgradeType Type;
        public string DisplayName;
        [TextArea(2, 4)] public string Description;

        [Tooltip("False while the system this upgrade improves doesn't read it yet — the upgrade can be " +
                 "bought, but has no gameplay effect and its Effects are ignored by game logic.")]
        public bool EffectActive;

        [Tooltip("Index 0 = starting level. Warehouse takes cost, upkeep and effect from " +
                 "WarehouseSettings; only RequiredReputation is read from here for it.")]
        public List<CompanyUpgradeLevel> Levels = new List<CompanyUpgradeLevel>();

        public CompanyUpgradeDefinition() { }

        public CompanyUpgradeDefinition(CompanyUpgradeType type, string displayName, string description,
            bool effectActive, params CompanyUpgradeLevel[] levels)
        {
            Type = type;
            DisplayName = displayName;
            Description = description;
            EffectActive = effectActive;
            Levels = new List<CompanyUpgradeLevel>(levels);
        }
    }

    /// <summary>
    /// Names, descriptions, prices, upkeep, reputation requirements and effect values of every
    /// company upgrade shown on Company → Development. If none is assigned to CompanyManager,
    /// an in-memory default is used.
    ///
    /// Costs follow how a dealer's business was paid for in 1884: little up front (a desk, tools,
    /// an introduction) and most of it every week — wages, retainers, subscriptions. Reference points:
    /// an office clerk 25s a week, a workshop hand 30s+, a carman 30s, a commissionaire guard ~24s.
    /// The Note of each level says who or what the upkeep pays for. Weekly upkeep the player can't
    /// pay costs the upgrade a level (CompanyManager.ChargeUpgradeUpkeep).
    /// </summary>
    [CreateAssetMenu(fileName = "CompanyUpgradeSettings", menuName = "AntiqueTradingSimulator/Company/Company Upgrade Settings")]
    public class CompanyUpgradeSettings : ScriptableObject
    {
        private static UpgradeEffect Access(int level) => new UpgradeEffect(UpgradeStat.InfoAccessLevel, level);
        private static UpgradeEffect TransportCost(float reduction) => new UpgradeEffect(UpgradeStat.TransportCostReduction, reduction);
        private static UpgradeEffect TransportWeeks(int weeks) => new UpgradeEffect(UpgradeStat.TransportWeeksReduction, weeks);
        private static UpgradeEffect Fee(float rate) => new UpgradeEffect(UpgradeStat.MarketFeeRate, rate);
        private static UpgradeEffect ContractLimit(int count) => new UpgradeEffect(UpgradeStat.MaxActiveContracts, count);
        private static UpgradeEffect RestorationCost(float reduction) => new UpgradeEffect(UpgradeStat.RestorationCostReduction, reduction);
        private static UpgradeEffect RestorationTime(float reduction) => new UpgradeEffect(UpgradeStat.RestorationTimeReduction, reduction);
        private static UpgradeEffect NewsReliability(float chance) => new UpgradeEffect(UpgradeStat.NewsReliability, chance);

        public List<CompanyUpgradeDefinition> Upgrades = new List<CompanyUpgradeDefinition>
        {
            new CompanyUpgradeDefinition(CompanyUpgradeType.Warehouse, "Warehouse",
                "Increase storage capacity. A larger warehouse lets you hold more antiques at once.",
                true,
                new CompanyUpgradeLevel(0, 0, 0, ""),
                new CompanyUpgradeLevel(0, 0, 0, ""),
                new CompanyUpgradeLevel(0, 0, 0, ""),
                new CompanyUpgradeLevel(0, 0, 500, ""),
                new CompanyUpgradeLevel(0, 0, 1000, "")),

            new CompanyUpgradeDefinition(CompanyUpgradeType.InformationNetwork, "Information Network",
                "Gain access to better news, rumours and market information.",
                true,
                new CompanyUpgradeLevel(0, 0, 0, "Basic news, often delayed", Access(1)),
                new CompanyUpgradeLevel(2, 0.15f, 0, "Daily papers and auction catalogues (3s a week)\nAuction houses, statistics, rumours", Access(2)),
                new CompanyUpgradeLevel(5, 0.5f, 500, "+ A paid informant in the trade\nLeaks before official announcements", Access(3)),
                new CompanyUpgradeLevel(10, 1.75f, 1000, "+ An agent who checks what you hear (25s a week)\nHelps judge credibility and impact", Access(4)),
                new CompanyUpgradeLevel(20, 3f, 1500, "+ Correspondents abroad and foreign telegrams\nForeign markets and regulations", Access(5))),

            new CompanyUpgradeDefinition(CompanyUpgradeType.Logistics, "Logistics",
                "Improve transport to reduce delivery time and costs.",
                true,
                new CompanyUpgradeLevel(0, 0, 0, "", TransportCost(0f), TransportWeeks(0)),
                new CompanyUpgradeLevel(1, 0.05f, 0, "An account with a carrier", TransportCost(0.05f), TransportWeeks(0)),
                new CompanyUpgradeLevel(5, 0.3f, 200, "+ A forwarding agent on retainer", TransportCost(0.10f), TransportWeeks(1)),
                new CompanyUpgradeLevel(45, 2.3f, 500, "+ Your own van, horse and carman (30s a week)", TransportCost(0.15f), TransportWeeks(1)),
                new CompanyUpgradeLevel(10, 2.8f, 1000, "+ An agent on the Continent", TransportCost(0.20f), TransportWeeks(2))),

            new CompanyUpgradeDefinition(CompanyUpgradeType.Experts, "Experts",
                "Hire experts to improve appraisals and news evaluation.",
                true,
                new CompanyUpgradeLevel(0, 0, 0, "Standard appraisal", NewsReliability(0f)),
                new CompanyUpgradeLevel(5, 0.25f, 0, "Reference books and a valuer on call\nBetter appraisal", NewsReliability(0.15f)),
                new CompanyUpgradeLevel(10, 0.75f, 200, "A valuer on retainer\nHints about authenticity", NewsReliability(0.25f)),
                new CompanyUpgradeLevel(20, 1.5f, 500, "A specialist consulted every week\nReliable authenticity checks", NewsReliability(0.35f)),
                new CompanyUpgradeLevel(30, 2.5f, 1000, "A resident connoisseur\nMaster appraisal", NewsReliability(0.50f))),

            new CompanyUpgradeDefinition(CompanyUpgradeType.Conservators, "Conservators",
                "Hire conservators to restore items faster and cheaper.",
                true,
                new CompanyUpgradeLevel(0, 0, 0, "", RestorationCost(0f), RestorationTime(0f)),
                new CompanyUpgradeLevel(5, 0.5f, 0, "Tools and an apprentice (10s a week)", RestorationCost(0.05f), RestorationTime(0.10f)),
                new CompanyUpgradeLevel(10, 1.5f, 200, "A journeyman restorer and an apprentice", RestorationCost(0.10f), RestorationTime(0.20f)),
                new CompanyUpgradeLevel(15, 3f, 500, "Two restorers (30s a week each)", RestorationCost(0.15f), RestorationTime(0.30f)),
                new CompanyUpgradeLevel(20, 4f, 1000, "A master restorer's workshop", RestorationCost(0.20f), RestorationTime(0.40f))),

            new CompanyUpgradeDefinition(CompanyUpgradeType.Staff, "Staff",
                "Expand your team to handle more operations.",
                true,
                new CompanyUpgradeLevel(0, 0, 0, "", Fee(0.05f), ContractLimit(2)),
                new CompanyUpgradeLevel(2, 0.5f, 0, "An errand boy (10s a week)", Fee(0.045f), ContractLimit(3)),
                new CompanyUpgradeLevel(3, 1.75f, 200, "+ A clerk (25s a week)", Fee(0.04f), ContractLimit(4)),
                new CompanyUpgradeLevel(3, 3f, 500, "+ A second clerk (25s a week)", Fee(0.035f), ContractLimit(5)),
                new CompanyUpgradeLevel(5, 4.75f, 1000, "+ A head clerk (35s a week)", Fee(0.03f), ContractLimit(6))),
        };

        public CompanyUpgradeDefinition Get(CompanyUpgradeType type)
        {
            foreach (var upgrade in Upgrades)
                if (upgrade != null && upgrade.Type == type)
                    return upgrade;
            return null;
        }
    }
}
