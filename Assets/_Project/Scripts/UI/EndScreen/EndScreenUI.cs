using System.Collections.Generic;
using System.Linq;
using AntiqueTradingSimulator.Company;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Saving;
using AntiqueTradingSimulator.UI.Charts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// The end of the campaign: who won the partnership, the player's place and verdict, the
    /// final ranking of every trader, the player's figures against the best rival and the
    /// rivals' average, and a chart of the whole campaign (net worth or score per day, one line
    /// per trader). Opens by itself when CampaignManager reports the end; Keep playing resumes
    /// the clock (sandbox), Main menu leaves the game.
    /// </summary>
    public class EndScreenUI : MonoBehaviour
    {
        private enum ChartMetric
        {
            NetWorth,
            Score
        }

        [Tooltip("The visible screen, as a CHILD of this object. This object stays active so it hears " +
                 "the campaign end even while the screen is hidden.")]
        [SerializeField] private GameObject panelRoot;

        [Header("Data (found automatically if empty)")]
        [SerializeField] private CampaignManager campaign;
        [SerializeField] private TimeManager timeManager;

        [Header("Header")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text resultText;
        [SerializeField] private TMP_Text placeText;
        [SerializeField] private TMP_Text verdictText;

        [Header("Ranking")]
        [SerializeField] private Transform rankingContainer;
        [SerializeField] private EndRankingRowUI rankingRowTemplate;

        [Header("Comparison")]
        [SerializeField] private Transform comparisonContainer;
        [SerializeField] private EndComparisonRowUI comparisonRowTemplate;

        [Header("Chart")]
        [SerializeField] private TMP_Text chartTitleText;
        [Tooltip("Series are stacked here, one stretched chart per trader.")]
        [SerializeField] private RectTransform chartArea;
        [SerializeField] private UILineChart chartTemplate;
        [SerializeField] private TMP_Text chartMaxText;
        [SerializeField] private TMP_Text chartMinText;
        [SerializeField] private TMP_Text chartStartText;
        [SerializeField] private TMP_Text chartEndText;
        [SerializeField] private Transform legendContainer;
        [SerializeField] private EndLegendItemUI legendItemTemplate;
        [SerializeField] private Button netWorthButton;
        [SerializeField] private Button scoreButton;

        [Header("Colours")]
        [SerializeField] private Color playerColor = new Color32(0xE6, 0xB2, 0x40, 0xFF);
        [SerializeField] private Color[] rivalColors =
        {
            new Color32(0x8F, 0xB8, 0xDE, 0xFF),
            new Color32(0xC0, 0x84, 0x6A, 0xFF),
            new Color32(0x9C, 0xC2, 0x8B, 0xFF),
            new Color32(0xB9, 0x9C, 0xD6, 0xFF),
            new Color32(0xD8, 0xC2, 0x8A, 0xFF),
            new Color32(0x86, 0xC4, 0xBE, 0xFF)
        };

        [Header("Chart buttons: the active one is lit, the other dimmed")]
        [SerializeField] private Color activeMetricLabel = new Color32(0xC9, 0x9A, 0x3C, 0xFF);
        [SerializeField] private Color inactiveMetricLabel = new Color32(0xAA, 0x98, 0x78, 0xFF);
        [SerializeField] private Color inactiveMetricTint = new Color(0.6f, 0.6f, 0.6f, 1f);

        [Header("Buttons")]
        [SerializeField] private Button keepPlayingButton;
        [SerializeField] private Button mainMenuButton;

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly List<GameObject> _chartSeries = new List<GameObject>();
        private ChartMetric _metric = ChartMetric.NetWorth;

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        private void Awake()
        {
            if (campaign == null) campaign = FindFirstObjectByType<CampaignManager>();
            if (timeManager == null) timeManager = FindFirstObjectByType<TimeManager>();

            if (keepPlayingButton != null) keepPlayingButton.onClick.AddListener(KeepPlaying);
            if (mainMenuButton != null) mainMenuButton.onClick.AddListener(ToMainMenu);
            if (netWorthButton != null) netWorthButton.onClick.AddListener(() => ShowChart(ChartMetric.NetWorth));
            if (scoreButton != null) scoreButton.onClick.AddListener(() => ShowChart(ChartMetric.Score));

            foreach (var template in new Component[] { rankingRowTemplate, comparisonRowTemplate, chartTemplate, legendItemTemplate })
                if (template != null) template.gameObject.SetActive(false);

            if (panelRoot == null || panelRoot == gameObject)
            {
                Debug.LogError("EndScreenUI: panelRoot must be a child object — this one has to stay active to hear the campaign end.", this);
                enabled = false;
                return;
            }

            if (campaign != null) campaign.OnCampaignFinished += Show;
            panelRoot.SetActive(false);
        }

        private void OnDestroy()
        {
            if (campaign != null) campaign.OnCampaignFinished -= Show;
        }

        /// <summary>Opens the results of a finished campaign. Does nothing while it still runs.</summary>
        public void Show()
        {
            if (campaign == null || !campaign.IsFinished || panelRoot == null) return;

            ClearSpawned();
            FillHeader();
            FillRanking();
            FillComparison();

            panelRoot.SetActive(true);
            transform.SetAsLastSibling();
            ShowChart(_metric);
        }

        public void Close()
        {
            if (panelRoot != null) panelRoot.SetActive(false);
        }

        // ---------------------------------------------------------------- header

        private void FillHeader()
        {
            var player = campaign.PlayerStanding;
            var winner = campaign.Winner;
            int count = campaign.FinalRanking.Count;

            SetText(titleText, $"The campaign is over — day {campaign.FinishedOnDay}");

            if (player == null || winner == null)
            {
                SetText(resultText, string.Empty);
                SetText(placeText, string.Empty);
                SetText(verdictText, string.Empty);
                return;
            }

            SetText(resultText, campaign.PlayerWon
                ? $"{player.Name} wins the partnership"
                : $"{winner.Name} wins the partnership");
            SetText(placeText, $"You finished {Ordinal(player.Place)} of {count} with a score of {Mathf.RoundToInt(player.Score)}");

            var scoring = campaign.Company != null ? campaign.Company.Scoring : null;
            SetText(verdictText, CampaignScoring.Verdict(scoring, player.Score));
        }

        // ---------------------------------------------------------------- ranking

        private void FillRanking()
        {
            if (rankingContainer == null || rankingRowTemplate == null) return;

            foreach (var standing in campaign.FinalRanking)
            {
                var row = Spawn(rankingRowTemplate, rankingContainer);
                var s = standing.Snapshot;
                row.Set(standing.Place, standing.Name, standing.Score, s.Wealth, s.CollectionValue,
                        s.Reputation, s.ContractsFulfilled, standing.IsPlayer);
            }
        }

        // ---------------------------------------------------------------- comparison

        private void FillComparison()
        {
            if (comparisonContainer == null || comparisonRowTemplate == null) return;

            var player = campaign.PlayerStanding;
            var rivals = campaign.FinalRanking.Where(s => !s.IsPlayer).ToList();
            if (player == null) return;

            AddComparison("Score", player.Score, rivals.Select(r => r.Score).ToList(),
                          v => Mathf.RoundToInt(v).ToString());

            var scoring = campaign.Company != null ? campaign.Company.Scoring : null;
            var criteria = scoring != null && scoring.Criteria != null
                ? scoring.Criteria.Where(c => c != null && c.Weight > 0f).Select(c => c.Criterion).Distinct()
                : System.Enum.GetValues(typeof(ScoreCriterion)).Cast<ScoreCriterion>();

            foreach (var criterion in criteria)
            {
                AddComparison(CompanyOverviewTab.CriterionLabel(criterion),
                              player.Snapshot.Value(criterion),
                              rivals.Select(r => r.Snapshot.Value(criterion)).ToList(),
                              v => CompanyOverviewTab.FormatCriterion(criterion, v));
            }
        }

        private void AddComparison(string label, float playerValue, List<float> rivalValues, System.Func<float, string> format)
        {
            var row = Spawn(comparisonRowTemplate, comparisonContainer);

            if (rivalValues.Count == 0)
            {
                row.Set(label, format(playerValue), "–", "–", null);
                return;
            }

            float best = rivalValues.Max();
            float average = rivalValues.Average();
            bool? ahead = Mathf.Approximately(playerValue, best) ? (bool?)null : playerValue > best;
            row.Set(label, format(playerValue), format(best), format(average), ahead);
        }

        // ---------------------------------------------------------------- chart

        private void ShowChart(ChartMetric metric)
        {
            _metric = metric;
            foreach (var go in _chartSeries)
                if (go != null) Destroy(go);
            _chartSeries.Clear();

            if (chartArea == null || chartTemplate == null || campaign.Company == null) return;

            SetText(chartTitleText, metric == ChartMetric.NetWorth ? "Wealth over the campaign" : "Score over the campaign");
            MarkMetricButton(netWorthButton, metric == ChartMetric.NetWorth);
            MarkMetricButton(scoreButton, metric == ChartMetric.Score);

            int days = Mathf.Max(1, campaign.FinishedOnDay);
            var scoring = campaign.Company.Scoring;
            var histories = campaign.Company.GetCompetition().ToDictionary(e => e.Id, e => e.History);

            // One value per day for every trader, so all lines share the same x axis.
            var series = new List<(FinalStanding standing, List<float> values)>();
            foreach (var standing in campaign.FinalRanking)
            {
                histories.TryGetValue(standing.Id, out var history);
                var values = DailyValues(history, days, snapshot =>
                    metric == ChartMetric.NetWorth ? snapshot.Wealth : CampaignScoring.TotalScore(scoring, snapshot));
                if (values != null) series.Add((standing, values));
            }

            if (series.Count == 0) return;

            float min = series.Min(s => s.values.Min());
            float max = series.Max(s => s.values.Max());
            if (metric == ChartMetric.Score) min = Mathf.Min(0f, min);
            if (max <= min) max = min + 1f;

            System.Func<float, string> label = metric == ChartMetric.NetWorth
                ? v => UIFormat.Money(v)
                : v => Mathf.RoundToInt(v).ToString();
            SetText(chartMaxText, label(max));
            SetText(chartMinText, label(min));
            SetText(chartStartText, "Day 1");
            SetText(chartEndText, $"Day {days}");

            int rivalIndex = 0;
            // Rivals first, the player last so its line is drawn on top.
            foreach (var (standing, values) in series.OrderBy(s => s.standing.IsPlayer))
            {
                Color color = standing.IsPlayer
                    ? playerColor
                    : rivalColors.Length > 0 ? rivalColors[rivalIndex++ % rivalColors.Length] : Color.gray;

                var chart = Instantiate(chartTemplate, chartArea);
                chart.gameObject.SetActive(true);
                chart.name = $"Series {standing.Name}";
                Stretch((RectTransform)chart.transform);
                chart.SetColors(color, Color.clear);
                chart.SetFixedRange(min, max);
                chart.SetValues(values);
                _chartSeries.Add(chart.gameObject);

                if (legendContainer != null && legendItemTemplate != null)
                {
                    var item = Instantiate(legendItemTemplate, legendContainer);
                    item.gameObject.SetActive(true);
                    item.Set(color, standing.Name, standing.IsPlayer);
                    _chartSeries.Add(item.gameObject);
                }
            }
        }

        /// <summary>Lights the button of the metric on show; both stay clickable (clicking the active one does nothing).</summary>
        private void MarkMetricButton(Button button, bool active)
        {
            if (button == null) return;

            if (button.image != null) button.image.color = active ? Color.white : inactiveMetricTint;
            var label = button.GetComponentInChildren<TMP_Text>(true);
            if (label != null) label.color = active ? activeMetricLabel : inactiveMetricLabel;
        }

        /// <summary>
        /// The value for each day 1..days: that day's snapshot, or the last one before it (before
        /// the first snapshot, the first one). Null without any history.
        /// </summary>
        private static List<float> DailyValues(IReadOnlyList<CompanySnapshot> history, int days, System.Func<CompanySnapshot, float> value)
        {
            if (history == null || history.Count == 0) return null;

            var sorted = history.OrderBy(s => s.Day).ToList();
            var result = new List<float>(days);
            int index = 0;
            CompanySnapshot current = sorted[0];

            for (int day = 1; day <= days; day++)
            {
                while (index < sorted.Count && sorted[index].Day <= day)
                    current = sorted[index++];
                result.Add(value(current));
            }

            return result;
        }

        // ---------------------------------------------------------------- buttons

        private void KeepPlaying()
        {
            Close();
            if (timeManager != null && !timeManager.IsRunning) timeManager.Resume();
        }

        private void ToMainMenu()
        {
            if (!GameSession.ReturnToMainMenu())
                Debug.LogWarning("EndScreenUI: there is no main menu scene to return to.");
        }

        // ---------------------------------------------------------------- helpers

        private T Spawn<T>(T template, Transform parent) where T : Component
        {
            var item = Instantiate(template, parent);
            item.gameObject.SetActive(true);
            _spawned.Add(item.gameObject);
            return item;
        }

        private void ClearSpawned()
        {
            foreach (var go in _spawned)
                if (go != null) Destroy(go);
            _spawned.Clear();
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private static string Ordinal(int n)
        {
            int lastTwo = n % 100;
            if (lastTwo >= 11 && lastTwo <= 13) return $"{n}th";
            return (n % 10) switch
            {
                1 => $"{n}st",
                2 => $"{n}nd",
                3 => $"{n}rd",
                _ => $"{n}th"
            };
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null) text.text = value;
        }
    }
}
