using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.EditorTools
{
    /// <summary>
    /// Puts the generated background illustrations into the UI:
    ///  - Main.unity  Canvas/ScreenBackground        bg_screen.png behind every view (first child of the Canvas)
    ///  - Main.unity  Canvas/GameMenu/Backdrop       bg_main_menu.png + a dim layer behind the pause menu panel
    ///  - InfoBar.prefab  WarehouseTab               bg_warehouse.png on the right, faded into the tab on the left;
    ///                                               the tab's content is padded so text stays on the dark side
    /// All illustrations cover their area without stretching (AspectRatioFitter, Envelope Parent)
    /// and never block clicks. Safe to run more than once — existing objects are updated, not duplicated.
    /// </summary>
    public static class BackgroundsBuilder
    {
        private const string MenuPath = "Tools/Antique Trading Simulator/UI/Apply Backgrounds";

        private const string BackgroundsFolder = "Assets/_Project/Art/UI/Backgrounds/";
        private const string ScreenSpritePath = BackgroundsFolder + "bg_screen.png";
        private const string MenuSpritePath = BackgroundsFolder + "bg_main_menu.png";
        private const string WarehouseSpritePath = BackgroundsFolder + "bg_warehouse.png";
        private const string FadeSpritePath = "Assets/_Project/Art/UI/Effects/ui_fade_left.png";

        private const string InfoBarPrefabPath = "Assets/_Project/Prefabs/UI/InfoBar/InfoBar.prefab";

        private const string SceneName = "Main";
        private const string CanvasName = "Canvas";
        private const string GameMenuBackdropPath = "GameMenu/Backdrop";
        private const string WarehouseTabPath = "WarehouseTab";

        private const string ScreenBackgroundName = "ScreenBackground";
        private const string MenuIllustrationName = "MenuIllustration";
        private const string MenuDimName = "MenuDim";
        private const string IllustrationName = "Illustration";
        private const string IllustrationFadeName = "IllustrationFade";

        /// <summary>All three illustrations are 16:9.</summary>
        private const float IllustrationAspect = 16f / 9f;

        /// <summary>Darkening over the pause-menu illustration so the buttons stay readable.</summary>
        private const float MenuDimAlpha = 0.45f;

        /// <summary>Share of the warehouse tab (from the right) covered by the illustration.</summary>
        private const float WarehouseIllustrationWidth = 0.6f;
        /// <summary>Right padding of the warehouse tab, so its text and bars stay over the dark left part.</summary>
        private const int WarehouseContentRightPadding = 190;

        [MenuItem(MenuPath)]
        public static void Run()
        {
            var screenSprite = LoadSprite(ScreenSpritePath);
            var menuSprite = LoadSprite(MenuSpritePath);
            var warehouseSprite = LoadSprite(WarehouseSpritePath);
            var fadeSprite = LoadSprite(FadeSpritePath);

            int changed = 0;
            changed += ApplyToScene(screenSprite, menuSprite);
            changed += ApplyToWarehouseTab(warehouseSprite, fadeSprite);

            Debug.Log($"[BackgroundsBuilder] Done ({changed} area(s) updated). Save the scene (Ctrl+S).");
        }

        // ------------------------------------------------------------------
        // Scene: screen background + pause menu
        // ------------------------------------------------------------------

        private static int ApplyToScene(Sprite screenSprite, Sprite menuSprite)
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name != SceneName)
            {
                Debug.LogWarning($"[BackgroundsBuilder] Open the {SceneName} scene to apply the screen and pause-menu backgrounds (skipped).");
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
                Debug.LogError($"[BackgroundsBuilder] No root '{CanvasName}' in {SceneName}.");
                return 0;
            }

            int changed = 0;

            if (screenSprite != null)
            {
                // First child = drawn before (under) every view, the HUD and the InfoBar.
                var screen = FindOrCreateImage(canvas, ScreenBackgroundName, useUndo: true);
                screen.transform.SetSiblingIndex(0);
                SetupIllustration(screen, screenSprite, useUndo: true);
                changed++;
            }

            Transform backdrop = canvas.Find(GameMenuBackdropPath);
            if (backdrop == null)
            {
                Debug.LogError($"[BackgroundsBuilder] '{CanvasName}/{GameMenuBackdropPath}' not found — pause menu background skipped.");
            }
            else if (menuSprite != null)
            {
                var illustration = FindOrCreateImage(backdrop, MenuIllustrationName, useUndo: true);
                illustration.transform.SetSiblingIndex(0);
                SetupIllustration(illustration, menuSprite, useUndo: true);

                var dim = FindOrCreateImage(backdrop, MenuDimName, useUndo: true);
                dim.transform.SetSiblingIndex(1);
                Undo.RecordObject(dim, "Configure menu dim");
                Stretch((RectTransform)dim.transform);
                dim.sprite = null;
                dim.color = new Color(0f, 0f, 0f, MenuDimAlpha);
                dim.raycastTarget = false;
                changed++;
            }

            if (changed > 0)
                EditorSceneManager.MarkSceneDirty(scene);

            return changed;
        }

        // ------------------------------------------------------------------
        // InfoBar prefab: warehouse illustration
        // ------------------------------------------------------------------

        private static int ApplyToWarehouseTab(Sprite warehouseSprite, Sprite fadeSprite)
        {
            if (warehouseSprite == null)
                return 0;

            if (AssetDatabase.LoadAssetAtPath<GameObject>(InfoBarPrefabPath) == null)
            {
                Debug.LogError($"[BackgroundsBuilder] Prefab not found: {InfoBarPrefabPath}");
                return 0;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(InfoBarPrefabPath);
            try
            {
                Transform tab = root.transform.Find(WarehouseTabPath);
                if (tab == null)
                {
                    Debug.LogError($"[BackgroundsBuilder] '{WarehouseTabPath}' not found in {InfoBarPrefabPath}");
                    return 0;
                }

                // Area on the right side of the tab that holds the picture and its fade;
                // its RectMask2D crops the 16:9 picture to the tab's height.
                var area = FindOrCreateRect(tab, IllustrationName + "Area");
                area.SetSiblingIndex(0);
                IgnoreLayout(area.gameObject);
                area.anchorMin = new Vector2(1f - WarehouseIllustrationWidth, 0f);
                area.anchorMax = Vector2.one;
                area.pivot = new Vector2(1f, 0.5f);
                area.offsetMin = Vector2.zero;
                area.offsetMax = Vector2.zero;
                if (area.GetComponent<RectMask2D>() == null)
                    area.gameObject.AddComponent<RectMask2D>();

                var illustration = FindOrCreateImage(area, IllustrationName, useUndo: false);
                illustration.transform.SetSiblingIndex(0);
                SetupIllustration(illustration, warehouseSprite, useUndo: false);

                if (fadeSprite != null)
                {
                    var fade = FindOrCreateImage(area, IllustrationFadeName, useUndo: false);
                    fade.transform.SetSiblingIndex(1);
                    var fadeRect = (RectTransform)fade.transform;
                    // Left half of the picture fades into the tab's own background colour.
                    fadeRect.anchorMin = Vector2.zero;
                    fadeRect.anchorMax = new Vector2(0.6f, 1f);
                    fadeRect.pivot = new Vector2(0f, 0.5f);
                    fadeRect.offsetMin = Vector2.zero;
                    fadeRect.offsetMax = Vector2.zero;
                    fade.sprite = fadeSprite;
                    fade.type = Image.Type.Simple;
                    fade.preserveAspect = false;
                    var tabImage = tab.GetComponent<Image>();
                    var fadeColor = tabImage != null ? tabImage.color : Color.black;
                    fadeColor.a = 1f;
                    fade.color = fadeColor;
                    fade.raycastTarget = false;
                }

                // Keep the title, slots bar and rows over the plain left part of the tab.
                var layout = tab.GetComponent<VerticalLayoutGroup>();
                if (layout != null)
                    layout.padding.right = WarehouseContentRightPadding;

                PrefabUtility.SaveAsPrefabAsset(root, InfoBarPrefabPath);
                return 1;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static Sprite LoadSprite(string path)
        {
            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
            if (sprite == null)
                Debug.LogError($"[BackgroundsBuilder] Sprite not found at {path} — check the file exists and is imported as Sprite (2D and UI).");
            return sprite;
        }

        /// <summary>Full-cover picture: centred, keeps 16:9, crops instead of stretching, ignores clicks.</summary>
        private static void SetupIllustration(Image image, Sprite sprite, bool useUndo)
        {
            if (useUndo)
                Undo.RecordObject(image, "Configure illustration");

            Stretch((RectTransform)image.transform);
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = false;
            image.color = Color.white;
            image.raycastTarget = false;

            var fitter = image.GetComponent<AspectRatioFitter>();
            if (fitter == null)
                fitter = useUndo ? Undo.AddComponent<AspectRatioFitter>(image.gameObject) : image.gameObject.AddComponent<AspectRatioFitter>();
            if (useUndo)
                Undo.RecordObject(fitter, "Configure illustration fitter");
            fitter.aspectMode = AspectRatioFitter.AspectMode.EnvelopeParent;
            fitter.aspectRatio = IllustrationAspect;

            IgnoreLayout(image.gameObject);
        }

        private static void IgnoreLayout(GameObject go)
        {
            var element = go.GetComponent<LayoutElement>();
            if (element == null)
                element = go.AddComponent<LayoutElement>();
            element.ignoreLayout = true;
        }

        private static Image FindOrCreateImage(Transform parent, string name, bool useUndo)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                var image = existing.GetComponent<Image>();
                return image != null ? image : existing.gameObject.AddComponent<Image>();
            }

            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            if (useUndo)
                Undo.RegisterCreatedObjectUndo(go, $"Create {name}");
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Stretch(rect);
            return go.GetComponent<Image>();
        }

        private static RectTransform FindOrCreateRect(Transform parent, string name)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
                return (RectTransform)existing;

            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            Stretch(rect);
            return rect;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }
    }
}
