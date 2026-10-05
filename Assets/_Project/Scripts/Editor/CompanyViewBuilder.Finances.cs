using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.UI;
using AntiqueTradingSimulator.UI.Charts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using U = AntiqueTradingSimulator.EditorTools.UIBuilderUtility;

namespace AntiqueTradingSimulator.EditorTools
{
    /// <summary>
    /// Company → Finances page: period buttons, overview tiles, cash/assets chart and the
    /// transaction table on the left; income/expenses by category and the expense donut
    /// on the right.
    /// </summary>
    public static partial class CompanyViewBuilder
    {
        private static readonly string[] PeriodLabels = { "Today", "Last 7 days", "Full campaign" };
        private static readonly float[] PeriodWidths = { 110f, 150f, 170f };

        private const float FinanceRowHeight = 22f;

        private static CompanyFinancesTab BuildFinancesPage(Transform pages)
        {
            var page = U.Node("FinancesPage", pages);
            U.Stretch(page);
            U.Vertical(page.gameObject, 10);

            var tab = page.gameObject.AddComponent<CompanyFinancesTab>();
            U.SetRef(tab, "playerTrader", Object.FindFirstObjectByType<PlayerTrader>(FindObjectsInactive.Include));

            // Period buttons, right-aligned like the mockup.
            var periodRow = U.Node("PeriodRow", page);
            U.Layout(periodRow.gameObject, -1, 36, -1, 0);
            U.Horizontal(periodRow.gameObject, 6);
            U.Spacer(periodRow);
            var periodButtons = new Object[PeriodLabels.Length];
            for (int i = 0; i < PeriodLabels.Length; i++)
                periodButtons[i] = U.CreateButton($"{PeriodLabels[i].Replace(" ", "")}Button", periodRow, PeriodLabels[i], 18, PeriodWidths[i], 36, out _);
            U.SetRefs(tab, "periodButtons", periodButtons);

            var body = U.Node("Body", page);
            U.Layout(body.gameObject, -1, -1, -1, 1);
            U.Horizontal(body.gameObject, 10);

            var left = U.Node("LeftColumn", body);
            U.Layout(left.gameObject, -1, -1, 1.7f, -1);
            U.Vertical(left.gameObject, 10);

            var overview = BuildFinanceOverview(left, tab);
            var chart = BuildCashChart(left, tab);
            BuildTransactions(left, tab);
            U.SetRefs(tab, "hideWhenExpanded", new Object[] { overview.gameObject, chart.gameObject });

            var right = U.Node("RightColumn", body);
            U.Layout(right.gameObject, -1, -1, 1f, -1);
            U.Vertical(right.gameObject, 10);

            BuildIncomeExpenses(right, tab);
            BuildExpenseDonut(right, tab);

            return tab;
        }

        // ------------------------------------------------------------------ overview

        private static RectTransform BuildFinanceOverview(Transform parent, CompanyFinancesTab tab)
        {
            var panel = U.Panel("FinancialOverview", parent, U.PanelColor);
            U.Layout(panel.gameObject, -1, 140, -1, 0);
            U.Vertical(panel.gameObject, 8, 12);

            var title = SectionTitle(panel, "FINANCIAL OVERVIEW (LAST 7 DAYS)");

            var tiles = U.Node("Tiles", panel);
            U.Layout(tiles.gameObject, -1, -1, -1, 1);
            var layout = U.Horizontal(tiles.gameObject, 8);
            layout.childForceExpandWidth = true;

            U.SetRef(tab, "overviewTitleText", title);
            U.SetRef(tab, "incomeTile", BuildCompactTile(tiles, "IncomeTile", "Total Income"));
            U.SetRef(tab, "expensesTile", BuildCompactTile(tiles, "ExpensesTile", "Total Expenses"));
            U.SetRef(tab, "profitTile", BuildCompactTile(tiles, "ProfitTile", "Net Profit"));
            U.SetRef(tab, "cashTile", BuildCompactTile(tiles, "CashTile", "Cash Balance"));
            return panel;
        }

        /// <summary>Stat tile with the icon on the left (lower than the Overview tiles).</summary>
        private static StatTileUI BuildCompactTile(Transform parent, string name, string label)
        {
            var tile = U.Panel(name, parent, U.TileColor);
            U.Layout(tile.gameObject, -1, -1, 1, -1);
            var layout = U.Horizontal(tile.gameObject, 10, 8, false);
            layout.childAlignment = TextAnchor.MiddleLeft;

            U.Placeholder("Icon", tile, 36, 36);

            var text = U.Node("Text", tile);
            U.Layout(text.gameObject, -1, -1, 1, 1);
            U.Vertical(text.gameObject, 2, 0, true, TextAnchor.MiddleLeft);

            var labelText = U.CreateText("LabelText", text, label, 14, U.LabelColor);
            U.Layout(labelText.gameObject, -1, 18, -1, 0);
            var valueText = U.CreateText("ValueText", text, "—", 22, Color.white);
            U.Layout(valueText.gameObject, -1, 28, -1, 0);
            var changeText = U.CreateText("ChangeText", text, "", 12, U.LabelColor);
            U.Layout(changeText.gameObject, -1, 16, -1, 0);

            var component = tile.gameObject.AddComponent<StatTileUI>();
            U.SetRef(component, "labelText", labelText);
            U.SetRef(component, "valueText", valueText);
            U.SetRef(component, "changeText", changeText);
            return component;
        }

        // ------------------------------------------------------------------ chart

        private static RectTransform BuildCashChart(Transform parent, CompanyFinancesTab tab)
        {
            var panel = U.Panel("CashAndAssets", parent, U.PanelColor);
            U.Layout(panel.gameObject, -1, -1, -1, 1);
            U.Vertical(panel.gameObject, 6, 12);

            var header = U.Node("Header", panel);
            U.Layout(header.gameObject, -1, 22, -1, 0);
            U.Horizontal(header.gameObject, 12);
            var title = U.CreateText("SectionTitle", header, "CASH AND ASSETS (LAST 7 DAYS)", 17, Color.white);
            U.Layout(title.gameObject, -1, -1, 1, -1);
            var legend = U.CreateText("LegendText", header,
                $"{UIFormat.Colorize("■", new Color32(0x59, 0xA1, 0x4F, 0xFF))} Cash     {UIFormat.Colorize("■", UIFormat.AccentColor)} Total assets",
                14, Color.white, TextAlignmentOptions.MidlineRight);
            U.Layout(legend.gameObject, 260, -1, 0, -1);

            // Total assets first (behind), cash on top; both share the range set by the tab.
            var wealth = BuildLineChart(panel, "WealthChart");
            var area = wealth.transform.parent;
            var cashNode = U.Node("CashChart", area);
            U.Stretch(cashNode);
            cashNode.SetSiblingIndex(wealth.transform.GetSiblingIndex() + 1);
            var cash = cashNode.gameObject.AddComponent<UILineChart>();
            cash.raycastTarget = false;

            var axis = U.Node("AxisRow", panel);
            U.Layout(axis.gameObject, -1, 16, -1, 0);
            U.Horizontal(axis.gameObject, 0);
            var start = U.CreateText("StartText", axis, "", 12, U.LabelColor);
            U.Layout(start.gameObject, -1, -1, 1, -1);
            var end = U.CreateText("EndText", axis, "Today", 12, U.LabelColor, TextAlignmentOptions.MidlineRight);
            U.Layout(end.gameObject, -1, -1, 1, -1);

            U.SetRef(tab, "chartTitleText", title);
            U.SetRef(tab, "wealthChart", wealth);
            U.SetRef(tab, "cashChart", cash);
            U.SetRef(tab, "chartStartText", start);
            return panel;
        }

        // ------------------------------------------------------------------ transactions

        private static void BuildTransactions(Transform parent, CompanyFinancesTab tab)
        {
            var panel = U.Panel("RecentTransactions", parent, U.PanelColor);
            // Tiny flexible height: keeps 270 next to the chart, takes the whole column when the chart hides.
            U.Layout(panel.gameObject, -1, 270, -1, 0.001f);
            U.Vertical(panel.gameObject, 6, 10);

            var header = U.Node("Header", panel);
            U.Layout(header.gameObject, -1, 30, -1, 0);
            U.Horizontal(header.gameObject, 8);
            var title = U.CreateText("SectionTitle", header, "RECENT TRANSACTIONS", 17, Color.white);
            U.Layout(title.gameObject, -1, -1, 1, -1);
            var toggle = U.CreateButton("ShowAllButton", header, "Show all", 15, 110, 28, out var toggleLabel);

            var columns = BuildTransactionColumns(panel, "ColumnHeader", true);
            U.Layout(columns.gameObject, -1, 20, -1, 0);

            var rows = U.Node("Rows", panel);
            U.Layout(rows.gameObject, -1, -1, -1, 1);
            U.Vertical(rows.gameObject, 2);
            var template = BuildTransactionRow(rows);
            template.gameObject.SetActive(false);

            var empty = U.CreateText("EmptyText", panel, "No transactions in this period.", 14, U.LabelColor);
            U.Layout(empty.gameObject, -1, 22, -1, 0);

            var pagination = U.Node("PaginationRow", panel);
            U.Layout(pagination.gameObject, -1, 32, -1, 0);
            U.Horizontal(pagination.gameObject, 6, 0, false, TextAnchor.MiddleCenter);
            var previous = U.CreateButton("PreviousButton", pagination, "<", 20, 32, 32, out _);
            var pageText = U.CreateText("PageText", pagination, "1 / 1", 16, Color.white, TextAlignmentOptions.Center);
            U.Layout(pageText.gameObject, 90, 32, 0, -1);
            var next = U.CreateButton("NextButton", pagination, ">", 20, 32, 32, out _);
            pagination.gameObject.SetActive(false);

            var list = panel.gameObject.AddComponent<TransactionListUI>();
            U.SetRef(list, "rowContainer", rows);
            U.SetRef(list, "rowTemplate", template);
            U.SetRef(list, "emptyText", empty);
            U.SetRef(list, "toggleButton", toggle);
            U.SetRef(list, "toggleButtonText", toggleLabel);
            U.SetRef(list, "paginationRow", pagination.gameObject);
            U.SetRef(list, "previousButton", previous);
            U.SetRef(list, "nextButton", next);
            U.SetRef(list, "pageText", pageText);

            U.SetRef(tab, "transactions", list);
        }

        /// <summary>Date | Type | Description | Amount — used for the header and for the row template.</summary>
        private static RectTransform BuildTransactionColumns(Transform parent, string name, bool header,
            TMP_Text[] texts = null)
        {
            var row = U.Node(name, parent);
            U.Horizontal(row.gameObject, 10, 0, false);
            var color = header ? U.LabelColor : Color.white;
            float size = header ? 13 : 14;

            var date = U.CreateText("DateText", row, header ? "Date" : "", size, header ? color : U.LabelColor);
            U.Layout(date.gameObject, 90, FinanceRowHeight, 0, -1);
            var type = U.CreateText("TypeText", row, header ? "Type" : "", size, color);
            U.Layout(type.gameObject, 100, FinanceRowHeight, 0, -1);
            var description = U.CreateText("DescriptionText", row, header ? "Description" : "", size, color);
            U.Layout(description.gameObject, -1, FinanceRowHeight, 1, -1);
            var amount = U.CreateText("AmountText", row, header ? "Amount" : "", size, color, TextAlignmentOptions.MidlineRight);
            U.Layout(amount.gameObject, 120, FinanceRowHeight, 0, -1);

            if (texts != null && texts.Length >= 4)
            {
                texts[0] = date;
                texts[1] = type;
                texts[2] = description;
                texts[3] = amount;
            }
            return row;
        }

        private static TransactionRowUI BuildTransactionRow(Transform parent)
        {
            var texts = new TMP_Text[4];
            var row = BuildTransactionColumns(parent, "TransactionRowTemplate", false, texts);
            U.Layout(row.gameObject, -1, FinanceRowHeight, -1, 0);

            var component = row.gameObject.AddComponent<TransactionRowUI>();
            U.SetRef(component, "dateText", texts[0]);
            U.SetRef(component, "typeText", texts[1]);
            U.SetRef(component, "descriptionText", texts[2]);
            U.SetRef(component, "amountText", texts[3]);
            return component;
        }

        // ------------------------------------------------------------------ income / expenses

        private static void BuildIncomeExpenses(Transform parent, CompanyFinancesTab tab)
        {
            var panel = U.Panel("IncomeAndExpenses", parent, U.PanelColor);
            U.Layout(panel.gameObject, -1, -1, -1, 1);
            U.Vertical(panel.gameObject, 6, 10);

            var title = SectionTitle(panel, "INCOME AND EXPENSES (LAST 7 DAYS)");

            var incomeTotal = BuildCategoryHeader(panel, "IncomeHeader", "Income", UIFormat.PositiveColor);
            var incomeRows = U.Node("IncomeRows", panel);
            U.Vertical(incomeRows.gameObject, 2);
            var template = BuildCategoryRow(incomeRows);
            template.gameObject.SetActive(false);

            var expensesTotal = BuildCategoryHeader(panel, "ExpensesHeader", "Expenses", UIFormat.NegativeColor);
            var expenseRows = U.Node("ExpenseRows", panel);
            U.Vertical(expenseRows.gameObject, 2);

            U.Spacer(panel, -1, 1);

            U.SetRef(tab, "categoriesTitleText", title);
            U.SetRef(tab, "incomeTotalText", incomeTotal);
            U.SetRef(tab, "expensesTotalText", expensesTotal);
            U.SetRef(tab, "incomeRows", incomeRows);
            U.SetRef(tab, "expenseRows", expenseRows);
            U.SetRef(tab, "categoryRowTemplate", template);
        }

        private static TextMeshProUGUI BuildCategoryHeader(Transform parent, string name, string label, Color color)
        {
            var header = U.Panel(name, parent, new Color(color.r, color.g, color.b, 0.25f));
            U.Layout(header.gameObject, -1, 28, -1, 0);
            var layout = U.Horizontal(header.gameObject, 8);
            layout.padding = new RectOffset(8, 8, 0, 0);

            var labelText = U.CreateText("LabelText", header, label, 18, color);
            U.Layout(labelText.gameObject, -1, -1, 1, -1);
            var total = U.CreateText("TotalText", header, "0 €", 18, color, TextAlignmentOptions.MidlineRight);
            U.Layout(total.gameObject, 150, -1, 0, -1);
            return total;
        }

        private static FinanceCategoryRowUI BuildCategoryRow(Transform parent)
        {
            var row = U.Node("CategoryRowTemplate", parent);
            U.Layout(row.gameObject, -1, FinanceRowHeight, -1, 0);
            var layout = U.Horizontal(row.gameObject, 8);
            layout.padding = new RectOffset(8, 8, 0, 0);

            var name = U.CreateText("NameText", row, "Category", 15, Color.white);
            U.Layout(name.gameObject, -1, -1, 1, -1);
            var amount = U.CreateText("AmountText", row, "0 €", 15, Color.white, TextAlignmentOptions.MidlineRight);
            U.Layout(amount.gameObject, 120, -1, 0, -1);
            var share = U.CreateText("ShareText", row, "—", 15, U.LabelColor, TextAlignmentOptions.MidlineRight);
            U.Layout(share.gameObject, 50, -1, 0, -1);

            var component = row.gameObject.AddComponent<FinanceCategoryRowUI>();
            U.SetRef(component, "nameText", name);
            U.SetRef(component, "amountText", amount);
            U.SetRef(component, "shareText", share);
            return component;
        }

        // ------------------------------------------------------------------ donut

        private static void BuildExpenseDonut(Transform parent, CompanyFinancesTab tab)
        {
            var panel = U.Panel("ExpensesBreakdown", parent, U.PanelColor);
            U.Layout(panel.gameObject, -1, 230, -1, 0);
            U.Vertical(panel.gameObject, 6, 10);

            SectionTitle(panel, "EXPENSES BREAKDOWN");

            var body = U.Node("Body", panel);
            U.Layout(body.gameObject, -1, -1, -1, 1);
            var layout = U.Horizontal(body.gameObject, 16, 0, false);
            layout.childAlignment = TextAnchor.MiddleLeft;

            var donutNode = U.Node("Donut", body);
            U.Layout(donutNode.gameObject, 170, 170, 0, 0, 170, 170);
            var donut = donutNode.gameObject.AddComponent<UIDonutChart>();
            donut.raycastTarget = false;

            var center = U.CreateText("CenterText", donutNode, "", 18, Color.white, TextAlignmentOptions.Center);
            U.Stretch(center.rectTransform);

            var legend = U.CreateText("LegendText", body, "", 15, Color.white, TextAlignmentOptions.MidlineLeft, FontStyles.Normal, true);
            U.Layout(legend.gameObject, -1, -1, 1, 1);

            U.SetRef(tab, "expenseDonut", donut);
            U.SetRef(tab, "donutCenterText", center);
            U.SetRef(tab, "donutLegendText", legend);
        }
    }
}
