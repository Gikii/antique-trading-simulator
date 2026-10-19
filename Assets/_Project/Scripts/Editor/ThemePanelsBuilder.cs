using System.Collections.Generic;
using AntiqueTradingSimulator.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.EditorTools
{
    /// <summary>
    /// Replaces the greybox backgrounds of the big containers with the dark theme from the mockup.
    /// Only panel backgrounds are touched (HUD, navigation column, view panels, side panels,
    /// sections, modal windows, pause menu, InfoBar) — cards, buttons, tabs and texts are a later step.
    /// Every target is listed explicitly with its role, so nothing else changes by accident;
    /// a missing path is reported and skipped. Safe to run more than once.
    /// Also creates Data/Resources/UITheme.asset and sets the 9-slice borders of the frame sprites.
    /// </summary>
    public static class ThemePanelsBuilder
    {
        private const string MenuPath = "Tools/Antique Trading Simulator/UI/Apply Theme Panels";

        private const string ThemePath = "Assets/_Project/Data/Resources/" + UITheme.ResourcePath + ".asset";
        private const string FramesFolder = "Assets/_Project/Art/UI/Frames/";
        private const string InfoBarPrefabPath = "Assets/_Project/Prefabs/UI/InfoBar/InfoBar.prefab";

        private const string SceneName = "Main";
        private const string CanvasName = "Canvas";

        private enum Role
        {
            /// <summary>Background removed (fully transparent) — lets the screen background show through.</summary>
            Clear,
            /// <summary>Framed dark container (ui_panel).</summary>
            Panel,
            /// <summary>Section inside a panel (ui_row).</summary>
            Inset,
            /// <summary>Flat strip, no frame.</summary>
            Base,
        }

        // Paths under Canvas in Main.unity.
        private static readonly (string path, Role role)[] SceneTargets =
        {
            ("Views", Role.Clear),
            ("HUD", Role.Panel),
            ("MainNavigation", Role.Panel),

            ("Views/MarketView/MainArea/ListPanel/ListAreaWrapper/ListArea", Role.Panel),
            ("Views/MarketView/MainArea/SidePanel/AntiqueDetails", Role.Panel),
            ("Views/MarketView/MainArea/SidePanel/AntiqueDetails/DetailsSection", Role.Inset),
            ("Views/MarketView/MainArea/SidePanel/AntiqueDetails/HistorySection", Role.Inset),
            ("Views/MarketView/MainArea/SidePanel/AntiqueDetails/SellerSection", Role.Inset),
            ("Views/MarketView/MainArea/SidePanel/TransactionHistory", Role.Panel),

            ("Views/InventoryView/MainArea/InventoryListPanel", Role.Panel),
            ("Views/InventoryView/MainArea/RIghtContent/CollectionSummary", Role.Panel),
            ("Views/InventoryView/MainArea/RIghtContent/InventoryDetails", Role.Panel),
            ("Views/InventoryView/MainArea/RIghtContent/InventoryDetails/SideActionsPanel", Role.Inset),

            ("Views/ContractsView/ListPanel/ListAreaWrapper/ListArea", Role.Panel),
            ("Views/ContractsView/DetailsPanel", Role.Panel),

            ("Views/NewsEventsView/Header", Role.Panel),
            ("Views/NewsEventsView/Header/InfoAccessPanel", Role.Inset),
            ("Views/NewsEventsView/Content/MainPanel", Role.Panel),
            ("Views/NewsEventsView/Content/MainPanel/TabsRow", Role.Inset),
            ("Views/NewsEventsView/Content/SidePanel", Role.Panel),
            ("Views/NewsEventsView/Content/SidePanel/EventDetails", Role.Clear),
            ("Views/NewsEventsView/Content/SidePanel/NewsDetails", Role.Clear),

            ("Views/CompanyView/Pages/OverviewPage/TopRow/CompanyCard", Role.Panel),
            ("Views/CompanyView/Pages/OverviewPage/TopRow/NextEventCard", Role.Panel),
            ("Views/CompanyView/Pages/OverviewPage/BottomRow/Competition", Role.Panel),
            ("Views/CompanyView/Pages/DevelopmentPage/SummaryRow/SummaryPanel", Role.Panel),
            ("Views/CompanyView/Pages/DevelopmentPage/SummaryRow/HintPanel", Role.Panel),
            ("Views/CompanyView/Pages/DevelopmentPage/Body/UpgradeDetails", Role.Panel),
            ("Views/CompanyView/Pages/ReputationPage/ValueRow/ReputationCard", Role.Panel),
            ("Views/CompanyView/Pages/ReputationPage/ValueRow/CredibilityCard", Role.Panel),
            ("Views/CompanyView/Pages/ReputationPage/LevelRow/ReputationLevel", Role.Panel),
            ("Views/CompanyView/Pages/ReputationPage/LevelRow/CredibilityLevel", Role.Panel),
            ("Views/CompanyView/Pages/ReputationPage/ChangesRow/ReputationChanges", Role.Panel),
            ("Views/CompanyView/Pages/ReputationPage/ChangesRow/CredibilityChanges", Role.Panel),

            ("Modals/BuyModal/Window", Role.Panel),
            ("Modals/SellNowModal/Window", Role.Panel),
            ("Modals/ListOnMarketModal/Window", Role.Panel),
            ("Modals/RestorationModal/Window", Role.Panel),
            ("Modals/ContractFulfillModal/Window", Role.Panel),
            ("Modals/ContractFulfillModal/Window/AntiqueScrollView", Role.Inset),
            ("Modals/CreateEventModal/Window", Role.Panel),

            ("GameMenu/Backdrop/Panel", Role.Panel),
        };

        // Paths inside InfoBar.prefab (relative to its root; "" = the root itself).
        private static readonly (string path, Role role)[] InfoBarTargets =
        {
            ("", Role.Base),
            ("TransportTab", Role.Panel),
            ("WarehouseTab", Role.Panel),
            ("NewsTab", Role.Panel),
            ("EventsTab", Role.Panel),
        };

        private const string WarehouseFadePath = "WarehouseTab/IllustrationArea/IllustrationFade";

        /// <summary>9-slice borders (left, bottom, right, top) in sprite pixels.</summary>
        private static readonly Dictionary<string, Vector4> FrameBorders = new Dictionary<string, Vector4>
        {
            { "ui_panel", new Vector4(8, 8, 8, 8) },
            { "ui_row", new Vector4(8, 8, 8, 8) },
            { "ui_row_selected", new Vector4(14, 14, 14, 14) },
            { "ui_button", new Vector4(8, 8, 8, 8) },
            { "ui_button_primary", new Vector4(8, 8, 8, 8) },
            { "ui_button_danger", new Vector4(8, 8, 8, 8) },
            { "ui_nav_item", new Vector4(8, 8, 8, 8) },
            { "ui_nav_active", new Vector4(8, 8, 8, 8) },
            { "ui_tab", new Vector4(6, 6, 6, 6) },
            { "ui_tab_active", new Vector4(6, 6, 6, 6) },
            { "ui_input", new Vector4(6, 6, 6, 6) },
            { "ui_badge_green", new Vector4(5, 5, 5, 5) },
            { "ui_divider", new Vector4(8, 0, 8, 0) },
        };

        [MenuItem(MenuPath)]
        public static void Run()
        {
            EnsureFrameBorders();
            var theme = LoadOrCreateTheme();
            if (theme.Panel == null || theme.Inset == null)
            {
                Debug.LogError($"[ThemePanelsBuilder] Frame sprites missing in {FramesFolder} — nothing applied.", theme);
                return;
            }

            int changed = ApplyToScene(theme) + ApplyToInfoBar(theme);
            Debug.Log($"[ThemePanelsBuilder] Restyled {changed} panel background(s). Save the scene (Ctrl+S).", theme);
        }

        // ------------------------------------------------------------------

        private static int ApplyToScene(UITheme theme)
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name != SceneName)
            {
                Debug.LogWarning($"[ThemePanelsBuilder] Open the {SceneName} scene to restyle its panels (InfoBar prefab is still updated).");
                return 0;
            }

            Transform canvas = null;
            foreach (var go in scene.GetRootGameObjects())
            {
                if (go.name == CanvasName)
                {
                    canvas = go.transform;
                    break;
                }
            }

            if (canvas == null)
            {
                Debug.LogError($"[ThemePanelsBuilder] No root '{CanvasName}' in {SceneName}.");
                return 0;
            }

            int changed = 0;
            foreach (var (path, role) in SceneTargets)
            {
                // Transform.Find also reaches inactive objects (hidden views, modals, menu).
                Transform target = canvas.Find(path);
                if (target == null)
                {
                    Debug.LogWarning($"[ThemePanelsBuilder] '{CanvasName}/{path}' not found — skipped.");
                    continue;
                }

                if (Apply(target, role, theme, useUndo: true))
                    changed++;
            }

            if (changed > 0)
                EditorSceneManager.MarkSceneDirty(scene);

            return changed;
        }

        private static int ApplyToInfoBar(UITheme theme)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(InfoBarPrefabPath) == null)
            {
                Debug.LogError($"[ThemePanelsBuilder] Prefab not found: {InfoBarPrefabPath}");
                return 0;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(InfoBarPrefabPath);
            try
            {
                int changed = 0;
                foreach (var (path, role) in InfoBarTargets)
                {
                    Transform target = string.IsNullOrEmpty(path) ? root.transform : root.transform.Find(path);
                    if (target == null)
                    {
                        Debug.LogWarning($"[ThemePanelsBuilder] '{path}' not found in {InfoBarPrefabPath} — skipped.");
                        continue;
                    }

                    if (Apply(target, role, theme, useUndo: false))
                        changed++;
                }

                // The warehouse illustration fades into the tab: match the new panel colour.
                Transform fade = root.transform.Find(WarehouseFadePath);
                var fadeImage = fade != null ? fade.GetComponent<Image>() : null;
                if (fadeImage != null)
                    fadeImage.color = theme.PanelFill;

                PrefabUtility.SaveAsPrefabAsset(root, InfoBarPrefabPath);
                return changed;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static bool Apply(Transform target, Role role, UITheme theme, bool useUndo)
        {
            var image = target.GetComponent<Image>();
            if (image == null)
            {
                Debug.LogWarning($"[ThemePanelsBuilder] '{target.name}' has no Image — skipped.", target);
                return false;
            }

            if (useUndo)
                Undo.RecordObject(image, "Apply theme panel");

            switch (role)
            {
                case Role.Clear:
                    image.sprite = null;
                    image.type = Image.Type.Simple;
                    image.color = new Color(0f, 0f, 0f, 0f);
                    break;

                case Role.Panel:
                    SetSliced(image, theme.Panel);
                    break;

                case Role.Inset:
                    SetSliced(image, theme.Inset);
                    break;

                case Role.Base:
                    image.sprite = null;
                    image.type = Image.Type.Simple;
                    image.color = theme.ScreenBase;
                    break;
            }

            if (!useUndo)
                EditorUtility.SetDirty(image);
            return true;
        }

        private static void SetSliced(Image image, Sprite sprite)
        {
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.fillCenter = true;
            image.pixelsPerUnitMultiplier = 1f;
            image.preserveAspect = false;
            image.color = Color.white; // the colours live in the sprite
        }

        // ------------------------------------------------------------------
        // Theme asset and sprite import settings
        // ------------------------------------------------------------------

        private static UITheme LoadOrCreateTheme()
        {
            var theme = AssetDatabase.LoadAssetAtPath<UITheme>(ThemePath);
            if (theme == null)
            {
                theme = ScriptableObject.CreateInstance<UITheme>();
                AssetDatabase.CreateAsset(theme, ThemePath);
                Debug.Log($"[ThemePanelsBuilder] Created {ThemePath}.", theme);
            }

            // Fill empty sprite slots from the frame files; slots set by hand are kept.
            theme.Panel = theme.Panel != null ? theme.Panel : Frame("ui_panel");
            theme.Inset = theme.Inset != null ? theme.Inset : Frame("ui_row");
            theme.RowSelected = theme.RowSelected != null ? theme.RowSelected : Frame("ui_row_selected");
            theme.Button = theme.Button != null ? theme.Button : Frame("ui_button");
            theme.ButtonPrimary = theme.ButtonPrimary != null ? theme.ButtonPrimary : Frame("ui_button_primary");
            theme.ButtonDanger = theme.ButtonDanger != null ? theme.ButtonDanger : Frame("ui_button_danger");
            theme.NavItem = theme.NavItem != null ? theme.NavItem : Frame("ui_nav_item");
            theme.NavActive = theme.NavActive != null ? theme.NavActive : Frame("ui_nav_active");
            theme.Tab = theme.Tab != null ? theme.Tab : Frame("ui_tab");
            theme.TabActive = theme.TabActive != null ? theme.TabActive : Frame("ui_tab_active");
            theme.Input = theme.Input != null ? theme.Input : Frame("ui_input");
            theme.BadgeGreen = theme.BadgeGreen != null ? theme.BadgeGreen : Frame("ui_badge_green");
            theme.Divider = theme.Divider != null ? theme.Divider : Frame("ui_divider");

            EditorUtility.SetDirty(theme);
            AssetDatabase.SaveAssets();
            return theme;
        }

        private static Sprite Frame(string name) =>
            AssetDatabase.LoadAssetAtPath<Sprite>(FramesFolder + name + ".png");

        /// <summary>Sets sprite borders for 9-slicing (and keeps the frames crisp: no compression, no mipmaps).</summary>
        private static void EnsureFrameBorders()
        {
            foreach (var pair in FrameBorders)
            {
                string path = FramesFolder + pair.Key + ".png";
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null)
                {
                    Debug.LogWarning($"[ThemePanelsBuilder] Frame sprite not found: {path}");
                    continue;
                }

                bool dirty = false;
                if (importer.textureType != TextureImporterType.Sprite) { importer.textureType = TextureImporterType.Sprite; dirty = true; }
                if (importer.spriteImportMode != SpriteImportMode.Single) { importer.spriteImportMode = SpriteImportMode.Single; dirty = true; }
                if (importer.spriteBorder != pair.Value) { importer.spriteBorder = pair.Value; dirty = true; }
                if (importer.mipmapEnabled) { importer.mipmapEnabled = false; dirty = true; }
                if (importer.textureCompression != TextureImporterCompression.Uncompressed) { importer.textureCompression = TextureImporterCompression.Uncompressed; dirty = true; }

                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                if (settings.spriteMeshType != SpriteMeshType.FullRect)
                {
                    settings.spriteMeshType = SpriteMeshType.FullRect;
                    importer.SetTextureSettings(settings);
                    dirty = true;
                }

                if (dirty)
                    importer.SaveAndReimport();
            }
        }
    }
}
