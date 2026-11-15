using AntiqueTradingSimulator.Events;
using UnityEngine;

namespace AntiqueTradingSimulator.Rumours
{
    /// <summary>
    /// Numbers for fake rumours: timing, credibility, cost, debunk penalties and NPC planting.
    /// Loaded from Rumours/Resources/FakeRumourSettings; built-in defaults if the asset is missing.
    /// </summary>
    [CreateAssetMenu(fileName = "FakeRumourSettings", menuName = "AntiqueTradingSimulator/Fake Rumour Settings")]
    public class FakeRumourSettings : ScriptableObject
    {
        /// <summary>Path under any Resources folder, without extension.</summary>
        public const string ResourcePath = "FakeRumourSettings";

        [Header("Timing (weeks)")]
        [Tooltip("Weeks from planting until every trader receives the rumour.")]
        public Vector2Int SpreadDelayWeeks = new Vector2Int(1, 5);

        [Tooltip("Weeks from spreading until the claimed event date, when the rumour is debunked.")]
        public Vector2Int ClaimLeadWeeks = new Vector2Int(2, 6);

        [Header("Credibility")]
        [Range(0f, 1f)] public float MinCredibility = 0.15f;

        [Tooltip("Highest credibility per Information Network level (index 0 = level 1). 0 = rumours locked.")]
        public float[] CredibilityCapPerLevel = { 0f, 0.55f, 0.65f, 0.75f, 0.85f };

        [Tooltip("Rumours the player may have active at once, per Information Network level (index 0 = level 1).")]
        public int[] SlotsPerLevel = { 0, 1, 1, 2, 2 };

        [Tooltip("Cost multiplier per Information Network level (index 0 = level 1). Lower = cheaper. Raised automatically where it would break the top-credibility rule below.")]
        public float[] CostMultiplierPerLevel = { 1f, 1f, 0.96f, 0.93f, 0.92f };

        [Tooltip("Trace-risk multiplier per Information Network level (index 0 = level 1). Lower = better cover.")]
        public float[] TraceMultiplierPerLevel = { 1f, 1f, 0.8f, 0.65f, 0.5f };

        [Header("Top-credibility rule")]
        [Tooltip("How close to its cap a level's top credibility counts as: cap − this.")]
        [Range(0.001f, 0.2f)] public float TopCredibilityMargin = 0.02f;

        [Tooltip("Each level's top credibility must cost at least this much more than the level below's. Validation raises CostMultiplierPerLevel until it does.")]
        [Range(0f, 1f)] public float MinTopSpendPremium = 0.03f;

        [Header("Cost")]
        [Tooltip("K = this × average reference price of the named antiques × claim weight. Spending K gives 63% of the way to the cap.")]
        [Min(0.01f)] public float CostScale = 0.15f;

        [Tooltip("Claim weight added per attribute past the first (type, country, century).")]
        [Min(0f)] public float ExtraAttributeWeight = 0.5f;

        [Tooltip("Claim weight added per value past the first within an attribute.")]
        [Min(0f)] public float ExtraValueWeight = 0.3f;

        [Tooltip("Most values one rumour may name across all three attributes.")]
        [Min(1)] public int MaxTotalValues = 6;

        [Tooltip("Minimum spend, as a share of the named antiques' average reference price.")]
        [Min(0f)] public float MinFeeShare = 0.05f;

        [Tooltip("Highest spend offered, in multiples of K.")]
        [Min(0.1f)] public float MaxSpendInK = 3f;

        [Tooltip("Spend options between the minimum and the maximum.")]
        [Min(1)] public int SpendSteps = 12;

        [Tooltip("Spend options are rounded to this.")]
        [Min(1f)] public float SpendRounding = 10f;

        [Header("Same-scope fatigue")]
        [Tooltip("Credibility multiplier per earlier fake on the same scope within the window.")]
        [Range(0f, 1f)] public float FatigueMultiplier = 0.6f;
        [Min(0)] public int FatigueWindowWeeks = 12;

        [Header("NPC reaction")]
        [Tooltip("Expert-level NPCs spot a fake with chance this × (1 − credibility).")]
        [Range(0f, 1f)] public float ExpertDetection = 0.4f;

        [Header("Debunk")]
        [Tooltip("Temporary demand change on the rumour's scope at the debunk: −this × credibility (opposite sign for a bearish rumour).")]
        [Min(0f)] public float HangoverDemand = 0.3f;
        [Min(0)] public int HangoverWeeks = 2;

        [Tooltip("Weeks the debunk notice stays in the news list.")]
        [Min(1)] public int DebunkNoticeWeeks = 2;

        [Tooltip("Skepticism added per debunk: this × credibility.")]
        [Min(0f)] public float SkepticismPerDebunk = 0.08f;
        [Min(0f)] public float SkepticismDecayPerWeek = 0.01f;
        [Range(0f, 1f)] public float MaxSkepticism = 0.5f;

        [Header("Being traced (rolled at the debunk)")]
        [Range(0f, 1f)] public float TraceBase = 0.05f;
        [Tooltip("Added × spend / (spend + K).")]
        [Range(0f, 1f)] public float TraceSpend = 0.15f;
        [Tooltip("Added per other fake the same planter planted within the recent window.")]
        [Range(0f, 1f)] public float TracePerRecent = 0.10f;
        [Min(0)] public int RecentWindowWeeks = 10;
        [Range(0f, 1f)] public float TracedCredibilityLoss = 0.10f;
        [Min(0)] public int TracedReputationLoss = 3;

        [Header("NPC planting")]
        [Tooltip("Most NPC-planted rumours active at once.")]
        [Min(0)] public int MaxActiveNpcRumours = 1;
        [Min(0)] public int NpcCooldownWeeks = 8;
        [Tooltip("Matching holdings an NPC needs before a pump is worth it.")]
        [Min(1)] public int NpcMinHoldings = 2;
        [Tooltip("Most of its cash an NPC spends on one rumour.")]
        [Range(0f, 1f)] public float NpcMaxCashShare = 0.25f;
        [Range(0f, 1f)] public float NpcCredibilityCap = 0.75f;
        [Tooltip("Expected price rise per NPC that acts, as a share of the planter's matching holdings value.")]
        [Min(0f)] public float NpcPriceImpactPerReactor = 0.15f;
        [Tooltip("Plants only if (expected gain − cost) ≥ cost × this.")]
        [Min(0f)] public float NpcMinReturnOnCost = 0.5f;
        [Tooltip("Spends tried, in multiples of K.")]
        public float[] NpcSpendOptionsInK = { 0.5f, 1f, 2f, 3f };
        [Min(1)] public int NpcSpreadDelayWeeks = 1;
        [Min(1)] public int NpcClaimLeadWeeks = 3;

        // ---------------------------------------------------------------- lookup

        public float CapFor(InfoAccessLevel level) => AtLevel(CredibilityCapPerLevel, level, 0f);

        public int SlotsFor(InfoAccessLevel level) => AtLevel(SlotsPerLevel, level, 0);

        public float CostMultiplierFor(InfoAccessLevel level) => Mathf.Max(0.01f, AtLevel(CostMultiplierPerLevel, level, 1f));

        public float TraceMultiplierFor(InfoAccessLevel level) => Mathf.Clamp01(AtLevel(TraceMultiplierPerLevel, level, 1f));

        /// <summary>
        /// How much the claim widens the cost. One named value is 1; each further attribute adds
        /// ExtraAttributeWeight and each further value within an attribute adds ExtraValueWeight, so
        /// naming another attribute or another value both raise it.
        ///
        /// With the defaults: one value 1.00 · three types 1.60 · type + country 1.50 ·
        /// three attributes with two values each 2.90.
        /// </summary>
        public float ClaimWeight(RumourClaim claim)
        {
            if (claim == null || claim.IsEmpty) return 0f;

            return 1f +
                   (claim.AttributeCount - 1) * ExtraAttributeWeight +
                   (claim.TotalValues - claim.AttributeCount) * ExtraValueWeight;
        }

        /// <summary>(min + (cap − min) × (1 − e^(−spend/K))) × fatigue.</summary>
        public float CredibilityFor(float spend, float costScale, float cap, float fatigue)
        {
            if (cap <= 0f || costScale <= 0f) return 0f;

            float min = Mathf.Min(MinCredibility, cap);
            float curve = 1f - Mathf.Exp(-Mathf.Max(0f, spend) / costScale);
            return Mathf.Clamp01((min + (cap - min) * curve) * Mathf.Clamp01(fatigue));
        }

        /// <summary>Chance of the planter being traced at the debunk, after the level's cover.</summary>
        public float TraceChanceFor(float spend, float costScale, int recentFakes, float traceMultiplier = 1f)
        {
            float spendShare = spend > 0f ? spend / (spend + Mathf.Max(0.01f, costScale)) : 0f;
            float chance = TraceBase + TraceSpend * spendShare + TracePerRecent * Mathf.Max(0, recentFakes);
            return Mathf.Clamp01(chance * Mathf.Clamp01(traceMultiplier));
        }

        /// <summary>Spend needed to reach <paramref name="credibility"/>, in multiples of K. Infinity at or above the cap.</summary>
        public float SpendInKFor(float credibility, float cap)
        {
            float min = Mathf.Min(MinCredibility, cap);
            if (cap <= min || credibility <= min) return 0f;
            if (credibility >= cap) return float.PositiveInfinity;

            return -Mathf.Log(1f - (credibility - min) / (cap - min));
        }

        /// <summary>The level's top credibility: its cap less TopCredibilityMargin.</summary>
        public float TopCredibilityFor(InfoAccessLevel level)
        {
            float cap = CapFor(level);
            return cap > 0f ? Mathf.Max(MinCredibility, cap - TopCredibilityMargin) : 0f;
        }

        /// <summary>What the level's top credibility costs, in multiples of K (before the claim's own scale).</summary>
        public float TopSpendInKFor(InfoAccessLevel level)
        {
            float cap = CapFor(level);
            if (cap <= 0f) return 0f;

            return SpendInKFor(TopCredibilityFor(level), cap) * CostMultiplierFor(level);
        }

        /// <summary>
        /// Keeps the rule the discounts have to obey: a level's top credibility must cost more than the
        /// level below's, by at least MinTopSpendPremium. A level that breaks it has its cost multiplier
        /// raised to the lowest value that keeps it. Called when the settings become active.
        ///
        /// The caps bound how deep the discount can go: with the defaults, pricing the top credibility
        /// at parity allows about 16% off by level 5, and the 3% premium leaves about 8%.
        /// </summary>
        public void ValidateLevelPricing()
        {
            if (CostMultiplierPerLevel == null || CostMultiplierPerLevel.Length == 0) return;

            float previousTop = 0f;

            for (int i = 0; i < CostMultiplierPerLevel.Length; i++)
            {
                var level = (InfoAccessLevel)(i + 1);
                float cap = CapFor(level);
                if (cap <= 0f) continue;

                float spendInK = SpendInKFor(TopCredibilityFor(level), cap);
                if (spendInK <= 0f || float.IsInfinity(spendInK)) continue;

                float required = previousTop * (1f + MinTopSpendPremium);
                float top = spendInK * CostMultiplierFor(level);

                if (previousTop > 0f && top < required)
                {
                    float raised = required / spendInK;
                    Debug.LogWarning($"FakeRumourSettings: cost multiplier for {level} raised from " +
                                     $"{CostMultiplierPerLevel[i]:0.###} to {raised:0.###} so its top credibility " +
                                     $"({TopCredibilityFor(level):P0}) still costs more than the level below's.");

                    CostMultiplierPerLevel[i] = raised;
                    top = required;
                }

                previousTop = top;
            }
        }

        private static T AtLevel<T>(T[] values, InfoAccessLevel level, T fallback)
        {
            if (values == null || values.Length == 0) return fallback;
            int index = Mathf.Clamp((int)level - 1, 0, values.Length - 1);
            return values[index];
        }

        // ---------------------------------------------------------------- loading

        private static FakeRumourSettings _cached;

        /// <summary>The asset from Resources, or the defaults above if none exists.</summary>
        public static FakeRumourSettings Active
        {
            get
            {
                if (_cached == null)
                    _cached = Resources.Load<FakeRumourSettings>(ResourcePath);

                if (_cached == null)
                    _cached = CreateInstance<FakeRumourSettings>();

                return _cached;
            }
        }

        /// <summary>Makes the instance assigned to RumourMill the one everything reads.</summary>
        public static void SetActive(FakeRumourSettings settings)
        {
            if (settings != null) _cached = settings;

            Active.ValidateLevelPricing();
        }
    }
}
