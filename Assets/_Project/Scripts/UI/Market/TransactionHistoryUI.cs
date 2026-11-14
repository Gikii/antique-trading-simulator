using System.Collections.Generic;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Market;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Market History in the Market view's side panel, shown whenever the Antique Details panel is closed:
    ///
    ///   Your trading: bought 5 for 3 400 £, sold 3 for 4 100 £
    ///   This week on the market: 12 sales, 31 400 £ turnover
    ///   THIS WEEK
    ///   ■ Hargreaves bought your Ormolu clock           +1 240 £
    ///   ■ You bought Delft vase                           -820 £
    ///   ■ Whitcombe won Regency bureau at auction        4 900 £
    ///       + 14 new listings
    ///
    /// Reads Market.Feed (structured entries) and rebuilds its rows (FeedList, shared with Contract
    /// History) whenever the history changes or a new week starts.
    /// </summary>
    public class TransactionHistoryUI : MonoBehaviour
    {
        public const string Title = "Market History";

        [Header("Dependencies")]
        [SerializeField] private EconomyManager economyManager;
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

        [Tooltip("Row background.")]
        [SerializeField] private Color rowBackgroundColor = new Color(0.08627451f, 0.07058824f, 0.043137256f, 1f);

        [Tooltip("Muted text: week headings, other traders' prices, new-listing notes.")]
        [SerializeField] private Color rowTextColor = new Color(0.6666667f, 0.59607846f, 0.47058824f, 1f);

        // Markers for buyers outside the simulated traders.
        private static readonly Color CollectorColor = new Color32(0x7E, 0x9C, 0xB0, 0xFF); // dusty blue
        private static readonly Color AuctionColor = new Color32(0xB8, 0x73, 0x33, 0xFF);   // copper

        private MarketFeed _feed;
        private bool _subscribed;
        private FeedList _list;

        // ------------------------------------------------------------------ lifecycle

        private void Awake()
        {
            if (economyManager == null)
                economyManager = FindFirstObjectByType<EconomyManager>();

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

            var market = economyManager != null ? economyManager.Market : null;
            if (market == null)
                return;

            _feed = market.Feed;
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

        // "This week" / "Last week" headings and the week's stats move on with the calendar.
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

            int currentWeek = timeManager != null ? timeManager.CurrentWeek : int.MinValue;

            _list.AddSummary(SummaryText(_feed.PlayerTotals, _feed.LatestWeek, currentWeek));

            IReadOnlyList<MarketFeedEntry> entries = _feed.Entries;
            if (entries.Count == 0)
            {
                _list.AddNote("No market activity yet.", indented: false);
                return;
            }

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

                if (entry.Kind == MarketFeedKind.Listed)
                {
                    _list.AddNote(entry.Count == 1 ? "+ 1 new listing" : $"+ {entry.Count} new listings");
                    continue;
                }

                string amount = AmountText(entry, out Color amountColor);
                Color textColor = entry.Kind == MarketFeedKind.AuctionUnsold ? _list.Muted : _list.Primary;
                _list.AddEntry(MarkerColor(entry), Describe(entry), textColor, amount, amountColor);
            }
        }

        private string SummaryText(MarketFeedTotals totals, MarketWeekStats week, int currentWeek)
        {
            string trading = totals == null || totals.Bought + totals.Sold == 0
                ? "Your trading: nothing bought or sold yet."
                : $"Your trading: bought {totals.Bought} for {UIFormat.Money(totals.Spent)}, " +
                  $"sold {totals.Sold} for {UIFormat.Money(totals.Earned)}";

            bool thisWeek = week != null && week.Week == currentWeek && week.Sales > 0;
            string market = thisWeek
                ? $"This week on the market: {Plural(week.Sales, "sale")}, {UIFormat.Money(week.Turnover)} turnover"
                : "This week on the market: no sales yet";

            return trading + "\n" + UIFormat.Colorize(market, rowTextColor);
        }

        private static string Plural(int count, string noun) => count == 1 ? $"1 {noun}" : $"{count} {noun}s";

        // ------------------------------------------------------------------ wording

        private static string Name(string ownerId) => UIFormat.OwnerName(ownerId, "A trader", "You");

        private static string Describe(MarketFeedEntry entry)
        {
            string item = entry.AntiqueName;
            bool hasSeller = !string.IsNullOrEmpty(entry.SellerId);

            switch (entry.Kind)
            {
                case MarketFeedKind.Purchase:
                    if (entry.SoldByPlayer) return $"{Name(entry.BuyerId)} bought your {item}";
                    return hasSeller
                        ? $"{Name(entry.BuyerId)} bought {item} from {Name(entry.SellerId)}"
                        : $"{Name(entry.BuyerId)} bought {item}";

                case MarketFeedKind.CollectorPurchase:
                    if (entry.SoldByPlayer) return $"A private collector bought your {item}";
                    return hasSeller
                        ? $"A private collector bought {item} from {Name(entry.SellerId)}"
                        : $"A private collector bought {item}";

                case MarketFeedKind.SoldToMarket:
                    return $"{Name(entry.SellerId)} sold {item} to the market";

                case MarketFeedKind.AuctionSold:
                    return $"{Name(entry.BuyerId)} won {item} at auction";

                case MarketFeedKind.AuctionUnsold:
                    return $"{item} found no buyer at auction";

                default:
                    return item;
            }
        }

        /// <summary>
        /// The player's money: income green (+), spending in the main text colour (−).
        /// Other traders' prices are shown muted.
        /// </summary>
        private string AmountText(MarketFeedEntry entry, out Color color)
        {
            color = _list.Muted;

            if (entry.Price <= 0f || entry.Kind == MarketFeedKind.AuctionUnsold)
                return null;

            if (entry.SoldByPlayer)
            {
                color = _list.Positive;
                return UIFormat.SignedMoney(entry.Price);
            }

            if (entry.BoughtByPlayer)
            {
                color = _list.Primary;
                return UIFormat.SignedMoney(-entry.Price);
            }

            return UIFormat.Money(entry.Price);
        }

        private Color MarkerColor(MarketFeedEntry entry)
        {
            if (entry.SoldByPlayer) return _list.Positive;
            if (entry.BoughtByPlayer) return _list.Accent;

            return entry.Kind switch
            {
                MarketFeedKind.CollectorPurchase => CollectorColor,
                MarketFeedKind.AuctionSold => AuctionColor,
                MarketFeedKind.AuctionUnsold => _list.Muted,
                _ => _list.Primary
            };
        }

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
