using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Company;
using AntiqueTradingSimulator.UI;
using AntiqueTradingSimulator.UI.Charts;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;
using U = AntiqueTradingSimulator.EditorTools.UIBuilderUtility;

namespace AntiqueTradingSimulator.EditorTools
{
    /// <summary>
    /// Company → Reputation page: three rows (value cards, level panels, change lists), each
    /// with reputation on the left and credibility on the right. One ReputationColumnUI per
    /// measure (on its change list panel) fills the objects of its side.
    /// </summary>
    public static partial class CompanyViewBuilder
    {
        private struct ReputationSide
        {
            public ReputationKind Kind;
            public string Title;
            public string Subtitle;
            public string LevelTitle;
            public string ListTitle;
        }

        private static readonly ReputationSide[] ReputationSides =
        {
            new ReputationSide
            {
                Kind = ReputationKind.Reputation,
                Title = "Reputation",
                Subtitle = "Your standing in the antique world.",
                LevelTitle = "REPUTATION LEVEL",
                ListTitle = "RECENT REPUTATION CHANGES"
            },
            new ReputationSide
            {
                Kind = ReputationKind.Credibility,
                Title = "Credibility",
                Subtitle = "Your reliability and trustworthiness.",
                LevelTitle = "CREDIBILITY LEVEL",
                ListTitle = "RECENT CREDIBILITY CHANGES"
            },
        };

        private static CompanyReputationTab BuildReputationPage(Transform pages)
        {
            var page = U.Node("ReputationPage", pages);
            U.Stretch(page);
            U.Vertical(page.gameObject, 10);

            var tab = page.gameObject.AddComponent<CompanyReputationTab>();
            U.SetRef(tab, "playerTrader", Object.FindFirstObjectByType<PlayerTrader>(FindObjectsInactive.Include));

            var valueRow = U.Node("ValueRow", page);
            U.Layout(valueRow.gameObject, -1, 180, -1, 0);
            U.Horizontal(valueRow.gameObject, 10);

            var levelRow = U.Node("LevelRow", page);
            U.Layout(levelRow.gameObject, -1, 262, -1, 0);
            U.Horizontal(levelRow.gameObject, 10);

            var listRow = U.Node("ChangesRow", page);
            U.Layout(listRow.gameObject, -1, -1, -1, 1);
            U.Horizontal(listRow.gameObject, 10);

            var columns = new ReputationColumnUI[ReputationSides.Length];
            for (int i = 0; i < ReputationSides.Length; i++)
            {
                var side = ReputationSides[i];

                // The column lives on its change list panel — the only row that's never hidden.
                var listPanel = BuildChangeList(listRow, side, out var changeList);
                var column = listPanel.gameObject.AddComponent<ReputationColumnUI>();
                SetKind(column, side.Kind);
                U.SetRef(column, "changeList", changeList);

                BuildValueCard(valueRow, side, column);
                BuildLevelPanel(levelRow, side, column);
                columns[i] = column;
            }

            U.SetRef(tab, "reputationColumn", columns[0]);
            U.SetRef(tab, "credibilityColumn", columns[1]);
            U.SetRefs(tab, "hideWhenExpanded", new Object[] { valueRow.gameObject, levelRow.gameObject });
            return tab;
        }

        private static void SetKind(ReputationColumnUI column, ReputationKind kind)
        {
            var so = new SerializedObject(column);
            so.FindProperty("kind").enumValueIndex = (int)kind;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        // ------------------------------------------------------------------ value card

        private static void BuildValueCard(Transform parent, ReputationSide side, ReputationColumnUI column)
        {
            var card = U.Panel($"{side.Title}Card", parent, U.PanelColor);
            U.Layout(card.gameObject, -1, -1, 1, -1);
            U.Horizontal(card.gameObject, 12, 12);

            U.Placeholder("Illustration", card, 140, 156);

            var info = U.Node("Info", card);
            U.Layout(info.gameObject, -1, -1, 0.85f, -1);
            U.Vertical(info.gameObject, 2);
            var title = U.CreateText("TitleText", info, side.Title, 26, Color.white);
            U.Layout(title.gameObject, -1, 32, -1, 0);
            var subtitle = U.CreateText("SubtitleText", info, side.Subtitle, 14, U.LabelColor,
                TextAlignmentOptions.TopLeft, FontStyles.Normal, true);
            U.Layout(subtitle.gameObject, -1, 36, -1, 0);
            U.Spacer(info, -1, 1);
            var value = U.CreateText("ValueText", info, "—", 44, Color.white, TextAlignmentOptions.MidlineLeft, FontStyles.Bold);
            U.Layout(value.gameObject, -1, 52, -1, 0);
            var change = U.CreateText("ChangeText", info, "", 16, U.LabelColor);
            U.Layout(change.gameObject, -1, 22, -1, 0);

            var chartColumn = U.Node("ChartColumn", card);
            U.Layout(chartColumn.gameObject, -1, -1, 1.15f, -1);
            U.Vertical(chartColumn.gameObject, 2);

            var chart = BuildLineChart(chartColumn, "Chart");

            var axis = U.Node("AxisRow", chartColumn);
            U.Layout(axis.gameObject, -1, 16, -1, 0);
            U.Horizontal(axis.gameObject, 0);
            var start = U.CreateText("StartText", axis, "", 12, U.LabelColor);
            U.Layout(start.gameObject, -1, -1, 1, -1);
            var end = U.CreateText("EndText", axis, "Today", 12, U.LabelColor, TextAlignmentOptions.MidlineRight);
            U.Layout(end.gameObject, -1, -1, 1, -1);

            U.SetRef(column, "valueText", value);
            U.SetRef(column, "changeText", change);
            U.SetRef(column, "chart", chart);
            U.SetRef(column, "chartStartText", start);
        }

        /// <summary>
        /// Inset area with a UILineChart filling it, max/min labels in the left corners and an
        /// "empty" label in the middle. Reused by the Finances page.
        /// </summary>
        private static UILineChart BuildLineChart(Transform parent, string name)
        {
            var area = U.Panel(name + "Area", parent, U.InsetColor);
            area.GetComponent<Image>().raycastTarget = false;
            U.Layout(area.gameObject, -1, -1, -1, 1, -1, 60);

            var chartNode = U.Node(name, area);
            U.Stretch(chartNode);
            var chart = chartNode.gameObject.AddComponent<UILineChart>();
            chart.raycastTarget = false;

            var max = U.CreateText("MaxLabel", area, "", 12, U.LabelColor, TextAlignmentOptions.TopLeft);
            max.rectTransform.anchorMin = new Vector2(0f, 1f);
            max.rectTransform.anchorMax = new Vector2(0f, 1f);
            max.rectTransform.pivot = new Vector2(0f, 1f);
            max.rectTransform.sizeDelta = new Vector2(120f, 16f);
            max.rectTransform.anchoredPosition = new Vector2(4f, -2f);

            var min = U.CreateText("MinLabel", area, "", 12, U.LabelColor, TextAlignmentOptions.BottomLeft);
            min.rectTransform.anchorMin = Vector2.zero;
            min.rectTransform.anchorMax = Vector2.zero;
            min.rectTransform.pivot = Vector2.zero;
            min.rectTransform.sizeDelta = new Vector2(120f, 16f);
            min.rectTransform.anchoredPosition = new Vector2(4f, 2f);

            var empty = U.CreateText("EmptyLabel", area, "Not enough history yet", 14, U.LabelColor, TextAlignmentOptions.Center);
            U.Stretch(empty.rectTransform);

            U.SetRef(chart, "maxLabel", max);
            U.SetRef(chart, "minLabel", min);
            U.SetRef(chart, "emptyLabel", empty);
            return chart;
        }

        // ------------------------------------------------------------------ level panel

        private static void BuildLevelPanel(Transform parent, ReputationSide side, ReputationColumnUI column)
        {
            var panel = U.Panel($"{side.Title}Level", parent, U.PanelColor);
            U.Layout(panel.gameObject, -1, -1, 1, -1);
            U.Vertical(panel.gameObject, 8, 12);

            SectionTitle(panel, side.LevelTitle);

            // Tier ladder
            var tierBarNode = U.Node("TierBar", panel);
            U.Layout(tierBarNode.gameObject, -1, 52, -1, 0);
            var tierLayout = U.Horizontal(tierBarNode.gameObject, 4);
            tierLayout.childForceExpandWidth = true;
            var tierBar = tierBarNode.gameObject.AddComponent<TierBarUI>();
            var segment = BuildTierSegment(tierBarNode);
            segment.gameObject.SetActive(false);
            U.SetRef(tierBar, "container", tierBarNode);
            U.SetRef(tierBar, "segmentTemplate", segment);

            // Current level | next level
            var bottom = U.Node("Levels", panel);
            U.Layout(bottom.gameObject, -1, -1, -1, 1);
            U.Horizontal(bottom.gameObject, 10);

            var current = U.Node("CurrentLevel", bottom);
            U.Layout(current.gameObject, -1, -1, 1, -1);
            U.Horizontal(current.gameObject, 10, 0, false, TextAnchor.UpperLeft);
            U.Placeholder("Icon", current, 72, 72);
            var currentText = U.Node("Text", current);
            U.Layout(currentText.gameObject, -1, -1, 1, 1);
            U.Vertical(currentText.gameObject, 2);
            var label = U.CreateText("Label", currentText, "Current level", 13, U.LabelColor);
            U.Layout(label.gameObject, -1, 18, -1, 0);
            var tierName = U.CreateText("TierNameText", currentText, "—", 22, Color.white);
            U.Layout(tierName.gameObject, -1, 28, -1, 0);
            var description = U.CreateText("DescriptionText", currentText, "", 14, Color.white,
                TextAlignmentOptions.TopLeft, FontStyles.Normal, true);
            U.Layout(description.gameObject, -1, -1, -1, 1);

            var next = U.Panel("NextLevel", bottom, U.TileColor);
            U.Layout(next.gameObject, -1, -1, 1, -1);
            U.Vertical(next.gameObject, 4, 8);
            var nextHeader = U.CreateText("NextHeaderText", next, "Next level", 16, Color.white);
            U.Layout(nextHeader.gameObject, -1, 22, -1, 0);
            var progress = U.CreateText("ProgressText", next, "", 14, Color.white);
            U.Layout(progress.gameObject, -1, 20, -1, 0);
            var unlocks = U.CreateText("UnlocksText", next, "", 14, Color.white,
                TextAlignmentOptions.TopLeft, FontStyles.Normal, true);
            U.Layout(unlocks.gameObject, -1, -1, -1, 1);

            U.SetRef(column, "tierBar", tierBar);
            U.SetRef(column, "currentTierText", tierName);
            U.SetRef(column, "currentDescriptionText", description);
            U.SetRef(column, "nextHeaderText", nextHeader);
            U.SetRef(column, "nextProgressText", progress);
            U.SetRef(column, "nextUnlocksText", unlocks);
        }

        private static TierSegmentUI BuildTierSegment(Transform parent)
        {
            var segment = U.Panel("TierSegmentTemplate", parent, U.Clear);
            segment.GetComponent<Image>().raycastTarget = false;
            U.Layout(segment.gameObject, -1, -1, 1, -1);
            U.Vertical(segment.gameObject, 2);

            var bar = U.Bar("Bar", segment, -1, 10, UIFormat.AccentColor, out var fill, out var fillImage);
            var progress = bar.gameObject.AddComponent<ProgressBarUI>();
            U.SetRef(progress, "fill", fill);
            U.SetRef(progress, "fillImage", fillImage);

            var name = U.CreateText("NameText", segment, "Tier", 13, U.LabelColor, TextAlignmentOptions.Center);
            U.Layout(name.gameObject, -1, 18, -1, 0);
            var range = U.CreateText("RangeText", segment, "0 - 0", 12, U.LabelColor, TextAlignmentOptions.Center);
            U.Layout(range.gameObject, -1, 16, -1, 0);

            var component = segment.gameObject.AddComponent<TierSegmentUI>();
            U.SetRef(component, "background", segment.GetComponent<Image>());
            U.SetRef(component, "bar", progress);
            U.SetRef(component, "nameText", name);
            U.SetRef(component, "rangeText", range);
            return component;
        }

        // ------------------------------------------------------------------ change list

        private static RectTransform BuildChangeList(Transform parent, ReputationSide side, out ReputationChangeListUI list)
        {
            var panel = U.Panel($"{side.Title}Changes", parent, U.PanelColor);
            U.Layout(panel.gameObject, -1, -1, 1, -1);
            U.Vertical(panel.gameObject, 6, 10);

            var header = U.Node("Header", panel);
            U.Layout(header.gameObject, -1, 30, -1, 0);
            U.Horizontal(header.gameObject, 8);
            var title = U.CreateText("SectionTitle", header, side.ListTitle, 17, Color.white);
            U.Layout(title.gameObject, -1, -1, 1, -1);
            var toggle = U.CreateButton("ShowAllButton", header, "Show all", 15, 110, 28, out var toggleLabel);

            var rows = U.Node("Rows", panel);
            U.Layout(rows.gameObject, -1, -1, -1, 1);
            U.Vertical(rows.gameObject, 2);
            var template = BuildChangeRow(rows);
            template.gameObject.SetActive(false);

            var empty = U.CreateText("EmptyText", panel, "No changes yet.", 14, U.LabelColor);
            U.Layout(empty.gameObject, -1, 22, -1, 0);

            var pagination = U.Node("PaginationRow", panel);
            U.Layout(pagination.gameObject, -1, 32, -1, 0);
            U.Horizontal(pagination.gameObject, 6, 0, false, TextAnchor.MiddleCenter);
            var previous = U.CreateButton("PreviousButton", pagination, "<", 20, 32, 32, out _);
            var pageText = U.CreateText("PageText", pagination, "1 / 1", 16, Color.white, TextAlignmentOptions.Center);
            U.Layout(pageText.gameObject, 90, 32, 0, -1);
            var next = U.CreateButton("NextButton", pagination, ">", 20, 32, 32, out _);
            pagination.gameObject.SetActive(false);

            list = panel.gameObject.AddComponent<ReputationChangeListUI>();
            U.SetRef(list, "rowContainer", rows);
            U.SetRef(list, "rowTemplate", template);
            U.SetRef(list, "emptyText", empty);
            U.SetRef(list, "toggleButton", toggle);
            U.SetRef(list, "toggleButtonText", toggleLabel);
            U.SetRef(list, "paginationRow", pagination.gameObject);
            U.SetRef(list, "previousButton", previous);
            U.SetRef(list, "nextButton", next);
            U.SetRef(list, "pageText", pageText);
            return panel;
        }

        private static ReputationChangeRowUI BuildChangeRow(Transform parent)
        {
            var row = U.Node("ChangeRowTemplate", parent);
            U.Layout(row.gameObject, -1, 24, -1, 0);
            U.Horizontal(row.gameObject, 10, 0, false);

            var date = U.CreateText("DateText", row, "22 April", 14, U.LabelColor);
            U.Layout(date.gameObject, 90, 24, 0, -1);
            var delta = U.CreateText("DeltaText", row, "+0", 14, Color.white, TextAlignmentOptions.MidlineRight);
            U.Layout(delta.gameObject, 60, 24, 0, -1);
            var marker = U.Panel("Marker", row, UIFormat.PositiveColor);
            marker.GetComponent<Image>().raycastTarget = false;
            U.Layout(marker.gameObject, 10, 10, 0, 0, 10, 10);
            var reason = U.CreateText("ReasonText", row, "", 14, Color.white);
            U.Layout(reason.gameObject, -1, 24, 1, -1);

            var component = row.gameObject.AddComponent<ReputationChangeRowUI>();
            U.SetRef(component, "dateText", date);
            U.SetRef(component, "deltaText", delta);
            U.SetRef(component, "marker", marker.GetComponent<Image>());
            U.SetRef(component, "reasonText", reason);
            return component;
        }
    }
}
