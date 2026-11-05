using System.Collections.Generic;
using System.IO;
using AntiqueTradingSimulator.Dependency;
using UnityEditor;
using UnityEngine;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.EditorTools
{
    /// <summary>
    /// Creates a starter DependencyGraph asset: a starting point for tuning.
    /// </summary>
    public static class ExampleDependencyGraphBuilder
    {
        private const string TargetFolder = "Assets/_Project/Data/Resources";
        private const string TargetPath = TargetFolder + "/DependencyGraph.asset";

        [MenuItem("Tools/Antique Trading Simulator/Economy/Create Example Dependency Graph")]
        public static void Create()
        {
            if (File.Exists(TargetPath))
            {
                var existing = AssetDatabase.LoadAssetAtPath<DependencyGraphSettings>(TargetPath);
                EditorGUIUtility.PingObject(existing);
                Selection.activeObject = existing;
                Debug.LogWarning($"A dependency graph already exists at {TargetPath} — selected it instead of overwriting. Delete it first if you want the example back.");
                return;
            }

            Directory.CreateDirectory(TargetFolder);

            var settings = ScriptableObject.CreateInstance<DependencyGraphSettings>();
            settings.Rules = BuildRules();
            settings.AutoRules = BuildAutoRules();

            AssetDatabase.CreateAsset(settings, TargetPath);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorGUIUtility.PingObject(settings);
            Selection.activeObject = settings;

            Debug.Log($"Created {TargetPath} with {settings.Rules.Count} hand-written rule(s) and {settings.AutoRules.Count} generated family/ies. " +
                      "Assign it to DependencyManager (or leave the field empty — it is loaded from Resources by name).");
        }

        private static List<DependencyRule> BuildRules()
        {
            // A hand-written rule no longer carries a kind: the sign of its strength already says
            // whether the two categories rise together or compete. The two helpers at the bottom
            // differ only in how easily they fire and how fast they fade.
            return new List<DependencyRule>
            {
                // --- around paintings -------------------------------------------------------
                // Frames and mirrors hang on the same wall — the closest thing the current enum
                // has to "frames". Mirrors follow paintings much more than the other way round.
                Complementary(AntiqueType.Painting, AntiqueType.Mirror, 0.65f,
                              Symmetry.Mirrored, reverseFactor: 0.6f),

                // Fine-art buyers move between the two.
                Complementary(AntiqueType.Painting, AntiqueType.Sculpture, 0.30f),

                // Catalogues, letters and provenance papers follow the artist.
                Complementary(AntiqueType.Painting, AntiqueType.Book, 0.15f),

                // A painting eats a large share of a collector's budget.
                Competition(AntiqueType.Painting, AntiqueType.Furniture, -0.20f),

                // --- around furniture -------------------------------------------------------
                // Furnishing a whole interior.
                Complementary(AntiqueType.Furniture, AntiqueType.Lamp, 0.60f),
                Complementary(AntiqueType.Furniture, AntiqueType.Rug, 0.55f),
                Complementary(AntiqueType.Furniture, AntiqueType.Mirror, 0.45f),
                Competition(AntiqueType.Furniture, AntiqueType.Porcelain, -0.15f),

                // --- around precious metal --------------------------------------------------
                // Bullion-minded buyers.
                Complementary(AntiqueType.Coin, AntiqueType.Silverware, 0.35f),
                Competition(AntiqueType.Coin, AntiqueType.Jewelry, -0.10f),
                Complementary(AntiqueType.Jewelry, AntiqueType.Silverware, 0.40f),

                // --- the obvious pairs ------------------------------------------------------
                // Nearly the same collector.
                Complementary(AntiqueType.Ceramic, AntiqueType.Porcelain, 0.70f),

                // Both are bought for the mechanism.
                Complementary(AntiqueType.Clock, AntiqueType.Instrument, 0.25f),

                // --- a cross-dimension link, to show it works -------------------------------
                // Interest in rugs pulls Ottoman pieces along with it; the reverse is weaker.
                new DependencyRule
                {
                    From = MarketFacet.Of(AntiqueType.Rug),
                    To = MarketFacet.Of(Country.Turkey),
                    Strength = 0.10f,
                    Symmetry = Symmetry.Mirrored,
                    ReverseFactor = 0.4f,
                    ActivationThreshold = 1f,
                    DecayPerDay = 0.2f
                }
            };
        }

        private static List<AutoDependencyRule> BuildAutoRules()
        {
            // These three reach every antique in a category at full strength, so they are kept
            // an order of magnitude below the hand-written links between two types.
            return new List<AutoDependencyRule>
            {
                // "A new Art Nouveau exhibition lifts everything from that period."
                new AutoDependencyRule
                {
                    Dimension = FacetKind.Century,
                    SameValueOnly = true,
                    Kind = DependencyKind.SharedEra,
                    Strength = 0.06f,
                    ActivationThreshold = 1f,
                    DecayPerDay = 0.2f
                },

                new AutoDependencyRule
                {
                    Dimension = FacetKind.Country,
                    SameValueOnly = true,
                    Kind = DependencyKind.SharedOrigin,
                    Strength = 0.03f,
                    ActivationThreshold = 1.5f,
                    DecayPerDay = 0.25f
                },

                // Background competition for the same wallets. Weak and slow to trigger:
                // the real budget limit belongs in NPCTrader, this is only the market's mood.
                new AutoDependencyRule
                {
                    Dimension = FacetKind.Type,
                    SameValueOnly = false,
                    Kind = DependencyKind.CapitalCompetition,
                    Strength = -0.03f,
                    ActivationThreshold = 2f,
                    DecayPerDay = 0.3f
                }
            };
        }

        /// <summary>Both categories rise together: fires on a modest signal, fades slowly.</summary>
        private static DependencyRule Complementary(AntiqueType from, AntiqueType to, float strength,
                                                    Symmetry symmetry = Symmetry.Mutual,
                                                    float reverseFactor = 1f) =>
            new DependencyRule
            {
                From = MarketFacet.Of(from),
                To = MarketFacet.Of(to),
                Strength = strength,
                Symmetry = symmetry,
                ReverseFactor = reverseFactor,
                ActivationThreshold = 0.5f,
                DecayPerDay = 0.2f
            };

        /// <summary>
        /// One category grows at the other's expense; strength must be negative. Harder to trigger
        /// and quicker to fade, because only a real rush strains a budget.
        /// </summary>
        private static DependencyRule Competition(AntiqueType from, AntiqueType to, float strength) =>
            new DependencyRule
            {
                From = MarketFacet.Of(from),
                To = MarketFacet.Of(to),
                Strength = strength,
                Symmetry = Symmetry.Mutual,
                ActivationThreshold = 1f,
                DecayPerDay = 0.25f
            };
    }
}
