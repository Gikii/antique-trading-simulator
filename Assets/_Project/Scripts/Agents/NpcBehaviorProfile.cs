using System.Collections.Generic;
using UnityEngine;
using AntiqueTradingSimulator.Events;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.Agents
{
    /// <summary>
    /// Static, author-time definition of an NPC archetype (Museum, Speculator, Investor,
    /// Novice) — mirrors AntiqueDefinition. Holds only data that never changes at runtime:
    /// interests, information trust, and buy/sell thresholds. Never referenced directly by
    /// a runtime NPCTrader; looked up by Id via NpcProfileDatabase instead.
    /// </summary>
    [CreateAssetMenu(fileName = "NewNpcProfile", menuName = "AntiqueTradingSimulator/NPC/Behavior Profile")]
    public class NpcBehaviorProfile : ScriptableObject
    {
        [Header("Identity")]
        public string Id;
        public string ProfileName;

        [Header("Information access")]
        public InfoAccessLevel AccessLevel = InfoAccessLevel.LocalPress;

        [Header("Interests (empty list = interested in everything on that axis)")]
        public List<AntiqueType> PreferredTypes = new();
        public List<Country> PreferredCountries = new();
        public List<TimePeriod> PreferredPeriods = new();

        [Header("Trust in information")]
        [Range(0f, 1f)] public float RumorTrust = 0.3f;
        [Range(0f, 1f)] public float LeakTrust = 0.7f;
        [Range(0, 5)] public int MinReactionDelayDays = 0;
        [Range(0, 5)] public int MaxReactionDelayDays = 2;

        [Header("Buying")]
        [Range(0f, 1f)] public float DailyBudgetFraction = 0.2f;
        public float MaxPriceMultiplierWillingToPay = 1.3f;
        [Range(0f, 1f)] public float RiskTolerance = 0.5f;

        [Header("Selling")]
        public float ProfitTargetMultiplier = 1.4f;
        public int MinHoldingDaysBeforeSell = 3;
    }
}