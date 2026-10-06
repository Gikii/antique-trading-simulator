using System.Collections.Generic;
using AntiqueTradingSimulator.Company;
using AntiqueTradingSimulator.UI.Charts;
using TMPro;
using UnityEngine;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Everything Company → Reputation shows for one measure (reputation or credibility):
    /// value card with trend and 30-day chart, tier ladder with current/next level and
    /// unlocks, and the list of recent changes. The referenced objects may live in
    /// different rows of the page — this component only fills them.
    /// </summary>
    public class ReputationColumnUI : MonoBehaviour
    {
        private const int TrendDays = 7;
        private const int ChartDays = 30;

        [SerializeField] private ReputationKind kind;

        [Header("Value card")]
        [SerializeField] private TMP_Text valueText;
        [SerializeField] private TMP_Text changeText;
        [SerializeField] private UILineChart chart;
        [SerializeField] private TMP_Text chartStartText;

        [Header("Level")]
        [SerializeField] private TierBarUI tierBar;
        [SerializeField] private TMP_Text currentTierText;
        [SerializeField] private TMP_Text currentDescriptionText;
        [SerializeField] private TMP_Text nextHeaderText;
        [SerializeField] private TMP_Text nextProgressText;
        [SerializeField] private TMP_Text nextUnlocksText;

        [Header("History")]
        [SerializeField] private ReputationChangeListUI changeList;

        private readonly List<float> _chartValues = new List<float>();
        private readonly List<ReputationChange> _changes = new List<ReputationChange>();

        public ReputationKind Kind => kind;
        public ReputationChangeListUI ChangeList => changeList;

        private ScoreCriterion Criterion =>
            kind == ReputationKind.Reputation ? ScoreCriterion.Reputation : ScoreCriterion.Credibility;

        private bool _chartConfigured;

        // Lazy instead of Awake: the tab may refresh this column before its Awake has run.
        private void ConfigureChart()
        {
            if (_chartConfigured || chart == null) return;
            _chartConfigured = true;

            chart.LabelFormatter = FormatValue;
            if (kind == ReputationKind.Credibility)
                chart.SetFixedRange(0f, 1f);
        }

        public void Refresh(CompanyManager company, bool expanded)
        {
            if (company == null) return;
            ConfigureChart();

            var reputation = company.Reputation;
            float value = reputation.Value(kind);

            RefreshValueCard(company, value);
            RefreshLevel(reputation, value);

            if (changeList != null)
            {
                _changes.Clear();
                var history = reputation.History;
                for (int i = history.Count - 1; i >= 0; i--)
                    if (history[i].Kind == kind)
                        _changes.Add(history[i]);
                changeList.Set(kind, _changes, expanded);
            }
        }

        // ------------------------------------------------------------------ value card

        private void RefreshValueCard(CompanyManager company, float value)
        {
            if (valueText != null) valueText.text = FormatValue(value);

            float change = company.Change(Criterion, TrendDays);
            if (changeText != null)
                changeText.text = UIFormat.ColorBySign($"{UIFormat.TrendArrow(change)} {FormatDelta(change)}", change) +
                                  " " + UIFormat.Colorize($"({TrendDays} days)", UIFormat.MutedColor);

            if (chart == null) return;

            // Daily snapshots of the last 30 days, with today's live value as the last point.
            _chartValues.Clear();
            int today = company.CurrentDay;
            foreach (var snapshot in company.History)
                if (snapshot.Day > today - ChartDays && snapshot.Day < today)
                    _chartValues.Add(snapshot.Value(Criterion));
            _chartValues.Add(value);

            chart.SetValues(_chartValues);
            if (chartStartText != null)
                chartStartText.text = _chartValues.Count > 1 ? $"-{_chartValues.Count - 1}d" : "";
        }

        // ------------------------------------------------------------------ level

        private void RefreshLevel(CompanyReputation reputation, float value)
        {
            var tiers = reputation.Tiers(kind);
            if (tierBar != null)
                tierBar.Set(tiers, value, i => RangeLabel(tiers, i), i => TierColor(i, tiers.Count));

            var current = reputation.CurrentTier(kind);
            var next = reputation.NextTier(kind);

            if (currentTierText != null) currentTierText.text = current?.Name ?? "";
            if (currentDescriptionText != null) currentDescriptionText.text = current?.Description ?? "";

            string measure = kind == ReputationKind.Reputation ? "Reputation" : "Credibility";
            if (next == null)
            {
                SetText(nextHeaderText, "Highest level reached");
                SetText(nextProgressText, $"{measure}: {FormatValue(value)}");
                SetText(nextUnlocksText, current != null ? Bullets(current.Unlocks, UIFormat.PositiveColor) : "");
            }
            else
            {
                SetText(nextHeaderText, $"Next level: {UIFormat.Colorize(next.Name, UIFormat.AccentColor)}");
                SetText(nextProgressText,
                    $"{measure}: {FormatValue(value)} / {FormatValue(next.MinValue)}  " +
                    UIFormat.Colorize($"({FormatValue(next.MinValue - value)} to go)", UIFormat.MutedColor));
                SetText(nextUnlocksText, Bullets(next.Unlocks, Color.white));
            }
        }

        private static string Bullets(List<string> items, Color color)
        {
            if (items == null || items.Count == 0) return "";

            var lines = new List<string>(items.Count);
            foreach (var item in items)
                if (!string.IsNullOrWhiteSpace(item))
                    lines.Add(UIFormat.Colorize("• ", UIFormat.AccentColor) + UIFormat.Colorize(item, color));
            return string.Join("\n", lines);
        }

        private string RangeLabel(IReadOnlyList<ReputationTier> tiers, int i)
        {
            float min = tiers[i].MinValue;
            if (i == tiers.Count - 1)
                return kind == ReputationKind.Reputation ? $"{UIFormat.Number(min)}+" : $"{FormatValue(min)} - 100%";

            float nextMin = tiers[i + 1].MinValue;
            return kind == ReputationKind.Reputation
                ? $"{UIFormat.Number(min)} - {UIFormat.Number(nextMin - 1)}"
                : $"{FormatValue(min)} - {FormatValue(nextMin - 0.01f)}";
        }

        private Color TierColor(int index, int count)
        {
            if (kind == ReputationKind.Reputation || count <= 1) return UIFormat.AccentColor;

            // Credibility: red → orange → green, like the mockup.
            float t = index / (float)(count - 1);
            return t < 0.5f
                ? Color.Lerp(UIFormat.NegativeColor, UIFormat.AccentColor, t * 2f)
                : Color.Lerp(UIFormat.AccentColor, UIFormat.PositiveColor, (t - 0.5f) * 2f);
        }

        // ------------------------------------------------------------------ formatting

        private string FormatValue(float value) =>
            kind == ReputationKind.Reputation ? UIFormat.Number(value) : UIFormat.Percent(value);

        private string FormatDelta(float delta) =>
            kind == ReputationKind.Reputation ? UIFormat.SignedNumber(delta) : UIFormat.SignedPercent(delta);

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null) text.text = value;
        }
    }
}
