using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.Market;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AntiqueTradingSimulator.Auctions
{
    /// <summary>
    /// The auction house. Publishes a catalog a few days ahead of each auction, and at the start hour
    /// opens a short window in which the player can join. With the player in the room the lots are
    /// auctioned live (real seconds, see AuctionSession / LotBidding); otherwise the NPCs play the
    /// session out on their own at once.
    ///
    /// Lot values grow over the campaign (AuctionSettings.ValueGrowth) but stay within what the NPCs
    /// can afford, so there is always competition. A result never sets a price: it is remembered as
    /// the type's last auction price and moves demand (hammer above market value = rising interest).
    /// </summary>
    public class AuctionManager : MonoBehaviour
    {
        [Header("Settings (in-memory defaults if empty)")]
        [SerializeField] private AuctionSettings settings;

        [Header("References (found automatically if empty)")]
        [SerializeField] private EconomyManager economyManager;
        [SerializeField] private Core.TimeManager timeManager;
        [SerializeField] private NPCManager npcManager;
        [SerializeField] private TransportManager transportManager;
        [SerializeField] private PlayerTrader playerTrader;

        [Header("Debug")]
        [Tooltip("Logs catalogs, NPC limits, every bid and every result to the console.")]
        [SerializeField] private bool logAuctions = true;

        /// <summary>Name the player's paddle shows during live bidding.</summary>
        public const string PlayerBidderName = "You";

        private readonly List<Auction> _upcoming = new List<Auction>();
        private readonly List<Auction> _history = new List<Auction>();
        private int _nextScheduleIndex;
        private float _joinSecondsLeft;
        private AuctionSession _liveSession;
        private bool _restored;

        public AuctionSettings Settings
        {
            get
            {
                if (settings == null) settings = ScriptableObject.CreateInstance<AuctionSettings>();
                return settings;
            }
        }

        /// <summary>Announced auctions not finished yet, earliest first.</summary>
        public IReadOnlyList<Auction> Upcoming => _upcoming;

        /// <summary>Finished auctions, newest first.</summary>
        public IReadOnlyList<Auction> History => _history;

        public Auction NextAuction => _upcoming.Count > 0 ? _upcoming[0] : null;

        /// <summary>The auction whose join window is open right now, or null.</summary>
        public Auction JoinWindowAuction => _upcoming.FirstOrDefault(a => a.Status == AuctionStatus.JoinWindow);

        /// <summary>Real seconds left to join (meaningful while JoinWindowAuction isn't null).</summary>
        public float JoinSecondsLeft => Mathf.Max(0f, _joinSecondsLeft);

        /// <summary>The live session with the player in the room, or null.</summary>
        public AuctionSession LiveSession => _liveSession;

        public bool IsPlayerInRoom => _liveSession != null && _liveSession.IsLive && !_liveSession.IsFinished;

        /// <summary>
        /// Set by the UI: while it returns true the join countdown stands still (e.g. the pause menu is
        /// open). The game clock is not involved — the countdown runs in real seconds at any game speed.
        /// </summary>
        public Func<bool> JoinCountdownPaused { get; set; }

        public event Action<Auction> OnAuctionAnnounced;
        public event Action<Auction> OnJoinWindowOpened;
        /// <summary>(auction, joined) — the window closed because the player joined or the time ran out.</summary>
        public event Action<Auction, bool> OnJoinWindowClosed;
        /// <summary>The player is in the room; subscribe to the session's events before its first lot opens.</summary>
        public event Action<AuctionSession> OnLiveSessionStarted;
        public event Action<Auction, AuctionLot> OnLotSettled;
        public event Action<Auction> OnAuctionFinished;
        public event Action OnAuctionsRestored;

        private int CurrentWeek => timeManager != null ? timeManager.CurrentWeek : 1;
        private int CurrentTick => timeManager != null ? timeManager.CurrentTick : 0;
        private TraderInventory PlayerInventory => playerTrader != null ? playerTrader.Inventory : null;

        // ---------------------------------------------------------------- lifecycle

        private void Awake()
        {
            if (economyManager == null) economyManager = FindFirstObjectByType<EconomyManager>();
            if (timeManager == null)
                timeManager = economyManager != null && economyManager.TimeManager != null
                    ? economyManager.TimeManager
                    : FindFirstObjectByType<Core.TimeManager>();
            if (npcManager == null) npcManager = FindFirstObjectByType<NPCManager>();
            if (transportManager == null) transportManager = FindFirstObjectByType<TransportManager>();
            if (playerTrader == null) playerTrader = FindFirstObjectByType<PlayerTrader>();
        }

        private void OnEnable()
        {
            if (timeManager == null) return;
            timeManager.OnWeekChanged += HandleWeekChanged;
            timeManager.OnTick += HandleTick;
        }

        private void OnDisable()
        {
            if (timeManager == null) return;
            timeManager.OnWeekChanged -= HandleWeekChanged;
            timeManager.OnTick -= HandleTick;
        }

        // After every Awake, so the market and the NPCs exist. A save loaded earlier already scheduled.
        private void Start()
        {
            if (!_restored) EnsureScheduled(CurrentWeek);
        }

        private void Update()
        {
            if (IsPlayerInRoom)
            {
                _liveSession.Advance(Time.unscaledDeltaTime);
                return;
            }

            var window = JoinWindowAuction;
            if (window == null) return;

            bool paused = JoinCountdownPaused != null && JoinCountdownPaused();
            if (!paused) _joinSecondsLeft -= Time.unscaledDeltaTime;

            if (_joinSecondsLeft <= 0f)
            {
                CloseJoinWindow(window, joined: false);
                RunWithoutPlayer(window);
            }
        }

        private void HandleTick(int week, int tick)
        {
            if (_liveSession != null || JoinWindowAuction != null) return;

            var due = _upcoming.FirstOrDefault(a => a.Status == AuctionStatus.Announced && a.Week == week && tick >= a.StartTick);
            if (due != null) OpenJoinWindow(due);
        }

        private void HandleWeekChanged(int newWeek)
        {
            // A window still open (or an auction whose hour was never reached) is played out without the player.
            foreach (var auction in _upcoming.ToList())
            {
                if (auction.Status == AuctionStatus.JoinWindow)
                {
                    CloseJoinWindow(auction, joined: false);
                    RunWithoutPlayer(auction);
                }
                else if (auction.Status == AuctionStatus.Announced && auction.Week < newWeek)
                {
                    RunWithoutPlayer(auction);
                }
            }

            RevalueLots();
            EnsureScheduled(newWeek);
        }

        // ---------------------------------------------------------------- player actions

        /// <summary>Joins the auction whose window is open. The session then runs live until the last lot.</summary>
        public bool JoinAuction()
        {
            var auction = JoinWindowAuction;
            if (auction == null || _liveSession != null) return false;

            CloseJoinWindow(auction, joined: true);
            auction.PlayerJoined = true;
            StartSession(auction, live: true);
            return true;
        }

        /// <summary>Leaves the room — the remaining lots are played out at once without the player.</summary>
        public void LeaveAuction()
        {
            if (!IsPlayerInRoom) return;
            _liveSession.LeaveRoom();
        }

        /// <summary>The asking bid plus buyer's premium and shipping — what winning the current lot at that bid would cost.</summary>
        public float PlayerCostAt(float bid) => PlayerCostFor(_liveSession?.Current?.Lot, bid);

        /// <summary>What the player would pay in total for <paramref name="lot"/> at <paramref name="bid"/>: hammer, premium, shipping.</summary>
        public float PlayerCostFor(AuctionLot lot, float bid)
        {
            float premium = bid * Settings.BuyersPremiumRate;
            if (lot == null || lot.Antique == null) return bid + premium;

            var quote = transportManager != null ? transportManager.Quote(lot.Antique, TransportOption.Standard, buyer: PlayerInventory) : null;
            return TraderInventory.AuctionCost(bid, premium, quote);
        }

        /// <summary>The auction a lot belongs to (upcoming or finished), or null.</summary>
        public Auction AuctionOf(AuctionLot lot) => lot != null ? FindAuctionOf(lot) : null;

        /// <summary>Whether the player may raise their paddle now; <paramref name="reason"/> says why not.</summary>
        public bool CanPlayerBid(out string reason)
        {
            reason = null;
            var bidding = _liveSession?.Current;
            var paddle = _liveSession?.Player;
            var inventory = PlayerInventory;

            if (!IsPlayerInRoom || bidding == null || paddle == null) { reason = "You are not in the auction room."; return false; }
            if (_liveSession.IsPresenting) { reason = "Bidding hasn't opened yet."; return false; }
            if (bidding.IsClosed) { reason = "The lot is closed."; return false; }
            if (!paddle.Active) { reason = "You passed on this lot."; return false; }
            if (bidding.Leader == paddle) { reason = "You are the highest bidder."; return false; }
            if (inventory == null) { reason = "No player inventory."; return false; }
            if (!inventory.HasFreeSlot) { reason = "Your warehouse is full."; return false; }

            float cost = PlayerCostAt(bidding.AskingBid);
            if (cost > inventory.Cash) { reason = $"Not enough cash — winning at this bid costs {cost:F0} £."; return false; }

            return true;
        }

        public bool PlayerBid()
        {
            if (!CanPlayerBid(out string reason))
            {
                if (logAuctions) Debug.Log($"AuctionManager: bid refused — {reason}");
                return false;
            }

            return _liveSession.Current.PlayerBid(_liveSession.Player);
        }

        /// <summary>Ends the presentation of the current lot and opens the bidding.</summary>
        public void StartBidding()
        {
            if (IsPlayerInRoom) _liveSession.BeginBidding();
        }

        /// <summary>The player sits the current lot out — it's auctioned among the NPCs at once.</summary>
        public void SkipLot()
        {
            if (IsPlayerInRoom) _liveSession.SkipCurrentLot();
        }

        /// <summary>The player stops bidding on the current lot.</summary>
        public void PlayerPassLot()
        {
            if (!IsPlayerInRoom || _liveSession.Current == null) return;
            _liveSession.Current.PlayerPass(_liveSession.Player);
        }

        // ---------------------------------------------------------------- session

        private void OpenJoinWindow(Auction auction)
        {
            auction.Status = AuctionStatus.JoinWindow;
            _joinSecondsLeft = Settings.JoinWindowSeconds;

            if (logAuctions)
                Debug.Log($"AuctionManager: auction #{auction.Number} is starting — join within {Settings.JoinWindowSeconds:F0} s.");

            OnJoinWindowOpened?.Invoke(auction);
        }

        private void CloseJoinWindow(Auction auction, bool joined)
        {
            _joinSecondsLeft = 0f;
            OnJoinWindowClosed?.Invoke(auction, joined);
        }

        private void RunWithoutPlayer(Auction auction) => StartSession(auction, live: false);

        private void StartSession(Auction auction, bool live)
        {
            auction.Status = AuctionStatus.Live;
            RevalueLots(auction);

            var rng = new System.Random(SeedFor(auction));
            var attendees = PickAttendees(rng);

            string playerName = PlayerBidderName;
            var session = new AuctionSession(auction, Settings, live, Antique.PlayerOwnerId, playerName,
                lot => CreateBidders(lot, attendees, rng), SettleLot);

            session.OnFinished += HandleSessionFinished;
            if (logAuctions) HookLogging(session);

            if (live)
            {
                _liveSession = session;
                OnLiveSessionStarted?.Invoke(session);
                session.Start();
            }
            else
            {
                session.Start();
                session.RunToEnd();
            }
        }

        private void HandleSessionFinished(AuctionSession session)
        {
            session.OnFinished -= HandleSessionFinished;
            if (_liveSession == session) _liveSession = null;

            var auction = session.Auction;
            auction.Status = AuctionStatus.Finished;
            _upcoming.Remove(auction);
            _history.Insert(0, auction);
            while (_history.Count > Settings.MaxHistory)
                _history.RemoveAt(_history.Count - 1);

            if (logAuctions)
                Debug.Log($"AuctionManager: auction #{auction.Number} finished — {auction.SoldCount}/{auction.Lots.Count} lots sold, " +
                          $"{auction.TotalHammer:F0} £ in total{(auction.PlayerJoined ? "" : " (without the player)")}.");

            OnAuctionFinished?.Invoke(auction);
        }

        /// <summary>Which NPCs come to this session (rolled once per session from their profiles).</summary>
        private List<NPCTrader> PickAttendees(System.Random rng)
        {
            var attendees = new List<NPCTrader>();
            if (npcManager == null) return attendees;

            foreach (var npc in npcManager.NPCs)
            {
                var profile = npc?.Profile;
                if (profile == null) continue;
                if (rng.NextDouble() < profile.AuctionAttendance) attendees.Add(npc);
            }

            return attendees;
        }

        /// <summary>Paddles for one lot — budgets are read now, so a lot won earlier lowers the next limit.</summary>
        private List<AuctionBidder> CreateBidders(AuctionLot lot, List<NPCTrader> attendees, System.Random rng)
        {
            var bidders = new List<AuctionBidder>();
            float premium = lot.HasProvenance ? Settings.ProvenancePremium : 0f;

            foreach (var npc in attendees)
            {
                var bidder = npc.CreateAuctionBidder(lot.Antique, premium, Settings.NpcCostAllowance, rng);
                if (bidder != null && bidder.Limit >= lot.StartPrice)
                    bidders.Add(bidder);
            }

            if (logAuctions)
            {
                string limits = bidders.Count == 0
                    ? "nobody interested"
                    : string.Join(", ", bidders.Select(b => $"{b.Name} ≤ {b.Limit:F0} £"));
                Debug.Log($"AuctionManager: lot {lot.LotNumber} {lot.Name} (value {lot.Antique.CurrentPrice:F0} £, opening {lot.StartPrice:F0} £) — {limits}");
            }

            return bidders;
        }

        // ---------------------------------------------------------------- settlement

        private void SettleLot(AuctionLot lot, LotBidding bidding)
        {
            var market = economyManager != null ? economyManager.Market : null;
            int week = CurrentWeek;

            lot.BidCount = bidding.BidCount;
            if (market != null) market.RecalculatePrice(lot.Antique);
            float value = lot.Antique.CurrentPrice;
            lot.MarketValueAtSale = value;

            if (bidding.IsSold && TryHandOver(lot, bidding.Leader, bidding.CurrentBid, week, out string winnerName))
            {
                lot.Status = LotStatus.Sold;
                lot.WinnerId = bidding.Leader.Id;
                lot.WinnerName = winnerName;
                lot.HammerPrice = bidding.CurrentBid;

                float ratio = lot.HammerPrice / Mathf.Max(1f, value);
                float signal = Mathf.Clamp((ratio - 1f) * Settings.DemandSignalStrength, -Settings.MaxDemandSignal, Settings.MaxDemandSignal);

                if (market != null)
                {
                    market.ApplyAuctionResult(lot.Antique.DefinitionId, lot.HammerPrice, week, signal);
                    market.Feed.Add($"Auction: {lot.Name} sold to {winnerName} for {lot.HammerPrice:F0} £ " +
                                    $"(estimate {lot.EstimateLow:F0}–{lot.EstimateHigh:F0} £)");
                }
            }
            else
            {
                lot.Status = LotStatus.Unsold;

                if (market != null)
                {
                    market.ApplyAuctionResult(lot.Antique.DefinitionId, 0f, week, -Settings.UnsoldDemandPenalty);
                    market.Feed.Add($"Auction: {lot.Name} found no buyer at {lot.StartPrice:F0} £");
                }
            }

            OnLotSettled?.Invoke(FindAuctionOf(lot), lot);
        }

        /// <summary>Charges the winner and ships the lot to them. False if they can no longer pay or store it.</summary>
        private bool TryHandOver(AuctionLot lot, AuctionBidder winner, float hammerPrice, int week, out string winnerName)
        {
            winnerName = winner != null ? winner.Name : null;
            if (winner == null) return false;

            TraderInventory inventory;
            NPCTrader npc = null;

            if (winner.IsPlayer)
            {
                inventory = PlayerInventory;
                if (playerTrader != null) winnerName = playerTrader.TraderName;
            }
            else
            {
                npc = npcManager != null ? npcManager.GetById(winner.Id) : null;
                inventory = npc?.Inventory;
            }

            if (inventory == null) return false;

            float premium = hammerPrice * Settings.BuyersPremiumRate;
            var quote = transportManager != null ? transportManager.Quote(lot.Antique, TransportOption.Standard, buyer: inventory) : null;

            if (!inventory.BuyAtAuction(lot.Antique, hammerPrice, premium, week, quote))
            {
                Debug.LogWarning($"AuctionManager: {winnerName} won {lot.Name} for {hammerPrice:F0} £ but can no longer pay or store it — the lot stays unsold.");
                return false;
            }

            if (quote != null) transportManager.Dispatch(lot.Antique, inventory, quote, winnerName);
            npc?.RecordAuctionPurchase(lot.Antique, week);
            return true;
        }

        private Auction FindAuctionOf(AuctionLot lot) =>
            _upcoming.FirstOrDefault(a => a.Lots.Contains(lot)) ?? _history.FirstOrDefault(a => a.Lots.Contains(lot));

        // ---------------------------------------------------------------- catalog

        /// <summary>Creates every auction whose catalog should be public by <paramref name="day"/>.</summary>
        private void EnsureScheduled(int week)
        {
            int guard = 0;
            while (guard++ < 1000)
            {
                int auctionWeek = Settings.AuctionWeekFor(_nextScheduleIndex);
                if (auctionWeek > week + Settings.AnnounceWeeksAhead) break;

                int index = _nextScheduleIndex++;
                if (auctionWeek < week) continue; // already past (e.g. a save from before auctions existed)

                var auction = CreateAuction(index, auctionWeek, week);
                _upcoming.Add(auction);
                OnAuctionAnnounced?.Invoke(auction);
            }
        }

        private Auction CreateAuction(int index, int auctionWeek, int thisWeek)
        {
            int ticks = timeManager != null ? timeManager.TicksPerPeriod : Core.TimeManager.DefaultTicksPerPeriod;

            var auction = new Auction
            {
                Id = Guid.NewGuid().ToString("N"),
                Number = index + 1,
                Week = auctionWeek,
                StartTick = Mathf.Clamp(Settings.StartTick, 0, ticks - 1),
                AnnouncedOnWeek = thisWeek
            };

            Vector2 band = LotValueBand(auctionWeek);
            var usedDefinitions = new HashSet<string>();

            for (int i = 0; i < Settings.LotsPerAuction; i++)
            {
                var lot = CreateLot(i + 1, band, usedDefinitions);
                if (lot != null) auction.Lots.Add(lot);
            }

            if (logAuctions)
            {
                string lots = string.Join(", ", auction.Lots.Select(l => $"{l.Name} ({l.EstimateLow:F0}–{l.EstimateHigh:F0} £)"));
                Debug.Log($"AuctionManager: auction #{auction.Number} on week {auction.Week} announced (lot values {band.x:F0}–{band.y:F0} £): {lots}");
            }

            return auction;
        }

        /// <summary>
        /// Range of market values for the lots of an auction on <paramref name="day"/>: the start range
        /// scaled by the campaign's progress, with the top capped by the second-largest NPC budget.
        /// </summary>
        public Vector2 LotValueBand(int week)
        {
            int length = timeManager != null ? timeManager.CampaignLength : 104;
            float progress = length > 1 ? Mathf.Clamp01((week - 1) / (float)(length - 1)) : 1f;
            float multiplier = Mathf.Max(0.01f, Settings.ValueGrowth.Evaluate(progress));

            float low = Settings.StartValueRange.x * multiplier;
            float high = Settings.StartValueRange.y * multiplier;

            float cap = BudgetCap();
            if (cap > 0f) high = Mathf.Min(high, cap);

            high = Mathf.Max(high, Settings.MinLotValue);
            if (low > high) low = high * 0.5f;
            low = Mathf.Clamp(low, Mathf.Min(Settings.MinLotValue, high), high);

            return new Vector2(low, high);
        }

        /// <summary>What the second-richest NPC could bid on one lot (the richest if there's only one). 0 = no NPCs.</summary>
        private float BudgetCap()
        {
            if (npcManager == null || npcManager.NPCs.Count == 0) return 0f;

            var budgets = npcManager.NPCs
                .Where(n => n != null)
                .Select(n => n.AuctionBudget(Settings.NpcCostAllowance))
                .OrderByDescending(b => b)
                .ToList();

            if (budgets.Count == 0) return 0f;

            float budget = budgets.Count >= 2 ? budgets[1] : budgets[0];
            return budget * Settings.BudgetCapFactor;
        }

        private AuctionLot CreateLot(int lotNumber, Vector2 band, HashSet<string> usedDefinitions)
        {
            var market = economyManager != null ? economyManager.Market : null;
            if (market == null) return null;

            float averageCondition = (Settings.LotConditionRange.x + Settings.LotConditionRange.y) * 0.5f;
            float averageFactor = (Settings.LotPriceFactorRange.x + Settings.LotPriceFactorRange.y) * 0.5f;

            // Expected value of a lot of each type, in the house's usual condition.
            var candidates = new List<(AntiqueDefinition definition, float value)>();
            foreach (var definition in AntiqueDatabase.GetAll())
            {
                if (definition == null) continue;
                var typeState = market.GetTypeState(definition.Id);
                if (typeState == null) continue;

                float value = PriceEngine.CalculateReferencePrice(definition.BasePrice, typeState) * averageCondition * averageFactor;
                candidates.Add((definition, value));
            }

            if (candidates.Count == 0) return null;

            bool InBand(float v) => v >= band.x && v <= band.y;

            var pool = candidates.Where(c => InBand(c.value) && !usedDefinitions.Contains(c.definition.Id)).ToList();
            if (pool.Count == 0) pool = candidates.Where(c => InBand(c.value)).ToList();
            if (pool.Count == 0)
            {
                // Nothing fits (e.g. late game beyond the most valuable type) — take the closest few.
                float Distance(float v) => v < band.x ? band.x - v : v > band.y ? v - band.y : 0f;
                pool = candidates.OrderBy(c => Distance(c.value)).Take(3).ToList();
            }

            var picked = pool[UnityEngine.Random.Range(0, pool.Count)].definition;
            usedDefinitions.Add(picked.Id);

            float condition = UnityEngine.Random.Range(Settings.LotConditionRange.x, Settings.LotConditionRange.y);
            float priceFactor = UnityEngine.Random.Range(Settings.LotPriceFactorRange.x, Settings.LotPriceFactorRange.y);

            var antique = new Antique(picked.Id, condition, priceFactor)
            {
                ShippingZone = Settings.AuctionHouseZone
            };

            bool provenance = Settings.ProvenanceTexts != null && Settings.ProvenanceTexts.Count > 0
                              && UnityEngine.Random.value < Settings.ProvenanceChance;
            if (provenance)
                antique.AppendHistory(Settings.ProvenanceTexts[UnityEngine.Random.Range(0, Settings.ProvenanceTexts.Count)]);

            market.RecalculatePrice(antique);
            float marketValue = antique.CurrentPrice;
            float highFactor = Settings.EstimateHighFactor * (provenance ? 1f + Settings.ProvenancePremium : 1f);

            var lot = new AuctionLot
            {
                LotNumber = lotNumber,
                Antique = antique,
                HasProvenance = provenance,
                EstimateLow = AuctionMath.RoundPrice(marketValue * Settings.EstimateLowFactor, Settings),
                EstimateHigh = AuctionMath.RoundPrice(marketValue * highFactor, Settings)
            };
            lot.StartPrice = AuctionMath.RoundPrice(lot.EstimateLow * Settings.StartPriceFactor, Settings);
            return lot;
        }

        /// <summary>Keeps the market value of catalog lots current (they aren't market listings).</summary>
        private void RevalueLots(Auction only = null)
        {
            var market = economyManager != null ? economyManager.Market : null;
            if (market == null) return;

            foreach (var auction in _upcoming)
            {
                if (only != null && auction != only) continue;
                foreach (var lot in auction.Lots)
                    if (lot.IsPending && lot.Antique != null)
                        market.RecalculatePrice(lot.Antique);
            }
        }

        private static int SeedFor(Auction auction)
        {
            unchecked
            {
                int hash = (int)2166136261;
                string id = auction.Id ?? "auction";
                for (int i = 0; i < id.Length; i++)
                    hash = (hash ^ id[i]) * 16777619;
                return hash;
            }
        }

        // ---------------------------------------------------------------- logging

        private static void HookLogging(AuctionSession session)
        {
            if (!session.IsLive) return; // instant sessions log only lots and results

            session.OnLotOpened += bidding =>
            {
                bidding.OnBid += (bidder, amount) => Debug.Log($"Auction lot {bidding.Lot.LotNumber}: {bidder.Name} bids {amount:F0} £");
                bidding.OnWithdraw += bidder => Debug.Log($"Auction lot {bidding.Lot.LotNumber}: {bidder.Name} withdraws");
                bidding.OnCall += call =>
                {
                    if (call != AuctioneerCall.Closed)
                        Debug.Log($"Auction lot {bidding.Lot.LotNumber}: {call} at {bidding.CurrentBid:F0} £");
                };
            };
            session.OnLotClosed += (lot, bidding) =>
                Debug.Log(lot.IsSold
                    ? $"Auction lot {lot.LotNumber}: SOLD to {lot.WinnerName} for {lot.HammerPrice:F0} £ ({lot.BidCount} bids)"
                    : $"Auction lot {lot.LotNumber}: unsold");
        }

        // ---------------------------------------------------------------- debug

        [ContextMenu("Debug: Start next auction now")]
        private void DebugStartNextAuction()
        {
            if (!Application.isPlaying || _liveSession != null || JoinWindowAuction != null) return;

            var auction = NextAuction;
            if (auction == null) return;

            auction.Week = CurrentWeek;
            auction.StartTick = CurrentTick;
            OpenJoinWindow(auction);
        }

        [ContextMenu("Debug: Join auction")]
        private void DebugJoin() => JoinAuction();

        [ContextMenu("Debug: Start bidding")]
        private void DebugStartBidding() => StartBidding();

        [ContextMenu("Debug: Player bid")]
        private void DebugBid() => PlayerBid();

        [ContextMenu("Debug: Pass lot")]
        private void DebugPass() => PlayerPassLot();

        [ContextMenu("Debug: Leave auction")]
        private void DebugLeave() => LeaveAuction();

        // ---------------------------------------------------------------- save / load

        public AuctionsState CaptureState()
        {
            var state = new AuctionsState { NextScheduleIndex = _nextScheduleIndex };

            foreach (var auction in _upcoming)
                state.Upcoming.Add(AuctionState.Capture(auction));

            foreach (var auction in _history)
                state.History.Add(AuctionState.Capture(auction));

            return state;
        }

        /// <summary>
        /// Replaces the schedule with the saved one. A session that was starting or running when the
        /// game was saved goes back to "announced": its window reopens at the start hour and the
        /// remaining lots are auctioned then. Call after the economy and NPCs are restored.
        /// </summary>
        public void RestoreState(AuctionsState state)
        {
            _restored = true;
            _liveSession = null;
            _joinSecondsLeft = 0f;
            _upcoming.Clear();
            _history.Clear();
            _nextScheduleIndex = 0;

            if (state != null)
            {
                _nextScheduleIndex = Mathf.Max(0, state.NextScheduleIndex);

                if (state.Upcoming != null)
                    foreach (var auctionState in state.Upcoming)
                    {
                        var auction = auctionState?.Restore();
                        if (auction == null || auction.IsFinished) continue;

                        auction.Status = AuctionStatus.Announced;
                        _upcoming.Add(auction);
                    }

                if (state.History != null)
                    foreach (var auctionState in state.History)
                    {
                        var auction = auctionState?.Restore();
                        if (auction != null) _history.Add(auction);
                    }
            }

            _upcoming.Sort((a, b) => a.Week.CompareTo(b.Week));
            RevalueLots();
            EnsureScheduled(CurrentWeek);

            OnAuctionsRestored?.Invoke();
        }
    }
}
