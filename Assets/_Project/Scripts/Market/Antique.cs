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

        // Day this specific item was placed on the REGULAR MARKET (Market._listings).
        // -1 = never listed on the regular market yet (e.g. exists only as part of an
        // auction pool). Set exclusively by Market.AddListing — not by the constructor,
        // since an Antique can exist without being a market listing.
        public int MarketListedOnDay = -1;

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

        // Unique identity of THIS specific physical item — used by Market.GetById/Buy
        // and TraderInventory's holdings dictionary. Never the same as another
        // Antique instance, even if they share a DefinitionId.
        public string Id => ListingId;
        public string Name => Definition != null ? Definition.DisplayName : "Unknown";
        public string Category => Definition != null ? Definition.Category : "Unknown";
        public float BasePrice => Definition != null ? Definition.BasePrice : 0f;

        public override string ToString()
        {
            return $"{Name} [{Category}] — Price: {CurrentPrice:F2}, Quality: {Quality:F1}, State: {State:F1}";
        }
    }
}