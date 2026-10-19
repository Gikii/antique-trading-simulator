using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Market;
using UnityEngine;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.Art
{
    /// <summary>
    /// Single place that decides which picture the UI shows, so every screen picks the same one.
    /// Antiques: definition artwork → type fallback → generic.
    /// Contracts: type / country / century artwork (depending on the requirement's scope) → generic contract.
    /// Returns null only when nothing at all is set up — callers keep their greybox look then.
    /// </summary>
    public static class AntiqueArt
    {
        private static AntiqueArtCatalog _catalog;
        private static bool _catalogLoaded;

        /// <summary>The catalog from Resources, or null if it hasn't been created yet.</summary>
        public static AntiqueArtCatalog Catalog
        {
            get
            {
                if (!_catalogLoaded)
                {
                    _catalog = Resources.Load<AntiqueArtCatalog>(AntiqueArtCatalog.ResourcePath);
                    _catalogLoaded = true;
                }
                return _catalog;
            }
        }

        // Keeps the cache correct when "Enter Play Mode" runs without a domain reload.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetCache()
        {
            _catalog = null;
            _catalogLoaded = false;
        }

        public static Sprite For(Antique antique) =>
            For(antique != null ? antique.Definition : null);

        public static Sprite For(AntiqueDefinition definition)
        {
            if (definition != null && definition.Artwork != null)
                return definition.Artwork;

            var catalog = Catalog;
            if (catalog == null)
                return null;

            if (definition != null)
            {
                var typeArt = catalog.GetTypeArtwork(definition.Type);
                if (typeArt != null)
                    return typeArt;
            }

            return catalog.GenericAntique;
        }

        public static Sprite ForContract(ContractRequirement requirement)
        {
            var catalog = Catalog;
            if (requirement == null)
                return catalog != null ? catalog.GenericContract : null;

            Sprite sprite = null;
            switch (requirement.Scope)
            {
                case ContractAttributeScope.AntiqueType:
                    sprite = ForType(requirement.AntiqueType);
                    break;
                case ContractAttributeScope.Country:
                    sprite = catalog != null ? catalog.GetCountryArtwork(requirement.Country) : null;
                    break;
                case ContractAttributeScope.Century:
                    sprite = catalog != null ? catalog.GetCenturyArtwork(requirement.Century) : null;
                    break;
            }

            if (sprite != null)
                return sprite;

            return catalog != null ? catalog.GenericContract : null;
        }

        /// <summary>Type fallback first (picked on purpose), otherwise the first definition of that type with artwork.</summary>
        public static Sprite ForType(AntiqueType type)
        {
            var catalog = Catalog;
            var typeArt = catalog != null ? catalog.GetTypeArtwork(type) : null;
            if (typeArt != null)
                return typeArt;

            foreach (var definition in AntiqueDatabase.GetByType(type))
                if (definition != null && definition.Artwork != null)
                    return definition.Artwork;

            return null;
        }
    }
}
