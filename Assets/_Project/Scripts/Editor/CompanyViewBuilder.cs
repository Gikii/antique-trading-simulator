using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.UI;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using U = AntiqueTradingSimulator.EditorTools.UIBuilderUtility;

namespace AntiqueTradingSimulator.EditorTools
{
    /// <summary>
    /// Builds the Company view inside Canvas/Views/CompanyView of the open scene: header,
    /// sub-tabs and their pages, then wires every serialized field and points the
    /// ViewManager's Company entry at the new CompanyView component. Safe to re-run —
    /// it replaces everything under CompanyView. One partial file per page.
    /// </summary>
    public static partial class CompanyViewBuilder
    {
        private const string MenuPath = "Tools/Antique Trading Simulator/Build Company View";
        private const string UndoName = "Build Company View";

        private static readonly string[] TabLabels = { "Overview", "Development", "Reputation", "Finances" };
        private static readonly float[] TabWidths = { 160f, 190f, 175f, 155f };

        [MenuItem(MenuPath)]
        public static void Build()
        {
            var scene = SceneManager.GetActiveScene();
            var root = U.FindInScene(scene, "CompanyView");
            if (root == null)
            {
                EditorUtility.DisplayDialog(UndoName, "No GameObject named 'CompanyView' in the open scene (expected under Canvas/Views).", "OK");
                return;
            }

            Undo.SetCurrentGroupName(UndoName);
            int undoGroup = Undo.GetCurrentGroup();

            var view = PrepareRoot(root);

            var header = BuildHeader(root.transform, out var tabButtons);
            var pages = U.Node("Pages", root.transform);
            U.Layout(pages.gameObject, -1, -1, -1, 1);

            var tabPages = new GameObject[TabLabels.Length];
            tabPages[(int)CompanyTab.Overview] = BuildOverviewPage(pages).gameObject;
            var developmentTab = BuildDevelopmentPage(pages);
            tabPages[(int)CompanyTab.Development] = developmentTab.gameObject;
            tabPages[(int)CompanyTab.Reputation] = BuildReputationPage(pages).gameObject;
            tabPages[(int)CompanyTab.Finances] = BuildFinancesPage(pages).gameObject;

            Undo.RegisterCreatedObjectUndo(header.gameObject, UndoName);
            Undo.RegisterCreatedObjectUndo(pages.gameObject, UndoName);

            U.SetRefs(view, "tabButtons", tabButtons);
            U.SetRefs(view, "tabPages", tabPages);
            U.SetRef(view, "developmentTab", developmentTab);

            for (int i = 0; i < tabPages.Length; i++)
                tabPages[i].SetActive(i == (int)CompanyTab.Overview);

            RegisterWithViewManager(view);

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = root;
            Debug.Log("CompanyViewBuilder: Company view built. Save the scene to keep it.");
        }

        // ------------------------------------------------------------------ root

        /// <summary>Clears CompanyView, swaps the placeholder for CompanyView and sets up the root layout.</summary>
        private static CompanyView PrepareRoot(GameObject root)
        {
            U.DestroyChildren(root.transform);

            foreach (var placeholder in root.GetComponents<PlaceholderView>())
                Undo.DestroyObjectImmediate(placeholder);
            foreach (var group in root.GetComponents<LayoutGroup>())
                Undo.DestroyObjectImmediate(group);

            var view = root.GetComponent<CompanyView>();
            if (view == null)
                view = Undo.AddComponent<CompanyView>(root);

            var rt = (RectTransform)root.transform;
            Undo.RecordObject(rt, UndoName);
            U.Stretch(rt);

            // Same root layout as InventoryView.
            var layout = Undo.AddComponent<VerticalLayoutGroup>(root);
            layout.spacing = 10;
            layout.padding = new RectOffset(16, 16, 14, 14);
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            return view;
        }

        // ------------------------------------------------------------------ header

        private static RectTransform BuildHeader(Transform parent, out Button[] tabButtons)
        {
            // Mirrors InventoryView's InventoryHeader (title row + tabs row).
            var header = U.Node("CompanyHeader", parent);
            U.Layout(header.gameObject, -1, 74, -1, 0);
            U.Vertical(header.gameObject, 6);

            var titleRow = U.Node("TitleRow", header);
            U.Layout(titleRow.gameObject, -1, 28, -1, 0);
            var titleLayout = U.Horizontal(titleRow.gameObject, 8, 0, false);
            titleLayout.padding = new RectOffset(4, 0, 0, 0);

            var icon = U.Panel("Icon", titleRow, Color.white);
            U.Layout(icon.gameObject, 22, 22, 0, 0);

            var title = U.CreateText("TitleText", titleRow, "Company", 36, Color.white);
            U.Layout(title.gameObject, -1, 28, 0);

            var help = U.CreateButton("HelpButton", titleRow, "?", 24, 24, 24, out _);
            var helpTooltip = help.gameObject.AddComponent<TooltipTrigger>();
            U.SetString(helpTooltip, "text",
                "Your company: its standing, development, reputation and finances.\n" +
                "<size=85%>At the end of the 90-day campaign the International Collectors Congress " +
                "evaluates wealth, collection, contracts, reputation, credibility and market share.</size>");

            var tabsRow = U.Node("TabsRow", header);
            U.Layout(tabsRow.gameObject, -1, 40, -1, 0);
            U.Horizontal(tabsRow.gameObject, 6);

            tabButtons = new Button[TabLabels.Length];
            for (int i = 0; i < TabLabels.Length; i++)
                tabButtons[i] = U.CreateButton($"{TabLabels[i]}Tab", tabsRow, TabLabels[i], 24, TabWidths[i], 40, out _);

            return header;
        }

        // ------------------------------------------------------------------ overview

        private static RectTransform BuildOverviewPage(Transform pages)
        {
            var page = U.Node("OverviewPage", pages);
            U.Stretch(page);
            U.Vertical(page.gameObject, 10);

            var tab = page.gameObject.AddComponent<CompanyOverviewTab>();
            U.SetRef(tab, "playerTrader", Object.FindFirstObjectByType<PlayerTrader>(FindObjectsInactive.Include));
            U.SetRef(tab, "timeManager", Object.FindFirstObjectByType<TimeManager>(FindObjectsInactive.Include));

            // Top row: company card + next major event.
            var top = U.Node("TopRow", page);
            U.Layout(top.gameObject, -1, 190, -1, 0);
            U.Horizontal(top.gameObject, 10);

            BuildCompanyCard(top, tab);
            BuildNextEventCard(top, tab);

            // Bottom row: metrics + readiness on the left, competition on the right.
            var bottom = U.Node("BottomRow", page);
            U.Layout(bottom.gameObject, -1, -1, -1, 1);
            U.Horizontal(bottom.gameObject, 10);

            var left = U.Node("LeftColumn", bottom);
            U.Layout(left.gameObject, -1, -1, 2.6f, -1);
            U.Vertical(left.gameObject, 10);

            BuildKeyMetrics(left, tab);

            var readinessRow = U.Node("ReadinessRow", left);
            U.Layout(readinessRow.gameObject, -1, -1, -1, 1);
            U.Horizontal(readinessRow.gameObject, 10);

            BuildReadiness(readinessRow, tab);
            BuildEstimatedScore(readinessRow, tab);
            BuildCompetition(bottom, tab);

            return page;
        }

        private static TextMeshProUGUI SectionTitle(Transform parent, string text)
        {
            var title = U.CreateText("SectionTitle", parent, text, 17, Color.white);
            U.Layout(title.gameObject, -1, 22, -1, 0);
            return title;
        }

        private static void BuildCompanyCard(Transform parent, CompanyOverviewTab tab)
        {
            var card = U.Panel("CompanyCard", parent, U.PanelColor);
            U.Layout(card.gameObject, -1, -1, 2.2f, -1);
            U.Horizontal(card.gameObject, 14, 12);

            U.Placeholder("Illustration", card, 250, 166);

            var info = U.Node("Info", card);
            U.Layout(info.gameObject, -1, -1, 1, -1);
            U.Vertical(info.gameObject, 6);

            var nameRow = U.Node("NameRow", info);
            U.Layout(nameRow.gameObject, -1, 54, -1, 0);
            U.Horizontal(nameRow.gameObject, 10);

            var nameText = U.CreateText("CompanyNameText", nameRow, "Antique Empire", 30, Color.white, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            U.Layout(nameText.gameObject, -1, -1, 1, -1);

            var dayBox = U.Panel("DayBox", nameRow, U.TileColor);
            U.Layout(dayBox.gameObject, 170, -1, 0, -1);
            U.Vertical(dayBox.gameObject, 2, 5);
            var dayText = U.CreateText("DayText", dayBox, "Day 1 / 90", 20, UIFormat.AccentColor, TextAlignmentOptions.Center);
            U.Layout(dayText.gameObject, -1, 24, -1, 0);
            var dateText = U.CreateText("DateText", dayBox, "22 April 1884", 14, U.LabelColor, TextAlignmentOptions.Center);
            U.Layout(dateText.gameObject, -1, 16, -1, 0);

            var titleText = U.CreateText("CompanyTitleText", info, "Local Shop", 20, Color.white);
            U.Layout(titleText.gameObject, -1, 24, -1, 0);

            var barRow = U.Node("TierProgressRow", info);
            U.Layout(barRow.gameObject, -1, 10, -1, 0);
            U.Horizontal(barRow.gameObject, 0);
            var bar = U.Bar("TierProgress", barRow, 320, 10, UIFormat.AccentColor, out var fill, out var fillImage);
            var progress = bar.gameObject.AddComponent<ProgressBarUI>();
            U.SetRef(progress, "fill", fill);
            U.SetRef(progress, "fillImage", fillImage);

            var nextText = U.CreateText("NextLevelText", info, "Next level: Local Dealer", 14, U.LabelColor);
            U.Layout(nextText.gameObject, -1, 18, -1, 0);

            var description = U.CreateText("CompanyDescriptionText", info, "A small antique shop inherited from your grandfather.",
                15, Color.white, TextAlignmentOptions.TopLeft, FontStyles.Normal, true);
            U.Layout(description.gameObject, -1, -1, -1, 1);

            U.SetRef(tab, "companyNameText", nameText);
            U.SetRef(tab, "companyTitleText", titleText);
            U.SetRef(tab, "tierProgress", progress);
            U.SetRef(tab, "nextLevelText", nextText);
            U.SetRef(tab, "companyDescriptionText", description);
            U.SetRef(tab, "dayText", dayText);
            U.SetRef(tab, "dateText", dateText);
        }

        private static void BuildNextEventCard(Transform parent, CompanyOverviewTab tab)
        {
            var card = U.Panel("NextEventCard", parent, U.PanelColor);
            U.Layout(card.gameObject, -1, -1, 1.3f, -1);
            U.Vertical(card.gameObject, 8, 12);

            SectionTitle(card, "NEXT MAJOR EVENT");

            var body = U.Node("Body", card);
            U.Layout(body.gameObject, -1, -1, -1, 1);
            U.Horizontal(body.gameObject, 12);

            U.Placeholder("Illustration", body, 170, 130);

            var column = U.Node("Text", body);
            U.Layout(column.gameObject, -1, -1, 1, -1);
            U.Vertical(column.gameObject, 6);

            var title = U.CreateText("EventTitleText", column, "International Collectors Congress", 22, Color.white,
                TextAlignmentOptions.TopLeft, FontStyles.Normal, true);
            U.Layout(title.gameObject, -1, 56, -1, 0);

            var description = U.CreateText("EventDescriptionText", column, "In 89 days the Congress will take place.",
                15, U.LabelColor, TextAlignmentOptions.TopLeft, FontStyles.Normal, true);
            U.Layout(description.gameObject, -1, -1, -1, 1);

            U.SetRef(tab, "eventTitleText", title);
            U.SetRef(tab, "eventDescriptionText", description);
        }

        private static void BuildKeyMetrics(Transform parent, CompanyOverviewTab tab)
        {
            var panel = U.Panel("KeyMetrics", parent, U.PanelColor);
            U.Layout(panel.gameObject, -1, 180, -1, 0);
            U.Vertical(panel.gameObject, 8, 12);

            SectionTitle(panel, "KEY COMPANY METRICS");

            var tiles = U.Node("Tiles", panel);
            U.Layout(tiles.gameObject, -1, -1, -1, 1);
            var tilesLayout = U.Horizontal(tiles.gameObject, 8);
            tilesLayout.childForceExpandWidth = true;

            U.SetRef(tab, "wealthTile", BuildStatTile(tiles, "WealthTile", "Wealth"));
            U.SetRef(tab, "collectionTile", BuildStatTile(tiles, "CollectionValueTile", "Collection Value"));
            U.SetRef(tab, "contractsTile", BuildStatTile(tiles, "ContractsTile", "Contracts Fulfilled"));
            U.SetRef(tab, "reputationTile", BuildStatTile(tiles, "ReputationTile", "Reputation"));
            U.SetRef(tab, "credibilityTile", BuildStatTile(tiles, "CredibilityTile", "Credibility"));
            U.SetRef(tab, "marketShareTile", BuildStatTile(tiles, "MarketShareTile", "Market Share"));
        }

        private static StatTileUI BuildStatTile(Transform parent, string name, string label)
        {
            var tile = U.Panel(name, parent, U.TileColor);
            U.Layout(tile.gameObject, -1, -1, 1, -1);
            U.Vertical(tile.gameObject, 3, 8);

            var iconRow = U.Node("IconRow", tile);
            U.Layout(iconRow.gameObject, -1, 30, -1, 0);
            U.Horizontal(iconRow.gameObject, 0, 0, false, TextAnchor.MiddleCenter);
            U.Placeholder("Icon", iconRow, 30, 30);

            var labelText = U.CreateText("LabelText", tile, label, 15, U.LabelColor, TextAlignmentOptions.Center);
            U.Layout(labelText.gameObject, -1, 18, -1, 0);
            var valueText = U.CreateText("ValueText", tile, "—", 22, Color.white, TextAlignmentOptions.Center);
            U.Layout(valueText.gameObject, -1, 28, -1, 0);
            var changeText = U.CreateText("ChangeText", tile, "", 13, U.LabelColor, TextAlignmentOptions.Center);
            U.Layout(changeText.gameObject, -1, 16, -1, 0);

            var component = tile.gameObject.AddComponent<StatTileUI>();
            U.SetRef(component, "labelText", labelText);
            U.SetRef(component, "valueText", valueText);
            U.SetRef(component, "changeText", changeText);
            return component;
        }

        private static void BuildReadiness(Transform parent, CompanyOverviewTab tab)
        {
            var panel = U.Panel("CongressReadiness", parent, U.PanelColor);
            U.Layout(panel.gameObject, -1, -1, 1.7f, -1);
            U.Vertical(panel.gameObject, 8, 12);

            SectionTitle(panel, "CONGRESS READINESS");

            var subtitle = U.CreateText("Subtitle", panel,
                "Your final score is based on multiple factors. Improve each area to achieve a high position at the International Collectors Congress.",
                14, U.LabelColor, TextAlignmentOptions.TopLeft, FontStyles.Normal, true);
            U.Layout(subtitle.gameObject, -1, 36, -1, 0);

            string[] labels = { "Wealth", "Collection Value", "Contracts Fulfilled", "Reputation", "Credibility", "Market Share" };
            var bars = new Object[labels.Length];
            for (int i = 0; i < labels.Length; i++)
                bars[i] = BuildScoreBar(panel, $"{labels[i].Replace(" ", "")}Score", labels[i]);

            U.SetRefs(tab, "scoreBars", bars);
        }

        private static ScoreBarUI BuildScoreBar(Transform parent, string name, string label)
        {
            var row = U.Node(name, parent);
            U.Layout(row.gameObject, -1, 26, -1, 0);
            U.Horizontal(row.gameObject, 10, 0, false);
            U.AddImage(row.gameObject, U.Clear); // invisible, catches the pointer for the tooltip

            U.Placeholder("Icon", row, 18, 18);

            var labelText = U.CreateText("LabelText", row, label, 16, Color.white);
            U.Layout(labelText.gameObject, 170, 26, 0, -1);

            var bar = U.Bar("Bar", row, -1, 12, UIFormat.AccentColor, out var fill, out var fillImage);
            var progress = bar.gameObject.AddComponent<ProgressBarUI>();
            U.SetRef(progress, "fill", fill);
            U.SetRef(progress, "fillImage", fillImage);

            var valueText = U.CreateText("ValueText", row, "0 / 100", 16, Color.white, TextAlignmentOptions.MidlineRight);
            U.Layout(valueText.gameObject, 80, 26, 0, -1);

            var component = row.gameObject.AddComponent<ScoreBarUI>();
            U.SetRef(component, "labelText", labelText);
            U.SetRef(component, "bar", progress);
            U.SetRef(component, "valueText", valueText);
            return component;
        }

        private static void BuildEstimatedScore(Transform parent, CompanyOverviewTab tab)
        {
            var panel = U.Panel("EstimatedScore", parent, U.PanelColor);
            U.Layout(panel.gameObject, -1, -1, 1, -1);
            U.Vertical(panel.gameObject, 4, 12, true, TextAnchor.UpperCenter);

            var title = U.CreateText("SectionTitle", panel, "ESTIMATED FINAL SCORE", 17, Color.white, TextAlignmentOptions.Center);
            U.Layout(title.gameObject, -1, 22, -1, 0);

            U.Spacer(panel, -1, 1);

            var score = U.CreateText("ScoreText", panel, "0", 72, Color.white, TextAlignmentOptions.Center, FontStyles.Bold);
            U.Layout(score.gameObject, -1, 84, -1, 0);
            var outOf = U.CreateText("OutOfText", panel, "/ 100", 20, U.LabelColor, TextAlignmentOptions.Center);
            U.Layout(outOf.gameObject, -1, 24, -1, 0);

            U.Spacer(panel, -1, 1);

            var verdict = U.CreateText("VerdictText", panel, "", 15, Color.white, TextAlignmentOptions.Top, FontStyles.Normal, true);
            U.Layout(verdict.gameObject, -1, 80, -1, 0);

            U.SetRef(tab, "scoreText", score);
            U.SetRef(tab, "verdictText", verdict);
        }

        private static void BuildCompetition(Transform parent, CompanyOverviewTab tab)
        {
            var panel = U.Panel("Competition", parent, U.PanelColor);
            U.Layout(panel.gameObject, -1, -1, 1.1f, -1);
            U.Vertical(panel.gameObject, 8, 12);

            var header = U.Node("Header", panel);
            U.Layout(header.gameObject, -1, 30, -1, 0);
            U.Horizontal(header.gameObject, 8);

            var title = U.CreateText("SectionTitle", header, "COMPETITION", 17, Color.white);
            U.Layout(title.gameObject, -1, -1, 1, -1);
            var sortButton = U.CreateButton("SortButton", header, "Sort: Wealth", 15, 150, 30, out var sortLabel);

            var list = U.Node("CompetitorList", panel);
            U.Layout(list.gameObject, -1, -1, -1, 1);
            U.Vertical(list.gameObject, 4);

            var template = BuildCompetitorRow(list);
            template.gameObject.SetActive(false);

            U.SetRef(tab, "competitorContainer", list);
            U.SetRef(tab, "competitorRowTemplate", template);
            U.SetRef(tab, "sortButton", sortButton);
            U.SetRef(tab, "sortButtonText", sortLabel);
        }

        private static CompetitorRowUI BuildCompetitorRow(Transform parent)
        {
            var row = U.Panel("CompetitorRowTemplate", parent, U.TileColor);
            U.Layout(row.gameObject, -1, 40, -1, 0);
            var layout = U.Horizontal(row.gameObject, 8, 0, false);
            layout.padding = new RectOffset(8, 8, 4, 4);

            var rank = U.CreateText("RankText", row, "1", 20, Color.white, TextAlignmentOptions.Center);
            U.Layout(rank.gameObject, 28, 32, 0, -1);
            U.Placeholder("Avatar", row, 30, 30);
            var name = U.CreateText("NameText", row, "Trader", 17, Color.white);
            U.Layout(name.gameObject, -1, 32, 1, -1);
            var value = U.CreateText("ValueText", row, "0 €", 17, Color.white, TextAlignmentOptions.MidlineRight);
            U.Layout(value.gameObject, 120, 32, 0, -1);

            var component = row.gameObject.AddComponent<CompetitorRowUI>();
            U.SetRef(component, "background", row.GetComponent<Image>());
            U.SetRef(component, "rankText", rank);
            U.SetRef(component, "nameText", name);
            U.SetRef(component, "valueText", value);
            return component;
        }

        // ------------------------------------------------------------------ view manager

        /// <summary>Points the ViewManager's Company entry at the CompanyView (adds the entry if missing).</summary>
        private static void RegisterWithViewManager(CompanyView view)
        {
            var manager = Object.FindFirstObjectByType<ViewManager>(FindObjectsInactive.Include);
            if (manager == null)
            {
                Debug.LogWarning("CompanyViewBuilder: no ViewManager in the scene — assign the Company view manually.");
                return;
            }

            var so = new SerializedObject(manager);
            var views = so.FindProperty("views");
            SerializedProperty entry = null;

            for (int i = 0; i < views.arraySize; i++)
            {
                var candidate = views.GetArrayElementAtIndex(i);
                if (candidate.FindPropertyRelative("type").intValue == (int)ViewType.Company)
                {
                    entry = candidate;
                    break;
                }
            }

            if (entry == null)
            {
                views.arraySize++;
                entry = views.GetArrayElementAtIndex(views.arraySize - 1);
                entry.FindPropertyRelative("type").intValue = (int)ViewType.Company;
            }

            entry.FindPropertyRelative("view").objectReferenceValue = view;
            so.ApplyModifiedProperties();
        }
    }
}
