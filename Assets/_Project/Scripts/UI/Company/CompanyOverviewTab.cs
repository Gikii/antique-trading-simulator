using System.Collections.Generic;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Company;
using AntiqueTradingSimulator.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Company → Overview: company card, next major event, key metrics with 7-day trends,
    /// Congress readiness (score preview) and the competition ranking. Reads everything
    /// from the player's CompanyManager and refreshes while visible.
    /// </summary>
    public class CompanyOverviewTab : MonoBehaviour
    {
        private const int TrendDays = 7;

        private enum CompetitionSort
        {
            Wealth,
            CollectionValue
        }

        [Header("Dependencies (found automatically if empty)")]
        [SerializeField] private PlayerTrader playerTrader;
        [SerializeField] private TimeManager timeManager;

        [Header("Company card")]
        [SerializeField] private TMP_Text companyNameText;
        [SerializeField] private TMP_Text companyTitleText;
        [SerializeField] private ProgressBarUI tierProgress;
        [SerializeField] private TMP_Text nextLevelText;
        [SerializeField] private TMP_Text companyDescriptionText;
        [SerializeField] private TMP_Text dayText;
        [SerializeField] private TMP_Text dateText;

        [Header("Next major event")]
        [SerializeField] private TMP_Text eventTitleText;
        [SerializeField] private TMP_Text eventDescriptionText;

        [Header("Key metrics")]
        [SerializeField] private StatTileUI wealthTile;
        [SerializeField] private StatTileUI collectionTile;
        [SerializeField] private StatTileUI contractsTile;
        [SerializeField] private StatTileUI reputationTile;
        [SerializeField] private StatTileUI credibilityTile;
        [SerializeField] private StatTileUI marketShareTile;

        [Header("Congress readiness")]
        [Tooltip("One bar per ScoreCriterion, in enum order.")]
        [SerializeField] private ScoreBarUI[] scoreBars = new ScoreBarUI[6];
        [SerializeField] private TMP_Text scoreText;
        [SerializeField] private TMP_Text verdictText;
        [Tooltip("Bars at or above this score use the positive colour, below it the accent colour.")]
        [Range(0, 100)] [SerializeField] private int goodScoreThreshold = 50;

        [Header("Competition")]
        [SerializeField] private RectTransform competitorContainer;
        [Tooltip("Inactive row inside the container, cloned for every competitor.")]
        [SerializeField] private CompetitorRowUI competitorRowTemplate;
        [SerializeField] private Button sortButton;
        [SerializeField] private TMP_Text sortButtonText;
        [Min(1)] [SerializeField] private int maxCompetitorRows = 8;

        [Header("Refresh")]
        [Tooltip("Seconds between refreshes while visible (NPC trades don't raise events).")]
        [Min(0.1f)] [SerializeField] private float refreshInterval = 1f;

        private readonly List<CompetitorRowUI> _rows = new List<CompetitorRowUI>();
        private CompetitionSort _sort = CompetitionSort.Wealth;
        private CompanyManager _company;
        private float _timer;

        private void Awake()
        {
            if (playerTrader == null) playerTrader = FindFirstObjectByType<PlayerTrader>();
            if (timeManager == null) timeManager = FindFirstObjectByType<TimeManager>();
            if (sortButton != null) sortButton.onClick.AddListener(ToggleSort);
            if (competitorRowTemplate != null) competitorRowTemplate.gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            Bind();
            Refresh();
        }

        private void OnDisable()
        {
            if (_company != null) _company.OnCompanyChanged -= Refresh;
            _company = null;
        }

        // PlayerTrader creates its company in Awake — if this page was enabled before that,
        // Refresh binds on its next call.
        private void Bind()
        {
            if (_company != null || playerTrader == null || playerTrader.Company == null) return;

            _company = playerTrader.Company;
            _company.OnCompanyChanged += Refresh;
        }

        private void Update()
        {
            _timer += Time.unscaledDeltaTime;
            if (_timer >= refreshInterval)
                Refresh();
        }

        private void ToggleSort()
        {
            _sort = _sort == CompetitionSort.Wealth ? CompetitionSort.CollectionValue : CompetitionSort.Wealth;
            RefreshCompetition();
        }

        // ------------------------------------------------------------------ refresh

        public void Refresh()
        {
            _timer = 0f;
            if (_company == null) Bind();
            if (_company == null) return;

            var now = _company.Capture();
            var past = _company.SnapshotDaysAgo(TrendDays);

            RefreshCompanyCard(now);
            RefreshNextEvent();
            RefreshMetrics(now, past);
            RefreshReadiness(now);
            RefreshCompetition();
        }

        private void RefreshCompanyCard(CompanySnapshot now)
        {
            var reputation = _company.Reputation;
            var tier = reputation.CurrentTier(ReputationKind.Reputation);
            var next = reputation.NextTier(ReputationKind.Reputation);

            SetText(companyNameText, _company.CompanyName);
            SetText(companyTitleText, tier?.Name ?? "");
            SetText(companyDescriptionText, tier?.Description ?? "");
            SetText(nextLevelText, next != null
                ? $"Next level: {UIFormat.Colorize(next.Name, UIFormat.AccentColor)} at {UIFormat.Number(next.MinValue)} reputation"
                : "Highest level reached");
            if (tierProgress != null)
            {
                tierProgress.SetValue(reputation.ProgressToNextTier(ReputationKind.Reputation));
                tierProgress.SetColor(UIFormat.AccentColor);
            }

            SetText(dayText, $"Day {now.Day} / {_company.CampaignLength}");
            SetText(dateText, timeManager != null ? TimeManager.FormatLong(timeManager.DayToDate(now.Day)) : "");
        }

        private void RefreshNextEvent()
        {
            int days = _company.DaysUntilCongress;
            SetText(eventTitleText, "International Collectors Congress");
            SetText(eventDescriptionText, days > 0
                ? $"In {UIFormat.Days(days)} the Congress will take place. Prepare your company, expand your collection and earn a high position in the ranking."
                : "The Congress takes place today. Your company will be evaluated.");
        }

        private void RefreshMetrics(CompanySnapshot now, CompanySnapshot past)
        {
            var scoring = _company.Scoring;

            SetTile(wealthTile, ScoreCriterion.Wealth, UIFormat.Money(now.Wealth),
                RelativeChange(now.Wealth, past.Wealth), now.Wealth - past.Wealth, scoring,
                "Cash plus the market value of every antique you own.");

            SetTile(collectionTile, ScoreCriterion.CollectionValue, UIFormat.Money(now.CollectionValue),
                RelativeChange(now.CollectionValue, past.CollectionValue), now.CollectionValue - past.CollectionValue, scoring,
                "Market value of all antiques you own, including items in transit.");

            int fulfilledChange = now.ContractsFulfilled - past.ContractsFulfilled;
            SetTile(contractsTile, ScoreCriterion.ContractsFulfilled, $"{now.ContractsFulfilled} / {_company.ContractsAccepted}",
                UIFormat.SignedNumber(fulfilledChange), fulfilledChange, scoring,
                $"Fulfilled / accepted contracts. Failed: {_company.ContractsFailed}.");

            int reputationChange = now.Reputation - past.Reputation;
            SetTile(reputationTile, ScoreCriterion.Reputation, UIFormat.Number(now.Reputation),
                UIFormat.SignedNumber(reputationChange), reputationChange, scoring,
                "Your standing in the antique world. Gives access to contracts, auctions and information — it never changes prices.");

            float credibilityChange = now.Credibility - past.Credibility;
            SetTile(credibilityTile, ScoreCriterion.Credibility, UIFormat.Percent(now.Credibility),
                UIFormat.SignedPercent(credibilityChange), credibilityChange, scoring,
                "How reliable partners consider your company. Grows with kept promises, drops with missed deadlines.");

            float shareChange = now.MarketShare - past.MarketShare;
            SetTile(marketShareTile, ScoreCriterion.MarketShare, UIFormat.PercentOneDecimal(now.MarketShare),
                UIFormat.SignedPercent(shareChange), shareChange, scoring,
                "Your share of all market purchases and sales since the start of the campaign.");
        }

        private static string RelativeChange(float now, float past) =>
            past > 0.01f ? UIFormat.SignedPercent((now - past) / past) : UIFormat.SignedMoney(now - past);

        private static void SetTile(StatTileUI tile, ScoreCriterion criterion, string value, string change, float delta,
            ScoringSettings scoring, string description)
        {
            if (tile == null) return;

            tile.SetLabel(CriterionLabel(criterion));
            tile.Set(value, ChangeLine(change, delta));
            tile.SetTooltip($"{description}\n<size=85%>{WeightLine(scoring, criterion)}</size>");
        }

        private static string ChangeLine(string change, float delta) =>
            UIFormat.ColorBySign($"{UIFormat.TrendArrow(delta)} {change}", delta) + " " +
            UIFormat.Colorize($"({TrendDays} days)", UIFormat.MutedColor);

        private static string WeightLine(ScoringSettings scoring, ScoreCriterion criterion)
        {
            var c = scoring != null ? scoring.Get(criterion) : null;
            if (c == null) return "Not part of the final score.";

            float total = 0f;
            foreach (var other in scoring.Criteria)
                if (other != null) total += other.Weight;

            return total > 0f
                ? $"Counts {UIFormat.Percent(c.Weight / total)} towards the final Congress score."
                : "Not part of the final score.";
        }

        private void RefreshReadiness(CompanySnapshot now)
        {
            var scoring = _company.Scoring;

            for (int i = 0; i < scoreBars.Length; i++)
            {
                if (scoreBars[i] == null) continue;

                var criterion = (ScoreCriterion)i;
                float value = now.Value(criterion);
                float score = CampaignScoring.CriterionScore(scoring, criterion, value);
                var settings = scoring.Get(criterion);

                scoreBars[i].Set(CriterionLabel(criterion), score,
                    score >= goodScoreThreshold ? UIFormat.PositiveColor : UIFormat.AccentColor);
                scoreBars[i].SetTooltip(settings != null
                    ? $"Now: {FormatCriterion(criterion, value)}\nFull score at: {FormatCriterion(criterion, settings.Target)}\n" +
                      $"<size=85%>{WeightLine(scoring, criterion)}</size>"
                    : "");
            }

            float total = CampaignScoring.TotalScore(scoring, now);
            SetText(scoreText, Mathf.RoundToInt(total).ToString());
            SetText(verdictText, CampaignScoring.Verdict(scoring, total));
        }

        private void RefreshCompetition()
        {
            if (_company == null || competitorContainer == null || competitorRowTemplate == null) return;

            var entries = _company.GetCompetition();
            if (_sort == CompetitionSort.CollectionValue)
                entries.Sort((a, b) => b.CollectionValue.CompareTo(a.CollectionValue));

            SetText(sortButtonText, _sort == CompetitionSort.Wealth ? "Sort: Wealth" : "Sort: Collection");

            // Top rows, but always keep the player visible (in the last row if needed).
            int shown = Mathf.Min(entries.Count, maxCompetitorRows);
            int playerIndex = entries.FindIndex(e => e.IsPlayer);
            bool playerBelowCut = playerIndex >= shown;

            EnsureRows(shown);
            for (int i = 0; i < _rows.Count; i++)
            {
                bool visible = i < shown;
                _rows[i].gameObject.SetActive(visible);
                if (!visible) continue;

                int index = playerBelowCut && i == shown - 1 ? playerIndex : i;
                var entry = entries[index];
                float value = _sort == CompetitionSort.Wealth ? entry.Wealth : entry.CollectionValue;
                _rows[i].Set(index + 1, entry.Name, UIFormat.Money(value), entry.IsPlayer);
            }
        }

        private void EnsureRows(int count)
        {
            while (_rows.Count < count)
            {
                var row = Instantiate(competitorRowTemplate, competitorContainer);
                row.name = $"CompetitorRow{_rows.Count + 1}";
                _rows.Add(row);
            }
        }

        // ------------------------------------------------------------------ helpers

        public static string CriterionLabel(ScoreCriterion criterion) => criterion switch
        {
            ScoreCriterion.Wealth => "Wealth",
            ScoreCriterion.CollectionValue => "Collection Value",
            ScoreCriterion.ContractsFulfilled => "Contracts Fulfilled",
            ScoreCriterion.Reputation => "Reputation",
            ScoreCriterion.Credibility => "Credibility",
            ScoreCriterion.MarketShare => "Market Share",
            _ => criterion.ToString()
        };

        public static string FormatCriterion(ScoreCriterion criterion, float value) => criterion switch
        {
            ScoreCriterion.Wealth => UIFormat.Money(value),
            ScoreCriterion.CollectionValue => UIFormat.Money(value),
            ScoreCriterion.ContractsFulfilled => UIFormat.Number(value),
            ScoreCriterion.Reputation => UIFormat.Number(value),
            ScoreCriterion.Credibility => UIFormat.Percent(value),
            ScoreCriterion.MarketShare => UIFormat.PercentOneDecimal(value),
            _ => value.ToString()
        };

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null) text.text = value;
        }
    }
}
