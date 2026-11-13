using UnityEngine;

namespace AntiqueTradingSimulator.Economy
{
    /// <summary>
    /// Solely responsible for calculating an antique listing's price based on its
    /// own Condition modifier plus the supply and demand of its type.
    /// No other module should set the price directly.
    /// </summary>
    public static class PriceEngine
    {
        // How strongly the supply/demand ratio affects price — to be tuned experimentally
        private const float SensitivityFactor = 1f;

        // Safeguards against division by zero / extreme price swings
        private const float MinSupply = 0.1f;
        private const float MaxPriceMultiplier = 5f;
        private const float MinPriceMultiplier = 0.1f;

        public static float CalculatePrice(Market.Antique listing, Market.AntiqueMarketState typeState)
        {
            float multiplier = CalculateSupplyDemandMultiplier(typeState);
            return listing.BasePrice * listing.Condition * listing.PriceFactor * multiplier;
        }

        /// <summary>
        /// Price for a "reference" item of this type (condition = 1), used for
        /// the type-level price history shown in charts — independent of any one
        /// listing's specific wear, since individual listings come and go.
        /// </summary>
        public static float CalculateReferencePrice(float basePrice, Market.AntiqueMarketState typeState)
        {
            return basePrice * CalculateSupplyDemandMultiplier(typeState);
        }

        /// <summary>
        /// How the type's price would change if its demand were <paramref name="effectiveDemand"/> instead
        /// of what it is now (supply unchanged): 1.1 = 10% higher. Used to price in a coming season.
        /// </summary>
        public static float PriceRatioForDemand(Market.AntiqueMarketState typeState, float effectiveDemand)
        {
            if (typeState == null) return 1f;

            float now = MultiplierFor(typeState, typeState.EffectiveDemand);
            return now > 0f ? MultiplierFor(typeState, effectiveDemand) / now : 1f;
        }

        private static float CalculateSupplyDemandMultiplier(Market.AntiqueMarketState typeState) =>
            typeState == null ? 1f : MultiplierFor(typeState, typeState.EffectiveDemand);

        private static float MultiplierFor(Market.AntiqueMarketState typeState, float effectiveDemand)
        {
            float safeSupply = Mathf.Max(typeState.Supply + typeState.TempSupplyMod, MinSupply);
            float ratio = Mathf.Max(0f, effectiveDemand) / safeSupply;
            float multiplier = Mathf.Pow(ratio, SensitivityFactor);
            return Mathf.Clamp(multiplier, MinPriceMultiplier, MaxPriceMultiplier);
        }
    }
}
