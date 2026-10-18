using AntiqueTradingSimulator.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.EditorTools
{
    /// <summary>
    /// Turns the existing antique / contract Image fields into layered slots:
    ///   AntiqueImage (existing Image = backdrop, + AntiqueImageSlot)
    ///   └─ ArtworkFrame (AspectRatioFitter 3:4, fits inside the slot)
    ///      ├─ Shadow   (soft ellipse at the object's base)
    ///      └─ Artwork  (the transparent antique PNG)
    /// The existing GameObjects, their LayoutElements and the UI scripts' references are kept,
    /// so slot sizes and layouts don't change. Safe to run more than once.
    /// Prefabs are saved directly; Main.unity must be open and is marked dirty (save it after).
    /// </summary>
    public static class AntiqueImageSlotBuilder
    {
        private const string MenuPath = "Tools/Antique Trading Simulator/Art/Build Antique Image Slots";

        private const string FrameName = "ArtworkFrame";
        private const string ShadowName = "Shadow";
        private const string ArtworkName = "Artwork";

        // Shadow ellipse in ArtworkFrame space: artwork files keep ~10% empty margin under the object.
        private static readonly Vector2 ShadowAnchorMin = new Vector2(0.15f, 0.05f);
        private static readonly Vector2 ShadowAnchorMax = new Vector2(0.85f, 0.13f);

        private struct PrefabSlot
        {
            public string PrefabPath;
            public string ChildPath;
            public bool ShowShadow;

            public PrefabSlot(string prefabPath, string childPath, bool showShadow)
            {
                PrefabPath = prefabPath;
                ChildPath = childPath;
                ShowShadow = showShadow;
            }
        }

        private struct SceneSlot
        {
            public string PathUnderCanvas;
            public bool ShowShadow;

            public SceneSlot(string pathUnderCanvas, bool showShadow)
            {
                PathUnderCanvas = pathUnderCanvas;
                ShowShadow = showShadow;
            }
        }

        private static readonly PrefabSlot[] PrefabSlots =
        {
            new PrefabSlot("Assets/_Project/Prefabs/UI/ListingCard.prefab", "Layout/MainContent/AntiqueImage", true),
            new PrefabSlot("Assets/_Project/Prefabs/UI/Inventory/InventoryListItem.prefab", "AntiqueImage", true),
            new PrefabSlot("Assets/_Project/Prefabs/UI/Contracts/ContractFulfillListItem.prefab", "AntiqueImage", true),
            // Contract pictures are emblems (country / century / type), not objects on a surface.
            new PrefabSlot("Assets/_Project/Prefabs/UI/Contracts/ContractListItem.prefab", "Icon", false),
        };

        private const string SceneName = "Main";
        private const string CanvasName = "Canvas";

        private static readonly SceneSlot[] SceneSlots =
        {
            new SceneSlot("Views/MarketView/MainArea/SidePanel/AntiqueDetails/OverviewSection/AntiqueImage", true),
            new SceneSlot("Views/InventoryView/MainArea/RIghtContent/InventoryDetails/MainDetails/OverviewSection/ImageColumn/AntiqueImage", true),
            new SceneSlot("Modals/ListOnMarketModal/Window/Body/LeftColumn/AntiqueImage", true),
            new SceneSlot("Views/ContractsView/DetailsPanel/DetailsContent/Header/ContractImage", false),
        };

        [MenuItem(MenuPath)]
        public static void Run()
        {
            int built = 0;

            foreach (var slot in PrefabSlots)
                built += BuildInPrefab(slot);

            built += BuildInScene();

            Debug.Log($"[AntiqueImageSlotBuilder] {built} slot(s) built or updated. " +
                      "Save the scene (Ctrl+S), then run Art → Assign Artwork if you haven't yet.");
        }

        // ------------------------------------------------------------------

        private static int BuildInPrefab(PrefabSlot target)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(target.PrefabPath) == null)
            {
                Debug.LogError($"[AntiqueImageSlotBuilder] Prefab not found: {target.PrefabPath}");
                return 0;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(target.PrefabPath);
            try
            {
                Transform slotTransform = root.transform.Find(target.ChildPath);
                if (slotTransform == null)
                {
                    Debug.LogError($"[AntiqueImageSlotBuilder] '{target.ChildPath}' not found in {target.PrefabPath}");
                    return 0;
                }

                if (!BuildSlot(slotTransform.gameObject, target.ShowShadow, useUndo: false))
                    return 0;

                PrefabUtility.SaveAsPrefabAsset(root, target.PrefabPath);
                return 1;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static int BuildInScene()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name != SceneName)
            {
                Debug.LogWarning($"[AntiqueImageSlotBuilder] Open the {SceneName} scene to build the slots in the details panels and modals (prefabs were still updated).");
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
                Debug.LogError($"[AntiqueImageSlotBuilder] No root '{CanvasName}' in {SceneName}.");
                return 0;
            }

            int built = 0;
            foreach (var target in SceneSlots)
            {
                // Transform.Find also reaches inactive objects (modals and details start hidden).
                Transform slotTransform = canvas.Find(target.PathUnderCanvas);
                if (slotTransform == null)
                {
                    Debug.LogError($"[AntiqueImageSlotBuilder] '{CanvasName}/{target.PathUnderCanvas}' not found.");
                    continue;
                }

                if (BuildSlot(slotTransform.gameObject, target.ShowShadow, useUndo: true))
                    built++;
            }

            if (built > 0)
                EditorSceneManager.MarkSceneDirty(scene);

            return built;
        }

        // ------------------------------------------------------------------

        private static bool BuildSlot(GameObject slotObject, bool showShadow, bool useUndo)
        {
            var background = slotObject.GetComponent<Image>();
            if (background == null)
            {
                Debug.LogError($"[AntiqueImageSlotBuilder] '{slotObject.name}' has no Image to use as the backdrop.", slotObject);
                return false;
            }

            var slot = slotObject.GetComponent<AntiqueImageSlot>();
            if (slot == null)
                slot = useUndo ? Undo.AddComponent<AntiqueImageSlot>(slotObject) : slotObject.AddComponent<AntiqueImageSlot>();

            // Frame: same proportions as the artwork files, centred in whatever shape the slot has.
            var frame = FindOrCreateChild(slotObject.transform, FrameName, useUndo, withImage: false);
            var fitter = frame.GetComponent<AspectRatioFitter>();
            if (fitter == null)
                fitter = useUndo ? Undo.AddComponent<AspectRatioFitter>(frame.gameObject) : frame.gameObject.AddComponent<AspectRatioFitter>();
            if (useUndo) Undo.RecordObject(fitter, "Configure artwork frame");
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = AntiqueImageSlot.ArtworkAspect;

            // Shadow first so it draws under the artwork.
            var shadowRect = FindOrCreateChild(frame, ShadowName, useUndo, withImage: true);
            if (useUndo) Undo.RecordObject(shadowRect, "Place shadow");
            shadowRect.SetSiblingIndex(0);
            shadowRect.anchorMin = ShadowAnchorMin;
            shadowRect.anchorMax = ShadowAnchorMax;
            shadowRect.pivot = new Vector2(0.5f, 0.5f);
            shadowRect.offsetMin = Vector2.zero;
            shadowRect.offsetMax = Vector2.zero;
            var shadowImage = shadowRect.GetComponent<Image>();
            if (useUndo) Undo.RecordObject(shadowImage, "Configure shadow");
            shadowImage.raycastTarget = false;
            shadowImage.color = new Color(0f, 0f, 0f, 0.6f);
            shadowImage.enabled = false;

            var artworkRect = FindOrCreateChild(frame, ArtworkName, useUndo, withImage: true);
            if (useUndo) Undo.RecordObject(artworkRect, "Place artwork");
            artworkRect.SetSiblingIndex(1);
            Stretch(artworkRect);
            var artworkImage = artworkRect.GetComponent<Image>();
            if (useUndo) Undo.RecordObject(artworkImage, "Configure artwork");
            artworkImage.raycastTarget = false;
            artworkImage.preserveAspect = true;
            artworkImage.color = Color.white;
            artworkImage.enabled = false;

            var serialized = new SerializedObject(slot);
            serialized.FindProperty("background").objectReferenceValue = background;
            serialized.FindProperty("shadow").objectReferenceValue = shadowImage;
            serialized.FindProperty("artwork").objectReferenceValue = artworkImage;
            serialized.FindProperty("showShadow").boolValue = showShadow;
            if (useUndo)
                serialized.ApplyModifiedProperties();
            else
                serialized.ApplyModifiedPropertiesWithoutUndo();

            return true;
        }

        private static RectTransform FindOrCreateChild(Transform parent, string name, bool useUndo, bool withImage)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
                return (RectTransform)existing;

            var go = withImage
                ? new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image))
                : new GameObject(name, typeof(RectTransform));
            if (useUndo)
                Undo.RegisterCreatedObjectUndo(go, $"Create {name}");

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
