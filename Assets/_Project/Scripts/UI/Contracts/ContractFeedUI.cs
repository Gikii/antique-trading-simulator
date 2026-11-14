using System.Collections.Generic;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Contract History in the Contracts view's side panel, shown whenever no contract is selected:
    ///
    ///   Your record: 3 fulfilled, 1 failed
    ///   Earned 24 300 £, penalties 6 454 £
    ///   THIS WEEK
    ///   ■ You fulfilled 4x Italy                     +12 907 £
    ///   ■ Hargreaves took on 1x XIX century (exclusive)
    ///     + 2 new contracts listed
    ///   LAST WEEK
    ///   ■ 2x China expired unfulfilled
    ///
    /// Reads ContractManager.Feed (structured entries) and rebuilds its rows (FeedList, shared with
    /// Market History) whenever the history changes or a new week starts ("This week" / "Last week"
    /// move on). Rows are made in code, so the scene only needs the scroll view.
    /// </summary>
    public class ContractFeedUI : MonoBehaviour
    {
        public const string Title = "Contract History";

        [Header("Dependencies")]
        [SerializeField] private ContractManager contractManager;
        [SerializeField] private TimeManager timeManager;

        [Header("Scroll view")]
        [Tooltip("The ScrollRect whose content holds the rows.")]
        [SerializeField] private ScrollRect scrollRect;
        [Tooltip("The ScrollRect's Content (a VerticalLayoutGroup + ContentSizeFitter). Rows are parented here.")]
        [SerializeField] private RectTransform content;
        [Tooltip("Only used for its font, and hidden – an empty history is a row in the list.")]
        [SerializeField] private TMP_Text emptyText;
        [Tooltip("Panel title, set from code. Auto-found at FeedHeader/FeedTitleText.")]
        [SerializeField] private TMP_Text titleText;

        [Header("Row style")]
        [SerializeField] private float rowFontSize = 17f;
        [Tooltip("Row background. Matches the shade the market feed's rows use.")]
        [SerializeField] private Color rowBackgroundColor = new Color(0.08627451f, 0.07058824f, 0.043137256f, 1f);
        [Tooltip("Muted text: week headings, expired contracts, new-listing notes.")]
        [SerializeField] private Color rowTextColor = new Color(0.6666667f, 0.59607846f, 0.47058824f, 1f);

        private ContractFeed _feed;
        private bool _subscribed;
        private FeedList _list;

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            if (contractManager == null)
                contractManager = FindFirstObjectByType<ContractManager>();

            if (timeManager == null)
                timeManager = FindFirstObjectByType<TimeManager>();

            if (scrollRect == null)
                scrollRect = GetComponentInChildren<ScrollRect>(true);

            if (content == null && scrollRect != null)
                content = scrollRect.content;

            if (titleText == null)
            {
                Transform title = transform.Find("FeedHeader/FeedTitleText");
                if (title != null) titleText = title.GetComponent<TMP_Text>();
            }

            TMP_FontAsset font = emptyText != null ? emptyText.font : titleText != null ? titleText.font : null;
            if (content != null)
                _list = new FeedList(content, font, rowFontSize, rowBackgroundColor, rowTextColor);
        }

        private void OnEnable()
        {
            Subscribe();
            Rebuild();
            ScrollToTop();
        }

        private void Start()
        {
            if (_subscribed)
                return;

            Subscribe();

            if (_subscribed)
            {
                Rebuild();
                ScrollToTop();
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (_subscribed)
                return;

            if (contractManager == null)
                contractManager = FindFirstObjectByType<ContractManager>();

            if (contractManager == null)
                return;

            _feed = contractManager.Feed;
            if (_feed == null)
                return;

            _feed.OnChanged += HandleChanged;

            if (timeManager != null)
                timeManager.OnWeekChanged += HandleWeekChanged;

            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
                return;

            if (_feed != null)
                _feed.OnChanged -= HandleChanged;

            if (timeManager != null)
                timeManager.OnWeekChanged -= HandleWeekChanged;

            _subscribed = false;
        }

        private void HandleChanged()
        {
            bool wasAtTop = scrollRect == null || scrollRect.verticalNormalizedPosition >= 0.999f;
            Rebuild();
            if (wasAtTop)
                ScrollToTop();
        }

        // "This week" / "Last week" headings move on with the calendar.
        private void HandleWeekChanged(int week) => HandleChanged();

        // ------------------------------------------------------------------ building

        private void Rebuild()
        {
            if (titleText != null)
                titleText.text = Title;

            if (emptyText != null)
                emptyText.gameObject.SetActive(false);

            if (_list == null)
                return;

            _list.Clear();

            if (_feed == null)
                return;

            _list.AddSummary(SummaryText(_feed.PlayerTotals));

            IReadOnlyList<ContractFeedEntry> entries = _feed.Entries;
            if (entries.Count == 0)
            {
                _list.AddNote("Nothing has happened on the contract board yet.", indented: false);
                return;
            }

            int currentWeek = timeManager != null ? timeManager.CurrentWeek : int.MinValue;
            int shownWeek = int.MinValue;

            foreach (var entry in entries)
            {
                if (entry == null)
                    continue;

                if (entry.Week != shownWeek)
                {
                    shownWeek = entry.Week;
                    _list.AddHeading(FeedList.WeekHeading(entry.Week, currentWeek));
                }

                if (entry.Kind == ContractFeedKind.Listed)
                {
                    _list.AddNote(entry.Count == 1 ? "+ 1 new contract listed" : $"+ {entry.Count} new contracts listed");
                    continue;
                }

                string amount = AmountText(entry, out Color amountColor);
                Color textColor = entry.Kind == ContractFeedKind.Expired ? _list.Muted : _list.Primary;
                _list.AddEntry(MarkerColor(entry), Describe(entry), textColor, amount, amountColor);
            }
        }

        private string SummaryText(ContractFeedTotals totals)
        {
            if (totals == null || totals.Fulfilled + totals.Failed == 0)
                return "Your record: no contracts completed yet.";

            string money = $"Earned {UIFormat.Money(totals.Earned)}";
            if (totals.PenaltiesPaid > 0f)
                money += $", penalties {UIFormat.Money(totals.PenaltiesPaid)}";

            return $"Your record: {totals.Fulfilled} fulfilled, {totals.Failed} failed\n" +
                   UIFormat.Colorize(money, rowTextColor);
        }

        // ------------------------------------------------------------------ wording

        private static string Describe(ContractFeedEntry entry)
        {
            string who = UIFormat.OwnerName(entry.TraderId, "A trader", "You");
            string name = entry.ContractName;

            return entry.Kind switch
            {
                ContractFeedKind.TakenOn => entry.Type == ContractType.Exclusive
                    ? $"{who} took on {name} (exclusive)"
                    : $"{who} took on {name}",
                ContractFeedKind.Fulfilled => $"{who} fulfilled {name}",
                ContractFeedKind.Failed => $"{who} failed {name}",
                ContractFeedKind.Expired => $"{name} expired unfulfilled",
                _ => name
            };
        }

        /// <summary>The player's money is green / red; other traders' amounts are shown muted.</summary>
        private string AmountText(ContractFeedEntry entry, out Color color)
        {
            color = _list.Muted;

            switch (entry.Kind)
            {
                case ContractFeedKind.Fulfilled when entry.Amount > 0f:
                    if (entry.IsPlayer) color = _list.Positive;
                    return entry.IsPlayer ? UIFormat.SignedMoney(entry.Amount) : UIFormat.Money(entry.Amount);

                case ContractFeedKind.Failed when entry.Amount > 0f:
                    if (entry.IsPlayer) color = _list.Negative;
                    return UIFormat.SignedMoney(-entry.Amount);

                default:
                    return null;
            }
        }

        private Color MarkerColor(ContractFeedEntry entry) => entry.Kind switch
        {
            ContractFeedKind.Fulfilled => entry.IsPlayer ? _list.Positive : _list.Primary,
            ContractFeedKind.TakenOn => _list.Accent,
            ContractFeedKind.Failed => _list.Negative,
            _ => _list.Muted
        };

        private void ScrollToTop()
        {
            if (scrollRect == null || content == null)
                return;

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            scrollRect.verticalNormalizedPosition = 1f;
        }
    }
}
