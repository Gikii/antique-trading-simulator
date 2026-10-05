using System.Collections.Generic;
using System.Text;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Company;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.UI.Charts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    public enum FinancePeriod
    {
        Today,
        Last7Days,
        Campaign
    }

    /// <summary>
    /// Company → Finances: income, expenses and profit for the selected period (today /
    /// last 7 days / whole campaign), the cash and total-assets chart, income and expenses
    /// by ledger category, an expense donut and the transaction table. Everything comes
    /// from the player's Ledger and the CompanyManager's daily snapshots.
    /// </summary>
    public class CompanyFinancesTab : MonoBehaviour
    {
        private const int MinChartDays = 7;

        private static readonly LedgerCategory[] IncomeCategories =
        {
            LedgerCategory.Sale, LedgerCategory.Contract, LedgerCategory.Auction, LedgerCategory.Other
        };

        private static readonly LedgerCategory[] ExpenseCategories =
        {
            LedgerCategory.Purchase, LedgerCategory.Transport, LedgerCategory.Warehouse, LedgerCategory.Commission,
            LedgerCategory.Upgrade, LedgerCategory.Upkeep, LedgerCategory.Penalty, LedgerCategory.Other
        };

        [Header("Dependencies (found automatically if empty)")]
        [SerializeField] private PlayerTrader playerTrader;

        [Header("Period")]
        [Tooltip("One button per FinancePeriod, in enum order.")]
        [SerializeField] private Button[] periodButtons = new Button[3];
        [SerializeField] private Color activePeriodColor = new Color32(0xC9, 0x9A, 0x3C, 0xFF);
        [SerializeField] private Color inactivePeriodColor = Color.white;
        [SerializeField] private FinancePeriod defaultPeriod = FinancePeriod.Last7Days;

        [Header("Overview")]
        [SerializeField] private TMP_Text overviewTitleText;
        [SerializeField] private StatTileUI incomeTile;
        [SerializeField] private StatTileUI expensesTile;
        [SerializeField] private StatTileUI profitTile;
        [SerializeField] private StatTileUI cashTile;

        [Header("Cash and assets chart")]
        [SerializeField] private TMP_Text chartTitleText;
        [Tooltip("Total assets (wealth). Its min/max labels describe both lines.")]
        [SerializeField] private UILineChart wealthChart;
        [SerializeField] private UILineChart cashChart;
        [SerializeField] private TMP_Text chartStartText;
        [SerializeField] private Color wealthColor = new Color32(0xC9, 0x9A, 0x3C, 0xFF);
        [SerializeField] private Color cashColor = new Color32(0x59, 0xA1, 0x4F, 0xFF);

        [Header("Income and expenses")]
        [SerializeField] private TMP_Text categoriesTitleText;
        [SerializeField] private TMP_Text incomeTotalText;
        [SerializeField] private TMP_Text expensesTotalText;
        [SerializeField] private RectTransform incomeRows;
        [SerializeField] private RectTransform expenseRows;
        [Tooltip("Inactive row cloned into both lists.")]
        [SerializeField] private FinanceCategoryRowUI categoryRowTemplate;

        [Header("Expenses breakdown")]
        [SerializeField] private UIDonutChart expenseDonut;
        [SerializeField] private TMP_Text donutCenterText;
        [SerializeField] private TMP_Text donutLegendText;

        [Header("Transactions")]
        [SerializeField] private TransactionListUI transactions;
        [Tooltip("Hidden while the full transaction list is shown (overview tiles, chart).")]
        [SerializeField] private GameObject[] hideWhenExpanded = new GameObject[0];

        private readonly List<FinanceCategoryRowUI> _incomeRows = new List<FinanceCategoryRowUI>();
        private readonly List<FinanceCategoryRowUI> _expenseRows = new List<FinanceCategoryRowUI>();
        private readonly Dictionary<LedgerCategory, float> _income = new Dictionary<LedgerCategory, float>();
        private readonly Dictionary<LedgerCategory, float> _expenses = new Dictionary<LedgerCategory, float>();
        private readonly List<float> _cashValues = new List<float>();
        private readonly List<float> _wealthValues = new List<float>();
        private readonly List<LedgerEntry> _periodEntries = new List<LedgerEntry>();

        private FinancePeriod _period;
        private bool _expanded;
        private bool _initialized;
        private CompanyManager _company;
        private TraderInventory _inventory;

        private void Awake() => Initialize();

        // Lazy so a refresh arriving before Awake still finds listeners and templates ready.
        private void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            _period = defaultPeriod;
            if (playerTrader == null) playerTrader = FindFirstObjectByType<PlayerTrader>();
            if (categoryRowTemplate != null) categoryRowTemplate.gameObject.SetActive(false);

            for (int i = 0; i < periodButtons.Length; i++)
            {
                if (periodButtons[i] == null) continue;
                var period = (FinancePeriod)i;
                periodButtons[i].onClick.AddListener(() => SetPeriod(period));
            }

            if (wealthChart != null) wealthChart.SetColors(wealthColor, WithAlpha(wealthColor, 0.25f));
            if (cashChart != null) cashChart.SetColors(cashColor, WithAlpha(cashColor, 0.2f));
        }

        private void OnEnable()
        {
            Initialize();
            Bind();
            if (transactions != null) transactions.OnToggleClicked += ToggleExpanded;
            Refresh();
        }

        private void OnDisable()
        {
            if (transactions != null) transactions.OnToggleClicked -= ToggleExpanded;
            if (_company != null) _company.OnCompanyChanged -= Refresh;
            if (_inventory != null) _inventory.Ledger.OnEntryAdded -= HandleLedgerEntry;
            _company = null;
            _inventory = null;
        }

        private void Bind()
        {
            if (_company != null || playerTrader == null || playerTrader.Company == null) return;

            _company = playerTrader.Company;
            _inventory = playerTrader.Inventory;
            _company.OnCompanyChanged += Refresh;
            if (_inventory != null) _inventory.Ledger.OnEntryAdded += HandleLedgerEntry;
        }

        private void HandleLedgerEntry(LedgerEntry entry) => Refresh();

        public void SetPeriod(FinancePeriod period)
        {
            _period = period;
            Refresh();
        }

        private void ToggleExpanded()
        {
            _expanded = !_expanded;
            Refresh();
        }

        // ------------------------------------------------------------------ periods

        private int Today => _company != null ? _company.CurrentDay : 1;

        /// <summary>First day of the selected period (inclusive).</summary>
        private int PeriodStart => _period switch
        {
            FinancePeriod.Today => Today,
            FinancePeriod.Last7Days => Today - 6,
            _ => int.MinValue
        };

        private string PeriodTitle => _period switch
        {
            FinancePeriod.Today => "TODAY",
            FinancePeriod.Last7Days => "LAST 7 DAYS",
            _ => "FULL CAMPAIGN"
        };

        private string ComparisonLabel => _period switch
        {
            FinancePeriod.Today => "vs yesterday",
            FinancePeriod.Last7Days => "vs previous 7 days",
            _ => ""
        };

        private bool HasPreviousPeriod => _period != FinancePeriod.Campaign;

        // ------------------------------------------------------------------ refresh

        public void Refresh()
        {
            Initialize();
            if (_company == null) Bind();

            for (int i = 0; i < periodButtons.Length; i++)
            {
                var image = periodButtons[i] != null ? periodButtons[i].targetGraphic as Image : null;
                if (image != null) image.color = i == (int)_period ? activePeriodColor : inactivePeriodColor;
            }

            foreach (var row in hideWhenExpanded)
                if (row != null) row.SetActive(!_expanded);

            if (_company == null || _inventory == null) return;

            var ledger = _inventory.Ledger;
            int from = PeriodStart;
            int to = Today;

            RefreshOverview(ledger, from, to);
            RefreshChart();
            RefreshCategories(ledger, from, to);
            RefreshTransactions(ledger, from, to);
        }

        private void RefreshOverview(Ledger ledger, int from, int to)
        {
            SetText(overviewTitleText, $"FINANCIAL OVERVIEW ({PeriodTitle})");

            float income = ledger.Income(from, to);
            float expenses = ledger.Expenses(from, to);
            float profit = income - expenses;

            float prevIncome = 0f, prevExpenses = 0f;
            if (HasPreviousPeriod)
            {
                int length = to - from + 1;
                prevIncome = ledger.Income(from - length, from - 1);
                prevExpenses = ledger.Expenses(from - length, from - 1);
            }

            SetTile(incomeTile, UIFormat.ColorBySign(UIFormat.SignedMoney(income), income), ChangeLine(income, prevIncome, false),
                "Money that came in: sales, contract rewards and other income.");
            SetTile(expensesTile, UIFormat.ColorBySign(UIFormat.SignedMoney(-expenses), -expenses), ChangeLine(expenses, prevExpenses, true),
                "Money that went out: purchases, transport, upkeep, commissions, upgrades and penalties.");
            SetTile(profitTile, UIFormat.ColorBySign(UIFormat.SignedMoney(profit), profit),
                ChangeLine(profit, prevIncome - prevExpenses, false),
                "Income minus expenses. Antiques bought are an expense here even though they keep their value — " +
                "see Total assets on the chart for the full picture.");

            // Cash at the start of the period = the snapshot taken when its first day began.
            float cash = _inventory.Cash;
            int daysBack = _period == FinancePeriod.Campaign ? Today : Today - from;
            float startCash = _company.SnapshotDaysAgo(daysBack).Cash;
            SetTile(cashTile, UIFormat.Money(cash), ChangeLine(cash, startCash, false, "since period start"),
                "Cash available right now.");
        }

        /// <summary>"↑ +12% vs previous 7 days" — empty when there's nothing to compare with.</summary>
        private string ChangeLine(float current, float previous, bool higherIsWorse, string label = null)
        {
            if (label == null)
            {
                if (!HasPreviousPeriod) return "";
                label = ComparisonLabel;
            }

            if (Mathf.Abs(previous) < 0.5f) return UIFormat.Colorize(label.Length > 0 ? $"— {label}" : "", UIFormat.MutedColor);

            float delta = current - previous;
            float ratio = delta / Mathf.Abs(previous);
            float goodness = higherIsWorse ? -delta : delta;
            return UIFormat.ColorBySign($"{UIFormat.TrendArrow(delta)} {UIFormat.SignedPercent(ratio)}", goodness) +
                   " " + UIFormat.Colorize(label, UIFormat.MutedColor);
        }

        private void RefreshChart()
        {
            int today = Today;
            int days = _period == FinancePeriod.Campaign ? today : MinChartDays;
            SetText(chartTitleText, _period == FinancePeriod.Campaign ? "CASH AND ASSETS (FULL CAMPAIGN)" : "CASH AND ASSETS (LAST 7 DAYS)");

            _cashValues.Clear();
            _wealthValues.Clear();
            foreach (var snapshot in _company.History)
            {
                if (snapshot.Day <= today - days || snapshot.Day >= today) continue;
                _cashValues.Add(snapshot.Cash);
                _wealthValues.Add(snapshot.Wealth);
            }

            var now = _company.Capture();
            _cashValues.Add(now.Cash);
            _wealthValues.Add(now.Wealth);

            // Same scale for both lines so they can be drawn on top of each other.
            float max = 1f;
            foreach (float v in _wealthValues) max = Mathf.Max(max, v);
            foreach (float v in _cashValues) max = Mathf.Max(max, v);
            max *= 1.1f;

            if (wealthChart != null)
            {
                wealthChart.SetFixedRange(0f, max);
                wealthChart.SetValues(_wealthValues);
            }

            if (cashChart != null)
            {
                cashChart.SetFixedRange(0f, max);
                cashChart.SetValues(_cashValues);
            }

            SetText(chartStartText, _wealthValues.Count > 1 ? $"-{_wealthValues.Count - 1}d" : "");
        }

        private void RefreshCategories(Ledger ledger, int from, int to)
        {
            SetText(categoriesTitleText, $"INCOME AND EXPENSES ({PeriodTitle})");

            _income.Clear();
            _expenses.Clear();
            foreach (var entry in ledger.Between(from, to))
            {
                var target = entry.IsIncome ? _income : _expenses;
                target.TryGetValue(entry.Category, out float sum);
                target[entry.Category] = sum + Mathf.Abs(entry.Amount);
            }

            float incomeTotal = Total(_income);
            float expensesTotal = Total(_expenses);

            SetText(incomeTotalText, UIFormat.Colorize(UIFormat.Money(incomeTotal), UIFormat.PositiveColor));
            SetText(expensesTotalText, UIFormat.Colorize(UIFormat.Money(expensesTotal), UIFormat.NegativeColor));

            FillCategories(_incomeRows, incomeRows, IncomeCategories, _income, incomeTotal, UIFormat.PositiveColor);
            FillCategories(_expenseRows, expenseRows, ExpenseCategories, _expenses, expensesTotal, UIFormat.NegativeColor);

            RefreshDonut(expensesTotal);
        }

        private static float Total(Dictionary<LedgerCategory, float> sums)
        {
            float total = 0f;
            foreach (var value in sums.Values) total += value;
            return total;
        }

        private void FillCategories(List<FinanceCategoryRowUI> pool, RectTransform container, LedgerCategory[] categories,
            Dictionary<LedgerCategory, float> sums, float total, Color color)
        {
            if (container == null || categoryRowTemplate == null) return;

            while (pool.Count < categories.Length)
            {
                var row = Instantiate(categoryRowTemplate, container);
                row.name = $"CategoryRow{pool.Count + 1}";
                row.gameObject.SetActive(true);
                pool.Add(row);
            }

            for (int i = 0; i < categories.Length; i++)
            {
                sums.TryGetValue(categories[i], out float value);
                bool any = value > 0.005f;

                pool[i].Set(
                    any ? categories[i].DisplayName() : UIFormat.Colorize(categories[i].DisplayName(), UIFormat.MutedColor),
                    UIFormat.Colorize(UIFormat.Money(value), any ? color : UIFormat.MutedColor),
                    UIFormat.Colorize(total > 0f ? UIFormat.Percent(value / total) : "—", UIFormat.MutedColor));
            }
        }

        private void RefreshDonut(float expensesTotal)
        {
            var ranked = new List<KeyValuePair<LedgerCategory, float>>();
            foreach (var pair in _expenses)
                if (pair.Value > 0.005f)
                    ranked.Add(pair);
            ranked.Sort((a, b) => b.Value.CompareTo(a.Value));

            var segments = new List<UIDonutChart.Segment>(ranked.Count);
            var legend = new StringBuilder();
            for (int i = 0; i < ranked.Count; i++)
            {
                var color = UIFormat.CategoryColorByRank(i);
                segments.Add(new UIDonutChart.Segment(ranked[i].Value, color));

                if (legend.Length > 0) legend.Append('\n');
                legend.Append(UIFormat.Colorize("■", color)).Append(' ')
                      .Append(ranked[i].Key.DisplayName()).Append("  ")
                      .Append(UIFormat.Colorize(UIFormat.Percent(ranked[i].Value / expensesTotal), UIFormat.MutedColor));
            }

            if (expenseDonut != null) expenseDonut.SetSegments(segments);
            SetText(donutCenterText, ranked.Count > 0
                ? $"{UIFormat.Money(expensesTotal)}\n<size=70%>{UIFormat.Colorize("Total", UIFormat.MutedColor)}</size>"
                : UIFormat.Colorize("No expenses", UIFormat.MutedColor));
            SetText(donutLegendText, legend.ToString());
        }

        private void RefreshTransactions(Ledger ledger, int from, int to)
        {
            if (transactions == null) return;

            _periodEntries.Clear();
            var entries = ledger.Entries;
            for (int i = entries.Count - 1; i >= 0; i--)
                if (entries[i].Day >= from && entries[i].Day <= to)
                    _periodEntries.Add(entries[i]);

            transactions.Set(_periodEntries, _expanded);
        }

        // ------------------------------------------------------------------ helpers

        private static void SetTile(StatTileUI tile, string value, string change, string tooltip)
        {
            if (tile == null) return;
            tile.Set(value, change);
            tile.SetTooltip(tooltip);
        }

        private static Color WithAlpha(Color color, float alpha) => new Color(color.r, color.g, color.b, alpha);

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null) text.text = value;
        }
    }
}
