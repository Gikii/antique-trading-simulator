using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using U = AntiqueTradingSimulator.EditorTools.UIBuilderUtility;

namespace AntiqueTradingSimulator.EditorTools
{
    /// <summary>Company → Development page: summary strip, scrollable upgrade cards, details panel.</summary>
    public static partial class CompanyViewBuilder
    {
        private const float UpgradeCardHeight = 100f;
        private const float UpgradeDetailsWidth = 430f;

        private static CompanyDevelopmentTab BuildDevelopmentPage(Transform pages)
        {
            var page = U.Node("DevelopmentPage", pages);
            U.Stretch(page);
            U.Vertical(page.gameObject, 10);

            var tab = page.gameObject.AddComponent<CompanyDevelopmentTab>();
            U.SetRef(tab, "playerTrader", Object.FindFirstObjectByType<PlayerTrader>(FindObjectsInactive.Include));

            BuildDevelopmentSummary(page, tab);

            var body = U.Node("Body", page);
            U.Layout(body.gameObject, -1, -1, -1, 1);
            U.Horizontal(body.gameObject, 10);

            BuildUpgradeList(body, tab);
            BuildUpgradeDetails(body, tab);

            return tab;
        }

        private static void BuildDevelopmentSummary(Transform parent, CompanyDevelopmentTab tab)
        {
            var row = U.Node("SummaryRow", parent);
            U.Layout(row.gameObject, -1, 44, -1, 0);
            U.Horizontal(row.gameObject, 10);

            var summary = U.Panel("SummaryPanel", row, U.PanelColor);
            U.Layout(summary.gameObject, -1, -1, 1.3f, -1);
            var summaryLayout = U.Horizontal(summary.gameObject, 0);
            summaryLayout.padding = new RectOffset(12, 12, 0, 0);
            var summaryText = U.CreateText("SummaryText", summary, "Cash: —     Reputation: —     Total upkeep: —", 17, Color.white);
            U.Layout(summaryText.gameObject, -1, -1, 1, -1);

            var hint = U.Panel("HintPanel", row, U.PanelColor);
            U.Layout(hint.gameObject, -1, -1, 1f, -1);
            var hintLayout = U.Horizontal(hint.gameObject, 10);
            hintLayout.padding = new RectOffset(12, 12, 0, 0);
            U.Placeholder("InfoIcon", hint, 22, 22);
            var hintText = U.CreateText("HintText", hint,
                "Upgrades are paid in cash. Higher levels require reputation — complete contracts and grow your business to unlock them.",
                14, U.LabelColor, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, true);
            U.Layout(hintText.gameObject, -1, -1, 1, -1);

            U.SetRef(tab, "summaryText", summaryText);
        }

        // ------------------------------------------------------------------ list

        private static void BuildUpgradeList(Transform parent, CompanyDevelopmentTab tab)
        {
            var list = U.Node("UpgradeList", parent);
            U.Layout(list.gameObject, -1, -1, 1, -1);
            U.AddImage(list.gameObject, U.Clear); // catches the mouse wheel over gaps

            var viewport = U.Node("Viewport", list);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.pivot = new Vector2(0f, 1f);
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = new Vector2(-16f, 0f); // room for the scrollbar
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = U.Node("Content", viewport);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.offsetMin = Vector2.zero;
            content.offsetMax = Vector2.zero;
            U.Vertical(content.gameObject, 6);
            var fitter = content.gameObject.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scrollbar = BuildVerticalScrollbar(list);

            var scroll = list.gameObject.AddComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 30f;
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;

            var template = BuildUpgradeCard(content);
            template.gameObject.SetActive(false);

            U.SetRef(tab, "cardContainer", content);
            U.SetRef(tab, "cardTemplate", template);
            U.SetRef(tab, "scrollRect", scroll);
        }

        private static Scrollbar BuildVerticalScrollbar(Transform parent)
        {
            var bar = U.Panel("Scrollbar", parent, U.BarBackgroundColor);
            bar.anchorMin = new Vector2(1f, 0f);
            bar.anchorMax = new Vector2(1f, 1f);
            bar.pivot = new Vector2(1f, 0.5f);
            bar.sizeDelta = new Vector2(10f, 0f);
            bar.anchoredPosition = Vector2.zero;

            var slidingArea = U.Node("SlidingArea", bar);
            U.Stretch(slidingArea);

            var handle = U.Panel("Handle", slidingArea, new Color(1f, 1f, 1f, 0.5f));
            U.Stretch(handle);

            var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handle.GetComponent<Image>();
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            return scrollbar;
        }

        private static UpgradeCardUI BuildUpgradeCard(Transform parent)
        {
            var card = U.Panel("UpgradeCardTemplate", parent, U.PanelColor);
            U.Layout(card.gameObject, -1, UpgradeCardHeight, -1, 0);
            var layout = U.Horizontal(card.gameObject, 14);
            layout.padding = new RectOffset(8, 10, 8, 8);

            // Whole-card button for selecting; the card colours itself, so no tint.
            var selectButton = card.gameObject.AddComponent<Button>();
            selectButton.targetGraphic = card.GetComponent<Image>();
            selectButton.transition = Selectable.Transition.None;

            U.Placeholder("Illustration", card, 110, 84);

            // Name, description, "effect coming soon"
            var info = U.Node("InfoColumn", card);
            U.Layout(info.gameObject, -1, -1, 1.5f, -1);
            U.Vertical(info.gameObject, 2);
            var name = U.CreateText("NameText", info, "Upgrade", 20, Color.white);
            U.Layout(name.gameObject, -1, 26, -1, 0);
            var description = U.CreateText("DescriptionText", info, "", 13, U.LabelColor,
                TextAlignmentOptions.TopLeft, FontStyles.Normal, true);
            U.Layout(description.gameObject, -1, -1, -1, 1);
            var status = U.CreateText("StatusText", info, "", 12, U.LabelColor, TextAlignmentOptions.MidlineLeft, FontStyles.Italic);
            U.Layout(status.gameObject, -1, 16, -1, 0);

            // Level
            var levelColumn = U.Node("LevelColumn", card);
            U.Layout(levelColumn.gameObject, 120, -1, 0, -1);
            U.Vertical(levelColumn.gameObject, 6, 0, true, TextAnchor.MiddleCenter);
            var level = U.CreateText("LevelText", levelColumn, "Level 1 / 5", 17, Color.white, TextAlignmentOptions.Center);
            U.Layout(level.gameObject, -1, 22, -1, 0);
            var segments = U.Node("LevelBar", levelColumn);
            U.Layout(segments.gameObject, -1, 10, -1, 0);
            U.Horizontal(segments.gameObject, 3);
            var levelBar = segments.gameObject.AddComponent<SegmentedBarUI>();
            U.SetRef(levelBar, "container", segments);

            // Current / next effect
            var current = U.CreateText("CurrentEffectText", card, "", 14, Color.white, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, true);
            U.Layout(current.gameObject, -1, -1, 1f, -1);
            var next = U.CreateText("NextEffectText", card, "", 14, Color.white, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, true);
            U.Layout(next.gameObject, -1, -1, 1f, -1);

            // Cost + button
            var costColumn = U.Node("CostColumn", card);
            U.Layout(costColumn.gameObject, 160, -1, 0, -1);
            U.Vertical(costColumn.gameObject, 4, 0, true, TextAnchor.MiddleCenter);
            var cost = U.CreateText("CostText", costColumn, "", 17, Color.white, TextAlignmentOptions.Center, FontStyles.Normal, true);
            U.Layout(cost.gameObject, -1, -1, -1, 1);
            var upgradeButton = U.CreateButton("UpgradeButton", costColumn, "Upgrade", 18, -1, 32, out var upgradeLabel);
            U.Layout(upgradeButton.gameObject, -1, 32, -1, 0);

            var component = card.gameObject.AddComponent<UpgradeCardUI>();
            U.SetRef(component, "background", card.GetComponent<Image>());
            U.SetRef(component, "selectButton", selectButton);
            U.SetRef(component, "nameText", name);
            U.SetRef(component, "descriptionText", description);
            U.SetRef(component, "statusText", status);
            U.SetRef(component, "levelText", level);
            U.SetRef(component, "levelBar", levelBar);
            U.SetRef(component, "currentEffectText", current);
            U.SetRef(component, "nextEffectText", next);
            U.SetRef(component, "costText", cost);
            U.SetRef(component, "upgradeButton", upgradeButton);
            U.SetRef(component, "upgradeButtonText", upgradeLabel);
            return component;
        }

        // ------------------------------------------------------------------ details

        private static void BuildUpgradeDetails(Transform parent, CompanyDevelopmentTab tab)
        {
            var panel = U.Panel("UpgradeDetails", parent, U.PanelColor);
            U.Layout(panel.gameObject, UpgradeDetailsWidth, -1, 0, -1);
            U.Vertical(panel.gameObject, 6, 10);

            // Header: icon, name, level
            var header = U.Node("Header", panel);
            U.Layout(header.gameObject, -1, 48, -1, 0);
            U.Horizontal(header.gameObject, 10);
            U.Placeholder("Icon", header, 48, 48);
            var titleColumn = U.Node("TitleColumn", header);
            U.Layout(titleColumn.gameObject, -1, -1, 1, -1);
            U.Vertical(titleColumn.gameObject, 2, 0, true, TextAnchor.MiddleLeft);
            var name = U.CreateText("NameText", titleColumn, "Warehouse", 24, Color.white);
            U.Layout(name.gameObject, -1, 28, -1, 0);
            var level = U.CreateText("LevelText", titleColumn, "Level 1 / 5", 16, UIFormat.AccentColor);
            U.Layout(level.gameObject, -1, 20, -1, 0);

            var segments = U.Node("LevelBar", panel);
            U.Layout(segments.gameObject, -1, 8, -1, 0);
            U.Horizontal(segments.gameObject, 3);
            var levelBar = segments.gameObject.AddComponent<SegmentedBarUI>();
            U.SetRef(levelBar, "container", segments);

            var illustration = U.Panel("Illustration", panel, U.PlaceholderColor);
            illustration.GetComponent<Image>().raycastTarget = false;
            U.Layout(illustration.gameObject, -1, 90, -1, 0);

            var description = U.CreateText("DescriptionText", panel, "", 15, Color.white,
                TextAlignmentOptions.TopLeft, FontStyles.Normal, true);
            U.Layout(description.gameObject, -1, 54, -1, 0);

            var note = U.CreateText("EffectNoteText", panel, "", 13, U.LabelColor,
                TextAlignmentOptions.TopLeft, FontStyles.Italic, true);
            U.Layout(note.gameObject, -1, 34, -1, 0);

            BuildRowsBox(panel, "CurrentLevelBox", "CURRENT LEVEL", out var currentHeader, out var currentRows);
            var rowTemplate = BuildKeyValueRow(currentRows);
            rowTemplate.gameObject.SetActive(false);
            BuildRowsBox(panel, "NextLevelBox", "NEXT LEVEL", out var nextHeader, out var nextRows);

            U.Spacer(panel, -1, 1);

            var hint = U.CreateText("HintText", panel, "", 14, U.LabelColor, TextAlignmentOptions.BottomLeft, FontStyles.Normal, true);
            U.Layout(hint.gameObject, -1, 36, -1, 0);

            var upgradeButton = U.CreateButton("UpgradeButton", panel, "Upgrade", 22, -1, 44, out var upgradeLabel);
            U.Layout(upgradeButton.gameObject, -1, 44, -1, 0);

            var details = panel.gameObject.AddComponent<UpgradeDetailsUI>();
            U.SetRef(details, "nameText", name);
            U.SetRef(details, "levelText", level);
            U.SetRef(details, "levelBar", levelBar);
            U.SetRef(details, "descriptionText", description);
            U.SetRef(details, "effectNoteText", note);
            U.SetRef(details, "currentHeaderText", currentHeader);
            U.SetRef(details, "currentRows", currentRows);
            U.SetRef(details, "nextHeaderText", nextHeader);
            U.SetRef(details, "nextRows", nextRows);
            U.SetRef(details, "rowTemplate", rowTemplate);
            U.SetRef(details, "hintText", hint);
            U.SetRef(details, "upgradeButton", upgradeButton);
            U.SetRef(details, "upgradeButtonText", upgradeLabel);

            U.SetRef(tab, "details", details);
        }

        /// <summary>Tile with a header line and a vertical list of key/value rows; height follows its rows.</summary>
        private static RectTransform BuildRowsBox(Transform parent, string name, string title,
            out TextMeshProUGUI header, out RectTransform rows)
        {
            var box = U.Panel(name, parent, U.TileColor);
            U.Layout(box.gameObject, -1, -1, -1, 0);
            U.Vertical(box.gameObject, 4, 8);

            header = U.CreateText("HeaderText", box, title, 15, UIFormat.AccentColor);
            U.Layout(header.gameObject, -1, 20, -1, 0);

            rows = U.Node("Rows", box);
            U.Vertical(rows.gameObject, 2);
            return box;
        }

        private static KeyValueRowUI BuildKeyValueRow(Transform parent)
        {
            var row = U.Node("RowTemplate", parent);
            U.Layout(row.gameObject, -1, 22, -1, 0);
            U.Horizontal(row.gameObject, 8);

            var label = U.CreateText("LabelText", row, "Label", 15, U.LabelColor);
            U.Layout(label.gameObject, -1, -1, 1.2f, -1);
            var value = U.CreateText("ValueText", row, "Value", 15, Color.white, TextAlignmentOptions.MidlineRight);
            U.Layout(value.gameObject, -1, -1, 1f, -1);

            var component = row.gameObject.AddComponent<KeyValueRowUI>();
            U.SetRef(component, "labelText", label);
            U.SetRef(component, "valueText", value);
            return component;
        }
    }
}
