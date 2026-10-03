using AntiqueTradingSimulator.UI;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.EditorTools
{
    /// <summary>
    /// One-click setup for the TRANSPORTS and WAREHOUSE tabs of the InfoBar (UI patch B).
    /// Creates InfoBarRow.prefab (styled like UpcomingEventListItem, with a label on the left
    /// and a detail on the right) and wires InfoBarTransportsUI / InfoBarWarehouseUI into
    /// InfoBar.prefab. Only prefabs are changed — the scene picks them up automatically.
    ///
    /// Menu: Tools → Antique Trading Simulator → Build Transport InfoBar.
    /// Safe to delete this file once the UI is built.
    /// </summary>
    public static class TransportInfoBarBuilder
    {
        private const string MenuPath = "Tools/Antique Trading Simulator/Build Transport InfoBar";
        private const string Folder = "Assets/_Project/Prefabs/UI/InfoBar";
        private const string InfoBarPath = Folder + "/InfoBar.prefab";
        private const string RowTemplatePath = Folder + "/UpcomingEventListItem.prefab";
        private const string RowPath = Folder + "/InfoBarRow.prefab";

        private const float FillBarHeight = 6f;

        [MenuItem(MenuPath, true)]
        private static bool Validate() => !EditorApplication.isPlaying;

        [MenuItem(MenuPath)]
        public static void Build()
        {
            InfoBarRowUI rowPrefab = GetOrCreateRowPrefab();
            if (rowPrefab == null)
                return;

            if (AssetDatabase.LoadAssetAtPath<GameObject>(InfoBarPath) == null)
            {
                Debug.LogError($"TransportInfoBarBuilder: {InfoBarPath} not found.");
                return;
            }

            GameObject infoBar = PrefabUtility.LoadPrefabContents(InfoBarPath);
            try
            {
                bool transports = SetupTransportsTab(infoBar.transform.Find("TransportTab"), rowPrefab);
                bool warehouse = SetupWarehouseTab(infoBar.transform.Find("WarehouseTab"), rowPrefab);

                if (!transports && !warehouse)
                    return;

                PrefabUtility.SaveAsPrefabAsset(infoBar, InfoBarPath);
                Debug.Log("TransportInfoBarBuilder: InfoBar updated — TRANSPORTS and WAREHOUSE tabs are wired. " +
                          "Enter Play mode to see them fill in.");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(infoBar);
            }

            Selection.activeObject = AssetDatabase.LoadAssetAtPath<GameObject>(InfoBarPath);
        }

        // ------------------------------------------------------------------
        // Row prefab
        // ------------------------------------------------------------------

        private static InfoBarRowUI GetOrCreateRowPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(RowPath);
            if (existing != null)
                return existing.GetComponent<InfoBarRowUI>();

            var template = AssetDatabase.LoadAssetAtPath<GameObject>(RowTemplatePath);
            if (template == null)
            {
                Debug.LogError($"TransportInfoBarBuilder: {RowTemplatePath} not found — can't style the row.");
                return null;
            }

            // A plain clone (not a prefab instance), so the result is an independent prefab.
            GameObject row = Object.Instantiate(template);
            row.name = "InfoBarRow";
            try
            {
                var oldScript = row.GetComponent<UpcomingEventListItemUI>();
                if (oldScript != null)
                    Object.DestroyImmediate(oldScript);

                Transform labelTransform = row.transform.Find("NameText");
                if (labelTransform == null)
                {
                    Debug.LogError("TransportInfoBarBuilder: UpcomingEventListItem has no NameText — build the row by hand.");
                    return null;
                }

                labelTransform.name = "LabelText";
                var label = labelTransform.GetComponent<TMP_Text>();
                label.text = "Label";
                label.textWrappingMode = TextWrappingModes.NoWrap;
                label.overflowMode = TextOverflowModes.Ellipsis;

                GameObject detailGo = Object.Instantiate(labelTransform.gameObject, row.transform);
                detailGo.name = "DetailText";
                var detail = detailGo.GetComponent<TMP_Text>();
                detail.text = "Detail";
                detail.horizontalAlignment = HorizontalAlignmentOptions.Right;
                detail.overflowMode = TextOverflowModes.Overflow;

                // Label takes the remaining width, detail its natural width on the right.
                var layout = row.GetComponent<HorizontalLayoutGroup>();
                if (layout == null) layout = row.AddComponent<HorizontalLayoutGroup>();
                layout.padding = new RectOffset(8, 8, 0, 0);
                layout.spacing = 8f;
                layout.childAlignment = TextAnchor.MiddleLeft;
                layout.childControlWidth = true;
                layout.childControlHeight = true;
                layout.childForceExpandWidth = false;
                layout.childForceExpandHeight = true;

                var labelElement = GetOrAdd<LayoutElement>(label.gameObject);
                labelElement.minWidth = 0f;
                labelElement.preferredWidth = 0f;
                labelElement.flexibleWidth = 1f;

                var detailElement = GetOrAdd<LayoutElement>(detailGo);
                detailElement.flexibleWidth = 0f;

                var rowUI = row.AddComponent<InfoBarRowUI>();
                var so = new SerializedObject(rowUI);
                so.FindProperty("labelText").objectReferenceValue = label;
                so.FindProperty("detailText").objectReferenceValue = detail;
                so.ApplyModifiedPropertiesWithoutUndo();

                GameObject saved = PrefabUtility.SaveAsPrefabAsset(row, RowPath);
                return saved != null ? saved.GetComponent<InfoBarRowUI>() : null;
            }
            finally
            {
                Object.DestroyImmediate(row);
            }
        }

        // ------------------------------------------------------------------
        // Tabs
        // ------------------------------------------------------------------

        private static bool SetupTransportsTab(Transform tab, InfoBarRowUI rowPrefab)
        {
            RectTransform content = Content(tab, "TransportTab");
            if (content == null) return false;

            var ui = GetOrAdd<InfoBarTransportsUI>(tab.gameObject);
            var so = new SerializedObject(ui);
            so.FindProperty("listContainer").objectReferenceValue = content;
            so.FindProperty("rowPrefab").objectReferenceValue = rowPrefab;
            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        private static bool SetupWarehouseTab(Transform tab, InfoBarRowUI rowPrefab)
        {
            RectTransform content = Content(tab, "WarehouseTab");
            if (content == null) return false;

            var ui = GetOrAdd<InfoBarWarehouseUI>(tab.gameObject);
            var so = new SerializedObject(ui);
            so.FindProperty("listContainer").objectReferenceValue = content;
            so.FindProperty("rowPrefab").objectReferenceValue = rowPrefab;

            // Thin slots bar between the tab header and the rows.
            if (so.FindProperty("fillBar").objectReferenceValue == null)
            {
                RectTransform fill = CreateFillBar(tab, content);
                so.FindProperty("fillBar").objectReferenceValue = fill;
                so.FindProperty("fillImage").objectReferenceValue = fill.GetComponent<Image>();
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        private static RectTransform CreateFillBar(Transform tab, RectTransform content)
        {
            var bar = new GameObject("SlotsBar", typeof(RectTransform));
            bar.transform.SetParent(tab, false);
            bar.transform.SetSiblingIndex(content.GetSiblingIndex()); // just above Content

            var background = bar.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.35f);
            background.raycastTarget = false;

            var element = bar.AddComponent<LayoutElement>();
            element.minHeight = FillBarHeight;
            element.preferredHeight = FillBarHeight;
            element.flexibleHeight = 0f;

            var fillGo = new GameObject("Fill", typeof(RectTransform));
            fillGo.transform.SetParent(bar.transform, false);
            var fill = (RectTransform)fillGo.transform;
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(0.5f, 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;

            var fillImage = fillGo.AddComponent<Image>();
            fillImage.color = new Color32(0xC9, 0x9A, 0x3C, 0xFF);
            fillImage.raycastTarget = false;

            return fill;
        }

        private static RectTransform Content(Transform tab, string tabName)
        {
            if (tab == null)
            {
                Debug.LogWarning($"TransportInfoBarBuilder: {tabName} not found in InfoBar — skipped.");
                return null;
            }

            var content = tab.Find("Content") as RectTransform;
            if (content == null)
                Debug.LogWarning($"TransportInfoBarBuilder: {tabName}/Content not found — skipped.");
            return content;
        }

        private static T GetOrAdd<T>(GameObject go) where T : Component
        {
            var component = go.GetComponent<T>();
            return component != null ? component : go.AddComponent<T>();
        }
    }
}
