using UnityEngine;

namespace AntiqueTradingSimulator.Market
{
    /// <summary>
    /// Tunable numbers for private collectors — buyers from outside the simulated traders who
    /// take owner-set listings (the player's, and NPCs' if they ever list). They make listing on
    /// the market a real alternative to selling instantly: a fair price sells within days, an
    /// ambitious one may take long or never. If none is assigned to EconomyManager, an in-memory
    /// default is used.
    /// </summary>
    [CreateAssetMenu(fileName = "CollectorDemandSettings", menuName = "AntiqueTradingSimulator/Market/Collector Demand Settings")]
    public class CollectorDemandSettings : ScriptableObject
    {
        public bool Enabled = true;

        [Tooltip("Daily chance that a collector buys a listing priced exactly at its market value, at normal demand.")]
        [Range(0f, 1f)] public float BaseDailyChance = 0.2f;

        [Tooltip("How fast the chance falls as the asking price rises above market value (and grows below it). " +
                 "5 → +10% over value ≈ 0.6× the chance, +20% ≈ 0.37×.")]
        [Min(0f)] public float PriceSensitivity = 5f;

        [Tooltip("Collectors never pay more than this multiple of the market value.")]
        [Min(1f)] public float MaxPriceRatio = 1.6f;

        [Tooltip("Upper limit of the daily chance, however cheap the listing.")]
        [Range(0f, 1f)] public float MaxDailyChance = 0.75f;

        [Tooltip("How much the antique type's demand (relative to its baseline) scales the chance. 0 = ignore demand.")]
        [Range(0f, 2f)] public float DemandInfluence = 1f;

        [Tooltip("Demand factor is clamped to this range so events can't make collectors vanish or flood in completely.")]
        public Vector2 DemandFactorRange = new Vector2(0.5f, 2f);
    }

    /// <summary>
    /// The collector model in one place, so the daily roll (EconomyManager) and the hint in the
    /// listing modal use the same numbers.
    /// </summary>
    public static class CollectorDemand
    {
        /// <summary>
        /// Chance per day that a collector buys this antique at <paramref name="askingPrice"/>.
        /// Depends on the price relative to the antique's market value (condition included) and
        /// on the current demand for its type. 0 if collectors are disabled or the price is too high.
        /// </summary>
        public static float DailyChance(Antique antique, float askingPrice, AntiqueMarketState typeState, CollectorDemandSettings settings)
        {
            if (antique == null || settings == null || !settings.Enabled || askingPrice <= 0f)
                return 0f;

            float value = Mathf.Max(1f, antique.CurrentPrice);
            float ratio = askingPrice / value;
            if (ratio > settings.MaxPriceRatio)
                return 0f;

            float priceFactor = Mathf.Exp(-settings.PriceSensitivity * (ratio - 1f));

            float demandFactor = 1f;
            if (typeState != null && typeState.BaselineDemand > 0f && settings.DemandInfluence > 0f)
            {
                float relative = (typeState.Demand + typeState.TempDemandMod) / typeState.BaselineDemand;
                relative = Mathf.Clamp(relative, settings.DemandFactorRange.x, settings.DemandFactorRange.y);
                demandFactor = Mathf.Pow(relative, settings.DemandInfluence);
            }

            return Mathf.Clamp(settings.BaseDailyChance * priceFactor * demandFactor, 0f, settings.MaxDailyChance);
        }

        /// <summary>Days after which the listing has sold with 50% probability (int.MaxValue if never).</summary>
        public static int MedianDaysToSell(float dailyChance)
        {
            if (dailyChance <= 0f) return int.MaxValue;
            if (dailyChance >= 1f) return 1;
            return Mathf.Max(1, Mathf.CeilToInt(Mathf.Log(0.5f) / Mathf.Log(1f - dailyChance)));
        }
    }
}
