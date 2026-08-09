using System;

namespace AntiqueTradingSimulator.Market
{
    /// <summary>
    /// Runtime instance of an antique on the market. Holds only dynamic data
    /// (price, supply, demand) plus a reference to its static definition by Id —
    /// never a direct object reference, so this class stays cleanly serializable
    /// for future save/load.
    /// </summary>
    [Serializable]
    public class Antique
    {
        public string DefinitionId;

        // --- Dynamic data ---
        public float CurrentPrice;
        public float Supply;
        public float Demand;

        [NonSerialized]
        private AntiqueDefinition _definitionCache;

        public Antique(string definitionId, float initialSupply, float initialDemand)
        {
            DefinitionId = definitionId;
            Supply = initialSupply;
            Demand = initialDemand;

            var def = Definition;
            CurrentPrice = def != null ? def.BasePrice : 0f;
        }

        public AntiqueDefinition Definition
        {
            get
            {
                if (_definitionCache == null)
                    _definitionCache = AntiqueDatabase.GetById(DefinitionId);
                return _definitionCache;
            }
        }

        public string Id => DefinitionId;
        public string Name => Definition != null ? Definition.DisplayName : "Unknown";
        public string Category => Definition != null ? Definition.Category : "Unknown";
        public float BasePrice => Definition != null ? Definition.BasePrice : 0f;

        public override string ToString()
        {
            return $"{Name} [{Category}] — Price: {CurrentPrice:F2}, Supply: {Supply:F1}, Demand: {Demand:F1}";
        }
    }
}