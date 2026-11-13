using System;

namespace AntiqueTradingSimulator.Market
{
    /// <summary>
    /// The quality bands an antique's condition falls into, and the only condition levels a
    /// contract asks for. Clients think in quality, not in arbitrary percentages: a contract
    /// wants a Good piece, which means 70% or better.
    /// </summary>
    // Explicit int values equal to the tier's minimum condition percent, so a tier can be cast
    // straight to int for the threshold and compared numerically (same idea as Century).
    // Unset = 0 exists so a scene or asset saved before these fields existed deserializes to
    // something recognisable instead of silently reading as Poor — see ContractManager.
    public enum ConditionTier
    {
        Unset = 0,
        Poor = 30,
        Fair = 50,
        Good = 70,
        Excellent = 90
    }

    public static class ConditionTiers
    {
        /// <summary>The real tiers, worst first. Unset is not one of them.</summary>
        public static readonly ConditionTier[] All =
        {
            ConditionTier.Poor,
            ConditionTier.Fair,
            ConditionTier.Good,
            ConditionTier.Excellent
        };

        public static ConditionTier Lowest => All[0];
        public static ConditionTier Highest => All[All.Length - 1];

        /// <summary>True for a tier that was actually configured, i.e. anything but Unset.</summary>
        public static bool IsSet(this ConditionTier tier) => Array.IndexOf(All, tier) >= 0;

        /// <summary>The condition a piece needs to reach this tier, as a 0..1 fraction. Unset gives 0.</summary>
        public static float Threshold(this ConditionTier tier) => (int)tier / 100f;

        /// <summary>The condition a piece needs to reach this tier, in whole percent.</summary>
        public static int ThresholdPercent(this ConditionTier tier) => (int)tier;

        public static string ToDisplayString(this ConditionTier tier) => tier switch
        {
            ConditionTier.Poor => "Poor",
            ConditionTier.Fair => "Fair",
            ConditionTier.Good => "Good",
            ConditionTier.Excellent => "Excellent",
            _ => "Unset"
        };

        /// <summary>
        /// Where this tier stops, i.e. the next tier's threshold — and just above 1 for the top
        /// tier, so a perfect piece counts as Excellent rather than falling off the end.
        /// </summary>
        public static float UpperBound(this ConditionTier tier)
        {
            int i = Array.IndexOf(All, tier);
            if (i < 0) return Lowest.Threshold();
            return i == All.Length - 1 ? 1.01f : All[i + 1].Threshold();
        }

        /// <summary>
        /// The tier a condition falls in. Conditions below the lowest tier's threshold can't occur
        /// (Antique.MinCondition is 30%), so anything under it reads as the lowest tier.
        /// </summary>
        public static ConditionTier Of(float condition)
        {
            for (int i = All.Length - 1; i >= 0; i--)
                if (condition + 0.0005f >= All[i].Threshold())
                    return All[i];

            return Lowest;
        }

        /// <summary>
        /// A tier picked uniformly between two tiers, inclusive. Order doesn't matter, and an
        /// Unset bound is treated as the nearest real tier so a half-configured class still rolls.
        /// </summary>
        public static ConditionTier Random(ConditionTier a, ConditionTier b)
        {
            int i = IndexOrDefault(a, 0);
            int j = IndexOrDefault(b, All.Length - 1);
            if (i > j) (i, j) = (j, i);

            return All[UnityEngine.Random.Range(i, j + 1)];
        }

        private static int IndexOrDefault(ConditionTier tier, int fallback)
        {
            int i = Array.IndexOf(All, tier);
            return i >= 0 ? i : fallback;
        }
    }
}
