using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AntiqueTradingSimulator.Art;
using AntiqueTradingSimulator.Market;
using UnityEditor;
using UnityEngine;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.EditorTools
{
    /// <summary>
    /// Wires artwork to data by file name, so adding a picture never needs inspector clicking:
    ///   ant_&lt;definitionId&gt;.png        → AntiqueDefinition.Artwork      (ant_clock_001.png)
    ///   ant_generic.png                  → catalog.GenericAntique
    ///   ant_type_&lt;type&gt;.png            → catalog.Types                   (ant_type_clock.png)
    ///   ant_bg_display.png / ant_shadow.png → catalog slot backdrop / floor shadow
    ///   contract_generic.png             → catalog.GenericContract
    ///   contract_country_&lt;country&gt;.png  → catalog.Countries               (contract_country_china.png)
    ///   contract_century_&lt;century&gt;.png  → catalog.Centuries               (contract_century_xviii.png)
    /// Files are looked up anywhere under Assets/_Project/Art/Sprites (names are case-insensitive).
    /// A missing file never clears a sprite that was assigned by hand. Safe to run any time.
    /// </summary>
    public static class AntiqueArtworkAssigner
    {
        private const string MenuPath = "Tools/Antique Trading Simulator/Art/Assign Artwork";
        private const string SpritesFolder = "Assets/_Project/Art/Sprites";
        private const string CatalogPath = "Assets/_Project/Data/Resources/" + AntiqueArtCatalog.ResourcePath + ".asset";

        [MenuItem(MenuPath)]
        public static void Run()
        {
            var sprites = FindSprites();
            var catalog = LoadOrCreateCatalog();

            int definitionsAssigned = AssignDefinitions(sprites, out var definitionsMissing);
            int catalogAssigned = AssignCatalog(catalog, sprites);

            AssetDatabase.SaveAssets();

            string missing = definitionsMissing.Count == 0
                ? "every antique has artwork"
                : $"no artwork yet for {definitionsMissing.Count}: {string.Join(", ", definitionsMissing)} (they use the type / generic fallback)";

            Debug.Log($"[AntiqueArtworkAssigner] {sprites.Count} sprite(s) found. " +
                      $"Antiques updated: {definitionsAssigned}, catalog entries updated: {catalogAssigned}. {missing}.",
                      catalog);
        }

        // ------------------------------------------------------------------

        private static Dictionary<string, Sprite> FindSprites()
        {
            var result = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
            if (!AssetDatabase.IsValidFolder(SpritesFolder))
                return result;

            foreach (string guid in AssetDatabase.FindAssets("t:Sprite", new[] { SpritesFolder }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null)
                    continue;

                string key = Path.GetFileNameWithoutExtension(path);
                if (result.ContainsKey(key))
                {
                    Debug.LogWarning($"[AntiqueArtworkAssigner] Two files named '{key}' under {SpritesFolder} — using the first one.");
                    continue;
                }
                result[key] = sprite;
            }
            return result;
        }

        private static AntiqueArtCatalog LoadOrCreateCatalog()
        {
            var catalog = AssetDatabase.LoadAssetAtPath<AntiqueArtCatalog>(CatalogPath);
            if (catalog != null)
                return catalog;

            catalog = ScriptableObject.CreateInstance<AntiqueArtCatalog>();
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            Debug.Log($"[AntiqueArtworkAssigner] Created {CatalogPath}.", catalog);
            return catalog;
        }

        private static int AssignDefinitions(Dictionary<string, Sprite> sprites, out List<string> missing)
        {
            int changed = 0;
            missing = new List<string>();

            var definitions = AssetDatabase.FindAssets("t:AntiqueDefinition")
                .Select(guid => AssetDatabase.LoadAssetAtPath<AntiqueDefinition>(AssetDatabase.GUIDToAssetPath(guid)))
                .Where(definition => definition != null && !string.IsNullOrEmpty(definition.Id))
                .OrderBy(definition => definition.Id);

            foreach (var definition in definitions)
            {
                if (sprites.TryGetValue("ant_" + definition.Id, out var sprite))
                {
                    if (definition.Artwork == sprite)
                        continue;

                    Undo.RecordObject(definition, "Assign antique artwork");
                    definition.Artwork = sprite;
                    EditorUtility.SetDirty(definition);
                    changed++;
                }
                else if (definition.Artwork == null)
                {
                    missing.Add(definition.Id);
                }
            }

            return changed;
        }

        private static int AssignCatalog(AntiqueArtCatalog catalog, Dictionary<string, Sprite> sprites)
        {
            Undo.RecordObject(catalog, "Assign artwork catalog");
            int changed = 0;

            changed += Pick(sprites, "ant_bg_display", ref catalog.SlotBackground);
            changed += Pick(sprites, "ant_shadow", ref catalog.FloorShadow);
            changed += Pick(sprites, "ant_generic", ref catalog.GenericAntique);
            changed += Pick(sprites, "contract_generic", ref catalog.GenericContract);

            foreach (AntiqueType type in Enum.GetValues(typeof(AntiqueType)))
            {
                if (!sprites.TryGetValue("ant_type_" + Key(type), out var sprite)) continue;

                var entry = catalog.Types.FirstOrDefault(e => e != null && e.Type == type);
                if (entry == null)
                {
                    entry = new AntiqueArtCatalog.TypeArtwork { Type = type };
                    catalog.Types.Add(entry);
                }
                if (entry.Sprite == sprite) continue;
                entry.Sprite = sprite;
                changed++;
            }

            foreach (Country country in Enum.GetValues(typeof(Country)))
            {
                if (!sprites.TryGetValue("contract_country_" + Key(country), out var sprite)) continue;

                var entry = catalog.Countries.FirstOrDefault(e => e != null && e.Country == country);
                if (entry == null)
                {
                    entry = new AntiqueArtCatalog.CountryArtwork { Country = country };
                    catalog.Countries.Add(entry);
                }
                if (entry.Sprite == sprite) continue;
                entry.Sprite = sprite;
                changed++;
            }

            foreach (Century century in Enum.GetValues(typeof(Century)))
            {
                if (century == Century.Unknown) continue;
                if (!sprites.TryGetValue("contract_century_" + Key(century), out var sprite)) continue;

                var entry = catalog.Centuries.FirstOrDefault(e => e != null && e.Century == century);
                if (entry == null)
                {
                    entry = new AntiqueArtCatalog.CenturyArtwork { Century = century };
                    catalog.Centuries.Add(entry);
                }
                if (entry.Sprite == sprite) continue;
                entry.Sprite = sprite;
                changed++;
            }

            if (changed > 0)
                EditorUtility.SetDirty(catalog);

            return changed;
        }

        private static int Pick(Dictionary<string, Sprite> sprites, string fileName, ref Sprite field)
        {
            if (!sprites.TryGetValue(fileName, out var sprite) || field == sprite)
                return 0;

            field = sprite;
            return 1;
        }

        /// <summary>"Clock" → "clock", "XVIII" → "xviii", "UnitedStates" → "unitedstates".</summary>
        private static string Key(Enum value) => value.ToString().ToLowerInvariant();
    }
}
