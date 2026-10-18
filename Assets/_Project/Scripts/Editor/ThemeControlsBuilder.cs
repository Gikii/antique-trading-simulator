using System.Collections.Generic;
using AntiqueTradingSimulator.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.EditorTools
{
    /// <summary>
    /// Second theming pass (after Apply Theme Panels): restyles the controls of the screens that are
    /// always visible plus the Market view —
    ///  - HUD: time-control buttons, separators, text colours; the placeholder logo block is hidden
    ///  - MainNavigation: framed buttons, active one highlighted at runtime by ViewManager
    ///  - MarketView: filter dropdowns (incl. their option lists), buttons, text colours,
    ///    transaction-history row colours
    ///  - ListingCard.prefab: card frame, "New!" badge, favourite toggle, details button, texts
    ///  - InfoBar.prefab / InfoBarRow.prefab: header buttons, row frames, texts
    /// Text colours are only replaced when they still have a known greybox colour (black, dark grey,
    /// light blue, violet, plain white/light grey), so colours chosen on purpose — gold date,
    /// green cash — are kept. Run Apply Theme Panels first (it creates UITheme). Safe to re-run.
    /// </summary>
    public static class ThemeControlsBuilder
    {
        private const string MenuPath = "Tools/Antique Trading Simulator/UI/Apply Theme Controls";

        private const string ThemePath = "Assets/_Project/Data/Resources/" + UITheme.ResourcePath + ".asset";
        private const string ListingCardPrefab = "Assets/_Project/Prefabs/UI/ListingCard.prefab";
        private const string InfoBarPrefab = "Assets/_Project/Prefabs/UI/InfoBar/InfoBar.prefab";
        private const string InfoBarRowPrefab = "Assets/_Project/Prefabs/UI/InfoBar/InfoBarRow.prefab";

        private const string SceneName = "Main";
        private const string CanvasName = "Canvas";

        private static readonly Color BadgeTextColor = new Color32(0x8C, 0xD2, 0x78, 0xFF);
        private static readonly Color HistoryRowColor = new Color32(0x16, 0x12, 0x0B, 0xFF);
        /// <summary>Height of the card's name for two lines at its 14 pt size.</summary>
        private const float NameTwoLineHeight = 36f;

        private static readonly Color OptionColor = new Color32(0x1A, 0x15, 0x0D, 0xFF);

        // Navigation button names → the view they open (ViewManager.ViewEntry.type names).
        private static readonly Dictionary<string, string> NavButtonsByView = new Dictionary<string, string>
        {
            { "Market", "MarketButton" },
            { "Inventory", "InventoryButton" },
            { "Auctions", "AuctionsButton" },
            { "Contracts", "ContractsButton" },
            { "NewsEvents", "NewsEventsButton" },
            { "Company", "CompanyButton" },
        };

        private enum TextRole { Keep, Primary, Muted, Positive }

        [MenuItem(MenuPath)]
        public static void Run()
        {
            var theme = AssetDatabase.LoadAssetAtPath<UITheme>(ThemePath);
            if (theme == null || theme.Button == null)
            {
                Debug.LogError("[ThemeControlsBuilder] UITheme not found or incomplete — run 'Apply Theme Panels' first.");
                return;
            }

            ApplyToScene(theme);
            ApplyToListingCard(theme);
            ApplyToInfoBar(theme);
            ApplyToInfoBarRow(theme);

            Debug.Log("[ThemeControlsBuilder] Done. Save the scene (Ctrl+S).");
        }

        // ------------------------------------------------------------------
        // Scene
        // ------------------------------------------------------------------

        private static void ApplyToScene(UITheme theme)
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name != SceneName)
            {
                Debug.LogWarning($"[ThemeControlsBuilder] Open the {SceneName} scene to restyle HUD, navigation and Market (prefabs are still updated).");
                return;
            }

            Transform canvas = null;
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == CanvasName) { canvas = go.transform; break; }

            if (canvas == null)
            {
                Debug.LogError($"[ThemeControlsBuilder] No root '{CanvasName}' in {SceneName}.");
                return;
            }

            // HUD
            Transform hud = canvas.Find("HUD");
            if (hud != null)
            {
                foreach (var button in hud.GetComponentsInChildren<Button>(true))
                    StyleButton(button, theme.Button, theme, true);

                foreach (Transform child in hud)
                {
                    if (child.name != "Separator") continue;
                    var separator = child.GetComponent<Image>();
                    if (separator == null) continue;
                    Undo.RecordObject(separator, "Theme separator");
                    separator.color = theme.Edge;
                }

                // Placeholder logo block — hidden until the real logo exists.
                var logo = hud.Find("LogoContainer/Logo");
                var logoImage = logo != null ? logo.GetComponent<Image>() : null;
                if (logoImage != null)
                {
                    Undo.RecordObject(logoImage, "Hide logo placeholder");
                    logoImage.color = new Color(0f, 0f, 0f, 0f);
                }

                StyleTexts(hud, theme, true);
            }
            else Debug.LogWarning("[ThemeControlsBuilder] Canvas/HUD not found.");

            // Navigation
            Transform nav = canvas.Find("MainNavigation");
            if (nav != null)
            {
                foreach (var button in nav.GetComponentsInChildren<Button>(true))
                    StyleButton(button, theme.NavItem, theme, true);
                StyleTexts(nav, theme, true);
                AssignNavButtons(canvas, nav);
            }
            else Debug.LogWarning("[ThemeControlsBuilder] Canvas/MainNavigation not found.");

            // Market view
            Transform market = canvas.Find("Views/MarketView");
            if (market != null)
            {
                var dropdownTemplates = new HashSet<Transform>();

                foreach (var dropdown in market.GetComponentsInChildren<MultiSelectDropdown>(true))
                {
                    var so = new SerializedObject(dropdown);
                    var template = so.FindProperty("template").objectReferenceValue as RectTransform;
                    StyleDropdown(dropdown.GetComponent<Image>(), dropdown.transform, template, theme, true);
                    if (template != null) dropdownTemplates.Add(template);
                }

                foreach (var dropdown in market.GetComponentsInChildren<TMP_Dropdown>(true))
                {
                    StyleDropdown(dropdown.GetComponent<Image>(), dropdown.transform, dropdown.template, theme, true);
                    if (dropdown.template != null) dropdownTemplates.Add(dropdown.template);
                }

                foreach (var button in market.GetComponentsInChildren<Button>(true))
                {
                    if (IsInside(button.transform, dropdownTemplates)) continue;
                    if (button.GetComponent<MultiSelectDropdown>() != null) continue;
                    bool primary = button.name.Contains("Buy");
                    StyleButton(button, primary ? theme.ButtonPrimary : theme.Button, theme, true);
                }

                foreach (var history in market.GetComponentsInChildren<AntiqueTradingSimulator.UI.TransactionHistoryUI>(true))
                {
                    var so = new SerializedObject(history);
                    so.FindProperty("rowBackgroundColor").colorValue = HistoryRowColor;
                    so.FindProperty("rowTextColor").colorValue = theme.TextMuted;
                    so.ApplyModifiedProperties();
                }

                StyleTexts(market, theme, true);
            }
            else Debug.LogWarning("[ThemeControlsBuilder] Canvas/Views/MarketView not found.");

            EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>Links each ViewManager entry to its navigation button, so the active one gets highlighted.</summary>
        private static void AssignNavButtons(Transform canvas, Transform nav)
        {
            var manager = Object.FindFirstObjectByType<ViewManager>(FindObjectsInactive.Include);
            if (manager == null)
            {
                Debug.LogWarning("[ThemeControlsBuilder] No ViewManager in the scene — navigation highlight not wired.");
                return;
            }

            var so = new SerializedObject(manager);
            var views = so.FindProperty("views");
            for (int i = 0; i < views.arraySize; i++)
            {
                var entry = views.GetArrayElementAtIndex(i);
                // intValue is the stored enum value itself (enumValueIndex can be out of range
                // for values the inspector doesn't list).
                var typeProp = entry.FindPropertyRelative("type");
                string typeName = ((ViewType)typeProp.intValue).ToString();
                if (!NavButtonsByView.TryGetValue(typeName, out var buttonName)) continue;

                var buttonTransform = nav.Find(buttonName);
                var button = buttonTransform != null ? buttonTransform.GetComponent<Button>() : null;
                if (button == null)
                {
                    Debug.LogWarning($"[ThemeControlsBuilder] MainNavigation/{buttonName} not found — {typeName} won't be highlighted.");
                    continue;
                }

                entry.FindPropertyRelative("navButton").objectReferenceValue = button;
            }
            so.ApplyModifiedProperties();
        }

        // ------------------------------------------------------------------
        // Prefabs
        // ------------------------------------------------------------------

        private static void ApplyToListingCard(UITheme theme)
        {
            EditPrefab(ListingCardPrefab, root =>
            {
                SetSliced(root.GetComponent<Image>(), theme.Inset, false);

                var badge = root.transform.Find("Layout/HeaderRow/NewBadge");
                if (badge != null)
                {
                    SetSliced(badge.GetComponent<Image>(), theme.BadgeGreen, false);
                    foreach (var text in badge.GetComponentsInChildren<TMP_Text>(true))
                        text.color = BadgeTextColor;
                }

                var favourite = root.transform.Find("Layout/HeaderRow/FavouriteButton");
                if (favourite != null)
                {
                    var button = favourite.GetComponent<Selectable>();
                    if (button != null) StyleButton(button, theme.Input, theme, false);
                    else SetSliced(favourite.GetComponent<Image>(), theme.Input, false);
                    var icon = favourite.Find("Icon");
                    var iconImage = icon != null ? icon.GetComponent<Image>() : null;
                    if (iconImage != null) iconImage.color = new Color(0f, 0f, 0f, 0f);
                }

                var details = root.transform.Find("Layout/ShowDetailsButton");
                var detailsButton = details != null ? details.GetComponent<Button>() : null;
                if (detailsButton != null) StyleButton(detailsButton, theme.Button, theme, false);

                // Name and price get their roles directly; the rest goes through the colour map.
                StyleTexts(root.transform, theme, false, badge);
                foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (text.name == "NameText")
                    {
                        text.color = theme.TextPrimary;
                        // Long names wrap to a second line instead of being cut off mid-word.
                        text.textWrappingMode = TextWrappingModes.Normal;
                        text.overflowMode = TextOverflowModes.Ellipsis;
                        FitInfoColumn(text);
                    }
                    else if (text.name == "PriceText") text.color = theme.Positive;
                }
            });
        }

        /// <summary>
        /// The card's info column has a fixed height that its texts used to overfill, so the layout
        /// squeezed every text — the name down to a single line. The name now gets a guaranteed two
        /// lines and the remaining height is split evenly between the other texts of the column.
        /// </summary>
        private static void FitInfoColumn(TMP_Text nameText)
        {
            var column = nameText.transform.parent;
            var columnLayout = column != null ? column.GetComponent<LayoutElement>() : null;
            var group = column != null ? column.GetComponent<VerticalLayoutGroup>() : null;

            var nameLayout = nameText.GetComponent<LayoutElement>();
            if (nameLayout == null) nameLayout = nameText.gameObject.AddComponent<LayoutElement>();
            nameLayout.minHeight = NameTwoLineHeight;
            nameLayout.preferredHeight = NameTwoLineHeight;

            if (columnLayout == null || group == null || columnLayout.preferredHeight <= 0f)
                return;

            var others = new List<LayoutElement>();
            foreach (Transform child in column)
            {
                if (child == nameText.transform || !child.gameObject.activeSelf) continue;
                var element = child.GetComponent<LayoutElement>();
                if (element == null || element.ignoreLayout) continue;
                others.Add(element);
            }
            if (others.Count == 0)
                return;

            float spacing = group.spacing * others.Count; // gaps between all (others + name) children
            float padding = group.padding.top + group.padding.bottom;
            float each = Mathf.Floor((columnLayout.preferredHeight - padding - spacing - NameTwoLineHeight) / others.Count);
            foreach (var element in others)
            {
                element.minHeight = -1f;
                element.preferredHeight = Mathf.Max(each, 14f);
            }
        }

        private static void ApplyToInfoBar(UITheme theme)
        {
            EditPrefab(InfoBarPrefab, root =>
            {
                foreach (var button in root.GetComponentsInChildren<Button>(true))
                    StyleButton(button, theme.Button, theme, false);
                StyleTexts(root.transform, theme, false);
            });
        }

        private static void ApplyToInfoBarRow(UITheme theme)
        {
            EditPrefab(InfoBarRowPrefab, root =>
            {
                SetSliced(root.GetComponent<Image>(), theme.Inset, false);
                StyleTexts(root.transform, theme, false);
            });
        }

        private static void EditPrefab(string path, System.Action<GameObject> edit)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null)
            {
                Debug.LogError($"[ThemeControlsBuilder] Prefab not found: {path}");
                return;
            }

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                edit(root);
                PrefabUtility.SaveAsPrefabAsset(root, path);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        // ------------------------------------------------------------------
        // Element styling
        // ------------------------------------------------------------------

        private static void StyleButton(Selectable button, Sprite sprite, UITheme theme, bool useUndo)
        {
            if (button == null) return;

            if (button.targetGraphic is Image image)
                SetSliced(image, sprite, useUndo);

            if (useUndo) Undo.RecordObject(button, "Theme button");
            // Sprites carry the colour; the tint only brightens on hover and darkens on press.
            var colors = button.colors;
            colors.normalColor = new Color(0.88f, 0.88f, 0.88f, 1f);
            colors.highlightedColor = Color.white;
            colors.pressedColor = new Color(0.7f, 0.7f, 0.7f, 1f);
            colors.selectedColor = new Color(0.88f, 0.88f, 0.88f, 1f);
            colors.disabledColor = new Color(0.55f, 0.55f, 0.55f, 0.6f);
            colors.colorMultiplier = 1f;
            button.colors = colors;
            if (!useUndo) EditorUtility.SetDirty(button);
        }

        private static void StyleDropdown(Image field, Transform root, RectTransform template, UITheme theme, bool useUndo)
        {
            if (field != null)
                SetSliced(field, theme.Input, useUndo);

            var arrow = root.Find("Arrow");
            var arrowImage = arrow != null ? arrow.GetComponent<Image>() : null;
            if (arrowImage != null)
            {
                if (useUndo) Undo.RecordObject(arrowImage, "Theme dropdown arrow");
                arrowImage.color = theme.Accent;
            }

            if (template == null)
                return;

            SetSliced(template.GetComponent<Image>(), theme.Panel, useUndo);

            // Option rows (the template item and any clones): dark rows that light up on hover,
            // gold check marks.
            foreach (var toggle in template.GetComponentsInChildren<Toggle>(true))
            {
                if (toggle.targetGraphic is Image background)
                {
                    if (useUndo) Undo.RecordObject(background, "Theme dropdown option");
                    background.sprite = null;
                    background.type = Image.Type.Simple;
                    background.color = OptionColor;
                }

                if (toggle.graphic != null)
                {
                    if (useUndo) Undo.RecordObject(toggle.graphic, "Theme dropdown check");
                    toggle.graphic.color = theme.Accent;
                }

                if (useUndo) Undo.RecordObject(toggle, "Theme dropdown option");
                var colors = toggle.colors;
                colors.normalColor = Color.white;
                colors.highlightedColor = new Color(1.8f, 1.8f, 1.8f, 1f);
                colors.pressedColor = new Color(2.2f, 2.2f, 2.2f, 1f);
                colors.selectedColor = Color.white;
                colors.colorMultiplier = 1f;
                toggle.colors = colors;
            }

            // Scrollbar track of the option list, if any.
            foreach (var scrollbar in template.GetComponentsInChildren<Scrollbar>(true))
            {
                var track = scrollbar.GetComponent<Image>();
                if (track != null)
                {
                    if (useUndo) Undo.RecordObject(track, "Theme scrollbar");
                    track.sprite = null;
                    track.color = theme.ScreenBase;
                }
                if (scrollbar.targetGraphic is Image handle)
                {
                    if (useUndo) Undo.RecordObject(handle, "Theme scrollbar");
                    handle.color = theme.Edge;
                }
            }
        }

        private static void SetSliced(Image image, Sprite sprite, bool useUndo)
        {
            if (image == null || sprite == null) return;
            if (useUndo) Undo.RecordObject(image, "Theme frame");
            image.sprite = sprite;
            image.type = Image.Type.Sliced;
            image.fillCenter = true;
            image.pixelsPerUnitMultiplier = 1f;
            image.preserveAspect = false;
            image.color = Color.white;
            if (!useUndo) EditorUtility.SetDirty(image);
        }

        /// <summary>Maps the known greybox text colours to theme roles; everything else is left alone.</summary>
        private static void StyleTexts(Transform root, UITheme theme, bool useUndo, Transform skip = null)
        {
            foreach (var text in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (skip != null && text.transform.IsChildOf(skip)) continue;

                Color color;
                switch (RoleOf(text.color))
                {
                    case TextRole.Primary: color = theme.TextPrimary; break;
                    case TextRole.Muted: color = theme.TextMuted; break;
                    case TextRole.Positive: color = theme.Positive; break;
                    default: continue;
                }

                if (useUndo) Undo.RecordObject(text, "Theme text");
                color.a = text.color.a;
                text.color = color;
                if (!useUndo) EditorUtility.SetDirty(text);
            }
        }

        private static TextRole RoleOf(Color c)
        {
            if (Near(c, 0x00, 0x00, 0x00) || Near(c, 0x32, 0x32, 0x32) || Near(c, 0xFF, 0xFF, 0xFF)) return TextRole.Primary;
            if (Near(c, 0xB0, 0xE0, 0xE6) || Near(c, 0xDD, 0xDD, 0xDD) || Near(c, 0xB8, 0xB8, 0xB8)) return TextRole.Muted;
            if (Near(c, 0xEE, 0x82, 0xEE)) return TextRole.Positive;
            return TextRole.Keep;
        }

        private static bool Near(Color c, int r, int g, int b)
        {
            const float tolerance = 3f / 255f;
            return Mathf.Abs(c.r - r / 255f) <= tolerance
                && Mathf.Abs(c.g - g / 255f) <= tolerance
                && Mathf.Abs(c.b - b / 255f) <= tolerance;
        }

        private static bool IsInside(Transform t, HashSet<Transform> roots)
        {
            foreach (var root in roots)
                if (root != null && t.IsChildOf(root)) return true;
            return false;
        }
    }
}
