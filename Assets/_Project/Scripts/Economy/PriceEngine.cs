using UnityEngine;

namespace AntiqueTradingSimulator.Economy
{
    /// <summary>
    /// Solely responsible for calculating an antique listing's price based on its
    /// own Quality/State modifiers plus the supply and demand of its type.
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
            return listing.BasePrice * listing.Quality * listing.State * multiplier;
        }

        /// <summary>
        /// Price for a "reference" item of this type (quality/state = 1), used for
        /// the type-level price history shown in charts — independent of any one
        /// listing's specific wear, since individual listings come and go.
        /// </summary>
        public static float CalculateReferencePrice(float basePrice, Market.AntiqueMarketState typeState)
        {
            return basePrice * CalculateSupplyDemandMultiplier(typeState);
        }

        private static float CalculateSupplyDemandMultiplier(Market.AntiqueMarketState typeState)
        {
            if (typeState == null) return 1f;

            float safeSupply = Mathf.Max(typeState.Supply, MinSupply);
            float ratio = typeState.Demand / safeSupply;
            float multiplier = Mathf.Pow(ratio, SensitivityFactor);
            return Mathf.Clamp(multiplier, MinPriceMultiplier, MaxPriceMultiplier);
        }
    }
}
