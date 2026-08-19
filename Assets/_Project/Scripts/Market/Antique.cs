using System;
using UnityEngine;

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
        public string ListingId;
        public string DefinitionId;

        // Quality modifier
        public const float MinQuality = 0.8f;
        public const float MaxQuality = 1.2f;

        // Physica condition modifier
        public const float MinState = 0.5f;
        public const float MaxState = 1f;


        // --- Dynamic data ---
        public float CurrentPrice;

        public float Quality;
        public float State;


        [NonSerialized]
        private AntiqueDefinition _definitionCache;

        public Antique(string definitionId, float quality, float state)
        {
            ListingId = Guid.NewGuid().ToString("N");
            DefinitionId = definitionId;
            Quality = Mathf.Clamp(quality, MinQuality, MaxQuality);
            State = Mathf.Clamp(state, MinState, MaxState);

            var def = Definition;
            CurrentPrice = def != null ? def.BasePrice * Quality * State : 0f;
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
            return $"{Name} [{Category}] — Price: {CurrentPrice:F2}, Supply: {Quality:F1}, Demand: {State:F1}";
        }
    }
}