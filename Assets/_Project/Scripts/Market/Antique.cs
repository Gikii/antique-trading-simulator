using System;

namespace AntiqueTradingSimulator.Market
{
    /// <summary>
    /// Represents a single antique on the market — static data (never changes)
    /// and dynamic data (updated by the economy engine).
    /// </summary>
    [Serializable]
    public class Antique
    {
        // --- Static data ---
        public string Id;
        public string Name;
        public string Category;
        public float BasePrice;

        // --- Dynamic data ---
        public float CurrentPrice;
        public float Supply;
        public float Demand;

        public Antique(string id, string name, string category, float basePrice, float initialSupply, float initialDemand)
        {
            Id = id;
            Name = name;
            Category = category;
            BasePrice = basePrice;
            CurrentPrice = basePrice;
            Supply = initialSupply;
            Demand = initialDemand;
        }

        public override string ToString()
        {
            return $"{Name} [{Category}] — Price: {CurrentPrice:F2}, Supply: {Supply:F1}, Demand: {Demand:F1}";
        }
    }
}