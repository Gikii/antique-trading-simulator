// Place this file inside an "Editor" folder anywhere under Assets
// (e.g. Assets/Editor/InfoBarBuilder.cs). It must NOT be in a regular
// script folder, since it references UnityEditor and would break builds
// otherwise.

using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AntiqueTradingSimulator.UI;

namespace AntiqueTradingSimulator.EditorTools
{
    /// <summary>
    /// Builds the bottom InfoBar (Transport / Warehouse / News / Events) entirely
    /// through Unity's UI API, instead of hand-edited prefab YAML — Unity itself
    /// generates all the serialization, so there's no risk of a malformed
    /// prefab. Run it once from the menu with the target scene open; it's safe
    /// to re-run, it replaces any InfoBar it previously built.
    ///
    /// What it creates:
    ///   Canvas
    ///     InfoBar                          (root strip, anchored to the bottom
    ///                                        159px gap already reserved below
    ///                                        HUD/Views/MainNavigation)
    ///       TransportTab / WarehouseTab / NewsTab / EventsTab
    ///         Header                       (Title on the left, button on the
    ///                                        right — InfoBarTabUI)
    ///         Content                      (empty on 3 tabs; on EventsTab,
    ///                                        InfoBarEventsUI fills it with the
    ///                                        3 soonest scheduled events)
    ///
    /// It also saves InfoBar.prefab and UpcomingEventListItem.prefab under
    /// Assets/_Project/Prefabs/UI/InfoBar/, matching how the rest of the
    /// project's UI prefabs are organized.
    ///
    /// Left for you to do afterwards in the Inspector:
    ///   - Wire each tab's ActionButton.onClick (Warehouse/News/Events map
    ///     directly to ViewManager.ShowWarehouse/ShowNews/ShowEvents; there's
    ///     no ViewType for Transport yet, so that one's left unwired).
    ///   - Swap the placeholder colors/icons for real art.
    /// </summary>
    public static class InfoBarBuilder
    {
        private const string PrefabFolder = "Assets/_Project/Prefabs/UI/InfoBar";
        private const string InfoBarPrefabPath = PrefabFolder + "/InfoBar.prefab";
        private const string ListItemPrefabPath = PrefabFolder + "/UpcomingEventListItem.prefab";

        // The TMP font already used throughout the project's UI (see any TMP
        // text in Main.unity) — used here instead of TMP's default font so the
        // new bar matches the rest of the game.
        private const string ProjectFontGuid = "8f586378b4e144a9851e7b34d9b748ee";

        private const float BarHeight = 159f; // exact height of the reserved gap below Views
        private const float HeaderHeight = 32f;
        private const float ButtonSize = 28f;

        private static readonly Color RootBackground = new Color(0.12f, 0.12f, 0.12f, 0.55f);
        private static readonly Color TabBackground = new Color(0.4f, 0.4f, 0.4f, 0.6f);
        private static readonly Color ButtonBackground = new Color(1f, 1f, 1f, 0.15f);
        private static readonly Color ListItemBackground = new Color(0.62f, 0.44f, 0.18f, 0.85f);

        private static readonly string[] TabNames =
        {
            "TransportTab", "WarehouseTab", "NewsTab", "EventsTab"
        };

        private static readonly string[] TabTitles =
        {
            "TRANSPORTS", "WAREHOUSE", "LATEST NEWS", "UPCOMING EVENTS"
        };

        [MenuItem("Tools/Antique Trading Simulator/Build Info Bar")]
        public static void Build()
        {
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            if (canvas == null)
            {
                Debug.LogError("InfoBarBuilder: no Canvas found in the open scene. Open Main.unity and try again.");
                return;
            }

            // Re-runnable: drop any previous build first.
            Transform existing = canvas.transform.Find("InfoBar");
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            EnsureFolder(PrefabFolder);

            TMP_FontAsset font = LoadProjectFont();
            GameObject listItemPrefab = BuildListItemPrefab(font);
            GameObject bar = BuildBar(canvas.transform, font, listItemPrefab);

            // Save it as a reusable prefab too, the same way the rest of
            // Assets/_Project/Prefabs/UI is organized, keeping the scene
            // instance connected to the new asset.
            PrefabUtility.SaveAsPrefabAssetAndConnect(bar, InfoBarPrefabPath, InteractionMode.AutomatedAction);

            Undo.RegisterCreatedObjectUndo(bar, "Create InfoBar");
            EditorSceneManager.MarkSceneDirty(bar.scene);
            AssetDatabase.SaveAssets();

            Selection.activeGameObject = bar;
            Debug.Log($"InfoBarBuilder: built InfoBar under '{canvas.name}' and saved it to {InfoBarPrefabPath}.");
        }

        [MenuItem("Tools/Antique Trading Simulator/Remove Info Bar")]
        public static void Remove()
        {
            Canvas canvas = Object.FindFirstObjectByType<Canvas>();
            Transform existing = canvas != null ? canvas.transform.Find("InfoBar") : null;

            if (existing == null)
            {
                Debug.Log("InfoBarBuilder: no InfoBar in the open scene.");
                return;
            }

            Undo.DestroyObjectImmediate(existing.gameObject);
            EditorSceneManager.MarkSceneDirty(canvas.gameObject.scene);
        }

        // ------------------------------------------------------------------
        // Hierarchy construction
        // ------------------------------------------------------------------

        private static GameObject BuildBar(Transform canvasTransform, TMP_FontAsset font, GameObject listItemPrefab)
        {
            GameObject bar = CreateUIObject("InfoBar", canvasTransform);

            RectTransform rt = bar.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = new Vector2(0f, BarHeight);

            AddImage(bar, RootBackground);

            HorizontalLayoutGroup layout = bar.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(8, 8, 8, 8);
            layout.spacing = 8f;
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = true;
            layout.childControlWidth = true;
            layout.childControlHeight = true;

            for (int i = 0; i < TabNames.Length; i++)
            {
                bool isEventsTab = TabNames[i] == "EventsTab";
                BuildTab(bar.transform, TabNames[i], TabTitles[i], font, isEventsTab, listItemPrefab);
            }

            return bar;
        }

        private static void BuildTab(
            Transform parent,
            string name,
            string title,
            TMP_FontAsset font,
            bool isEventsTab,
            GameObject listItemPrefab)
        {
            GameObject tab = CreateUIObject(name, parent);
            AddImage(tab, TabBackground);

            VerticalLayoutGroup tabLayout = tab.AddComponent<VerticalLayoutGroup>();
            tabLayout.padding = new RectOffset(10, 10, 8, 8);
            tabLayout.spacing = 8f;
            tabLayout.childAlignment = TextAnchor.UpperLeft;
            tabLayout.childForceExpandWidth = true;
            tabLayout.childForceExpandHeight = false;
            tabLayout.childControlWidth = true;
            tabLayout.childControlHeight = true;

            // --- Header: title on the left, button on the right ---
            GameObject header = CreateUIObject("Header", tab.transform);
            AddLayoutElement(header, preferredHeight: HeaderHeight, flexibleWidth: -1, flexibleHeight: 0f);

            HorizontalLayoutGroup headerLayout = header.AddComponent<HorizontalLayoutGroup>();
            headerLayout.padding = new RectOffset(0, 0, 0, 0);
            headerLayout.spacing = 8f;
            headerLayout.childAlignment = TextAnchor.MiddleLeft;
            headerLayout.childForceExpandWidth = false;
            headerLayout.childForceExpandHeight = false;
            headerLayout.childControlWidth = true;
            headerLayout.childControlHeight = true;

            GameObject titleGO = CreateUIObject("TitleText", header.transform);
            AddLayoutElement(titleGO, flexibleWidth: 1f, flexibleHeight: 0f);
            TMP_Text titleText = AddTMPText(titleGO, title, 16f, TextAlignmentOptions.Left, font);

            GameObject actionButtonGO = CreateUIObject("ActionButton", header.transform);
            AddLayoutElement(actionButtonGO, preferredWidth: ButtonSize, preferredHeight: ButtonSize, flexibleWidth: 0f, flexibleHeight: 0f);
            Image buttonImage = AddImage(actionButtonGO, ButtonBackground);
            Button button = actionButtonGO.AddComponent<Button>();
            button.targetGraphic = buttonImage;

            // --- Content: empty on 3 tabs, filled by InfoBarEventsUI on EventsTab ---
            GameObject content = CreateUIObject("Content", tab.transform);
            AddLayoutElement(content, flexibleWidth: -1, flexibleHeight: 1f);

            VerticalLayoutGroup contentLayout = content.AddComponent<VerticalLayoutGroup>();
            contentLayout.padding = new RectOffset(0, 0, 4, 0);
            contentLayout.spacing = 4f;
            contentLayout.childAlignment = TextAnchor.UpperLeft;
            contentLayout.childForceExpandWidth = true;
            contentLayout.childForceExpandHeight = false;
            contentLayout.childControlWidth = true;
            contentLayout.childControlHeight = true;

            RectTransform contentRT = content.GetComponent<RectTransform>();

            InfoBarTabUI tabUi = tab.AddComponent<InfoBarTabUI>();
            SerializedObject tabSo = new SerializedObject(tabUi);
            tabSo.FindProperty("titleText").objectReferenceValue = titleText;
            tabSo.FindProperty("actionButton").objectReferenceValue = button;
            tabSo.FindProperty("contentContainer").objectReferenceValue = contentRT;
            tabSo.FindProperty("title").stringValue = title;
            tabSo.ApplyModifiedPropertiesWithoutUndo();

            if (isEventsTab)
            {
                InfoBarEventsUI events = tab.AddComponent<InfoBarEventsUI>();
                SerializedObject eventsSo = new SerializedObject(events);
                eventsSo.FindProperty("listContainer").objectReferenceValue = contentRT;
                eventsSo.FindProperty("listItemPrefab").objectReferenceValue = listItemPrefab;
                eventsSo.FindProperty("maxEvents").intValue = 3;
                eventsSo.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        private static GameObject BuildListItemPrefab(TMP_FontAsset font)
        {
            GameObject root = CreateUIObject("UpcomingEventListItem", null);
            AddLayoutElement(root, preferredHeight: 24f, flexibleWidth: 1f, flexibleHeight: 0f);
            AddImage(root, ListItemBackground);

            // Text goes on its own child — Image and TextMeshProUGUI are both
            // Graphic components, and Unity only allows one Graphic per GameObject.
            GameObject textGO = CreateUIObject("NameText", root.transform);
            RectTransform textRT = textGO.GetComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = Vector2.zero;
            textRT.offsetMax = Vector2.zero;

            TMP_Text nameText = AddTMPText(textGO, "Event name", 14f, TextAlignmentOptions.Left, font);
            nameText.margin = new Vector4(8f, 0f, 8f, 0f); // keep text off the background's edges

            UpcomingEventListItemUI ui = root.AddComponent<UpcomingEventListItemUI>();
            SerializedObject so = new SerializedObject(ui);
            so.FindProperty("nameText").objectReferenceValue = nameText;
            so.ApplyModifiedPropertiesWithoutUndo();

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, ListItemPrefabPath);
            Object.DestroyImmediate(root);
            return prefab;
        }

        // ------------------------------------------------------------------
        // Small UI-building helpers
        // ------------------------------------------------------------------

        private static GameObject CreateUIObject(string name, Transform parent)
        {
            GameObject go = new GameObject(name, typeof(RectTransform));
            go.layer = LayerMask.NameToLayer("UI");

            if (parent != null)
                go.transform.SetParent(parent, false);

            return go;
        }

        private static Image AddImage(GameObject go, Color color)
        {
            Image image = go.AddComponent<Image>();
            image.color = color;
            return image;
        }

        private static TMP_Text AddTMPText(GameObject go, string text, float fontSize, TextAlignmentOptions alignment, TMP_FontAsset font)
        {
            TextMeshProUGUI tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.alignment = alignment;
            tmp.color = Color.white;

            if (font != null)
                tmp.font = font;

            return tmp;
        }

        private static LayoutElement AddLayoutElement(
            GameObject go,
            float preferredWidth = -1f,
            float preferredHeight = -1f,
            float flexibleWidth = -1f,
            float flexibleHeight = -1f)
        {
            LayoutElement le = go.AddComponent<LayoutElement>();
            le.preferredWidth = preferredWidth;
            le.preferredHeight = preferredHeight;
            le.flexibleWidth = flexibleWidth;
            le.flexibleHeight = flexibleHeight;
            return le;
        }

        private static TMP_FontAsset LoadProjectFont()
        {
            string path = AssetDatabase.GUIDToAssetPath(ProjectFontGuid);

            if (string.IsNullOrEmpty(path))
            {
                Debug.LogWarning("InfoBarBuilder: could not find the project's TMP font asset — falling back to TMP's default font.");
                return null;
            }

            return AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(path);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
                return;

            string parent = System.IO.Path.GetDirectoryName(path)?.Replace('\\', '/');
            string folderName = System.IO.Path.GetFileName(path);

            if (string.IsNullOrEmpty(parent))
                return;

            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
