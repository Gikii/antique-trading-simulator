using UnityEngine;

namespace AntiqueTradingSimulator.Economy
{
    /// <summary>
    /// Solely responsible for calculating an antique's price based on supply and demand.
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

        public static float CalculatePrice(Market.Antique antique)
        {
            float safeSupply = Mathf.Max(antique.Supply, MinSupply);
            float ratio = antique.Demand / safeSupply;

            float multiplier = Mathf.Pow(ratio, SensitivityFactor);
            multiplier = Mathf.Clamp(multiplier, MinPriceMultiplier, MaxPriceMultiplier);

            return antique.BasePrice * multiplier;
        }
    }
}