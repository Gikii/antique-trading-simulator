using System;
using System.Collections.Generic;
using UnityEngine;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.Art
{
    /// <summary>
    /// Shared artwork that is not tied to one AntiqueDefinition: the backdrop and floor shadow of
    /// every image slot, fallbacks for antiques without their own picture, and contract artwork
    /// for requirements that name a country or a century instead of a concrete antique.
    /// Lives in Data/Resources so AntiqueArt can load it without a scene reference.
    /// Filled by Tools → Antique Trading Simulator → Art → Assign Artwork (by file name).
    /// </summary>
    [CreateAssetMenu(fileName = "AntiqueArtCatalog", menuName = "AntiqueTradingSimulator/Antique Art Catalog")]
    public class AntiqueArtCatalog : ScriptableObject
    {
        /// <summary>Path under any Resources folder, without extension.</summary>
        public const string ResourcePath = "AntiqueArtCatalog";

        [Header("Image slot")]
        [Tooltip("Backdrop drawn behind every antique / contract picture (ant_bg_display.png).")]
        public Sprite SlotBackground;
        [Tooltip("Plain backdrop colour, used only while SlotBackground is empty (the sprite is drawn untinted).")]
        public Color SlotBackgroundColor = new Color32(0x2A, 0x21, 0x1A, 0xFF);
        [Tooltip("Soft ellipse under the object (ant_shadow.png).")]
        public Sprite FloorShadow;
        [Range(0f, 1f)]
        public float FloorShadowAlpha = 0.6f;

        [Header("Antique fallbacks")]
        [Tooltip("Used when neither the definition nor its type has artwork (ant_generic.png).")]
        public Sprite GenericAntique;
        [Tooltip("Per-type fallback (ant_type_clock.png, …). Also used for contracts that ask for a type.")]
        public List<TypeArtwork> Types = new List<TypeArtwork>();

        [Header("Contracts")]
        [Tooltip("Used when a contract has no more specific artwork (contract_generic.png).")]
        public Sprite GenericContract;
        [Tooltip("contract_country_china.png, …")]
        public List<CountryArtwork> Countries = new List<CountryArtwork>();
        [Tooltip("contract_century_xviii.png, …")]
        public List<CenturyArtwork> Centuries = new List<CenturyArtwork>();

        [Serializable]
        public class TypeArtwork
        {
            public AntiqueType Type;
            public Sprite Sprite;
        }

        [Serializable]
        public class CountryArtwork
        {
            public Country Country;
            public Sprite Sprite;
        }

        [Serializable]
        public class CenturyArtwork
        {
            public Century Century;
            public Sprite Sprite;
        }

        public Sprite GetTypeArtwork(AntiqueType type)
        {
            foreach (var entry in Types)
                if (entry != null && entry.Type == type && entry.Sprite != null)
                    return entry.Sprite;
            return null;
        }

        public Sprite GetCountryArtwork(Country country)
        {
            foreach (var entry in Countries)
                if (entry != null && entry.Country == country && entry.Sprite != null)
                    return entry.Sprite;
            return null;
        }

        public Sprite GetCenturyArtwork(Century century)
        {
            foreach (var entry in Centuries)
                if (entry != null && entry.Century == century && entry.Sprite != null)
                    return entry.Sprite;
            return null;
        }
    }
}
