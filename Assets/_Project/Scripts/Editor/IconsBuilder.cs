using System.Collections.Generic;
using System.Reflection;
using System.Text;
using AntiqueTradingSimulator.Company;
using AntiqueTradingSimulator.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.EditorTools
{
    /// <summary>
    /// Puts the gold emblem icons (Art/UI/Icons/ico_*.png) into the UI:
    ///  - creates / fills Data/Resources/UIIcons.asset (each field ← ico_&lt;field_in_snake_case&gt;.png)
    ///  - replaces the placeholder squares and text glyphs (■ ● ◷ "i" "!" "€" …) in Main.unity —
    ///    HUD stats and date, view titles, Company tiles and scores, modal headers and info notes,
    ///    news and event detail rows — and adds the missing Cash / Wealth icons to the HUD
    ///  - links the icons that change at runtime: upgrade cards and details (per upgrade),
    ///    reputation / credibility level badges, news rows (per news type)
    ///  - rebuilds MainNavigation buttons like the mockup: icon, title, muted subtitle
    ///  - ActiveEventListItem.prefab / NewsListItem.prefab: same as the scene rows
    /// Glyph placeholders are text objects, which can't also hold an Image, so their text is cleared
    /// and the icon is drawn by a "Glyph" child. Safe to run more than once.
    /// </summary>
    public static class IconsBuilder
    {
        private const string MenuPath = "Tools/Antique Trading Simulator/UI/Apply Icons";

        private const string IconsFolder = "Assets/_Project/Art/UI/Icons/";
        private const string IconsAssetPath = "Assets/_Project/Data/Resources/" + UIIcons.ResourcePath + ".asset";
        private const string ThemePath = "Assets/_Project/Data/Resources/" + UITheme.ResourcePath + ".asset";
        private const string SubtitleFontPath = "Assets/_Project/Art/Fonts/EBGaramond-Medium SDF.asset";
        private const string ActiveEventPrefab = "Assets/_Project/Prefabs/UI/NewsEvents/ActiveEventListItem.prefab";
        private const string NewsItemPrefab = "Assets/_Project/Prefabs/UI/NewsEvents/NewsListItem.prefab";

        private const string SceneName = "Main";
        private const string CanvasName = "Canvas";
        private const string GlyphName = "Glyph";
        private const int ReputationBadgeCount = 5;
        private const int CredibilityBadgeCount = 5;

        // Navigation button layout (buttons are 276 × 90).
        private const float NavIconSize = 42f;
        private const float NavIconLeft = 16f;
        private const float NavTextLeft = 70f;
        private const float NavTextRight = 8f;
        private const float NavTitleSize = 22f;
        private const float NavSubtitleSize = 15f;

        private static readonly Color MutedText = new Color32(0xAA, 0x98, 0x78, 0xFF);

        /// <summary>Navigation button → icon field and the subtitle under its title.</summary>
        private static readonly (string button, string icon, string subtitle)[] NavButtons =
        {
            ("MarketButton", nameof(UIIcons.Market), "Buy antiques"),
            ("InventoryButton", nameof(UIIcons.Collection), "Your antiques"),
            ("AuctionsButton", nameof(UIIcons.Auction), "Bid and sell"),
            ("ContractsButton", nameof(UIIcons.Contracts), "Client orders"),
            ("NewsEventsButton", nameof(UIIcons.News), "Market intelligence"),
            ("CompanyButton", nameof(UIIcons.Company), "Your business"),
        };

        private const string Modals = "Modals/";
        private const string CompanyViewPath = "Views/CompanyView/";
        private const string Overview = CompanyViewPath + "Pages/OverviewPage/BottomRow/LeftColumn/";
        private const string Finances = CompanyViewPath + "Pages/FinancesPage/Body/LeftColumn/FinancialOverview/Tiles/";
        private const string Development = CompanyViewPath + "Pages/DevelopmentPage/";
        private const string ReputationLevels = CompanyViewPath + "Pages/ReputationPage/LevelRow/";
        private const string CreateEventDetails = Modals + "CreateEventModal/Window/Body/DetailsColumn/";
        private const string NewsSide = "Views/NewsEventsView/Content/SidePanel/";
        private const string SampleEvent = "Views/NewsEventsView/Content/MainPanel/ContentSwitcher/ActiveEventsContent/ActiveEventsScrollView/Viewport/ActiveEventsList/ActiveEventItem_02/";

        /// <summary>Placeholders under Canvas → icon field. Optional ones may not exist (no warning).</summary>
        private static readonly (string path, string icon, bool optional)[] SceneIcons =
        {
            // HUD
            ("HUD/DayBlock/CalendarIcon", nameof(UIIcons.Calendar), false),
            ("HUD/StatCash/ValueRow/Icon", nameof(UIIcons.Cash), false),
            ("HUD/StatWealth/ValueRow/Icon", nameof(UIIcons.Wealth), false),
            ("HUD/StatReputation/ValueRow/Icon", nameof(UIIcons.Reputation), false),
            ("HUD/StatCredibility/ValueRow/Icon", nameof(UIIcons.Credibility), false),
            ("HUD/StatMarketShare/ValueRow/Icon", nameof(UIIcons.MarketShare), false),

            // View titles
            ("Views/MarketView/MainArea/ListPanel/MarketTitleRow/Icon", nameof(UIIcons.Market), false),
            ("Views/InventoryView/InventoryHeader/TitleRow/Icon", nameof(UIIcons.Collection), false),
            (CompanyViewPath + "CompanyHeader/TitleRow/Icon", nameof(UIIcons.Company), false),
            ("Views/NewsEventsView/Header/HeaderInfo/TitleRow/NewsIcon", nameof(UIIcons.News), false),
            ("Views/NewsEventsView/Header/InfoAccessPanel/InfoIcon", nameof(UIIcons.InfoAccess), false),

            // Company → Overview
            (Overview + "KeyMetrics/Tiles/WealthTile/IconRow/Icon", nameof(UIIcons.Wealth), false),
            (Overview + "KeyMetrics/Tiles/CollectionValueTile/IconRow/Icon", nameof(UIIcons.CollectionValue), false),
            (Overview + "KeyMetrics/Tiles/ReputationTile/IconRow/Icon", nameof(UIIcons.Reputation), false),
            (Overview + "KeyMetrics/Tiles/CredibilityTile/IconRow/Icon", nameof(UIIcons.Credibility), false),
            (Overview + "KeyMetrics/Tiles/MarketShareTile/IconRow/Icon", nameof(UIIcons.MarketShare), false),
            (Overview + "KeyMetrics/Tiles/ContractsTile/IconRow/Icon", nameof(UIIcons.ContractsFulfilled), false),
            (Overview + "ReadinessRow/CongressReadiness/WealthScore/Icon", nameof(UIIcons.Wealth), false),
            (Overview + "ReadinessRow/CongressReadiness/CollectionValueScore/Icon", nameof(UIIcons.CollectionValue), false),
            (Overview + "ReadinessRow/CongressReadiness/ReputationScore/Icon", nameof(UIIcons.Reputation), false),
            (Overview + "ReadinessRow/CongressReadiness/CredibilityScore/Icon", nameof(UIIcons.Credibility), false),
            (Overview + "ReadinessRow/CongressReadiness/MarketShareScore/Icon", nameof(UIIcons.MarketShare), false),
            (Overview + "ReadinessRow/CongressReadiness/ContractsFulfilledScore/Icon", nameof(UIIcons.ContractsFulfilled), false),

            // Company → Finances
            (Finances + "CashTile/Icon", nameof(UIIcons.Cash), false),
            (Finances + "IncomeTile/Icon", nameof(UIIcons.Income), false),
            (Finances + "ExpensesTile/Icon", nameof(UIIcons.Expenses), false),
            (Finances + "ProfitTile/Icon", nameof(UIIcons.Profit), false),

            // Company → Development / Reputation (runtime replaces these with the current upgrade / level)
            (Development + "SummaryRow/HintPanel/InfoIcon", nameof(UIIcons.Info), false),
            (Development + "Body/UpgradeDetails/Header/Icon", nameof(UIIcons.UpgWarehouse), false),
            (Development + "Body/UpgradeList/Viewport/Content/UpgradeCardTemplate/Illustration", nameof(UIIcons.UpgWarehouse), false),
            (ReputationLevels + "ReputationLevel/Levels/CurrentLevel/Icon", nameof(UIIcons.ReputationBadges), false),
            (ReputationLevels + "CredibilityLevel/Levels/CurrentLevel/Icon", nameof(UIIcons.CredibilityBadges), false),

            // Modals
            (Modals + "BuyModal/Window/Header/Icon", nameof(UIIcons.Buy), false),
            (Modals + "SellNowModal/Window/Header/Icon", nameof(UIIcons.Sell), false),
            (Modals + "ListOnMarketModal/Window/Header/Icon", nameof(UIIcons.List), false),
            (Modals + "RestorationModal/Window/Header/Icon", nameof(UIIcons.Restore), false),
            (Modals + "ContractFulfillModal/Window/Header/Icon", nameof(UIIcons.Fulfil), false),
            (Modals + "CreateEventModal/Window/Header/Icon", nameof(UIIcons.CreateEvent), false),
            (Modals + "BuyModal/Window/Footer/InfoIcon", nameof(UIIcons.Info), true),
            (Modals + "SellNowModal/Window/Footer/InfoIcon", nameof(UIIcons.Info), true),
            (Modals + "ListOnMarketModal/Window/Footer/InfoIcon", nameof(UIIcons.Info), true),
            (Modals + "RestorationModal/Window/Footer/InfoIcon", nameof(UIIcons.Info), true),
            (Modals + "CreateEventModal/Window/Footer/InfoIcon", nameof(UIIcons.Info), true),
            (CreateEventDetails + "SelectedTemplate/Icon", nameof(UIIcons.CreateEvent), false),
            (Modals + "CreateEventModal/Window/Body/TemplatesColumn/TemplateList/TemplateItem/Icon", nameof(UIIcons.CreateEvent), true),
            (CreateEventDetails + "DetailRows/TargetCategory/Icon", nameof(UIIcons.Category), false),
            (CreateEventDetails + "DetailRows/TargetPeriod/Icon", nameof(UIIcons.Period), false),
            (CreateEventDetails + "DetailRows/TargetRegion/Icon", nameof(UIIcons.Region), false),
            (CreateEventDetails + "DetailRows/Duration/Icon", nameof(UIIcons.Duration), false),
            (CreateEventDetails + "DetailRows/GeneratedNews/Icon", nameof(UIIcons.News), false),

            // News & Events side panel
            (NewsSide + "NewsDetails/Details/ExpectedEventDate/Icon", nameof(UIIcons.Date), false),
            (NewsSide + "NewsDetails/Details/TargetCategory/Icon", nameof(UIIcons.Category), false),
            (NewsSide + "NewsDetails/Details/TargetPeriod/Icon", nameof(UIIcons.Period), false),
            (NewsSide + "NewsDetails/Details/TargetRegion/Icon", nameof(UIIcons.Region), false),
            (NewsSide + "NewsDetails/InformationNote/InfoIcon", nameof(UIIcons.Info), false),
            (NewsSide + "EventDetails/EventInfo/TargetCategory/Icon", nameof(UIIcons.Category), false),
            (NewsSide + "EventDetails/EventInfo/TargetPeriod/Icon", nameof(UIIcons.Period), false),
            (NewsSide + "EventDetails/EventInfo/TargetRegion/Icon", nameof(UIIcons.Region), false),
            (NewsSide + "EventDetails/EventInfo/TimeRemaining/Icon", nameof(UIIcons.Duration), false),
            (NewsSide + "EventDetails/InformationNote/InfoIcon", nameof(UIIcons.Info), false),
            (SampleEvent + "MainInfo/MetaRow/LocationInfo/Icon", nameof(UIIcons.Region), true),
            (SampleEvent + "MainInfo/MetaRow/TimeInfo/Icon", nameof(UIIcons.Duration), true),
        };

        /// <summary>HUD stats that have no icon yet — one is copied from the reputation stat.</summary>
        private static readonly string[] HudStatsWithoutIcon = { "HUD/StatCash/ValueRow", "HUD/StatWealth/ValueRow" };
        private const string HudIconTemplate = "HUD/StatReputation/ValueRow/Icon";

        private static readonly (string path, string icon)[] ActiveEventIcons =
        {
            ("MainInfo/MetaRow/LocationInfo/Icon", nameof(UIIcons.Region)),
            ("MainInfo/MetaRow/TimeInfo/Icon", nameof(UIIcons.Duration)),
        };

        [MenuItem(MenuPath)]
        public static void Run()
        {
            var icons = CreateOrFillIconsAsset();
            if (icons == null)
                return;

            int placed = ApplyToScene(icons);
            placed += ApplyToPrefab(ActiveEventPrefab, icons, ActiveEventIcons);
            placed += ApplyToPrefab(NewsItemPrefab, icons, new[] { ("Thumbnail", nameof(UIIcons.NewsOfficial)) });

            Debug.Log($"[IconsBuilder] Done ({placed} icon(s) placed). Save the scene (Ctrl+S).");
        }

        // ------------------------------------------------------------------
        // UIIcons asset
        // ------------------------------------------------------------------

        private static UIIcons CreateOrFillIconsAsset()
        {
            var icons = AssetDatabase.LoadAssetAtPath<UIIcons>(IconsAssetPath);
            if (icons == null)
            {
                icons = ScriptableObject.CreateInstance<UIIcons>();
                AssetDatabase.CreateAsset(icons, IconsAssetPath);
            }

            var missing = new List<string>();
            foreach (var field in typeof(UIIcons).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (field.FieldType != typeof(Sprite))
                    continue;

                string file = "ico_" + ToSnakeCase(field.Name) + ".png";
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(IconsFolder + file);
                if (sprite == null) missing.Add(file);
                field.SetValue(icons, sprite);
            }

            icons.ReputationBadges = LoadBadges("ico_rep_", ReputationBadgeCount, missing);
            icons.CredibilityBadges = LoadBadges("ico_cred_", CredibilityBadgeCount, missing);

            EditorUtility.SetDirty(icons);
            AssetDatabase.SaveAssets();

            if (missing.Count > 0)
                Debug.LogWarning($"[IconsBuilder] Missing in {IconsFolder} (or not imported as Sprite): {string.Join(", ", missing)}", icons);

            return icons;
        }

        private static Sprite[] LoadBadges(string prefix, int count, List<string> missing)
        {
            var badges = new List<Sprite>(count);
            for (int i = 1; i <= count; i++)
            {
                string file = prefix + i + ".png";
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(IconsFolder + file);
                if (sprite != null) badges.Add(sprite);
                else missing.Add(file);
            }
            return badges.ToArray();
        }

        /// <summary>"CollectionValue" → "collection_value".</summary>
        private static string ToSnakeCase(string name)
        {
            var sb = new StringBuilder(name.Length + 4);
            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (char.IsUpper(c) && i > 0) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>Sprite of a field by name; badge arrays give their first (lowest) badge.</summary>
        private static Sprite Get(UIIcons icons, string fieldName)
        {
            var field = typeof(UIIcons).GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            if (field == null) return null;
            if (field.GetValue(icons) is Sprite sprite) return sprite;
            if (field.GetValue(icons) is Sprite[] badges && badges.Length > 0) return badges[0];
            return null;
        }

        // ------------------------------------------------------------------
        // Scene
        // ------------------------------------------------------------------

        private static int ApplyToScene(UIIcons icons)
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.name != SceneName)
            {
                Debug.LogWarning($"[IconsBuilder] Open the {SceneName} scene to place the icons there (prefabs are still updated).");
                return 0;
            }

            Transform canvas = null;
            foreach (var go in scene.GetRootGameObjects())
                if (go.name == CanvasName) { canvas = go.transform; break; }

            if (canvas == null)
            {
                Debug.LogError($"[IconsBuilder] No root '{CanvasName}' in {SceneName}.");
                return 0;
            }

            AddMissingHudIcons(canvas);

            int placed = 0;
            foreach (var (path, iconName, optional) in SceneIcons)
            {
                Transform target = canvas.Find(path);
                if (target == null)
                {
                    if (!optional) Debug.LogWarning($"[IconsBuilder] '{CanvasName}/{path}' not found — skipped.");
                    continue;
                }

                if (PlaceIcon(target, Get(icons, iconName), useUndo: true))
                    placed++;
            }

            WireRuntimeIcons(canvas);
            BuildNavigation(canvas, icons);

            EditorSceneManager.MarkSceneDirty(scene);
            return placed;
        }

        /// <summary>Cash and Wealth in the HUD had no icon slot — copy the reputation one in front of the value.</summary>
        private static void AddMissingHudIcons(Transform canvas)
        {
            Transform template = canvas.Find(HudIconTemplate);
            if (template == null)
            {
                Debug.LogWarning($"[IconsBuilder] '{CanvasName}/{HudIconTemplate}' not found — no Cash / Wealth icons in the HUD.");
                return;
            }

            foreach (var rowPath in HudStatsWithoutIcon)
            {
                Transform row = canvas.Find(rowPath);
                if (row == null || row.Find(template.name) != null)
                    continue;

                var copy = Object.Instantiate(template.gameObject, row, false);
                copy.name = template.name;
                copy.transform.SetSiblingIndex(0);
                Undo.RegisterCreatedObjectUndo(copy, "Add HUD icon");
            }
        }

        /// <summary>Points the runtime icon fields at the objects that show them.</summary>
        private static void WireRuntimeIcons(Transform canvas)
        {
            foreach (var card in canvas.GetComponentsInChildren<UpgradeCardUI>(true))
                SetReference(card, "icon", FindImage(card.transform, "Illustration"));

            foreach (var details in canvas.GetComponentsInChildren<UpgradeDetailsUI>(true))
                SetReference(details, "icon", FindImage(details.transform, "Header/Icon"));

            foreach (var column in canvas.GetComponentsInChildren<ReputationColumnUI>(true))
            {
                var so = new SerializedObject(column);
                var kind = so.FindProperty("kind");
                if (kind == null) continue;

                string level = kind.intValue == (int)ReputationKind.Reputation ? "ReputationLevel" : "CredibilityLevel";
                SetReference(column, "tierIcon", FindImage(canvas, ReputationLevels + level + "/Levels/CurrentLevel/Icon"));
            }
        }

        private static Image FindImage(Transform root, string path)
        {
            var t = root.Find(path);
            return t != null ? t.GetComponent<Image>() : null;
        }

        private static void SetReference(Component owner, string field, Object value)
        {
            if (value == null)
            {
                Debug.LogWarning($"[IconsBuilder] No icon object for {owner.GetType().Name}.{field} on '{owner.name}'.", owner);
                return;
            }

            var so = new SerializedObject(owner);
            var prop = so.FindProperty(field);
            if (prop == null)
            {
                Debug.LogWarning($"[IconsBuilder] {owner.GetType().Name} has no field '{field}'.", owner);
                return;
            }

            prop.objectReferenceValue = value;
            so.ApplyModifiedProperties();
        }

        // ------------------------------------------------------------------
        // Navigation: icon + title + subtitle
        // ------------------------------------------------------------------

        private static void BuildNavigation(Transform canvas, UIIcons icons)
        {
            Transform nav = canvas.Find("MainNavigation");
            if (nav == null)
            {
                Debug.LogWarning("[IconsBuilder] Canvas/MainNavigation not found — navigation not rebuilt.");
                return;
            }

            var theme = AssetDatabase.LoadAssetAtPath<UITheme>(ThemePath);
            var subtitleFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(SubtitleFontPath);

            foreach (var (buttonName, iconName, subtitleText) in NavButtons)
            {
                Transform button = nav.Find(buttonName);
                if (button == null)
                {
                    Debug.LogWarning($"[IconsBuilder] MainNavigation/{buttonName} not found — skipped.");
                    continue;
                }

                // Icon on the left, vertically centred.
                var icon = FindOrCreateImage(button, "Icon");
                var iconRect = (RectTransform)icon.transform;
                Undo.RecordObject(iconRect, "Layout navigation icon");
                iconRect.anchorMin = iconRect.anchorMax = new Vector2(0f, 0.5f);
                iconRect.pivot = new Vector2(0f, 0.5f);
                iconRect.anchoredPosition = new Vector2(NavIconLeft, 0f);
                iconRect.sizeDelta = new Vector2(NavIconSize, NavIconSize);
                SetIcon(icon, Get(icons, iconName), useUndo: true);

                // Title: the existing label, moved to the upper half.
                TMP_Text title = null;
                foreach (var text in button.GetComponentsInChildren<TMP_Text>(true))
                    if (text.name != ViewManager.NavSubtitleName) { title = text; break; }
                if (title == null)
                {
                    Debug.LogWarning($"[IconsBuilder] MainNavigation/{buttonName} has no label — subtitle skipped.");
                    continue;
                }

                Undo.RecordObject(title, "Layout navigation title");
                Undo.RecordObject(title.rectTransform, "Layout navigation title");
                SetTextArea(title.rectTransform, new Vector2(0f, 0.5f), Vector2.one, bottom: -1f, top: -12f);
                title.fontSize = NavTitleSize;
                title.enableAutoSizing = false;
                title.alignment = TextAlignmentOptions.BottomLeft;
                title.textWrappingMode = TextWrappingModes.NoWrap;
                title.overflowMode = TextOverflowModes.Ellipsis;
                title.margin = Vector4.zero;
                if (theme != null) title.color = theme.TextPrimary;

                // Subtitle: a copy of the title, smaller, muted, not upper-case, in the lower half.
                var subtitleTransform = button.Find(ViewManager.NavSubtitleName);
                TMP_Text subtitle;
                if (subtitleTransform == null)
                {
                    var copy = Object.Instantiate(title.gameObject, button, false);
                    copy.name = ViewManager.NavSubtitleName;
                    Undo.RegisterCreatedObjectUndo(copy, "Add navigation subtitle");
                    subtitle = copy.GetComponent<TMP_Text>();
                }
                else
                {
                    subtitle = subtitleTransform.GetComponent<TMP_Text>();
                    Undo.RecordObject(subtitle, "Layout navigation subtitle");
                    Undo.RecordObject(subtitle.rectTransform, "Layout navigation subtitle");
                }

                SetTextArea(subtitle.rectTransform, Vector2.zero, new Vector2(1f, 0.5f), bottom: 8f, top: -3f);
                subtitle.text = subtitleText;
                if (subtitleFont != null)
                {
                    subtitle.font = subtitleFont;
                    subtitle.fontSharedMaterial = subtitleFont.material;
                }
                subtitle.fontStyle = FontStyles.Normal;
                subtitle.fontSize = NavSubtitleSize;
                subtitle.enableAutoSizing = false;
                subtitle.characterSpacing = 0f;
                subtitle.alignment = TextAlignmentOptions.TopLeft;
                subtitle.textWrappingMode = TextWrappingModes.NoWrap;
                subtitle.overflowMode = TextOverflowModes.Ellipsis;
                subtitle.color = theme != null ? theme.TextMuted : MutedText;
                subtitle.raycastTarget = false;
            }
        }

        /// <summary>Text area between the icon and the right edge, inside the given anchor band.</summary>
        private static void SetTextArea(RectTransform rect, Vector2 anchorMin, Vector2 anchorMax, float bottom, float top)
        {
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = new Vector2(0f, 0.5f);
            rect.offsetMin = new Vector2(NavTextLeft, bottom);
            rect.offsetMax = new Vector2(-NavTextRight, top);
        }

        // ------------------------------------------------------------------
        // Prefabs
        // ------------------------------------------------------------------

        private static int ApplyToPrefab(string prefabPath, UIIcons icons, (string path, string icon)[] entries)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath) == null)
            {
                Debug.LogError($"[IconsBuilder] Prefab not found: {prefabPath}");
                return 0;
            }

            int placed = 0;
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                foreach (var (path, iconName) in entries)
                {
                    Transform target = root.transform.Find(path);
                    if (target == null)
                    {
                        Debug.LogWarning($"[IconsBuilder] '{path}' not found in {prefabPath} — skipped.");
                        continue;
                    }
                    if (PlaceIcon(target, Get(icons, iconName), useUndo: false))
                        placed++;
                }

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
            return placed;
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        /// <summary>
        /// Shows the icon on a placeholder: on its own Image (letters inside it are hidden),
        /// or — for a text glyph placeholder — on a "Glyph" child filling it, with the glyph cleared.
        /// </summary>
        private static bool PlaceIcon(Transform target, Sprite sprite, bool useUndo)
        {
            if (sprite == null)
                return false;

            var image = target.GetComponent<Image>();
            if (image != null)
            {
                SetIcon(image, sprite, useUndo);

                // "i", "!", "€", "R" … drawn over the old square.
                foreach (Transform child in target)
                {
                    if (child.GetComponent<TMP_Text>() == null || !child.gameObject.activeSelf) continue;
                    if (useUndo) Undo.RecordObject(child.gameObject, "Hide icon letter");
                    child.gameObject.SetActive(false);
                }
                return true;
            }

            var glyph = target.GetComponent<TMP_Text>();
            if (glyph == null)
            {
                Debug.LogWarning($"[IconsBuilder] '{target.name}' has neither an Image nor a text — skipped.", target);
                return false;
            }

            if (useUndo) Undo.RecordObject(glyph, "Clear glyph");
            glyph.text = "";

            var holder = FindOrCreateImage(target, GlyphName, useUndo);
            var rect = (RectTransform)holder.transform;
            if (useUndo) Undo.RecordObject(rect, "Layout glyph icon");
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            SetIcon(holder, sprite, useUndo);
            return true;
        }

        private static void SetIcon(Image image, Sprite sprite, bool useUndo)
        {
            if (useUndo) Undo.RecordObject(image, "Set icon");
            image.sprite = sprite;
            image.type = Image.Type.Simple;
            image.preserveAspect = true;
            image.color = Color.white;
            image.raycastTarget = false;
        }

        private static Image FindOrCreateImage(Transform parent, string name, bool useUndo = true)
        {
            Transform existing = parent.Find(name);
            if (existing != null)
            {
                var found = existing.GetComponent<Image>();
                if (found != null) return found;
                return useUndo ? Undo.AddComponent<Image>(existing.gameObject) : existing.gameObject.AddComponent<Image>();
            }

            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            var layoutElement = go.AddComponent<LayoutElement>();
            layoutElement.ignoreLayout = true;
            if (useUndo) Undo.RegisterCreatedObjectUndo(go, "Create " + name);
            return go.GetComponent<Image>();
        }
    }
}
