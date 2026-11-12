using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Auctions;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Logistics;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// The auction room. Opened when the player joins (AuctionJoinBannerUI calls Open), it pauses the
    /// game clock and follows the live session lot by lot, in three screens:
    ///  - Presentation: the lot in full — picture, description, provenance, estimate, market value and
    ///    what winning would cost. Bidding opens when the player presses Start bidding (or skips the lot).
    ///  - Bidding: current bid and its leader, the auctioneer's calls ("1 500 £ for the first time…")
    ///    with a countdown to the next one, who is still in, a short log. Bid / Pass lot / Leave.
    ///    Between lots the result stays on screen for a moment.
    ///  - Summary after the last lot: what the player won. Close hides the modal.
    /// Escape (via ModalTracker) leaves the room while lots are running and closes the summary afterwards.
    /// </summary>
    public class LiveAuctionModalUI : MonoBehaviour, IModalPanel
    {
        private const int MaxLogLines = 7;
        private const float RefreshInterval = 0.1f;

        [Header("Dependencies (auto-found if empty)")]
        [SerializeField] private AuctionManager auctionManager;
        [SerializeField] private TimeManager timeManager;
        [SerializeField] private PlayerTrader playerTrader;
        [SerializeField] private EconomyManager economyManager;

        [Tooltip("Pause the game clock while the room is open (the auction itself runs in real seconds).")]
        [SerializeField] private bool pauseTimeWhileOpen = true;

        [Header("Header")]
        [SerializeField] private TMP_Text headerText;
        [SerializeField] private TMP_Text feesText;

        [Header("Presentation")]
        [SerializeField] private GameObject presentationContent;
        [SerializeField] private Image presentationImage;
        [SerializeField] private TMP_Text presentationNameText;
        [SerializeField] private TMP_Text presentationInfoText;
        [SerializeField] private TMP_Text presentationProvenanceText;
        [SerializeField] private TMP_Text presentationDescriptionText;
        [SerializeField] private TMP_Text presentationFactLabelsText;
        [SerializeField] private TMP_Text presentationFactValuesText;
        [SerializeField] private TMP_Text presentationHintText;

        [Header("Bidding")]
        [SerializeField] private GameObject liveContent;
        [SerializeField] private Image lotImage;
        [SerializeField] private TMP_Text lotNameText;
        [SerializeField] private TMP_Text lotInfoText;
        [SerializeField] private TMP_Text lotProvenanceText;
        [SerializeField] private TMP_Text lotFactsText;
        [SerializeField] private TMP_Text currentBidText;
        [SerializeField] private TMP_Text leaderText;
        [SerializeField] private TMP_Text callText;
        [SerializeField] private ProgressBarUI callBar;
        [SerializeField] private TMP_Text callTimerText;
        [SerializeField] private TMP_Text biddersText;
        [SerializeField] private TMP_Text logText;
        [SerializeField] private TMP_Text cashText;

        [Header("Summary")]
        [SerializeField] private GameObject summaryContent;
        [SerializeField] private TMP_Text summaryTitleText;
        [SerializeField] private TMP_Text summaryBodyText;

        [Header("Footer")]
        [SerializeField] private TMP_Text warningText;
        [SerializeField] private Button startBiddingButton;
        [SerializeField] private Button skipLotButton;
        [SerializeField] private Button bidButton;
        [SerializeField] private TMP_Text bidButtonLabel;
        [SerializeField] private Button passButton;
        [SerializeField] private Button leaveButton;
        [SerializeField] private Button closeButton;

        private enum RoomScreen { Presentation, Bidding, Summary }

        private AuctionSession _session;
        private LotBidding _bidding;
        private readonly List<string> _log = new List<string>();
        private RoomScreen _screen;
        private bool _initialized;
        private bool _pausedByModal;
        private bool _playerLeft;
        private float _refreshTimer;

        private TraderInventory Inventory => playerTrader != null ? playerTrader.Inventory : null;
        private Market.Market CurrentMarket => economyManager != null ? economyManager.Market : null;

        // ------------------------------------------------------------------ open / close

        public void Open(AuctionSession session)
        {
            if (session == null) return;

            EnsureInitialized();
            Detach();

            _session = session;
            _bidding = null;
            _playerLeft = false;
            _log.Clear();

            _session.OnLotOpened += HandleLotOpened;
            _session.OnBiddingStarted += HandleBiddingStarted;
            _session.OnLotClosed += HandleLotClosed;
            _session.OnFinished += HandleFinished;

            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            if (pauseTimeWhileOpen && timeManager != null && timeManager.IsRunning)
            {
                timeManager.Pause();
                _pausedByModal = true;
            }

            if (feesText != null && auctionManager != null)
                feesText.text = $"Buyer's premium {UIFormat.Percent(auctionManager.Settings.BuyersPremiumRate)} · " +
                                $"ships from {auctionManager.Settings.AuctionHouseZone.ToDisplayString()}";

            ShowScreen(RoomScreen.Presentation);
            if (_session.Current != null) HandleLotOpened(_session.Current);
            if (_session.IsFinished) HandleFinished(_session);

            RefreshDynamic();
        }

        /// <summary>Escape / close: leaves the room while lots are running, hides the summary afterwards.</summary>
        public void Close()
        {
            if (_session != null && !_session.IsFinished)
            {
                Leave();
                if (gameObject.activeSelf) ModalTracker.SetOpen(this); // stay tracked while the summary is shown
                return;
            }

            Hide();
        }

        private void Hide()
        {
            Detach();
            _session = null;
            gameObject.SetActive(false);
        }

        private void OnEnable() => ModalTracker.SetOpen(this);

        private void OnDisable()
        {
            ModalTracker.SetClosed(this);

            if (_pausedByModal && timeManager != null)
                timeManager.Resume();
            _pausedByModal = false;

            // Hidden by something else mid-auction: the player is no longer watching.
            if (_session != null && !_session.IsFinished && auctionManager != null && auctionManager.IsPlayerInRoom)
                auctionManager.LeaveAuction();
        }

        private void EnsureInitialized()
        {
            if (_initialized) return;
            _initialized = true;

            if (auctionManager == null) auctionManager = FindFirstObjectByType<AuctionManager>();
            if (timeManager == null) timeManager = FindFirstObjectByType<TimeManager>();
            if (playerTrader == null) playerTrader = FindFirstObjectByType<PlayerTrader>();
            if (economyManager == null) economyManager = FindFirstObjectByType<EconomyManager>();

            if (startBiddingButton != null) startBiddingButton.onClick.AddListener(StartBidding);
            if (skipLotButton != null) skipLotButton.onClick.AddListener(SkipLot);
            if (bidButton != null) bidButton.onClick.AddListener(Bid);
            if (passButton != null) passButton.onClick.AddListener(Pass);
            if (leaveButton != null) leaveButton.onClick.AddListener(Leave);
            if (closeButton != null) closeButton.onClick.AddListener(Hide);
        }

        private void Detach()
        {
            UnhookBidding();

            if (_session != null)
            {
                _session.OnLotOpened -= HandleLotOpened;
                _session.OnBiddingStarted -= HandleBiddingStarted;
                _session.OnLotClosed -= HandleLotClosed;
                _session.OnFinished -= HandleFinished;
            }
        }

        private void UnhookBidding()
        {
            if (_bidding == null) return;

            _bidding.OnBid -= HandleBid;
            _bidding.OnWithdraw -= HandleWithdraw;
            _bidding.OnCall -= HandleCall;
            _bidding = null;
        }

        // ------------------------------------------------------------------ actions

        private void StartBidding()
        {
            if (auctionManager != null) auctionManager.StartBidding();
        }

        private void SkipLot()
        {
            if (auctionManager == null) return;
            AddLog(UIFormat.Colorize("You sit this lot out.", UIFormat.MutedColor));
            auctionManager.SkipLot();
        }

        private void Bid()
        {
            if (auctionManager == null) return;
            auctionManager.PlayerBid();
            RefreshDynamic();
        }

        private void Pass()
        {
            if (auctionManager == null) return;
            auctionManager.PlayerPassLot();
            RefreshDynamic();
        }

        private void Leave()
        {
            if (auctionManager == null || _session == null || _session.IsFinished) return;

            _playerLeft = true;
            auctionManager.LeaveAuction(); // finishes the session at once → HandleFinished shows the summary
        }

        // ------------------------------------------------------------------ session events

        private void HandleLotOpened(LotBidding bidding)
        {
            UnhookBidding();

            _bidding = bidding;
            _bidding.OnBid += HandleBid;
            _bidding.OnWithdraw += HandleWithdraw;
            _bidding.OnCall += HandleCall;

            _log.Clear();
            Set(logText, "");

            FillPresentation(bidding.Lot);
            FillLotColumn(bidding.Lot);

            if (_session.IsPresenting) ShowScreen(RoomScreen.Presentation);
            else HandleBiddingStarted(bidding);

            RefreshDynamic();
        }

        private void HandleBiddingStarted(LotBidding bidding)
        {
            ShowScreen(RoomScreen.Bidding);

            int npcs = bidding.Bidders.Count(b => !b.IsPlayer);
            AddLog(UIFormat.Colorize(
                npcs == 0
                    ? $"Bidding opens at {UIFormat.Money(bidding.Lot.StartPrice)} — no other bidders in the room."
                    : $"Bidding opens at {UIFormat.Money(bidding.Lot.StartPrice)} · {npcs} {(npcs == 1 ? "bidder" : "bidders")} in the room.",
                UIFormat.MutedColor));

            RefreshDynamic();
        }

        private void HandleBid(AuctionBidder bidder, float amount)
        {
            string name = bidder.IsPlayer ? UIFormat.Colorize("You", UIFormat.AccentColor) : bidder.Name;
            AddLog($"{name} bids {UIFormat.Money(amount)}");
            RefreshDynamic();
        }

        private void HandleWithdraw(AuctionBidder bidder)
        {
            string name = bidder.IsPlayer ? "You" : bidder.Name;
            AddLog(UIFormat.Colorize($"{name} withdraws", UIFormat.MutedColor));
            RefreshDynamic();
        }

        private void HandleCall(AuctioneerCall call)
        {
            if (_bidding == null) return;

            if (call == AuctioneerCall.GoingOnce || call == AuctioneerCall.GoingTwice)
                AddLog(UIFormat.Colorize(CallLine(_bidding, call), UIFormat.AccentColor));

            RefreshDynamic();
        }

        private void HandleLotClosed(AuctionLot lot, LotBidding bidding)
        {
            ShowScreen(RoomScreen.Bidding); // a skipped lot shows its result here too

            AddLog(lot.IsSold
                ? UIFormat.Colorize($"{UIFormat.Money(lot.HammerPrice)} for the third time — sold to {AuctionPresentation.Winner(lot)}!",
                    AuctionPresentation.IsPlayer(lot.WinnerId) ? UIFormat.PositiveColor : UIFormat.AccentColor)
                : UIFormat.Colorize($"No bids at {UIFormat.Money(lot.StartPrice)} — the lot is withdrawn.", UIFormat.MutedColor));
            RefreshDynamic();
        }

        private void HandleFinished(AuctionSession session)
        {
            if (session != _session) return;
            ShowSummary();
        }

        // ------------------------------------------------------------------ screens

        private void ShowScreen(RoomScreen screen)
        {
            _screen = screen;

            SetActive(presentationContent, screen == RoomScreen.Presentation);
            SetActive(liveContent, screen == RoomScreen.Bidding);
            SetActive(summaryContent, screen == RoomScreen.Summary);

            SetActive(startBiddingButton, screen == RoomScreen.Presentation);
            SetActive(skipLotButton, screen == RoomScreen.Presentation);
            SetActive(bidButton, screen == RoomScreen.Bidding);
            SetActive(passButton, screen == RoomScreen.Bidding);
            SetActive(leaveButton, screen != RoomScreen.Summary);
            SetActive(closeButton, screen == RoomScreen.Summary);

            if (screen != RoomScreen.Bidding) Set(warningText, "");
        }

        private void FillPresentation(AuctionLot lot)
        {
            if (lot == null) return;

            if (presentationImage != null) presentationImage.ShowAntique(lot.Antique);
            Set(presentationNameText, lot.Name);
            Set(presentationInfoText, UIFormat.Colorize(AuctionPresentation.InfoLine(lot.Antique), UIFormat.MutedColor));

            string provenance = AuctionPresentation.Provenance(lot);
            SetActive(presentationProvenanceText, !string.IsNullOrEmpty(provenance));
            Set(presentationProvenanceText, string.IsNullOrEmpty(provenance) ? "" : $"<i>Provenance: {provenance}</i>");

            string description = lot.Antique != null ? lot.Antique.Description : "";
            SetActive(presentationDescriptionText, !string.IsNullOrWhiteSpace(description));
            Set(presentationDescriptionText, description);

            var facts = AuctionPresentation.CatalogFacts(lot, auctionManager, CurrentMarket, includeOpeningBid: true);
            Set(presentationFactLabelsText, string.Join("\n", facts.Select(f => f.label)));
            Set(presentationFactValuesText, string.Join("\n", facts.Select(f => f.value)));
        }

        /// <summary>The compact lot column shown next to the bidding.</summary>
        private void FillLotColumn(AuctionLot lot)
        {
            if (lot == null) return;

            if (lotImage != null) lotImage.ShowAntique(lot.Antique);
            Set(lotNameText, lot.Name);
            Set(lotInfoText, UIFormat.Colorize(AuctionPresentation.InfoLine(lot.Antique), UIFormat.MutedColor));

            string provenance = AuctionPresentation.Provenance(lot);
            SetActive(lotProvenanceText, !string.IsNullOrEmpty(provenance));
            Set(lotProvenanceText, string.IsNullOrEmpty(provenance) ? "" : $"<i>{provenance}</i>");

            Set(lotFactsText,
                $"Estimate  <b>{AuctionPresentation.Estimate(lot)}</b>\n" +
                $"Opening bid  {UIFormat.Money(lot.StartPrice)}\n" +
                $"Market value  {(lot.Antique != null ? UIFormat.Money(lot.Antique.CurrentPrice) : "—")}\n" +
                $"Last auction price  {AuctionPresentation.LastAuctionPrice(CurrentMarket, lot.Antique)}");
        }

        // ------------------------------------------------------------------ refresh

        private void Update()
        {
            if (_session == null || _session.IsFinished) return;

            _refreshTimer -= Time.unscaledDeltaTime;
            if (_refreshTimer > 0f) return;

            _refreshTimer = RefreshInterval;
            RefreshDynamic();
        }

        /// <summary>Everything that changes while a lot is up.</summary>
        private void RefreshDynamic()
        {
            if (_session == null || _screen == RoomScreen.Summary) return;

            var auction = _session.Auction;
            int lotNumber = _bidding != null ? auction.Lots.IndexOf(_bidding.Lot) + 1 : 1;
            string stage = _screen == RoomScreen.Presentation ? "presentation" : "bidding";
            Set(headerText, $"{AuctionPresentation.Title(auction)} · lot {Mathf.Max(1, lotNumber)} of {auction.Lots.Count} · {stage}");

            if (Inventory != null) Set(cashText, $"Your cash  <b>{UIFormat.Money(Inventory.Cash)}</b>");

            if (_bidding == null) return;

            if (_screen == RoomScreen.Presentation)
            {
                float auto = _session.PresentationSecondsLeft;
                Set(presentationHintText, UIFormat.Colorize(auto > 0f
                        ? $"Bidding opens in {Mathf.CeilToInt(auto)} s — or press Start bidding when you're ready."
                        : "Take your time. Bidding opens when you press Start bidding; Skip lot leaves it to the others.",
                    UIFormat.MutedColor));
                return;
            }

            var bidding = _bidding;
            var paddle = _session.Player;
            bool playerLeads = paddle != null && bidding.Leader == paddle;

            Set(currentBidText, bidding.HasBid
                ? UIFormat.Money(bidding.CurrentBid)
                : UIFormat.Colorize(UIFormat.Money(bidding.Lot.StartPrice), UIFormat.MutedColor));

            Set(leaderText, !bidding.HasBid
                ? UIFormat.Colorize("Waiting for an opening bid", UIFormat.MutedColor)
                : playerLeads
                    ? UIFormat.Colorize("You are the highest bidder", UIFormat.PositiveColor)
                    : $"Highest bidder: {bidding.Leader.Name}");

            RefreshCall(bidding);
            Set(biddersText, BiddersList(bidding));

            string reason = "No auction house.";
            bool canBid = auctionManager != null && auctionManager.CanPlayerBid(out reason);

            float asking = bidding.AskingBid;
            float total = auctionManager != null ? auctionManager.PlayerCostAt(asking) : asking;
            Set(bidButtonLabel, $"Bid {UIFormat.Money(asking)}  <size=80%>(total {UIFormat.Money(total)})</size>");
            SetInteractable(bidButton, canBid);

            bool canPass = !bidding.IsClosed && paddle != null && paddle.Active && !playerLeads;
            SetInteractable(passButton, canPass);

            string warning = "";
            if (!canBid && !bidding.IsClosed && !playerLeads && reason != null)
                warning = UIFormat.Colorize(reason, UIFormat.NegativeColor);
            Set(warningText, warning);
        }

        private void RefreshCall(LotBidding bidding)
        {
            if (bidding.IsClosed)
            {
                var lot = bidding.Lot;
                Set(callText, lot.IsSold
                    ? UIFormat.Colorize($"Sold to {AuctionPresentation.Winner(lot)} for {UIFormat.Money(lot.HammerPrice)}!",
                        AuctionPresentation.IsPlayer(lot.WinnerId) ? UIFormat.PositiveColor : UIFormat.AccentColor)
                    : UIFormat.Colorize("Unsold — no bids", UIFormat.MutedColor));

                Set(callTimerText, _session.Auction.FirstPendingLot != null
                    ? UIFormat.Colorize($"Next lot in {Mathf.CeilToInt(_session.SecondsToNextLot)} s", UIFormat.MutedColor)
                    : "");
                if (callBar != null) callBar.SetValue(0f);
                return;
            }

            Set(callText, bidding.LastCall == AuctioneerCall.None
                ? UIFormat.Colorize(bidding.HasBid
                        ? $"{UIFormat.Money(bidding.CurrentBid)} — any advance?"
                        : $"Opening at {UIFormat.Money(bidding.Lot.StartPrice)} — who will start?",
                    UIFormat.MutedColor)
                : UIFormat.Colorize(CallLine(bidding, bidding.LastCall), UIFormat.AccentColor));

            // Countdown to the auctioneer's next call.
            int seconds = Mathf.CeilToInt(bidding.SecondsToNextCall);
            Set(callTimerText, $"{NextCallLabel(bidding)} in <b>{seconds} s</b>");

            if (callBar != null && auctionManager != null)
            {
                float span = bidding.LastCall == AuctioneerCall.None && !bidding.HasBid
                    ? auctionManager.Settings.CallIntervalSeconds + auctionManager.Settings.OpeningGraceSeconds
                    : auctionManager.Settings.CallIntervalSeconds;
                callBar.SetValue(bidding.SecondsToNextCall / Mathf.Max(0.01f, span));
            }
        }

        /// <summary>"1 500 £ for the first time…" — what the auctioneer just called.</summary>
        private static string CallLine(LotBidding bidding, AuctioneerCall call)
        {
            string price = bidding.HasBid ? UIFormat.Money(bidding.CurrentBid) : UIFormat.Money(bidding.Lot.StartPrice);
            string tail = bidding.HasBid ? "" : " — no bids yet";

            return call switch
            {
                AuctioneerCall.GoingOnce => $"{price} for the first time…{tail}",
                AuctioneerCall.GoingTwice => $"{price} for the second time…{tail}",
                AuctioneerCall.Closed => bidding.HasBid ? $"{price} for the third time — sold!" : "Withdrawn — no bids",
                _ => ""
            };
        }

        /// <summary>What comes next if nobody bids: "First call", "Second call", "Sold" or "Withdrawn".</summary>
        private static string NextCallLabel(LotBidding bidding) => bidding.LastCall switch
        {
            AuctioneerCall.None => "First call",
            AuctioneerCall.GoingOnce => "Second call",
            _ => bidding.HasBid ? "Third call — sold" : "Withdrawn"
        };

        private static string BiddersList(LotBidding bidding)
        {
            var sb = new StringBuilder();
            foreach (var bidder in bidding.Bidders)
            {
                string name = bidder.IsPlayer ? "You" : bidder.Name;
                string status = bidding.Leader == bidder
                    ? UIFormat.Colorize("leading", UIFormat.PositiveColor)
                    : bidder.Active
                        ? UIFormat.Colorize("in", UIFormat.AccentColor)
                        : UIFormat.Colorize("out", UIFormat.MutedColor);

                if (sb.Length > 0) sb.Append('\n');
                sb.Append(bidder.Active || bidding.Leader == bidder ? name : UIFormat.Colorize(name, UIFormat.MutedColor));
                sb.Append("  · ").Append(status);
            }

            if (bidding.Bidders.All(b => b.IsPlayer))
                sb.Append('\n').Append(UIFormat.Colorize("No one else wants this lot.", UIFormat.MutedColor));

            return sb.ToString();
        }

        private void ShowSummary()
        {
            ShowScreen(RoomScreen.Summary);

            var auction = _session != null ? _session.Auction : null;
            if (auction == null) return;

            Set(headerText, $"{AuctionPresentation.Title(auction)} · finished");

            var won = auction.Lots.Where(l => l.IsSold && AuctionPresentation.IsPlayer(l.WinnerId)).ToList();
            float premiumRate = auctionManager != null ? auctionManager.Settings.BuyersPremiumRate : 0f;

            Set(summaryTitleText, won.Count == 0
                ? "You didn't win any lot"
                : won.Count == 1 ? "You won 1 lot" : $"You won {won.Count} lots");

            var sb = new StringBuilder();
            foreach (var lot in won)
            {
                float premium = lot.HammerPrice * premiumRate;
                sb.Append($"{AuctionPresentation.LotTitle(lot)}  —  {UIFormat.Money(lot.HammerPrice)} + {UIFormat.Money(premium)} premium\n");
            }

            if (won.Count > 0)
            {
                float spent = won.Sum(l => l.HammerPrice * (1f + premiumRate));
                sb.Append(UIFormat.Colorize($"Paid {UIFormat.Money(spent)} plus transport. The lots are on their way to your warehouse.\n", UIFormat.MutedColor));
            }

            sb.Append('\n');
            foreach (var lot in auction.Lots.Where(l => !won.Contains(l)))
            {
                string result = lot.IsSold
                    ? $"sold to {lot.WinnerName} for {UIFormat.Money(lot.HammerPrice)}"
                    : "unsold";
                sb.Append(UIFormat.Colorize($"{AuctionPresentation.LotTitle(lot)}  —  {result}\n", UIFormat.MutedColor));
            }

            if (_playerLeft)
                sb.Append('\n').Append(UIFormat.Colorize("You left the room early — the remaining lots were auctioned without you.", UIFormat.MutedColor));

            Set(summaryBodyText, sb.ToString().TrimEnd('\n'));
        }

        // ------------------------------------------------------------------ helpers

        private void AddLog(string line)
        {
            _log.Add(line);
            while (_log.Count > MaxLogLines) _log.RemoveAt(0);
            Set(logText, string.Join("\n", _log));
        }

        private static void Set(TMP_Text target, string value)
        {
            if (target != null) target.text = value ?? "";
        }

        private static void SetActive(Component target, bool active)
        {
            if (target != null && target.gameObject.activeSelf != active) target.gameObject.SetActive(active);
        }

        private static void SetActive(GameObject target, bool active)
        {
            if (target != null && target.activeSelf != active) target.SetActive(active);
        }

        private static void SetInteractable(Selectable target, bool interactable)
        {
            if (target != null) target.interactable = interactable;
        }
    }
}
