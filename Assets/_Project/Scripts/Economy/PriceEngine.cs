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
            float supplyDemandMultiplier = 1f;

            if (typeState != null)
            {
                float safeSupply = Mathf.Max(typeState.Supply, MinSupply);
                float ratio = typeState.Demand / safeSupply;

                supplyDemandMultiplier = Mathf.Pow(ratio, SensitivityFactor);
                supplyDemandMultiplier = Mathf.Clamp(supplyDemandMultiplier, MinPriceMultiplier, MaxPriceMultiplier);
            }

            // BasePrice modified by this specific item's quality and physical state,
            // then scaled by how the type as a whole is trading.
            return listing.BasePrice * listing.Quality * listing.State * supplyDemandMultiplier;
        }
    }
}
