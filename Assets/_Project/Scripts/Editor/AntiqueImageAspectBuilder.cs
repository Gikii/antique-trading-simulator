using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.EditorTools
{
    /// <summary>
    /// Gives the antique image slots that were not portrait yet the same proportions as the
    /// rest of the UI (InventoryDetails 210x283, AntiqueDetails 178x240, ListingCard 119x160,
    /// ContractDetails 210x283 — all ~0.742 width / height):
    ///  - ContractListItem.prefab / Icon             64x72 -> 53x72
    ///  - ContractFulfillListItem.prefab / AntiqueImage 51x58 -> 43x58
    ///  - Main.unity ListOnMarketModal AntiqueImage  stretched ~440x280 -> 208x280, centred
    ///    inside a new AntiqueImageFrame that takes over the old slot in the column layout.
    /// Safe to run more than once.
    /// </summary>
    public static class AntiqueImageAspectBuilder
    {
        /// <summary>Width / height of every antique picture slot (210 / 283 ≈ 0.742).</summary>
        public const float AntiqueImageAspect = 210f / 283f;

        private const string MenuPath = "Tools/Antique Trading Simulator/UI/Fix Antique Image Proportions";

        private const string ContractListItemPrefab = "Assets/_Project/Prefabs/UI/Contracts/ContractListItem.prefab";
        private const string ContractFulfillListItemPrefab = "Assets/_Project/Prefabs/UI/Contracts/ContractFulfillListItem.prefab";

        // Height the slot actually gets from its row:
        // ContractListItem row is 84 high with 6 px top/bottom padding and force-expands child height.
        private const float ContractListIconHeight = 72f;
        // ContractFulfillListItem uses the image's own preferred height (58) inside a 68 px row.
        private const float ContractFulfillImageHeight = 58f;

        private const string SceneName = "Main";
        private const string CanvasName = "Canvas";
        private const string ListOnMarketColumnPath = "Modals/ListOnMarketModal/Window/Body/LeftColumn";
        private const string ImageName = "AntiqueImage";
        private const string FrameName = "AntiqueImageFrame";

        [MenuItem(MenuPath)]
        public static void Run()
        {
            int changed = 0;
            changed += FixPrefabSlot(ContractListItemPrefab, "Icon", ContractListIconHeight, setHeight: false);
            changed += FixPrefabSlot(ContractFulfillListItemPrefab, ImageName, ContractFulfillImageHeight, setHeight: true);
            changed += FixListOnMarketImage();

            Debug.Log(changed > 0
                ? $"[AntiqueImageAspectBuilder] Updated {changed} slot(s) to aspect {AntiqueImageAspect:0.000}. Save the scene (Ctrl+S)."
                : "[AntiqueImageAspectBuilder] Nothing to change — all slots already have the right proportions.");
        }

        // ------------------------------------------------------------------
        // Prefab rows: fixed width derived from the height the row gives the image
        // ------------------------------------------------------------------

        private static int FixPrefabSlot(string prefabPath, string childName, float height, bool setHeight)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                Debug.LogError($"[AntiqueImageAspectBuilder] Prefab not found: {prefabPath}");
                return 0;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Transform slot = root.transform.Find(childName);
                if (slot == null)
                {
                    Debug.LogError($"[AntiqueImageAspectBuilder] '{childName}' not found in {prefabPath}");
                    return 0;
                }

                float width = Mathf.Round(height * AntiqueImageAspect);

                var layout = slot.GetComponent<LayoutElement>();
                if (layout == null)
                    layout = slot.gameObject.AddComponent<LayoutElement>();

                var image = slot.GetComponent<Image>();

                bool alreadyOk =
                    Mathf.Approximately(layout.minWidth, width) &&
                    Mathf.Approximately(layout.preferredWidth, width) &&
                    Mathf.Approximately(layout.flexibleWidth, 0f) &&
                    (!setHeight || (Mathf.Approximately(layout.minHeight, height) &&
                                    Mathf.Approximately(layout.preferredHeight, height))) &&
                    (image == null || image.preserveAspect);

                if (alreadyOk)
                    return 0;

                layout.minWidth = width;
                layout.preferredWidth = width;
                layout.flexibleWidth = 0f;

                if (setHeight)
                {
                    layout.minHeight = height;
                    layout.preferredHeight = height;
                    layout.flexibleHeight = 0f;
                }

                // Real sprites never get squashed, even if the row height changes later.
                if (image != null)
                    image.preserveAspect = true;

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                Debug.Log($"[AntiqueImageAspectBuilder] {prefabPath} / {childName} -> {width}x{height}");
                return 1;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ------------------------------------------------------------------
        // ListOnMarket modal: frame keeps the old layout slot, image fits inside it
        // ------------------------------------------------------------------

        private static int FixListOnMarketImage()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name != SceneName)
            {
                Debug.LogWarning($"[AntiqueImageAspectBuilder] Open the {SceneName} scene to fix the ListOnMarket modal image (skipped).");
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

            // Transform.Find also works on inactive objects (the modal is hidden by default).
            Transform column = canvas != null ? canvas.Find(ListOnMarketColumnPath) : null;
            if (column == null)
            {
                Debug.LogError($"[AntiqueImageAspectBuilder] '{CanvasName}/{ListOnMarketColumnPath}' not found in {SceneName}.");
                return 0;
            }

            Transform frame = column.Find(FrameName);
            Transform imageTransform = frame != null ? frame.Find(ImageName) : column.Find(ImageName);
            if (imageTransform == null)
            {
                Debug.LogError($"[AntiqueImageAspectBuilder] '{ImageName}' not found under {ListOnMarketColumnPath}.");
                return 0;
            }

            int changed = 0;

            if (frame == null)
            {
                frame = CreateFrame(column, imageTransform);
                changed = 1;
            }

            var imageGo = imageTransform.gameObject;

            // The frame has no layout group, so the image's own LayoutElement would only be
            // misleading — the frame now carries those sizes.
            var oldLayout = imageGo.GetComponent<LayoutElement>();
            if (oldLayout != null)
            {
                Undo.DestroyObjectImmediate(oldLayout);
                changed = 1;
            }

            var fitter = imageGo.GetComponent<AspectRatioFitter>();
            if (fitter == null)
            {
                fitter = Undo.AddComponent<AspectRatioFitter>(imageGo);
                changed = 1;
            }

            if (fitter.aspectMode != AspectRatioFitter.AspectMode.FitInParent ||
                !Mathf.Approximately(fitter.aspectRatio, AntiqueImageAspect))
            {
                Undo.RecordObject(fitter, "Fit antique image");
                fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
                fitter.aspectRatio = AntiqueImageAspect;
                changed = 1;
            }

            var rect = (RectTransform)imageTransform;
            if (rect.pivot != new Vector2(0.5f, 0.5f) || rect.anchoredPosition != Vector2.zero)
            {
                Undo.RecordObject(rect, "Centre antique image");
                rect.pivot = new Vector2(0.5f, 0.5f);
                rect.anchoredPosition = Vector2.zero;
                changed = 1;
            }

            var image = imageGo.GetComponent<Image>();
            if (image != null && !image.preserveAspect)
            {
                Undo.RecordObject(image, "Preserve antique image aspect");
                image.preserveAspect = true;
                changed = 1;
            }

            if (changed > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                Debug.Log($"[AntiqueImageAspectBuilder] ListOnMarketModal image wrapped in {FrameName} and fitted to {AntiqueImageAspect:0.000}.");
            }

            return changed;
        }

        /// <summary>
        /// Inserts an empty frame where the image was and moves the image into it. The frame copies
        /// the image's LayoutElement (min 200 / preferred 280 high, stretched to the column width), so
        /// the rest of the column lays out exactly as before.
        /// </summary>
        private static Transform CreateFrame(Transform column, Transform imageTransform)
        {
            int siblingIndex = imageTransform.GetSiblingIndex();
            var oldLayout = imageTransform.GetComponent<LayoutElement>();

            var frameGo = new GameObject(FrameName, typeof(RectTransform), typeof(LayoutElement));
            Undo.RegisterCreatedObjectUndo(frameGo, "Create antique image frame");
            frameGo.layer = imageTransform.gameObject.layer;

            var frameRect = (RectTransform)frameGo.transform;
            frameRect.SetParent(column, false);
            frameRect.SetSiblingIndex(siblingIndex);
            frameRect.anchorMin = Vector2.zero;
            frameRect.anchorMax = Vector2.one;
            frameRect.pivot = new Vector2(0.5f, 0.5f);
            frameRect.sizeDelta = Vector2.zero;

            var frameLayout = frameGo.GetComponent<LayoutElement>();
            if (oldLayout != null)
            {
                frameLayout.minWidth = oldLayout.minWidth;
                frameLayout.minHeight = oldLayout.minHeight;
                frameLayout.preferredWidth = oldLayout.preferredWidth;
                frameLayout.preferredHeight = oldLayout.preferredHeight;
                frameLayout.flexibleWidth = oldLayout.flexibleWidth;
                frameLayout.flexibleHeight = oldLayout.flexibleHeight;
            }
            else
            {
                frameLayout.minHeight = 200f;
                frameLayout.preferredHeight = 280f;
                frameLayout.flexibleHeight = 0f;
            }

            Undo.SetTransformParent(imageTransform, frameRect, "Move antique image into frame");
            imageTransform.SetSiblingIndex(0);

            return frameRect;
        }
    }
}
