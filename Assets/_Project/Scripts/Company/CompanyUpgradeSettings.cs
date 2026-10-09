using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace AntiqueTradingSimulator.Company
{
    public enum CompanyUpgradeType
    {
        Warehouse,
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
        TransportDaysReduction,

        /// <summary>Market fee rate on the player's own listings (0.05 = 5% of the price).</summary>
        MarketFeeRate,

        /// <summary>How many contracts the player can have accepted at once.</summary>
        MaxActiveContracts,

        /// <summary>
        /// Warehouse slots. Comes from WarehouseSettings.CapacityPerLevel — CompanyUpgrades fills
        /// it in for the UI; don't author it in Levels.
        /// </summary>
        WarehouseCapacity
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

        [Tooltip("Daily upkeep while at this level.")]
        [Min(0f)] public float DailyUpkeep;

        [Tooltip("Reputation needed to upgrade TO this level (0 = none).")]
        [Min(0)] public int RequiredReputation;

        [Tooltip("Optional descriptive lines shown above the effects (e.g. the name of the information source). " +
                 "Not read by game logic — put numbers in Effects so the UI and the systems stay in sync.")]
        [FormerlySerializedAs("Effect")]
        [TextArea(1, 3)] public string Note;

        [Tooltip("Values this level gives to game systems. Shown in the UI automatically.")]
        public List<UpgradeEffect> Effects = new List<UpgradeEffect>();

        public CompanyUpgradeLevel() { }

        public CompanyUpgradeLevel(float cost, float dailyUpkeep, int requiredReputation, string note,
            params UpgradeEffect[] effects)
        {
            Cost = cost;
            DailyUpkeep = dailyUpkeep;
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

        [Tooltip("Index 0 = starting level. Warehouse and Security take cost, upkeep and effect " +
                 "from WarehouseSettings; only RequiredReputation is read from here for them.")]
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
    /// </summary>
    [CreateAssetMenu(fileName = "CompanyUpgradeSettings", menuName = "AntiqueTradingSimulator/Company/Company Upgrade Settings")]
    public class CompanyUpgradeSettings : ScriptableObject
    {
        private static UpgradeEffect Access(int level) => new UpgradeEffect(UpgradeStat.InfoAccessLevel, level);
        private static UpgradeEffect TransportCost(float reduction) => new UpgradeEffect(UpgradeStat.TransportCostReduction, reduction);
        private static UpgradeEffect TransportDays(int days) => new UpgradeEffect(UpgradeStat.TransportDaysReduction, days);
        private static UpgradeEffect Fee(float rate) => new UpgradeEffect(UpgradeStat.MarketFeeRate, rate);
        private static UpgradeEffect ContractLimit(int count) => new UpgradeEffect(UpgradeStat.MaxActiveContracts, count);

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

            new CompanyUpgradeDefinition(CompanyUpgradeType.Security, "Security",
                "Improve security to reduce the risk of theft or damage.",
                false,
                new CompanyUpgradeLevel(0, 0, 0, "Basic protection\nTheft risk: -0%"),
                new CompanyUpgradeLevel(0, 0, 0, "Improved protection\nTheft risk: -10%"),
                new CompanyUpgradeLevel(0, 0, 500, "Guarded storage\nTheft risk: -25%"),
                new CompanyUpgradeLevel(0, 0, 1000, "Vault with guards\nTheft risk: -40%")),

            new CompanyUpgradeDefinition(CompanyUpgradeType.InformationNetwork, "Information Network",
                "Gain access to better news, rumours and market information.",
                true,
                new CompanyUpgradeLevel(0, 0, 0, "Basic news, often delayed", Access(1)),
                new CompanyUpgradeLevel(1200, 3, 0, "Auction houses, statistics, rumours", Access(2)),
                new CompanyUpgradeLevel(3000, 6, 500, "Leaks before official announcements", Access(3)),
                new CompanyUpgradeLevel(6000, 10, 1000, "Helps judge credibility and impact", Access(4)),
                new CompanyUpgradeLevel(10000, 15, 1500, "Foreign markets and regulations", Access(5))),

            new CompanyUpgradeDefinition(CompanyUpgradeType.Logistics, "Logistics",
                "Improve transport to reduce delivery time and costs.",
                true,
                new CompanyUpgradeLevel(0, 0, 0, "", TransportCost(0f), TransportDays(0)),
                new CompanyUpgradeLevel(800, 2, 0, "", TransportCost(0.05f), TransportDays(0)),
                new CompanyUpgradeLevel(2000, 4, 200, "", TransportCost(0.10f), TransportDays(1)),
                new CompanyUpgradeLevel(4000, 7, 500, "", TransportCost(0.15f), TransportDays(1)),
                new CompanyUpgradeLevel(7000, 10, 1000, "", TransportCost(0.20f), TransportDays(2))),

            new CompanyUpgradeDefinition(CompanyUpgradeType.Experts, "Experts",
                "Hire experts to improve appraisals and news evaluation.",
                false,
                new CompanyUpgradeLevel(0, 0, 0, "Standard appraisal\nNews reliability: +0%"),
                new CompanyUpgradeLevel(1000, 3, 0, "Better appraisal\nNews reliability: +15%"),
                new CompanyUpgradeLevel(2500, 6, 200, "Hints about authenticity\nNews reliability: +25%"),
                new CompanyUpgradeLevel(5000, 10, 500, "Reliable authenticity checks\nNews reliability: +35%"),
                new CompanyUpgradeLevel(8000, 15, 1000, "Master appraisal\nNews reliability: +50%")),

            new CompanyUpgradeDefinition(CompanyUpgradeType.Conservators, "Conservators",
                "Hire conservators to restore items faster and cheaper.",
                false,
                new CompanyUpgradeLevel(0, 0, 0, "Restoration cost: -0%\nRestoration time: -0%"),
                new CompanyUpgradeLevel(900, 3, 0, "Restoration cost: -15%\nRestoration time: -15%"),
                new CompanyUpgradeLevel(2200, 5, 200, "Restoration cost: -25%\nRestoration time: -25%"),
                new CompanyUpgradeLevel(4500, 9, 500, "Restoration cost: -35%\nRestoration time: -35%"),
                new CompanyUpgradeLevel(7500, 13, 1000, "Restoration cost: -45%\nRestoration time: -45%")),

            new CompanyUpgradeDefinition(CompanyUpgradeType.Staff, "Staff",
                "Expand your team to handle more operations.",
                true,
                new CompanyUpgradeLevel(0, 0, 0, "", Fee(0.05f), ContractLimit(2)),
                new CompanyUpgradeLevel(700, 4, 0, "", Fee(0.045f), ContractLimit(3)),
                new CompanyUpgradeLevel(1800, 8, 200, "", Fee(0.04f), ContractLimit(4)),
                new CompanyUpgradeLevel(3800, 13, 500, "", Fee(0.035f), ContractLimit(5)),
                new CompanyUpgradeLevel(6500, 18, 1000, "", Fee(0.03f), ContractLimit(6))),
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
