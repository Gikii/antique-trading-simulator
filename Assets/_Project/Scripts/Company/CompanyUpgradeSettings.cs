using System;
using System.Collections.Generic;
using UnityEngine;

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

    [Serializable]
    public class CompanyUpgradeLevel
    {
        [Tooltip("Price of upgrading TO this level. Ignored for the first level (starting level).")]
        [Min(0f)] public float Cost;

        [Tooltip("Daily upkeep while at this level.")]
        [Min(0f)] public float DailyUpkeep;

        [Tooltip("Reputation needed to upgrade TO this level (0 = none).")]
        [Min(0)] public int RequiredReputation;

        [Tooltip("What this level gives, one line per effect.")]
        [TextArea(1, 3)] public string Effect;

        public CompanyUpgradeLevel() { }

        public CompanyUpgradeLevel(float cost, float dailyUpkeep, int requiredReputation, string effect)
        {
            Cost = cost;
            DailyUpkeep = dailyUpkeep;
            RequiredReputation = requiredReputation;
            Effect = effect;
        }
    }

    [Serializable]
    public class CompanyUpgradeDefinition
    {
        public CompanyUpgradeType Type;
        public string DisplayName;
        [TextArea(2, 4)] public string Description;

        [Tooltip("False while the system this upgrade improves doesn't read it yet — " +
                 "the upgrade can be bought, but has no gameplay effect.")]
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
    /// Names, descriptions, prices, upkeep and reputation requirements of every company
    /// upgrade shown on Company → Development. If none is assigned to CompanyManager, an
    /// in-memory default is used.
    /// </summary>
    [CreateAssetMenu(fileName = "CompanyUpgradeSettings", menuName = "AntiqueTradingSimulator/Company/Company Upgrade Settings")]
    public class CompanyUpgradeSettings : ScriptableObject
    {
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
                false,
                new CompanyUpgradeLevel(0, 0, 0, "Local press\nBasic news, often delayed"),
                new CompanyUpgradeLevel(1200, 3, 0, "Industry sources\nAuction houses, statistics, rumours"),
                new CompanyUpgradeLevel(3000, 6, 500, "Informant network\nLeaks before official announcements"),
                new CompanyUpgradeLevel(6000, 10, 1000, "Expert\nHelps judge credibility and impact"),
                new CompanyUpgradeLevel(10000, 15, 1500, "International network\nForeign markets and regulations")),

            new CompanyUpgradeDefinition(CompanyUpgradeType.Logistics, "Logistics",
                "Improve transport to reduce delivery time and costs.",
                false,
                new CompanyUpgradeLevel(0, 0, 0, "Transport cost: -0%\nDelivery time: -0%"),
                new CompanyUpgradeLevel(800, 2, 0, "Transport cost: -5%\nDelivery time: -5%"),
                new CompanyUpgradeLevel(2000, 4, 200, "Transport cost: -10%\nDelivery time: -10%"),
                new CompanyUpgradeLevel(4000, 7, 500, "Transport cost: -15%\nDelivery time: -20%"),
                new CompanyUpgradeLevel(7000, 10, 1000, "Transport cost: -20%\nDelivery time: -25%")),

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
                false,
                new CompanyUpgradeLevel(0, 0, 0, "Market fee: -0%\nMax active contracts: 2"),
                new CompanyUpgradeLevel(700, 4, 0, "Market fee: -10%\nMax active contracts: 3"),
                new CompanyUpgradeLevel(1800, 8, 200, "Market fee: -20%\nMax active contracts: 4"),
                new CompanyUpgradeLevel(3800, 13, 500, "Market fee: -30%\nMax active contracts: 5"),
                new CompanyUpgradeLevel(6500, 18, 1000, "Market fee: -40%\nMax active contracts: 6")),
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
