using System;
using System.Collections.Generic;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Restoration;
using UnityEngine;

namespace AntiqueTradingSimulator.Company
{
    /// <summary>
    /// A row of the competition ranking: the player's company or an NPC trader, with the same
    /// numbers for both (a snapshot of now, its campaign score and its daily history).
    /// </summary>
    public readonly struct CompetitorEntry
    {
        /// <summary>Owner Id: <see cref="Market.Antique.PlayerOwnerId"/> for the player, the NPC's Id otherwise.</summary>
        public readonly string Id;
        public readonly string Name;
        public readonly bool IsPlayer;

        /// <summary>The trader's numbers right now.</summary>
        public readonly CompanySnapshot Snapshot;

        /// <summary>Campaign score of <see cref="Snapshot"/> (ScoringSettings, same rules for everyone).</summary>
        public readonly float Score;

        /// <summary>One snapshot per day, oldest first.</summary>
        public readonly IReadOnlyList<CompanySnapshot> History;

        public float Wealth => Snapshot.Wealth;
        public float CollectionValue => Snapshot.CollectionValue;

        public CompetitorEntry(string id, string name, bool isPlayer, CompanySnapshot snapshot, float score,
                               IReadOnlyList<CompanySnapshot> history)
        {
            Id = id;
            Name = name;
            IsPlayer = isPlayer;
            Snapshot = snapshot;
            Score = score;
            History = history;
        }
    }

    /// <summary>
    /// The player's company: reputation and credibility, upgrades, contract statistics,
    /// a daily history of key numbers, market share, the competition ranking and the
    /// Congress readiness score. Lives next to PlayerTrader (added automatically if
    /// missing) and is what the Company view and the HUD read.
    ///
    /// Reputation sources wired today: contracts (fulfilled/failed) and market sales.
    /// Other systems can call AddReputation/AddCredibility as they come.
    /// </summary>
    [DisallowMultipleComponent]
    public class CompanyManager : MonoBehaviour
    {
        [SerializeField] private string companyName = "Antique Empire";

        [Header("Settings (in-memory defaults if empty)")]
        [SerializeField] private ReputationSettings reputationSettings;
        [SerializeField] private CompanyUpgradeSettings upgradeSettings;
        [SerializeField] private ScoringSettings scoringSettings;

        [Header("New game")]
        [Tooltip("Information Network level at the start of a new game (1 = Local Press). Handy for testing " +
                 "news access; the level is saved with the other upgrades, so a loaded game overrides this.")]
        [Range(1, 5)] [SerializeField] private int startingInformationNetworkLevel = 1;

        [Header("History")]
        [Tooltip("Weekly snapshots kept for charts, trends and the end screen. Keep it ≥ the campaign length " +
                 "so the end screen can chart the whole campaign.")]
        [Min(8)] [SerializeField] private int maxSnapshotDays = 160;

        private PlayerTrader _player;
        private TimeManager _timeManager;
        private EconomyManager _economyManager;
        private NPCManager _npcManager;
        private ContractManager _contractManager;
        private RestorationManager _restorationManager;

        private TraderRecord _record;
        private CompanyUpgrades _upgrades;

        private bool _subscribed;

        public string CompanyName => companyName;
        /// <summary>
        /// The company's reputation, contract counts and daily history — the same kind of record
        /// every NPC keeps, so the final ranking compares like with like.
        /// </summary>
        public TraderRecord Record
        {
            get
            {
                if (_record == null)
                {
                    EnsureSettings();
                    _record = new TraderRecord(reputationSettings, maxSnapshotDays);
                    _record.Changed += HandleRecordChanged;
                }
                return _record;
            }
        }

        public CompanyReputation Reputation => Record.Reputation;

        /// <summary>The reputation rules, shared with the NPCs.</summary>
        public ReputationSettings ReputationSettings
        {
            get
            {
                EnsureSettings();
                return reputationSettings;
            }
        }
        public ScoringSettings Scoring
        {
            get
            {
                EnsureSettings();
                return scoringSettings;
            }
        }

        /// <summary>Created on first use — needs the player's inventory and warehouse.</summary>
        public CompanyUpgrades Upgrades
        {
            get
            {
                if (_upgrades == null && Player != null && Player.Inventory != null)
                {
                    EnsureSettings();
                    _upgrades = new CompanyUpgrades(upgradeSettings, Player.Inventory, startingInformationNetworkLevel - 1);
                    _upgrades.OnUpgraded += HandleUpgraded;
                }
                return _upgrades;
            }
        }

        public PlayerTrader Player => _player != null ? _player : (_player = GetComponent<PlayerTrader>());

        public int ContractsAccepted => Record.ContractsAccepted;
        public int ContractsFulfilled => Record.ContractsFulfilled;
        public int ContractsFailed => Record.ContractsFailed;

        /// <summary>One snapshot per day, oldest first.</summary>
        public IReadOnlyList<CompanySnapshot> History => Record.History;

        public int CurrentDay => Clock != null ? Clock.CurrentDay : 1;
        public int CampaignLength => Clock != null ? Clock.CampaignLength : 104;

        private TimeManager Clock
        {
            get
            {
                if (_timeManager == null) ResolveReferences();
                return _timeManager;
            }
        }
        public int DaysUntilCongress => Mathf.Max(0, CampaignLength - CurrentDay);

        /// <summary>Anything the Company view shows changed (reputation, upgrade, contract, new day).</summary>
        public event Action OnCompanyChanged;

        // ---------------------------------------------------------------- lifecycle

        private void Awake()
        {
            EnsureSettings();
            _ = Record;
        }

        private void EnsureSettings()
        {
            if (reputationSettings == null) reputationSettings = ScriptableObject.CreateInstance<ReputationSettings>();
            if (upgradeSettings == null) upgradeSettings = ScriptableObject.CreateInstance<CompanyUpgradeSettings>();
            if (scoringSettings == null) scoringSettings = ScriptableObject.CreateInstance<ScoringSettings>();
        }

        private void Start()
        {
            ResolveReferences();
            // Created now rather than on first use, so the upgrade effects (trading modifiers)
            // are in place before the first trade.
            _ = Upgrades;
            if (_contractManager != null)
                _contractManager.PlayerReputationProvider = () => Reputation.Reputation;
            Subscribe();
            RecordSnapshot();
            OnCompanyChanged?.Invoke();
        }

        // Also called lazily, since the HUD may ask for values before our Start ran.
        private void ResolveReferences()
        {
            if (_economyManager == null) _economyManager = FindFirstObjectByType<EconomyManager>();
            if (_timeManager == null)
                _timeManager = _economyManager != null && _economyManager.TimeManager != null
                    ? _economyManager.TimeManager
                    : FindFirstObjectByType<TimeManager>();
            if (_npcManager == null) _npcManager = FindFirstObjectByType<NPCManager>();
            if (_contractManager == null) _contractManager = FindFirstObjectByType<ContractManager>();
            if (_restorationManager == null) _restorationManager = FindFirstObjectByType<RestorationManager>();
        }

        private void OnDestroy()
        {
            if (_record != null) _record.Changed -= HandleRecordChanged;
            if (_upgrades != null) _upgrades.OnUpgraded -= HandleUpgraded;
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (_subscribed || Player == null) return;

            if (_timeManager != null) _timeManager.OnDayChanged += HandleDayChanged;
            Player.OnContractAccepted += HandleContractAccepted;
            Player.OnContractFulfilled += HandleContractFulfilled;
            Player.OnContractFailed += HandleContractFailed;
            if (Player.Inventory != null) Player.Inventory.Ledger.OnEntryAdded += HandleLedgerEntry;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;

            if (_timeManager != null) _timeManager.OnDayChanged -= HandleDayChanged;
            if (_player != null)
            {
                _player.OnContractAccepted -= HandleContractAccepted;
                _player.OnContractFulfilled -= HandleContractFulfilled;
                _player.OnContractFailed -= HandleContractFailed;
                if (_player.Inventory != null) _player.Inventory.Ledger.OnEntryAdded -= HandleLedgerEntry;
            }
            _subscribed = false;
        }

        // ---------------------------------------------------------------- reputation API

        public void AddReputation(int delta, string reason) => Reputation.AddReputation(delta, reason, CurrentDay);

        public void AddCredibility(float delta, string reason) => Reputation.AddCredibility(delta, reason, CurrentDay);

        /// <summary>
        /// What a reputation or credibility tier gives access to, generated from the contract
        /// classes, restoration workshops and upgrade requirements the game enforces, plus
        /// not-yet-implemented extras.
        /// </summary>
        public List<ReputationUnlock> UnlocksFor(ReputationKind kind, int tierIndex)
        {
            if (_contractManager == null || _restorationManager == null) ResolveReferences();
            return ReputationUnlocks.For(kind, tierIndex, Reputation.Settings, Upgrades,
                _contractManager != null ? _contractManager.ReputationClasses : null,
                _restorationManager != null ? _restorationManager.Options : null);
        }

        /// <summary>Name of the reputation tier a given number of points falls in ("Known Dealer").</summary>
        public string TierNameFor(int reputation)
        {
            var tiers = Reputation.Tiers(ReputationKind.Reputation);
            if (tiers.Count == 0) return "";
            return tiers[Reputation.TierIndexFor(ReputationKind.Reputation, reputation)].Name;
        }

        // ---------------------------------------------------------------- upgrades API

        public UpgradeInfo GetUpgradeInfo(CompanyUpgradeType type) =>
            Upgrades?.GetInfo(type, Reputation.Reputation);

        public bool TryUpgrade(CompanyUpgradeType type) =>
            Upgrades != null && Upgrades.TryUpgrade(type, Reputation.Reputation);

        // ---------------------------------------------------------------- event handlers

        private void HandleDayChanged(int newDay)
        {
            Record.FlushSales(newDay - 1);
            ChargeUpgradeUpkeep();
            RecordSnapshot();
            OnCompanyChanged?.Invoke();
        }

        private void ChargeUpgradeUpkeep()
        {
            var inventory = Player != null ? Player.Inventory : null;
            if (inventory == null || Upgrades == null) return;

            foreach (var (_, name, upkeep) in Upgrades.NonWarehouseUpkeep())
                inventory.RemoveCash(upkeep, LedgerCategory.Upkeep, $"{name} upkeep");
        }

        private void HandleLedgerEntry(LedgerEntry entry)
        {
            if (entry.Category == LedgerCategory.Sale)
                Record.RecordSale(entry.Amount);
        }

        private void HandleContractAccepted(Contract contract) => Record.ContractAccepted();

        private void HandleContractFulfilled(Contract contract) => Record.ContractFulfilled(contract, CurrentDay);

        private void HandleContractFailed(Contract contract) => Record.ContractFailed(contract, CurrentDay);

        private void HandleRecordChanged() => OnCompanyChanged?.Invoke();

        private void HandleUpgraded(CompanyUpgradeType type) => OnCompanyChanged?.Invoke();

        // ---------------------------------------------------------------- market share

        /// <summary>
        /// The player's share of all market trading volume (purchases + sales of every
        /// registered trader), 0..1. <paramref name="days"/> = 0 → whole campaign so far.
        /// </summary>
        public float MarketShare(int days = 0)
        {
            var inventory = Player != null ? Player.Inventory : null;
            if (_economyManager == null) ResolveReferences();
            if (inventory == null || _economyManager == null) return 0f;

            int from = days > 0 ? CurrentDay - days + 1 : int.MinValue;
            return TraderStats.MarketShare(inventory, _economyManager.Inventories, from);
        }

        // ---------------------------------------------------------------- snapshots & trends

        /// <summary>The company's numbers right now.</summary>
        public CompanySnapshot Capture()
        {
            var inventory = Player != null ? Player.Inventory : null;
            return Record.Capture(CurrentDay, inventory, MarketShare());
        }

        private void RecordSnapshot() => Record.Record(Capture());

        /// <summary>
        /// The latest recorded snapshot at least <paramref name="days"/> days old, or the
        /// oldest one if history doesn't reach that far back (live state if there's none).
        /// </summary>
        public CompanySnapshot SnapshotDaysAgo(int days)
        {
            return Record.SnapshotDaysAgo(CurrentDay, days) ?? Capture();
        }

        /// <summary>Change of one criterion over the last <paramref name="days"/> days (live minus past).</summary>
        public float Change(ScoreCriterion criterion, int days) =>
            Capture().Value(criterion) - SnapshotDaysAgo(days).Value(criterion);

        // ---------------------------------------------------------------- score

        public float CriterionScore(ScoreCriterion criterion) =>
            CampaignScoring.CriterionScore(scoringSettings, criterion, Capture().Value(criterion));

        public float EstimatedScore() => CampaignScoring.TotalScore(scoringSettings, Capture());

        // ---------------------------------------------------------------- competition

        /// <summary>The player and every NPC trader, richest first.</summary>
        public List<CompetitorEntry> GetCompetition()
        {
            var result = Competitors();
            result.Sort((a, b) => b.Wealth.CompareTo(a.Wealth));
            return result;
        }

        /// <summary>
        /// The player and every NPC trader by campaign score, best first — the final ranking.
        /// Scores are compared as shown (whole points), so two traders showing the same score
        /// are ordered by wealth, never by a hidden fraction.
        /// </summary>
        public List<CompetitorEntry> GetRanking()
        {
            var result = Competitors();
            result.Sort((a, b) =>
            {
                int byScore = Mathf.RoundToInt(b.Score).CompareTo(Mathf.RoundToInt(a.Score));
                return byScore != 0 ? byScore : b.Wealth.CompareTo(a.Wealth);
            });
            return result;
        }

        private List<CompetitorEntry> Competitors()
        {
            var result = new List<CompetitorEntry>();
            var scoring = Scoring;

            var inventory = Player != null ? Player.Inventory : null;
            if (inventory != null)
            {
                var now = Capture();
                result.Add(new CompetitorEntry(Market.Antique.PlayerOwnerId, companyName, true, now,
                                               CampaignScoring.TotalScore(scoring, now), Record.History));
            }

            if (_npcManager == null || _economyManager == null) ResolveReferences();
            if (_npcManager != null)
            {
                foreach (var npc in _npcManager.NPCs)
                {
                    if (npc?.Inventory == null) continue;

                    float share = _economyManager != null
                        ? TraderStats.MarketShare(npc.Inventory, _economyManager.Inventories)
                        : 0f;
                    var now = npc.Record.Capture(CurrentDay, npc.Inventory, share);
                    result.Add(new CompetitorEntry(npc.Id, npc.TraderName, false, now,
                                                   CampaignScoring.TotalScore(scoring, now), npc.Record.History));
                }
            }

            return result;
        }

        // ---------------------------------------------------------------- save / load

        /// <summary>
        /// Snapshot of the company. The competition ranking and the Congress score are not saved.
        /// </summary>
        public CompanyState CaptureState()
        {
            // The company's save keeps its own (older) shape: the record's fields, flattened,
            // plus the upgrade levels only the player has.
            var record = Record.CaptureState();
            return new CompanyState
            {
                ContractsAccepted = record.ContractsAccepted,
                ContractsFulfilled = record.ContractsFulfilled,
                ContractsFailed = record.ContractsFailed,
                PendingSalesValue = record.PendingSalesValue,
                PendingSalesCount = record.PendingSalesCount,
                History = record.History,
                Reputation = record.Reputation,
                UpgradeLevels = Upgrades != null
                    ? Upgrades.CaptureState()
                    : new Dictionary<CompanyUpgradeType, int>()
            };
        }

        /// <summary>
        /// Replaces the company's statistics, reputation, upgrade levels and snapshot history.
        /// Call AFTER the player's inventory and warehouse have been restored.
        /// </summary>
        public void RestoreState(CompanyState state)
        {
            if (state == null) return;

            Record.RestoreState(new TraderRecordState
            {
                ContractsAccepted = state.ContractsAccepted,
                ContractsFulfilled = state.ContractsFulfilled,
                ContractsFailed = state.ContractsFailed,
                PendingSalesValue = state.PendingSalesValue,
                PendingSalesCount = state.PendingSalesCount,
                History = state.History,
                Reputation = state.Reputation
            });

            if (state.UpgradeLevels != null && state.UpgradeLevels.Count > 0)
            {
                if (Upgrades != null)
                    Upgrades.RestoreState(state.UpgradeLevels);
                else
                    Debug.LogWarning("CompanyManager: upgrade levels could not be restored — the player's inventory does not exist yet. Restore the company after the economy.");
            }

            OnCompanyChanged?.Invoke();
        }

#if UNITY_EDITOR
        // ---------------------------------------------------------------- debug (editor only)
        // Inspector → CompanyManager → ⋮ menu, in Play mode. For testing upgrades and reputation
        // unlocks without grinding cash and reputation first.

        [ContextMenu("Debug/Add 10 000 cash")]
        private void DebugAddCash()
        {
            if (Player == null || Player.Inventory == null) return;
            Player.Inventory.AddCash(10000f, LedgerCategory.Other, "Debug cash");
            OnCompanyChanged?.Invoke();
        }

        [ContextMenu("Debug/Add 500 reputation")]
        private void DebugAddReputation() => AddReputation(500, "Debug");

        [ContextMenu("Debug/Remove 500 reputation")]
        private void DebugRemoveReputation() => AddReputation(-500, "Debug");

        [ContextMenu("Debug/Generate 5 contracts")]
        private void DebugGenerateContracts()
        {
            ResolveReferences();
            if (_contractManager == null) return;
            for (int i = 0; i < 5; i++)
                _contractManager.GenerateContract(CurrentDay);
        }

        [ContextMenu("Debug/Log upgrade effects")]
        private void DebugLogUpgradeEffects()
        {
            if (Player == null || Player.Inventory == null || Upgrades == null)
            {
                Debug.Log("CompanyManager: no player or upgrades yet (enter Play mode).");
                return;
            }

            var m = Player.Inventory.Modifiers;
            string levels = "";
            foreach (var type in CompanyUpgrades.AllTypes)
                levels += $"{type} {Upgrades.GetLevel(type) + 1}, ";

            int limit = Player.MaxActiveContracts;
            Debug.Log($"CompanyManager effects — reputation {Reputation.Reputation} ({Reputation.Title}); " +
                      $"levels: {levels.TrimEnd(',', ' ')}; " +
                      $"access: {Player.AccessLevel}; market fee: {m.ListingFeeRate:P1}; " +
                      $"transport: -{m.TransportCostReduction:P0} cost, -{m.TransportDaysReduction} week(s); " +
                      $"contracts: {Player.ActiveContractCount}/{(limit < 0 ? "unlimited" : limit.ToString())}");
        }
#endif

    }
}
