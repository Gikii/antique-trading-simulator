using System;
using System.Collections.Generic;
using UnityEngine;

namespace AntiqueTradingSimulator.Company
{
    /// <summary>
    /// One step on the reputation or credibility ladder. Reputation tiers double as the
    /// company's title ("Local Shop", "Recognised Dealer", ...). Reputation gives access,
    /// it never changes prices. What a tier unlocks is listed by ReputationUnlocks: contract
    /// classes and upgrade levels automatically, then the hand-written Unlocks below.
    /// </summary>
    [Serializable]
    public class ReputationTier
    {
        public string Name;

        [Tooltip("Lowest value of this tier. Reputation: points. Credibility: 0..1.")]
        public float MinValue;

        [TextArea(2, 4)]
        public string Description;

        [Tooltip("Unlocks no game system enforces yet — shown as \"coming soon\". Don't list contract classes " +
                 "or upgrade levels here: those are added automatically from ContractManager and CompanyUpgradeSettings.")]
        public List<string> Unlocks = new List<string>();

        public ReputationTier() { }

        public ReputationTier(string name, float minValue, string description, params string[] unlocks)
        {
            Name = name;
            MinValue = minValue;
            Description = description;
            Unlocks = new List<string>(unlocks);
        }
    }

    /// <summary>
    /// Tunable numbers for company reputation (prestige, how well-known the firm is) and
    /// credibility (reliability, 0..1). If none is assigned to CompanyManager, an in-memory
    /// default is used.
    /// </summary>
    [CreateAssetMenu(fileName = "ReputationSettings", menuName = "AntiqueTradingSimulator/Company/Reputation Settings")]
    public class ReputationSettings : ScriptableObject
    {
        [Header("Starting values")]
        [Min(0)] public int StartingReputation = 100;
        [Range(0f, 1f)] public float StartingCredibility = 0.5f;

        [Header("Reputation tiers (ascending, first must start at 0)")]
        public List<ReputationTier> ReputationTiers = new List<ReputationTier>
        {
            new ReputationTier("Local Shop", 0,
                "A small antique shop inherited from your grandfather. Few collectors know your name yet."),
            new ReputationTier("Local Dealer", 200,
                "Local collectors know and visit your shop."),
            new ReputationTier("Known Dealer", 500,
                "Your name is known among dealers in the country."),
            new ReputationTier("Recognised Dealer", 1000,
                "A recognised dealer with a solid reputation in the antique world.",
                "Access to international auctions", "Higher chance for rare items"),
            new ReputationTier("Renowned Dealer", 1500,
                "Collectors across Europe seek your opinion and your stock."),
            new ReputationTier("Elite House", 2500,
                "One of the most respected antique houses in Europe.",
                "Invitations to private sales"),
        };

        [Header("Credibility tiers (ascending, 0..1, first must start at 0)")]
        public List<ReputationTier> CredibilityTiers = new List<ReputationTier>
        {
            new ReputationTier("Unreliable", 0f,
                "Partners doubt your word and avoid long-term deals."),
            new ReputationTier("Questionable", 0.25f,
                "Some partners hesitate to work with you."),
            new ReputationTier("Reliable", 0.5f,
                "You are seen as a dependable partner.",
                "Better contract terms", "Partners share more information"),
            new ReputationTier("Trusted", 0.75f,
                "Collectors and dealers are willing to work with you and share valuable opportunities.",
                "Best contracts and commissions", "Access to exclusive information", "Invitations to private sales"),
        };

        [Header("Contracts")]
        public int ContractFulfilledReputation = 15;
        [Tooltip("Extra reputation per € of contract reward (0.01 = +1 per 100 €).")]
        [Min(0f)] public float ReputationPerContractReward = 0.01f;
        public int ExclusiveContractBonusReputation = 10;
        public float ContractFulfilledCredibility = 0.03f;
        public float ExclusiveContractBonusCredibility = 0.02f;
        public int ContractFailedReputation = -25;
        public float ContractFailedCredibility = -0.08f;

        [Header("Market sales")]
        [Tooltip("Reputation per € of market sales, summed per day (0.02 = +1 per 50 €).")]
        [Min(0f)] public float ReputationPerSaleValue = 0.02f;

        [Header("History")]
        [Tooltip("How many reputation/credibility changes are kept for the history lists.")]
        [Min(10)] public int MaxHistoryEntries = 200;
    }
}
