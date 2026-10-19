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
        /// <summary>Extra room under the card's two-line name.</summary>
        private const float NameExtraHeight = 2f;

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

        /// <summary>Views restyled by the generic pass (under Canvas).</summary>
        private static readonly string[] GenericViews = { "Views/CompanyView", "Views/InventoryView", "Views/ContractsView", "Views/NewsEventsView", "Modals", "GameMenu" };

        /// <summary>List-row prefabs: flat dark row, warm dark gold when selected, themed texts.</summary>
        private static readonly string[] RowPrefabs =
        {
            "Assets/_Project/Prefabs/UI/Inventory/InventoryListItem.prefab",
            "Assets/_Project/Prefabs/UI/Contracts/ContractFulfillListItem.prefab",
            "Assets/_Project/Prefabs/UI/Contracts/ContractListItem.prefab",
            "Assets/_Project/Prefabs/UI/NewsEvents/NewsListItem.prefab",
            "Assets/_Project/Prefabs/UI/NewsEvents/ActiveEventListItem.prefab",
        };

        /// <summary>Other colour fields of scripts, set by name (calendar cells and day numbers).</summary>
        private static readonly Dictionary<string, Color> NamedColorFields = new Dictionary<string, Color>
        {
            { "cellNormal", new Color32(0x1A, 0x15, 0x0E, 0xFF) },
            { "cellSelected", new Color32(0x2E, 0x24, 0x14, 0xFF) },
            { "cellToday", new Color32(0x4A, 0x35, 0x18, 0xFF) },
            { "textCurrentMonth", new Color32(0xEA, 0xDF, 0xC8, 0xFF) },
            { "textOtherMonth", new Color32(0x6E, 0x60, 0x4A, 0xFF) },
            { "emptyColor", new Color32(0x2A, 0x21, 0x16, 0xFF) }, // donut ring with no data
        };

        /// <summary>Buttons whose name contains one of these confirm the main action (green frame).</summary>
        private static readonly string[] PrimaryButtonNames = { "Confirm", "Create", "Buy", "Accept", "Fulfill" };
        /// <summary>Option boxes that cannot be picked (e.g. a workshop the player can't use yet).</summary>
        private static readonly Color UnavailableColor = new Color32(0x0F, 0x0C, 0x08, 0xFF);

        /// <summary>Serialized Button arrays that are tab bars (switched at runtime via UITheme.ApplyTabState).</summary>
        private static readonly string[] TabArrayFields = { "tabButtons", "periodButtons" };

        /// <summary>Serialized Button fields that are tabs.</summary>
        private static readonly string[] TabButtonFields = { "collectionTabButton", "transportTabButton", "renovationTabButton", "forSaleTabButton",
                                                                  "availableTabButton", "myContractsTabButton",
                                                                  "newsTabButton", "activeEventsTabButton",
                                                                  "allFilterButton", "officialFilterButton", "rumoursFilterButton", "leakedFilterButton" };

        /// <summary>Selected card, player row, current tier — replaces the bright greybox gold.</summary>
        private static readonly Color HighlightColor = new Color32(0x4A, 0x35, 0x18, 0xFF);
        private static readonly string[] HighlightColorFields = { "selectedColor", "playerColor", "itemSelected" };
        private static readonly string[] NormalColorFields = { "normalColor", "itemNormal" };
        private const float TabLabelSize = 17f;
        private const float SelectionOverlayAlpha = 0.14f;
        private static readonly Color StatusPillColor = new Color32(0x2F, 0x4A, 0x2A, 0xFF);
        private static readonly Color ModalBackdropColor = new Color(0f, 0f, 0f, 0.7f);

        /// <summary>Flat colour of list rows / cards whose background a script recolours at runtime.</summary>
        private static readonly Color RowColor = new Color32(0x17, 0x13, 0x0C, 0xFF);

        [MenuItem(MenuPath)]
        public static void Run()
        {
            var theme = AssetDatabase.LoadAssetAtPath<UITheme>(ThemePath);
            if (theme != null && theme.ButtonDanger == null)
            {
                theme.ButtonDanger = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/_Project/Art/UI/Frames/ui_button_danger.png");
                EditorUtility.SetDirty(theme);
            }
            if (theme == null || theme.Button == null)
            {
                Debug.LogError("[ThemeControlsBuilder] UITheme not found or incomplete — run 'Apply Theme Panels' first.");
                return;
            }

            ApplyToScene(theme);
            ApplyToListingCard(theme);
            ApplyToInfoBar(theme);
            ApplyToInfoBarRow(theme);
            foreach (var path in RowPrefabs)
                ApplyToRowPrefab(path, theme);

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
                StyleAntiqueDetails(market, theme);
            }
            else Debug.LogWarning("[ThemeControlsBuilder] Canvas/Views/MarketView not found.");

            // Other views: the generic pass (frames by greybox colour, buttons, tabs, dropdowns, texts).
            foreach (var viewPath in GenericViews)
            {
                Transform view = canvas.Find(viewPath);
                if (view != null) ApplyToView(view, theme);
                else Debug.LogWarning($"[ThemeControlsBuilder] Canvas/{viewPath} not found.");
            }
            RestoreModalBackdrops(canvas);

            EditorSceneManager.MarkSceneDirty(scene);
        }

        /// <summary>
        /// Side panel of the Market: the description and history texts get the free height of
        /// their sections (they were one-line boxes and spilled over the next section), and the
        /// white placeholders (chart background, seller avatar) take theme colours.
        /// </summary>
        private static void StyleAntiqueDetails(Transform market, UITheme theme)
        {
            const string details = "MainArea/SidePanel/AntiqueDetails/";

            foreach (var path in new[] { "DetailsSection/DetailsText", "HistorySection/HistoryText" })
            {
                var target = market.Find(details + path);
                var layout = target != null ? target.GetComponent<LayoutElement>() : null;
                if (layout == null) continue;
                Undo.RecordObject(layout, "Fit details text");
                layout.flexibleHeight = 1f;
            }

            var chart = market.Find(details + "MarketValueSection/MarketValueBody/PriceChart");
            if (chart != null) SetSliced(chart.GetComponent<Image>(), theme.Inset, true);

            var avatar = market.Find(details + "SellerSection/SellerRow/SellerAvatar");
            var avatarImage = avatar != null ? avatar.GetComponent<Image>() : null;
            if (avatarImage != null)
            {
                Undo.RecordObject(avatarImage, "Theme avatar placeholder");
                avatarImage.sprite = null;
                avatarImage.color = theme.Edge;
            }
        }

        // ------------------------------------------------------------------
        // Generic view pass
        // ------------------------------------------------------------------

        /// <summary>
        /// Restyles a whole view by what its greybox elements look like:
        ///  - translucent grey containers (#999999 36%) → panel frame, or inset frame when nested in one
        ///  - faint white boxes (white 9%) → inset frame
        ///  - white placeholders for icons / illustrations (white 25%, or plain white "Icon") → bronze block
        ///  - buttons → button frame, tab buttons → tab frame (the active one is set at runtime)
        ///  - dropdowns, input fields, scrollbars → their themed look
        ///  - rows / cards recoloured by script (fields "normalColor") → dark row colour
        ///  - texts → colour map
        /// </summary>
        private static void ApplyToView(Transform view, UITheme theme)
        {
            var tabs = CollectTabs(view);
            var templates = new HashSet<Transform>();

            foreach (var dropdown in view.GetComponentsInChildren<MultiSelectDropdown>(true))
            {
                var so = new SerializedObject(dropdown);
                var template = so.FindProperty("template").objectReferenceValue as RectTransform;
                StyleDropdown(dropdown.GetComponent<Image>(), dropdown.transform, template, theme, true);
                if (template != null) templates.Add(template);
            }

            foreach (var dropdown in view.GetComponentsInChildren<TMP_Dropdown>(true))
            {
                StyleDropdown(dropdown.GetComponent<Image>(), dropdown.transform, dropdown.template, theme, true);
                if (dropdown.template != null) templates.Add(dropdown.template);
            }

            foreach (var input in view.GetComponentsInChildren<TMP_InputField>(true))
                if (input.targetGraphic is Image field) SetSliced(field, theme.Input, true);

            foreach (var scrollbar in view.GetComponentsInChildren<Scrollbar>(true))
                StyleScrollbar(scrollbar, theme);

            var selectableGraphics = new HashSet<Graphic>();
            foreach (var selectable in view.GetComponentsInChildren<Selectable>(true))
                if (selectable.targetGraphic != null) selectableGraphics.Add(selectable.targetGraphic);

            foreach (var button in view.GetComponentsInChildren<Button>(true))
            {
                if (IsInside(button.transform, templates)) continue;
                bool isTab = tabs.Contains(button);
                StyleButton(button, isTab ? theme.Tab : ButtonSpriteFor(button, theme), theme, true);
                if (!isTab) continue;

                // Some views mark the selected tab by making it non-interactable: don't dim it.
                var tabColors = button.colors;
                tabColors.disabledColor = Color.white;
                button.colors = tabColors;

                // Tab labels at the size of panel text, not of headings.
                foreach (var label in button.GetComponentsInChildren<TMP_Text>(true))
                {
                    if (label.fontSize <= TabLabelSize) continue;
                    Undo.RecordObject(label, "Theme tab label");
                    label.fontSize = TabLabelSize;
                }
            }

            foreach (var image in view.GetComponentsInChildren<Image>(true))
            {
                if (selectableGraphics.Contains(image) || IsInside(image.transform, templates)) continue;
                if (image.GetComponent<AntiqueImageSlot>() != null || image.GetComponentInParent<AntiqueImageSlot>(true) != null) continue;
                if (image.sprite != null && image.sprite != BuiltinUISprite()) continue; // already framed / real art

                var c = image.color;

                // Greybox "buttons" that are only an Image (no Button component yet).
                if (image.name.EndsWith("Button") && image.GetComponent<Selectable>() == null)
                {
                    SetSliced(image, theme.Button, true);
                    continue;
                }
                if (image.name.EndsWith("Dropdown") && image.GetComponent<Selectable>() == null)
                {
                    SetSliced(image, theme.Input, true);
                    continue;
                }

                if (IsGrey(c, 0x99, 0.36f) || IsGrey(c, 0xB2, 0.18f) || IsGrey(c, 0xB2, 0.24f) || IsGrey(c, 0xC0, 0.11f))
                    SetSliced(image, HasFramedAncestor(image.transform, view, theme) ? theme.Inset : theme.Panel, true);
                else if (IsGrey(c, 0xFF, 0.09f) || IsGrey(c, 0xFF, 0.16f))
                    SetSliced(image, theme.Inset, true);
                else if (IsGrey(c, 0xFF, 0.06f) || IsGrey(c, 0x40, 1f) || IsGrey(c, 0x33, 1f))
                    SetSliced(image, theme.Inset, true);
                else if (image.name.Contains("Divider") && (IsGrey(c, 0xFF, 0.12f) || IsColor(c, 0x57, 0x4C, 0x38)))
                {
                    Undo.RecordObject(image, "Theme divider");
                    image.color = theme.Edge;
                }
                else if (IsGrey(c, 0xFF, 0.25f)
                         || image.name.Contains("Thumbnail") || image.name.Contains("HeroImage")
                         || (image.name.Contains("Icon") && (IsGrey(c, 0xFF, 1f) || IsGrey(c, 0x5C, 1f) || IsGrey(c, 0x66, 1f) || IsColor(c, 0x1F, 0x59, 0xC7))))
                {
                    Undo.RecordObject(image, "Theme placeholder");
                    image.sprite = null;
                    image.color = theme.Edge;
                }
            }

            // Rows and cards that a script recolours (normal vs selected / player).
            foreach (var behaviour in view.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                var so = new SerializedObject(behaviour);
                bool namedChanged = false;
                foreach (var pair in NamedColorFields)
                {
                    var prop = so.FindProperty(pair.Key);
                    if (prop == null || prop.propertyType != SerializedPropertyType.Color) continue;
                    prop.colorValue = pair.Value;
                    namedChanged = true;
                }
                if (namedChanged) so.ApplyModifiedProperties();

                // Tier segments: transparent normally, highlighted when current.
                var current = so.FindProperty("currentBackground");
                if (current != null && current.propertyType == SerializedPropertyType.Color)
                {
                    current.colorValue = HighlightColor;
                    so.ApplyModifiedProperties();
                }

                SerializedProperty normal = null;
                foreach (var field in NormalColorFields)
                {
                    var prop = so.FindProperty(field);
                    if (prop != null && prop.propertyType == SerializedPropertyType.Color) { normal = prop; break; }
                }
                var background = so.FindProperty("background");
                if (normal == null) continue;

                normal.colorValue = RowColor;
                var unavailable = so.FindProperty("unavailableColor");
                if (unavailable != null && unavailable.propertyType == SerializedPropertyType.Color)
                    unavailable.colorValue = UnavailableColor;
                // Selected / player / current: a warm dark gold that keeps gold and cream text readable.
                foreach (var field in HighlightColorFields)
                {
                    var highlight = so.FindProperty(field);
                    if (highlight != null && highlight.propertyType == SerializedPropertyType.Color)
                        highlight.colorValue = HighlightColor;
                }
                so.ApplyModifiedProperties();

                // Row / option / card scripts that leave "background" empty tint the Image on their
                // own object. Only for those: a modal script with an item colour (CreateEventModalUI)
                // sits on the modal root, whose Image is the dimmed backdrop and must stay as it is.
                var rowImage = background != null ? background.objectReferenceValue as Image : null;
                if (rowImage == null && IsRowScript(behaviour)) rowImage = behaviour.GetComponent<Image>();
                if (rowImage != null)
                {
                    Undo.RecordObject(rowImage, "Theme row");
                    rowImage.sprite = null;
                    rowImage.type = Image.Type.Simple;
                    rowImage.color = RowColor;
                }
            }

            StyleTexts(view, theme, true);
        }

        private static bool IsRowScript(MonoBehaviour behaviour)
        {
            string type = behaviour.GetType().Name;
            return type.EndsWith("OptionUI") || type.EndsWith("ItemUI") || type.EndsWith("RowUI") || type.EndsWith("CardUI");
        }

        /// <summary>
        /// Modal roots are the dimmed backdrop behind the window (black, 70%). Repairs a backdrop an
        /// earlier run of this tool painted over with the opaque row colour.
        /// </summary>
        private static void RestoreModalBackdrops(Transform canvas)
        {
            Transform modals = canvas.Find("Modals");
            if (modals == null) return;

            foreach (Transform modal in modals)
            {
                var image = modal.GetComponent<Image>();
                if (image == null || image.sprite != null || !IsColor(image.color, 0x17, 0x13, 0x0C)) continue;
                Undo.RecordObject(image, "Restore modal backdrop");
                image.color = ModalBackdropColor;
            }
        }

        private static HashSet<Selectable> CollectTabs(Transform view)
        {
            var tabs = new HashSet<Selectable>();
            foreach (var behaviour in view.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null) continue;
                var so = new SerializedObject(behaviour);

                foreach (var field in TabArrayFields)
                {
                    var array = so.FindProperty(field);
                    if (array == null || !array.isArray) continue;
                    for (int i = 0; i < array.arraySize; i++)
                        if (array.GetArrayElementAtIndex(i).objectReferenceValue is Selectable tab) tabs.Add(tab);
                }

                foreach (var field in TabButtonFields)
                {
                    var prop = so.FindProperty(field);
                    if (prop != null && prop.propertyType == SerializedPropertyType.ObjectReference
                        && prop.objectReferenceValue is Selectable tab)
                        tabs.Add(tab);
                }
            }
            // Buttons laid out in a "TabsRow" are tabs too, even without a script field.
            foreach (var button in view.GetComponentsInChildren<Button>(true))
                if (button.transform.parent != null && button.transform.parent.name == "TabsRow")
                    tabs.Add(button);

            return tabs;
        }

        private static void StyleScrollbar(Scrollbar scrollbar, UITheme theme)
        {
            var track = scrollbar.GetComponent<Image>();
            if (track != null)
            {
                Undo.RecordObject(track, "Theme scrollbar");
                track.sprite = null;
                track.color = theme.ScreenBase;
            }
            if (scrollbar.targetGraphic is Image handle)
            {
                Undo.RecordObject(handle, "Theme scrollbar");
                handle.color = theme.Edge;
            }
        }

        private static bool HasFramedAncestor(Transform t, Transform view, UITheme theme)
        {
            for (var p = t.parent; p != null && p != view; p = p.parent)
            {
                var image = p.GetComponent<Image>();
                if (image == null) continue;
                if (image.sprite == theme.Panel || image.sprite == theme.Inset || IsGrey(image.color, 0x99, 0.36f))
                    return true;
            }
            return false;
        }

        private static Sprite _builtinUISprite;

        /// <summary>Unity's default "UISprite" — greybox buttons and boxes use it, it isn't real art.</summary>
        private static Sprite BuiltinUISprite()
        {
            if (_builtinUISprite == null)
                _builtinUISprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
            return _builtinUISprite;
        }

        /// <summary>Green for the main confirm action, red where the greybox button was red, brown otherwise.</summary>
        private static Sprite ButtonSpriteFor(Button button, UITheme theme)
        {
            foreach (var name in PrimaryButtonNames)
                if (button.name.Contains(name))
                {
                    var c = button.targetGraphic != null ? button.targetGraphic.color : Color.white;
                    if (c.r > c.g * 1.5f && c.r > 0.2f && theme.ButtonDanger != null)
                        return theme.ButtonDanger; // e.g. "Sell now" — a confirm that loses value
                    return theme.ButtonPrimary;
                }

            var color = button.targetGraphic != null ? button.targetGraphic.color : Color.white;
            if (color.r > color.g * 1.5f && color.r > 0.2f && theme.ButtonDanger != null)
                return theme.ButtonDanger; // e.g. "Exit game"
            return theme.Button;
        }

        private static bool IsColor(Color c, int r, int g, int b)
        {
            const float tolerance = 3f / 255f;
            return Mathf.Abs(c.r - r / 255f) <= tolerance && Mathf.Abs(c.g - g / 255f) <= tolerance
                && Mathf.Abs(c.b - b / 255f) <= tolerance;
        }

        private static bool IsGrey(Color c, int value, float alpha)
        {
            const float tolerance = 3f / 255f;
            float v = value / 255f;
            return Mathf.Abs(c.r - v) <= tolerance && Mathf.Abs(c.g - v) <= tolerance
                && Mathf.Abs(c.b - v) <= tolerance && Mathf.Abs(c.a - alpha) <= 0.02f;
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
        /// <summary>Height of one line of this text, from its font's metrics.</summary>
        internal static float LineHeight(TMP_Text text)
        {
            var face = text.font != null ? text.font.faceInfo : default;
            float ratio = face.pointSize > 0f ? face.lineHeight / face.pointSize : 1.2f;
            return text.fontSize * ratio;
        }

        internal static void FitInfoColumn(TMP_Text nameText)
        {
            var column = nameText.transform.parent;
            var columnLayout = column != null ? column.GetComponent<LayoutElement>() : null;
            var group = column != null ? column.GetComponent<VerticalLayoutGroup>() : null;

            var nameLayout = nameText.GetComponent<LayoutElement>();
            if (nameLayout == null) nameLayout = nameText.gameObject.AddComponent<LayoutElement>();
            // Two lines of the name's own font and size (EB Garamond lines are taller than LiberationSans').
            float nameHeight = Mathf.Ceil(2f * LineHeight(nameText) + NameExtraHeight);
            nameLayout.minHeight = nameHeight;
            nameLayout.preferredHeight = nameHeight;

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
            float each = Mathf.Floor((columnLayout.preferredHeight - padding - spacing - nameHeight) / others.Count);
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

        private static void ApplyToRowPrefab(string path, UITheme theme)
        {
            EditPrefab(path, root =>
            {
                var image = root.GetComponent<Image>();
                if (image != null)
                {
                    image.sprite = null;
                    image.type = Image.Type.Simple;
                    image.color = RowColor; // also the "normal" colour the row script caches
                    EditorUtility.SetDirty(image);
                }

                foreach (var behaviour in root.GetComponents<MonoBehaviour>())
                {
                    if (behaviour == null) continue;
                    var so = new SerializedObject(behaviour);
                    bool any = false;
                    foreach (var field in HighlightColorFields)
                    {
                        var prop = so.FindProperty(field);
                        if (prop == null || prop.propertyType != SerializedPropertyType.Color) continue;
                        prop.colorValue = HighlightColor;
                        any = true;
                    }
                    var normal = so.FindProperty("normalColor");
                    if (normal != null && normal.propertyType == SerializedPropertyType.Color)
                    {
                        normal.colorValue = RowColor;
                        any = true;
                    }
                    if (any) so.ApplyModifiedPropertiesWithoutUndo();
                }

                foreach (var child in root.GetComponentsInChildren<Image>(true))
                {
                    if (child.gameObject == root) continue;
                    if (child.GetComponentInParent<AntiqueImageSlot>(true) != null) continue;

                    if (child.name.Contains("Thumbnail") || child.name == "StatusIcon")
                    {
                        child.sprite = null;
                        child.color = theme.Edge; // placeholder until real art
                        EditorUtility.SetDirty(child);
                    }
                    else if (child.name == "Status")
                    {
                        child.sprite = null;
                        child.color = StatusPillColor; // "ACTIVE" pill: was greybox blue
                        EditorUtility.SetDirty(child);
                    }
                    else if (child.name == "SelectedHighlight")
                    {
                        var c = theme.Accent;
                        c.a = SelectionOverlayAlpha; // the greybox 25% gold read too bright on dark rows
                        child.color = c;
                        EditorUtility.SetDirty(child);
                    }
                }

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

            // Buttons without a Target Graphic still draw the Image on their own object.
            var image = button.targetGraphic as Image;
            if (image == null) image = button.GetComponent<Image>();
            if (image != null)
                SetSliced(image, sprite, useUndo);

            // A button with a set height keeps it — a crowded layout must not squeeze it until
            // its label disappears (taller EB Garamond texts nearby take more room).
            var layout = button.GetComponent<LayoutElement>();
            if (layout != null && layout.preferredHeight > 0f && layout.minHeight < layout.preferredHeight)
            {
                if (useUndo) Undo.RecordObject(layout, "Keep button height");
                layout.minHeight = layout.preferredHeight;
                if (!useUndo) EditorUtility.SetDirty(layout);
            }

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
