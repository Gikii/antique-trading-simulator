using System;
using System.Collections.Generic;
using System.IO;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Auctions;
using AntiqueTradingSimulator.Company;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Dependency;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Events;
using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.News;
using AntiqueTradingSimulator.Restoration;
using AntiqueTradingSimulator.Rumours;
using AntiqueTradingSimulator.Saving;
using UnityEngine;
using UnityEngine.SceneManagement;
using Antique = AntiqueTradingSimulator.Market.Antique;
using GameMarket = AntiqueTradingSimulator.Market.Market;

namespace AntiqueTradingSimulator.Diagnostics
{
    /// <summary>
    /// Installs itself into the game scene and writes the diagnostics tables.
    ///
    /// ONE SESSION PER FILE SET. A session begins when the game scene loads (at startup, on a new
    /// game, or when a save is loaded) and ends at the score screen, on quit, or when the scene unloads. 
    /// </summary>
    [DefaultExecutionOrder(2000)]
    public sealed class TelemetryRecorder : MonoBehaviour
    {
        /// <summary>Null whenever there is no open session, so the facade's calls cost one null check.</summary>
        public static TelemetryRecorder Instance { get; private set; }

        public TelemetryRun Run { get; private set; }

        public DimensionRecorder Dimensions { get; private set; }
        public TraderRecorder Traders { get; private set; }
        public MarketRecorder MarketLog { get; private set; }
        public ContractRecorder ContractLog { get; private set; }
        public WorldRecorder WorldLog { get; private set; }
        public WeeklyRecorder WeeklyLog { get; private set; }

        public EconomyManager Economy { get; private set; }
        public Core.TimeManager Clock { get; private set; }
        public NPCManager Npcs { get; private set; }
        public PlayerTrader Player { get; private set; }
        public ContractManager Board { get; private set; }
        public TransportManager Transport { get; private set; }
        public RestorationManager Restorations { get; private set; }
        public NewsManager NewsBoard { get; private set; }
        public AuctionManager AuctionHouse { get; private set; }
        public EventManager WorldEvents { get; private set; }
        public DependencyManager Dependencies { get; private set; }
        public CampaignManager Campaign { get; private set; }

        public GameMarket Market => Economy != null ? Economy.Market : null;

        private readonly List<Action> _unsubscribe = new List<Action>();
        private SaveManager _saveManager;
        private Action<SaveSlot, GameState> _loadedHandler;
        private string _startReason;
        private int _lastWeek = -1;
        private int _lastTick = -1;
        private bool _ready;

        // Awake, not Start: GameBootstrap restores a save in its own Start (execution order 1000,
        // ahead of this one), so the only way to see that a save was loaded is to be subscribed
        // before any Start runs.
        private void Awake()
        {
            try
            {
                _saveManager = FindFirstObjectByType<SaveManager>();
                if (_saveManager == null) return;

                _loadedHandler = (slot, state) => _startReason = "LoadSlot";
                _saveManager.OnLoaded += _loadedHandler;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Telemetry: could not watch for a save being loaded — {e.GetType().Name}: {e.Message}");
            }
        }

        private void Start()
        {
            try
            {
                BeginSession();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Telemetry: could not start — {e.GetType().Name}: {e.Message}. Recording disabled.");
                Instance = null;
                _ready = false;
                enabled = false;
            }
        }

        // ---------------------------------------------------------------- session lifecycle

        private void BeginSession()
        {
            if (_saveManager != null && _loadedHandler != null)
            {
                _saveManager.OnLoaded -= _loadedHandler;
                _loadedHandler = null;
            }

            Economy = FindFirstObjectByType<EconomyManager>();
            Clock = Economy != null && Economy.TimeManager != null
                ? Economy.TimeManager
                : FindFirstObjectByType<Core.TimeManager>();

            if (Economy == null || Clock == null)
            {
                Debug.LogWarning("Telemetry: no EconomyManager or TimeManager in the scene — recording disabled.");
                enabled = false;
                return;
            }

            Npcs = FindFirstObjectByType<NPCManager>();
            Player = FindFirstObjectByType<PlayerTrader>();
            Board = FindFirstObjectByType<ContractManager>();
            Transport = FindFirstObjectByType<TransportManager>();
            Restorations = FindFirstObjectByType<RestorationManager>();
            NewsBoard = FindFirstObjectByType<NewsManager>();
            AuctionHouse = FindFirstObjectByType<AuctionManager>();
            WorldEvents = FindFirstObjectByType<EventManager>();
            Dependencies = FindFirstObjectByType<DependencyManager>();
            Campaign = FindFirstObjectByType<CampaignManager>();

            if (_startReason == null) _startReason = TelemetryBootstrap.MenuSeen ? "NewGame" : "Startup";

            string root = TelemetryBootstrap.OutputRoot();
            Run = new TelemetryRun(root, TelemetryBootstrap.NextSessionIndex()) { Clock = Clock };

            Dimensions = new DimensionRecorder(this);
            Traders = new TraderRecorder(this);
            MarketLog = new MarketRecorder(this);
            ContractLog = new ContractRecorder(this);
            WorldLog = new WorldRecorder(this);
            WeeklyLog = new WeeklyRecorder(this);

            Dimensions.WriteRun();
            Dimensions.WriteDefinitions();
            Dimensions.WriteRestorationOptions();

            Subscribe();

            _lastWeek = Clock.CurrentWeek;
            _lastTick = Clock.CurrentTick;
            _ready = true;

            // Published only now: until this point the sub-recorders are half-built, and the
            // facade must never hand game code a partly constructed recorder.
            Instance = this;

            WriteSessionRow("Started", _startReason);

            // Baseline, so the first diffs have something to diff against.
            Traders.Reseed();
            MarketLog.Reseed();
            ContractLog.Reseed();
            WorldLog.Reseed();
            RunWeeklyPasses(1);

            Debug.Log(
                $"ATS diagnostics: SESSION {Run.SessionIndex} STARTED ({_startReason}) — recording.\n" +
                $"  run id   : {Run.RunId}\n" +
                $"  folder   : {Run.Directory}\n" +
                $"  week     : {Clock.CurrentWeek} of {Clock.CampaignLength}\n" +
                $"  traders  : {(Npcs != null ? Npcs.NPCs.Count : 0)} NPCs" +
                (Player != null ? " plus the player" : " and no player") + "\n" +
                $"  tables   : {(TelemetryBootstrap.DefineActive ? "34" : "31 — ATS_DIAGNOSTICS is not defined")}");
        }

        /// <summary>
        /// Closes the session: unsubscribes, writes the closing row, flushes and shuts the files.
        /// Safe to call more than once; only the first call does anything.
        /// </summary>
        private void EndSession(string reason)
        {
            if (!_ready || Run == null || Run.Closed) return;

            _ready = false;
            if (Instance == this) Instance = null;

            try
            {
                WriteSessionRow("Ended", reason);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Telemetry: could not write the closing row — {e.GetType().Name}: {e.Message}");
            }

            Detach();

            long rows = Run.TotalRowsWritten;
            int tableCount = 0;
            foreach (var unused in Run.Tables) tableCount++;

            Run.FlushAll();
            Run.Dispose();

            Debug.Log($"ATS diagnostics: SESSION {Run.SessionIndex} ENDED ({reason}) — " +
                      $"{rows} rows across {tableCount} files in {Run.Directory}");
        }

        private void WriteSessionRow(string eventName, string reason)
        {
            var table = Run.Table(T.FactSessionName, T.FactSessionHeader);
            bool ended = eventName == "Ended";

            table.Open()
                .S(Run.RunId)
                .I(Run.NextSeq())
                .I(Run.Week)
                .I(Run.Tick)
                .D(Run.Date)
                .S(eventName)
                .S(reason)
                .I(Run.SessionIndex)
                .S(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"))
                .I(Clock != null ? Clock.CampaignLength : 0)
                .B(Campaign != null && Campaign.IsFinished)
                .I(ended ? (int?)Run.TotalRowsWritten : null)
                .F(ended ? (float)(DateTime.UtcNow - Run.StartedUtc).TotalSeconds : (float?)null)
                .End();
        }

        private void LateUpdate()
        {
            if (!_ready) return;

            try
            {
                if (Clock == null)
                {
                    EndSession("SceneUnloaded");
                    return;
                }

                int week = Clock.CurrentWeek;
                int tick = Clock.CurrentTick;
                if (week == _lastWeek && tick == _lastTick) return;

                // Above 1 means FastForwardWeek ran several ticks inside one frame.
                int ticksElapsed = week == _lastWeek
                    ? Mathf.Max(1, tick - _lastTick)
                    : Mathf.Max(1, (Clock.TicksPerPeriod - _lastTick) + tick);

                bool weekChanged = week != _lastWeek;
                _lastWeek = week;
                _lastTick = tick;

                int elapsed = ticksElapsed;
                Run.Safe(T.FactOwnershipName, "TraderTickPass", () => Traders.TickPass(elapsed));
                Run.Safe(T.FactTypeStateName, "MarketTickPass", () => MarketLog.TickPass(elapsed));
                Run.Safe(T.FactContractStateName, "ContractTickPass", () => ContractLog.TickPass());
                Run.Safe(T.FactDependencyInfluenceName, "WorldTickPass", () => WorldLog.TickPass());

                if (weekChanged) RunWeeklyPasses(elapsed);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Telemetry: the tick pass threw and recording has stopped — " +
                                 $"{e.GetType().Name}: {e.Message}. The game is unaffected.");
                EndSession("RecorderFault");
            }
        }

        private void RunWeeklyPasses(int ticksElapsed)
        {
            int elapsed = ticksElapsed;
            Run.Safe(T.FactTypeStateName, "WeeklyGrid", () => MarketLog.WeeklyGrid(elapsed));
            Run.Safe(T.DimTraderName, "DimTraders", () => Dimensions.WriteTraders());
            Run.Safe(T.FactListingWeekName, "ListingWeek", () => WeeklyLog.ListingWeek());
            Run.Safe(T.FactContractProgressName, "ContractProgress", () => ContractLog.WeeklyProgress());

            if (Telemetry.Mirrors)
                Run.Safe(T.FactContractEligibilityWeekName, "ContractEligibility", () => ContractLog.WeeklyEligibility());

            Run.Safe(T.FactTraderWeekName, "TraderWeek", () => WeeklyLog.TraderWeek());

            // The week just ended. The next Review clears the agenda and ages the beliefs, and it
            // runs on the first tick after this.
            int justEnded = Run.Week;
            Run.Safe(T.FactAgendaItemName, "AgendaItem", () => WeeklyLog.Agenda.Emit(justEnded));
            Run.Safe(T.FactOutlookWeekName, "OutlookWeek", () => WeeklyLog.Mind.OutlookWeek());
            Run.Safe(T.FactPositionName, "Positions", () => WeeklyLog.Mind.Positions());
            Run.Safe(T.FactBeliefOutcomeName, "BeliefOutcomes", () => WeeklyLog.Mind.BeliefOutcomes());

            // Integrity before EconomyWeek: EconomyWeek clears the week's counters, and one of the
            // checks reads the unexplained-ownership count. The other way round it always saw 0.
            Run.Safe(T.FactIntegrityName, "Integrity", () => WeeklyLog.Integrity());
            Run.Safe(T.FactEconomyWeekName, "EconomyWeek", () => WeeklyLog.EconomyWeek());

            Run.FlushAll();
            Heartbeat();
        }

        /// <summary>
        /// Says the recorder is alive and writing, so a silent failure is obvious. Every tenth
        /// week by default, every week with -atsVerbose.
        /// </summary>
        private void Heartbeat()
        {
            int week = Run.Week;
            bool due = TelemetryBootstrap.Verbose || week <= 1 || week % 10 == 0;
            if (!due) return;

            long rows = Run.TotalRowsWritten;
            Debug.Log($"ATS diagnostics: session {Run.SessionIndex}, week {week}/{Clock.CampaignLength} — " +
                      $"{rows} rows written so far in {Run.Directory}");
        }

        // ---------------------------------------------------------------- wiring

        /// <summary>Registers an undo action to be run when the session ends.</summary>
        public void OnDetach(Action undo)
        {
            if (undo != null) _unsubscribe.Add(undo);
        }

        private void Subscribe()
        {
            var market = Market;
            if (market != null)
            {
                Action<Antique> added = MarketLog.ListingAdded;
                Action<Antique> removed = MarketLog.ListingRemoved;
                Action<Antique, TraderInventory, float, float> ownerSold = MarketLog.OwnerListingSold;
                Action<string, float, float> pressure = MarketLog.PressureApplied;
                Action feedChanged = MarketLog.FeedChanged;

                market.OnListingAdded += added;
                market.OnListingRemoved += removed;
                market.OnOwnerListingSold += ownerSold;
                market.OnPressureApplied += pressure;
                market.Feed.OnChanged += feedChanged;

                OnDetach(() =>
                {
                    market.OnListingAdded -= added;
                    market.OnListingRemoved -= removed;
                    market.OnOwnerListingSold -= ownerSold;
                    market.OnPressureApplied -= pressure;
                    market.Feed.OnChanged -= feedChanged;
                });
            }

            if (Board != null)
            {
                Action<Contract> created = ContractLog.ContractCreated;
                Action<Contract, string> claimed = ContractLog.ContractClaimed;
                Action<Contract> fulfilled = ContractLog.ContractFulfilled;
                Action<Contract> expired = ContractLog.ContractExpired;

                Board.OnContractCreated += created;
                Board.OnContractClaimed += claimed;
                Board.OnContractFulfilled += fulfilled;
                Board.OnContractExpired += expired;

                OnDetach(() =>
                {
                    Board.OnContractCreated -= created;
                    Board.OnContractClaimed -= claimed;
                    Board.OnContractFulfilled -= fulfilled;
                    Board.OnContractExpired -= expired;
                });
            }

            if (Restorations != null)
            {
                Action<RestorationJob> started = WorldLog.RestorationStarted;
                Action<RestorationRecord> completed = WorldLog.RestorationCompleted;

                Restorations.OnRestorationStarted += started;
                Restorations.OnRestorationCompleted += completed;

                OnDetach(() =>
                {
                    Restorations.OnRestorationStarted -= started;
                    Restorations.OnRestorationCompleted -= completed;
                });
            }

            if (Transport != null)
            {
                Action<Shipment> dispatched = WorldLog.ShipmentDispatched;
                Action<Shipment, Antique, float> delivered = WorldLog.ShipmentDelivered;
                Action<ShippingZone, int> delayed = WorldLog.ShipmentsDelayed;

                Transport.OnShipmentDispatched += dispatched;
                Transport.OnShipmentDelivered += delivered;
                Transport.OnShipmentsDelayed += delayed;

                OnDetach(() =>
                {
                    Transport.OnShipmentDispatched -= dispatched;
                    Transport.OnShipmentDelivered -= delivered;
                    Transport.OnShipmentsDelayed -= delayed;
                });
            }

            if (NewsBoard != null)
            {
                Action<NewsItem> published = WorldLog.NewsPublished;
                NewsBoard.OnNewsPublished += published;
                OnDetach(() => NewsBoard.OnNewsPublished -= published);
            }

            var mill = RumourMill.Current;
            if (mill != null)
            {
                Action<RumourRecord> planted = WorldLog.RumourPlanted;
                Action<RumourRecord> debunked = WorldLog.RumourDebunked;

                mill.OnRumourPlanted += planted;
                mill.OnRumourDebunked += debunked;

                OnDetach(() =>
                {
                    mill.OnRumourPlanted -= planted;
                    mill.OnRumourDebunked -= debunked;
                });
            }

            if (AuctionHouse != null)
            {
                Action<Auction> announced = WorldLog.AuctionAnnounced;
                Action<Auction, AuctionLot> settled = WorldLog.LotSettled;
                Action<Auction> finished = WorldLog.AuctionFinished;
                Action<AuctionSession> live = WorldLog.LiveSessionStarted;

                AuctionHouse.OnAuctionAnnounced += announced;
                AuctionHouse.OnLotSettled += settled;
                AuctionHouse.OnAuctionFinished += finished;
                AuctionHouse.OnLiveSessionStarted += live;

                OnDetach(() =>
                {
                    AuctionHouse.OnAuctionAnnounced -= announced;
                    AuctionHouse.OnLotSettled -= settled;
                    AuctionHouse.OnAuctionFinished -= finished;
                    AuctionHouse.OnLiveSessionStarted -= live;
                });
            }

            if (WorldEvents != null)
            {
                Action<ActiveEvent> triggered = WorldLog.EventTriggered;
                Action<ActiveEvent> ended = WorldLog.EventEnded;
                Action<EventDefinition, int> scheduled = WorldLog.EventScheduled;
                Action<EventDefinition, int> failed = WorldLog.EventFailed;

                WorldEvents.OnEventTriggered += triggered;
                WorldEvents.OnEventEnded += ended;
                WorldEvents.OnEventScheduled += scheduled;
                WorldEvents.OnEventFailed += failed;

                OnDetach(() =>
                {
                    WorldEvents.OnEventTriggered -= triggered;
                    WorldEvents.OnEventEnded -= ended;
                    WorldEvents.OnEventScheduled -= scheduled;
                    WorldEvents.OnEventFailed -= failed;
                });
            }

            if (Npcs != null)
            {
                Action<NPCTrader> npcAdded = Traders.NpcAdded;
                Action<NPCTrader> npcRemoved = Traders.NpcRemoved;

                Npcs.OnNPCAdded += npcAdded;
                Npcs.OnNPCRemoved += npcRemoved;

                OnDetach(() =>
                {
                    Npcs.OnNPCAdded -= npcAdded;
                    Npcs.OnNPCRemoved -= npcRemoved;
                });

                foreach (var npc in Npcs.NPCs) Traders.NpcAdded(npc);
            }

            if (Player != null) Traders.AttachPlayer(Player);

            // The score screen is the end of the session, not a pause in it.
            if (Campaign != null)
            {
                Action finishedCampaign = () => EndSession("CampaignFinished");
                Campaign.OnCampaignFinished += finishedCampaign;
                OnDetach(() => Campaign.OnCampaignFinished -= finishedCampaign);
            }
        }

        /// <summary>
        /// Releases every subscription, including the per-trader ones the sub-recorders made. A
        /// handler left attached to an inventory that outlives the session would fire into a
        /// closed recorder, which is exactly the kind of leak that reaches game code.
        /// </summary>
        private void Detach()
        {
            Traders?.Detach();
            WorldLog?.Detach();

            for (int i = _unsubscribe.Count - 1; i >= 0; i--)
            {
                try { _unsubscribe[i](); }
                catch { /* the publisher is already gone */ }
            }
            _unsubscribe.Clear();

            if (_saveManager != null && _loadedHandler != null)
            {
                _saveManager.OnLoaded -= _loadedHandler;
                _loadedHandler = null;
            }
        }

        private void OnApplicationQuit() => EndSession("Quit");

        private void OnDestroy()
        {
            // A scene change destroys this object; the next game scene starts a fresh session.
            EndSession("SceneUnloaded");

            Detach();
            if (Instance == this) Instance = null;
        }
    }
}
