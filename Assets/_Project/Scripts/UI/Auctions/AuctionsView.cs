using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Auctions;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Market;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    public enum AuctionsTab
    {
        Upcoming,
        Results
    }

    /// <summary>
    /// The Auctions tab. "Upcoming" shows the published catalogs (the next auction on top) with the
    /// details of the selected lot — there's no bidding here, auctions are live only; while an
    /// auction's join window is open the header offers a Join button. "Results" lists finished
    /// auctions lot by lot: hammer price vs. estimate and who won.
    /// Built by Tools → Antique Trading Simulator → Build Auctions UI.
    /// </summary>
    public class AuctionsView : UIView
    {
        [Header("Dependencies (auto-found if empty)")]
        [SerializeField] private AuctionManager auctionManager;
        [SerializeField] private EconomyManager economyManager;
        [SerializeField] private TimeManager timeManager;
        [SerializeField] private PlayerTrader playerTrader;

        [Header("Tabs")]
        [SerializeField] private Button upcomingTabButton;
        [SerializeField] private Button resultsTabButton;

        [Header("Header (next auction)")]
        [SerializeField] private GameObject headerPanel;
        [SerializeField] private TMP_Text headerTitleText;
        [SerializeField] private TMP_Text headerSubtitleText;
        [SerializeField] private Button joinButton;
        [SerializeField] private TMP_Text joinButtonLabel;

        [Header("List")]
        [SerializeField] private RectTransform listContent;
        [Tooltip("Inactive row cloned for every lot.")]
        [SerializeField] private AuctionLotRowUI rowTemplate;
        [Tooltip("Inactive text cloned for every auction heading.")]
        [SerializeField] private TMP_Text sectionTemplate;
        [SerializeField] private TMP_Text emptyText;
        [SerializeField] private ScrollRect scrollRect;

        [Header("Details")]
        [SerializeField] private AuctionLotDetailsUI details;

        [Header("Greybox tab colours (used until the UI theme exists)")]
        [SerializeField] private Color tabActiveColor = new Color32(0x4A, 0x35, 0x18, 0xFF);
        [SerializeField] private Color tabInactiveColor = new Color32(0x17, 0x13, 0x0C, 0xFF);

        private readonly List<GameObject> _spawned = new List<GameObject>();
        private readonly Dictionary<AuctionLot, AuctionLotRowUI> _rowsByLot = new Dictionary<AuctionLot, AuctionLotRowUI>();
        private AuctionsTab _activeTab = AuctionsTab.Upcoming;
        private AuctionLot _selectedLot;
        private bool _subscribed;
        private bool _wasJoinWindowOpen;

        private int CurrentDay => timeManager != null ? timeManager.CurrentDay : 1;
        private Market.Market CurrentMarket => economyManager != null ? economyManager.Market : null;

        private void Awake()
        {
            if (auctionManager == null) auctionManager = FindFirstObjectByType<AuctionManager>();
            if (economyManager == null) economyManager = FindFirstObjectByType<EconomyManager>();
            if (timeManager == null) timeManager = FindFirstObjectByType<TimeManager>();
            if (playerTrader == null) playerTrader = FindFirstObjectByType<PlayerTrader>();

            if (upcomingTabButton != null) upcomingTabButton.onClick.AddListener(() => SetTab(AuctionsTab.Upcoming));
            if (resultsTabButton != null) resultsTabButton.onClick.AddListener(() => SetTab(AuctionsTab.Results));
            if (joinButton != null) joinButton.onClick.AddListener(Join);

            if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);
            if (sectionTemplate != null) sectionTemplate.gameObject.SetActive(false);
        }

        protected override void OnShown()
        {
            Subscribe();
            SetTab(_activeTab);
        }

        protected override void OnHidden() => Unsubscribe();

        private void OnDestroy() => Unsubscribe();

        private void Subscribe()
        {
            if (_subscribed) return;

            if (auctionManager != null)
            {
                auctionManager.OnAuctionAnnounced += HandleAuctionChanged;
                auctionManager.OnJoinWindowOpened += HandleAuctionChanged;
                auctionManager.OnJoinWindowClosed += HandleJoinWindowClosed;
                auctionManager.OnLotSettled += HandleLotSettled;
                auctionManager.OnAuctionFinished += HandleAuctionChanged;
                auctionManager.OnAuctionsRestored += Refresh;
            }

            if (timeManager != null) timeManager.OnDayChanged += HandleDayChanged;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;

            if (auctionManager != null)
            {
                auctionManager.OnAuctionAnnounced -= HandleAuctionChanged;
                auctionManager.OnJoinWindowOpened -= HandleAuctionChanged;
                auctionManager.OnJoinWindowClosed -= HandleJoinWindowClosed;
                auctionManager.OnLotSettled -= HandleLotSettled;
                auctionManager.OnAuctionFinished -= HandleAuctionChanged;
                auctionManager.OnAuctionsRestored -= Refresh;
            }

            if (timeManager != null) timeManager.OnDayChanged -= HandleDayChanged;
            _subscribed = false;
        }

        private void HandleAuctionChanged(Auction auction) => Refresh();
        private void HandleJoinWindowClosed(Auction auction, bool joined) => Refresh();
        private void HandleLotSettled(Auction auction, AuctionLot lot) => Refresh();
        private void HandleDayChanged(int day) => Refresh();

        private void Update()
        {
            // The join countdown ticks in real seconds — keep the header's button label current.
            bool open = auctionManager != null && auctionManager.JoinWindowAuction != null;
            if (open || _wasJoinWindowOpen) RefreshHeader();
            _wasJoinWindowOpen = open;
        }

        // ---------------------------------------------------------------- tabs

        public void SetTab(AuctionsTab tab)
        {
            _activeTab = tab;
            _selectedLot = null;

            UITheme.ApplyTabState(upcomingTabButton, tab == AuctionsTab.Upcoming, tabActiveColor, tabInactiveColor);
            UITheme.ApplyTabState(resultsTabButton, tab == AuctionsTab.Results, tabActiveColor, tabInactiveColor);

            if (details != null) details.ShowEmpty();
            if (scrollRect != null) scrollRect.verticalNormalizedPosition = 1f;

            Refresh();
        }

        public void Refresh()
        {
            if (!isActiveAndEnabled) return;

            RefreshHeader();
            RebuildList();

            if (_selectedLot != null && _rowsByLot.ContainsKey(_selectedLot))
                Select(_selectedLot);
            else
            {
                _selectedLot = null;
                if (details != null) details.ShowEmpty();
            }
        }

        // ---------------------------------------------------------------- header

        private void RefreshHeader()
        {
            if (auctionManager == null)
            {
                SetText(headerTitleText, "Auctions");
                SetText(headerSubtitleText, UIFormat.Colorize("No auction house in this scene.", UIFormat.MutedColor));
                SetJoin(false, "");
                return;
            }

            var window = auctionManager.JoinWindowAuction;
            if (window != null)
            {
                int seconds = Mathf.CeilToInt(auctionManager.JoinSecondsLeft);
                SetText(headerTitleText, $"{AuctionPresentation.Title(window)} is starting");
                SetText(headerSubtitleText, $"{window.Lots.Count} lots · join within {seconds} s, then bidding is live");
                SetJoin(true, $"Join ({seconds} s)");
                return;
            }

            SetJoin(false, "");

            if (auctionManager.IsPlayerInRoom)
            {
                var live = auctionManager.LiveSession.Auction;
                SetText(headerTitleText, $"{AuctionPresentation.Title(live)} is in progress");
                SetText(headerSubtitleText, "You are in the auction room.");
                return;
            }

            var next = auctionManager.NextAuction;
            if (next == null)
            {
                SetText(headerTitleText, "No auction announced");
                SetText(headerSubtitleText, UIFormat.Colorize("Catalogs are published a few days before each auction.", UIFormat.MutedColor));
                return;
            }

            SetText(headerTitleText, $"{AuctionPresentation.Title(next)} · {UIFormat.GameDate(next.Day)}, {AuctionPresentation.Hour(next.StartTick)}");
            SetText(headerSubtitleText,
                $"{Capitalize(AuctionPresentation.When(next, CurrentDay))} · {next.Lots.Count} lots · estimates {AuctionPresentation.TotalEstimate(next)} in total");
        }

        private void SetJoin(bool visible, string label)
        {
            if (joinButton != null && joinButton.gameObject.activeSelf != visible)
                joinButton.gameObject.SetActive(visible);
            SetText(joinButtonLabel, label);
        }

        private void Join()
        {
            if (auctionManager == null) return;
            if (auctionManager.JoinAuction()) Refresh();
        }

        // ---------------------------------------------------------------- list

        private void RebuildList()
        {
            ClearList();

            int shown = _activeTab == AuctionsTab.Upcoming ? BuildUpcoming() : BuildResults();

            if (emptyText != null)
            {
                emptyText.gameObject.SetActive(shown == 0);
                emptyText.text = _activeTab == AuctionsTab.Upcoming
                    ? "No catalog published yet."
                    : "No auction has been held yet.";
            }
        }

        private int BuildUpcoming()
        {
            if (auctionManager == null) return 0;

            int rows = 0;
            int today = CurrentDay;

            foreach (var auction in auctionManager.Upcoming)
            {
                AddSection($"{AuctionPresentation.Title(auction)} · {UIFormat.GameDate(auction.Day)}, {AuctionPresentation.Hour(auction.StartTick)}" +
                           UIFormat.Colorize($"  ({AuctionPresentation.When(auction, today)})", UIFormat.MutedColor));

                foreach (var lot in auction.Lots)
                {
                    string subtitle = UIFormat.Colorize(SubtitleFor(lot), UIFormat.MutedColor);
                    string detail = lot.IsPending
                        ? $"Est. {AuctionPresentation.Estimate(lot)}"
                        : ResultDetail(lot);
                    AddRow(lot, subtitle, detail);
                    rows++;
                }
            }

            return rows;
        }

        private int BuildResults()
        {
            if (auctionManager == null) return 0;

            int rows = 0;
            foreach (var auction in auctionManager.History)
            {
                string attendance = auction.PlayerJoined ? "you took part" : "held without you";
                AddSection($"{AuctionPresentation.Title(auction)} · {UIFormat.GameDate(auction.Day)}" +
                           UIFormat.Colorize($"  ({auction.SoldCount}/{auction.Lots.Count} sold · {attendance})", UIFormat.MutedColor));

                foreach (var lot in auction.Lots)
                {
                    string subtitle = lot.IsSold
                        ? (AuctionPresentation.IsPlayer(lot.WinnerId)
                            ? UIFormat.Colorize("Won by you", UIFormat.AccentColor)
                            : UIFormat.Colorize($"Sold to {lot.WinnerName}", UIFormat.MutedColor))
                        : UIFormat.Colorize("No buyer", UIFormat.MutedColor);

                    AddRow(lot, subtitle, ResultDetail(lot));
                    rows++;
                }
            }

            return rows;
        }

        private static string SubtitleFor(AuctionLot lot)
        {
            var antique = lot.Antique;
            if (antique == null) return "";
            string text = $"{antique.Category} · {antique.Century.ToDisplayString()} · {UIFormat.ConditionLabel(antique.Condition)}";
            return lot.HasProvenance ? text + " · provenance" : text;
        }

        private static string ResultDetail(AuctionLot lot) =>
            lot.IsSold ? UIFormat.Money(lot.HammerPrice) : UIFormat.Colorize("Unsold", UIFormat.MutedColor);

        private void AddSection(string text)
        {
            if (sectionTemplate == null || listContent == null) return;

            var section = Instantiate(sectionTemplate, listContent);
            section.gameObject.SetActive(true);
            section.text = text;
            _spawned.Add(section.gameObject);
        }

        private void AddRow(AuctionLot lot, string subtitle, string detail)
        {
            if (rowTemplate == null || listContent == null) return;

            var row = Instantiate(rowTemplate, listContent);
            row.gameObject.SetActive(true);
            row.Setup(lot.Antique, AuctionPresentation.LotTitle(lot), subtitle, detail, () => Select(lot));
            _spawned.Add(row.gameObject);
            _rowsByLot[lot] = row;
        }

        private void ClearList()
        {
            foreach (var go in _spawned)
                if (go != null) Destroy(go);

            _spawned.Clear();
            _rowsByLot.Clear();
        }

        // ---------------------------------------------------------------- details

        private void Select(AuctionLot lot)
        {
            _selectedLot = lot;

            foreach (var pair in _rowsByLot)
                pair.Value.SetSelected(pair.Key == lot);

            if (details == null || lot == null) return;

            var auction = auctionManager != null ? auctionManager.AuctionOf(lot) : null;
            var facts = lot.IsPending ? CatalogFacts(lot) : ResultFacts(lot);
            string note = lot.IsPending ? CatalogNote(auction) : "";

            details.Show(lot.Antique, lot.Name, AuctionPresentation.InfoLine(lot.Antique),
                AuctionPresentation.Provenance(lot), facts, note);
        }

        private List<(string label, string value)> CatalogFacts(AuctionLot lot) =>
            AuctionPresentation.CatalogFacts(lot, auctionManager, CurrentMarket, includeOpeningBid: false);

        private static List<(string label, string value)> ResultFacts(AuctionLot lot)
        {
            var facts = new List<(string label, string value)>();

            if (lot.IsSold)
            {
                facts.Add(("Hammer price", $"{UIFormat.Money(lot.HammerPrice)}  <size=85%>{AuctionPresentation.VersusEstimate(lot)}</size>"));
                facts.Add(("Winner", AuctionPresentation.IsPlayer(lot.WinnerId)
                    ? UIFormat.Colorize("You", UIFormat.AccentColor)
                    : lot.WinnerName));
            }
            else
            {
                facts.Add(("Result", UIFormat.Colorize($"Unsold — no bid at {UIFormat.Money(lot.StartPrice)}", UIFormat.MutedColor)));
            }

            facts.Add(("Estimate", AuctionPresentation.Estimate(lot)));
            facts.Add(("Market value at sale", UIFormat.Money(lot.MarketValueAtSale)));
            return facts;
        }

        private string CatalogNote(Auction auction)
        {
            if (auction == null) return "";

            string when = AuctionPresentation.When(auction, CurrentDay);
            return UIFormat.Colorize(
                $"Bidding is live only. When the auction starts ({when}) you'll have a few seconds to join.",
                UIFormat.MutedColor);
        }

        // ---------------------------------------------------------------- helpers

        private static string Capitalize(string text) =>
            string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

        private static void SetText(TMP_Text target, string value)
        {
            if (target != null) target.text = value ?? "";
        }
    }
}
