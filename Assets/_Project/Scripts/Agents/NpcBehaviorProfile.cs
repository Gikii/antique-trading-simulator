using AntiqueTradingSimulator.Events;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.Agents
{
    /// <summary>
    /// Static, author-time definition of an NPC archetype (Museum, Speculator, Investor,
    /// Novice) — mirrors AntiqueDefinition. Holds only data that never changes at runtime:
    /// interests, information trust, and buy/sell thresholds. Every NPC runs the same logic
    /// (NPCTrader); the archetypes differ only through these numbers. Never referenced directly
    /// by a runtime NPCTrader; looked up by Id via NpcProfileDatabase instead.
    /// Numbers were tuned with Tools/economy_sim.py — check changes there first.
    /// </summary>
    [CreateAssetMenu(fileName = "NewNpcProfile", menuName = "AntiqueTradingSimulator/NPC/Behavior Profile")]
    public class NpcBehaviorProfile : ScriptableObject
    {
        [Header("Identity")]
        [FormerlySerializedAs("Id")]
        [ReadOnly ,SerializeField] private string id = Guid.NewGuid().ToString("N");
        public string Id => id;
        public string ProfileName;

        [Tooltip("Names given to NPCs with this profile, in order (shown in the competition ranking and the " +
                 "market feed). When they run out: \"<Profile Name> 2\", \"<Profile Name> 3\", ...")]
        public List<string> TraderNames = new();

        [Tooltip("Cash an NPC with this profile starts with. 0 = NPCManager's default.")]
        [Min(0f)] public float StartingCash;

        [Header("Interests (empty list = interested in everything on that axis)")]
        public List<AntiqueType> PreferredTypes = new();
        public List<Country> PreferredCountries = new();
        public List<Century> PreferredCenturies = new();

        [Header("Actions")]
        [Tooltip("The number of actions the NPC undertakes per period")]
        [Min(0)] public int ActionsPerPeriod = 5;

        [Tooltip("The percentage of the NPC's action fullfilling a contract can take up while the contract is not yet judged urgent")]
        [Range(0f, 1f)] public float ContractSlotShare = 0.8f;

        [Tooltip("The minimum return on a purchase to dedicate an action to it. This value is doubled when a contract is being fullfilled")]
        public float MinBargainScore = 0.08f;

        [Header("Information access")]
        [Header("Information access")]
        public InfoAccessLevel AccessLevel = InfoAccessLevel.LocalPress;

        [Header("Trust in information")]
        [Range(0f, 1f)] public float RumorTrust = 0.3f;
        [Range(0f, 1f)] public float LeakTrust = 0.7f;
        [FormerlySerializedAs("MinReactionDelayDays")]
        [Range(0, 5)] public int MinReactionDelayWeeks = 0;
        [FormerlySerializedAs("MaxReactionDelayDays")]
        [Range(0, 5)] public int MaxReactionDelayWeeks = 2;

        [Header("Buying")]
        [FormerlySerializedAs("DailyBudgetFraction")]
        [Range(0f, 1f)] public float WeeklyBudgetFraction = 0.2f;
        [Tooltip("Most the NPC pays relative to what the antique is worth in its condition " +
                 "(reference price of its type × condition). 1.3 = up to 30% over that value.")]
        public float MaxPriceMultiplierWillingToPay = 1.3f;
        [Range(0f, 1f)] public float RiskTolerance = 0.5f;

        [Tooltip("Doesn't buy antiques in worse condition than this (contracts set their own minimum).")]
        [Range(0f, 1f)] public float MinConditionToBuy;

        [Tooltip("Extra willingness to pay for good condition: the price limit is multiplied by " +
                 "1 + premium × (condition − 0.5). 0.6 → +27% at 95% condition.")]
        [Min(0f)] public float ConditionPremium;

        [Header("Selling")]
        [Tooltip("Builds a collection and never sells (e.g. a museum) — not even on bad news.")]
        public bool NeverSells;

        public float ProfitTargetMultiplier = 1.4f;
        [FormerlySerializedAs("MinHoldingDaysBeforeSell")]
        public int MinHoldingWeeksBeforeSell = 3;

        [Tooltip("After holding an antique this long, the NPC settles for Patient Profit Multiplier instead of the " +
                 "profit target — keeps its money moving instead of sitting on stock forever. 0 = never.")]
        [FormerlySerializedAs("PatientSellAfterDays")]
        [Min(0)] public int PatientSellAfterWeeks = 10;
        [Min(0f)] public float PatientProfitMultiplier = 1.05f;

        [Tooltip("Panic selling: sells at once (instant sale) when an antique is worth less than its cost × this. " +
                 "0 = never.")]
        [Range(0f, 1f)] public float StopLossMultiplier;

        [Tooltip("Lists antiques on the market at a markup instead of selling them instantly — the player " +
                 "(and other NPCs and collectors) can buy them.")]
        public bool ListsOnMarket;

        [Tooltip("Asking price = current value × this (never below cost × the profit target in use).")]
        [Min(1f)] public float ListingMarkup = 1.15f;

        [Tooltip("A listing that hasn't sold after this many weeks is withdrawn and re-evaluated.")]
        [FormerlySerializedAs("RelistAfterDays")]
        [Min(1)] public int RelistAfterWeeks = 5;

        [Header("Seasons")]
        [Tooltip("How much of the expected seasonal price change the NPC prices in: 0 = ignores the calendar, " +
                 "1 = fully. A far-sighted trader pays more for a type whose season is coming, holds it until the " +
                 "season peaks and sells before it ends.")]
        [Range(0f, 1f)] public float SeasonalForesight;

        [Tooltip("How many weeks ahead the NPC looks when judging the season.")]
        [FormerlySerializedAs("SeasonalHorizonDays")]
        [Min(0)] public int SeasonalHorizonWeeks = 8;

        [Header("Contracts")]
        [Tooltip("Takes delivery contracts from the board. Contracts already taken are finished either way.")]
        public bool TakesContracts = true;

        [Tooltip("The number of weeks before a contract's expiry date the NPC focuses entirely on fullfilling the contract")]
        [FormerlySerializedAs("ContractPanicDays")]
        [Min(0)] public int ContractPanicWeeks = 2;

        [Header("Auctions")]
        [Tooltip("Chance to attend an auction session at all.")]
        [Range(0f, 1f)] public float AuctionAttendance = 0.7f;

        [Tooltip("Most the NPC bids relative to what a lot is worth in its condition (fair value incl. condition premium). " +
                 "1.3 = up to 30% over that value.")]
        [Min(0f)] public float AuctionValueMultiplier = 1f;

        [Tooltip("Share of free cash (after the contract reserve) the NPC may spend on one lot.")]
        [Range(0f, 1f)] public float AuctionBudgetShare = 0.5f;

        [Tooltip("Bids this much higher (or lower) per believed news item about the lot's type, country or century.")]
        [Range(0f, 1f)] public float AuctionNewsPremium = 0.15f;

        [Tooltip("Auction fever: chance, once per lot, to raise the limit when the price passes it.")]
        [Range(0f, 1f)] public float AuctionFeverChance = 0.1f;

        [Tooltip("How much fever raises the limit (0.1 = +10%), never beyond the budget.")]
        [Range(0f, 1f)] public float AuctionFeverRaise = 0.1f;

        [Tooltip("0 = answers every bid quickly; 1 = thinks up to twice as long as the price nears the limit.")]
        [Range(0f, 1f)] public float AuctionHesitation = 0.4f;
    }
}