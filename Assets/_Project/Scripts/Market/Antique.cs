using System;
using UnityEngine;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.Market
{
    /// <summary>
    /// Runtime instance of an antique on the market. Holds only dynamic data
    /// (price, condition) plus a reference to its static definition by Id —
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

        // Purely descriptive physical condition (drives the "Good"/"Poor" label
        // shown in UI). Still factors into price, but is no longer the only
        // source of price randomness — see PriceFactor below.
         public const float MinCondition = 0.3f;
        public const float MaxCondition = 1f;
 
        // Independent price variance unrelated to physical condition — think
        // negotiation quirks, seller mood, market noise for this one listing.
        // Two items with identical Condition can still sell for different prices.
        public const float MinPriceFactor = 0.7f;
        public const float MaxPriceFactor = 1.25f;

        public const string PlayerOwnerId = "Player";

        // Which numbered copy this is within its edition (e.g. 1 for "1/5").
        // Meaningful only when Definition.EditionSize > 0 — left at 0 otherwise.
        public int EditionNumber = 0;

        // Empty = unowned / anonymous market seller. Set to PlayerOwnerId on
        // purchase; will hold an NPC Id once NpcProfileDatabase exists.
        public string OwnerId = "";


        // --- Dynamic data ---
        public float CurrentPrice;
        public float Condition;
        public float PriceFactor;


        // Free-form provenance text for this specific instance (e.g. "Dawna kolekcja
        // rodziny Li z prowincji Zhejiang"). Purely descriptive for now — not wired
        // into pricing until there's a concrete mechanic for it.
        public string History = "";

        public string ReservedForContractId = null;
        public bool IsReservedForContract => ReservedForContractId != null;


        [NonSerialized]
        private AntiqueDefinition _definitionCache;

        public Antique(string definitionId, float condition, float priceFactor, string history = "", int editionNumber = 0, string ownerId = "")
        {
            ListingId = Guid.NewGuid().ToString("N");
            DefinitionId = definitionId;
            Condition = Mathf.Clamp(condition, MinCondition, MaxCondition);
            PriceFactor = Mathf.Clamp(priceFactor, MinPriceFactor, MaxPriceFactor);
            History = history;
            EditionNumber = editionNumber;
            OwnerId = ownerId;

            var def = Definition;
            CurrentPrice = def != null ? def.BasePrice * Condition * PriceFactor : 0f;

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

        public string Id => ListingId;
        public string Name => Definition != null ? Definition.DisplayName : "Unknown";
        public string Category => Definition != null ? Definition.Type.ToDisplayString() : "Unknown";
        public AntiqueType Type => Definition != null ? Definition.Type : AntiqueType.Other;
        public Century Century => Definition != null ? Definition.Century : Century.Unknown;
        public Country Country => Definition != null ? Definition.Country : Country.Other;
        public string Description => Definition != null ? Definition.Description : "";
        public int EditionSize => Definition != null ? Definition.EditionSize : 0;
        public bool IsLimitedEdition => EditionSize > 0;
        public string RarityLabel => IsLimitedEdition ? $"{EditionNumber}/{EditionSize}" : "";



        public float BasePrice => Definition != null ? Definition.BasePrice : 0f;

        public override string ToString()
        {
            string rarity = IsLimitedEdition ? $", Rarity: {RarityLabel}" : "";
            return $"{Name} [{Category}] — Price: {CurrentPrice:F2}, Condition: {Condition:F2}, PriceFactor: {PriceFactor:F2}{rarity}";
        }
    }
}