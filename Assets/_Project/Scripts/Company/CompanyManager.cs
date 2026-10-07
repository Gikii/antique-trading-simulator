using System;
using System.Collections.Generic;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Economy;
using UnityEngine;

namespace AntiqueTradingSimulator.Company
{
    /// <summary>A row of the competition ranking: the player's company or an NPC trader.</summary>
    public readonly struct CompetitorEntry
    {
        public readonly string Name;
        public readonly float Wealth;
        public readonly float CollectionValue;
        public readonly bool IsPlayer;

        public CompetitorEntry(string name, float wealth, float collectionValue, bool isPlayer)
        {
            Name = name;
            Wealth = wealth;
            CollectionValue = collectionValue;
            IsPlayer = isPlayer;
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

        [Header("History")]
        [Tooltip("Daily snapshots kept for charts and trends.")]
        [Min(8)] [SerializeField] private int maxSnapshotDays = 120;

        private PlayerTrader _player;
        private TimeManager _timeManager;
        private EconomyManager _economyManager;
        private NPCManager _npcManager;

        private CompanyReputation _reputation;
        private CompanyUpgrades _upgrades;
        private readonly List<CompanySnapshot> _history = new List<CompanySnapshot>();

        // Market sales are turned into reputation once per day, as one history entry.
        private float _pendingSalesValue;
        private int _pendingSalesCount;

        private bool _subscribed;

        public string CompanyName => companyName;
        public CompanyReputation Reputation
        {
            get
            {
                if (_reputation == null)
                {
                    EnsureSettings();
                    _reputation = new CompanyReputation(reputationSettings);
                }
                return _reputation;
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
                    _upgrades = new CompanyUpgrades(upgradeSettings, Player.Inventory, (int)Player.AccessLevel - 1);
                    _upgrades.OnUpgraded += HandleUpgraded;
                }
                return _upgrades;
            }
        }

        public PlayerTrader Player => _player != null ? _player : (_player = GetComponent<PlayerTrader>());

        public int ContractsAccepted { get; private set; }
        public int ContractsFulfilled { get; private set; }
        public int ContractsFailed { get; private set; }

        /// <summary>One snapshot per day, oldest first.</summary>
        public IReadOnlyList<CompanySnapshot> History => _history;

        public int CurrentDay => Clock != null ? Clock.CurrentDay : 1;
        public int CampaignLength => Clock != null ? Clock.CampaignLength : 90;

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
            Reputation.OnChanged += HandleReputationChanged;
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
        }

        private void OnDestroy()
        {
            if (_reputation != null) _reputation.OnChanged -= HandleReputationChanged;
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

        // ---------------------------------------------------------------- upgrades API

        public UpgradeInfo GetUpgradeInfo(CompanyUpgradeType type) =>
            Upgrades?.GetInfo(type, Reputation.Reputation);

        public bool TryUpgrade(CompanyUpgradeType type) =>
            Upgrades != null && Upgrades.TryUpgrade(type, Reputation.Reputation);

        // ---------------------------------------------------------------- event handlers

        private void HandleDayChanged(int newDay)
        {
            FlushSalesReputation(newDay - 1);
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
            if (entry.Category != LedgerCategory.Sale || entry.Amount <= 0f) return;

            _pendingSalesValue += entry.Amount;
            _pendingSalesCount++;
        }

        private void FlushSalesReputation(int day)
        {
            int points = Mathf.RoundToInt(_pendingSalesValue * Reputation.Settings.ReputationPerSaleValue);
            int count = _pendingSalesCount;
            _pendingSalesValue = 0f;
            _pendingSalesCount = 0;

            if (points > 0)
                Reputation.AddReputation(points, count == 1 ? "Antique sold on the market" : $"{count} antiques sold on the market", day);
        }

        private void HandleContractAccepted(Contract contract)
        {
            ContractsAccepted++;
            OnCompanyChanged?.Invoke();
        }

        private void HandleContractFulfilled(Contract contract)
        {
            ContractsFulfilled++;
            if (contract == null) return;

            var s = Reputation.Settings;
            bool exclusive = contract.Type == ContractType.Exclusive;

            int reputation = s.ContractFulfilledReputation
                             + Mathf.RoundToInt(contract.TotalReward * s.ReputationPerContractReward)
                             + (exclusive ? s.ExclusiveContractBonusReputation : 0);
            float credibility = s.ContractFulfilledCredibility + (exclusive ? s.ExclusiveContractBonusCredibility : 0f);

            AddReputation(reputation, exclusive ? $"Exclusive contract fulfilled ({contract.Requirement})" : $"Contract fulfilled ({contract.Requirement})");
            AddCredibility(credibility, $"Contract delivered on time ({contract.Requirement})");
        }

        private void HandleContractFailed(Contract contract)
        {
            ContractsFailed++;

            var s = Reputation.Settings;
            string what = contract != null ? $" ({contract.Requirement})" : "";
            AddReputation(s.ContractFailedReputation, "Failed to deliver contract" + what);
            AddCredibility(s.ContractFailedCredibility, "Contract deadline missed" + what);
        }

        private void HandleReputationChanged(ReputationChange change) => OnCompanyChanged?.Invoke();

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
            int to = int.MaxValue;

            float total = 0f;
            foreach (var trader in _economyManager.Inventories)
                if (trader != null)
                    total += trader.Ledger.TradeVolume(from, to);

            return total > 0f ? inventory.Ledger.TradeVolume(from, to) / total : 0f;
        }

        // ---------------------------------------------------------------- snapshots & trends

        /// <summary>The company's numbers right now.</summary>
        public CompanySnapshot Capture()
        {
            var inventory = Player != null ? Player.Inventory : null;
            return new CompanySnapshot(
                CurrentDay,
                inventory?.Cash ?? 0f,
                inventory?.Wealth ?? 0f,
                inventory?.TotalHoldingsValue ?? 0f,
                Reputation.Reputation,
                Reputation.Credibility,
                MarketShare(),
                ContractsFulfilled);
        }

        private void RecordSnapshot()
        {
            var snapshot = Capture();
            _history.RemoveAll(s => s.Day == snapshot.Day);
            _history.Add(snapshot);
            if (_history.Count > maxSnapshotDays)
                _history.RemoveAt(0);
        }

        /// <summary>
        /// The latest recorded snapshot at least <paramref name="days"/> days old, or the
        /// oldest one if history doesn't reach that far back (live state if there's none).
        /// </summary>
        public CompanySnapshot SnapshotDaysAgo(int days)
        {
            if (_history.Count == 0) return Capture();

            int day = CurrentDay - days;
            for (int i = _history.Count - 1; i >= 0; i--)
                if (_history[i].Day <= day)
                    return _history[i];

            return _history[0];
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
            var result = new List<CompetitorEntry>();

            var inventory = Player != null ? Player.Inventory : null;
            if (inventory != null)
                result.Add(new CompetitorEntry(companyName, inventory.Wealth, inventory.TotalHoldingsValue, true));

            if (_npcManager == null) ResolveReferences();
            if (_npcManager != null)
                foreach (var npc in _npcManager.NPCs)
                    if (npc?.Inventory != null)
                        result.Add(new CompetitorEntry(npc.TraderName, npc.Inventory.Wealth, npc.Inventory.TotalHoldingsValue, false));

            result.Sort((a, b) => b.Wealth.CompareTo(a.Wealth));
            return result;
        }

        // ---------------------------------------------------------------- save / load

        /// <summary>
        /// Snapshot of the company. The competition ranking and the Congress score are not saved.
        /// </summary>
        public CompanyState CaptureState() => new CompanyState
        {
            ContractsAccepted = ContractsAccepted,
            ContractsFulfilled = ContractsFulfilled,
            ContractsFailed = ContractsFailed,
            PendingSalesValue = _pendingSalesValue,
            PendingSalesCount = _pendingSalesCount,
            History = new List<CompanySnapshot>(_history),
            Reputation = Reputation.CaptureState(),
            UpgradeLevels = Upgrades != null
                ? Upgrades.CaptureState()
                : new Dictionary<CompanyUpgradeType, int>()
        };

        /// <summary>
        /// Replaces the company's statistics, reputation, upgrade levels and snapshot history.
        /// Call AFTER the player's inventory and warehouse have been restored.
        /// </summary>
        public void RestoreState(CompanyState state)
        {
            if (state == null) return;

            ContractsAccepted = state.ContractsAccepted;
            ContractsFulfilled = state.ContractsFulfilled;
            ContractsFailed = state.ContractsFailed;

            _pendingSalesValue = state.PendingSalesValue;
            _pendingSalesCount = state.PendingSalesCount;

            // Through the property, so the lazy CompanyReputation exists before it is filled.
            Reputation.RestoreState(state.Reputation);

            if (state.UpgradeLevels != null && state.UpgradeLevels.Count > 0)
            {
                if (Upgrades != null)
                    Upgrades.RestoreState(state.UpgradeLevels);
                else
                    Debug.LogWarning("CompanyManager: upgrade levels could not be restored — the player's inventory does not exist yet. Restore the company after the economy.");
            }

            _history.Clear();
            if (state.History != null)
            {
                _history.AddRange(state.History);

                if (_history.Count > maxSnapshotDays)
                    _history.RemoveRange(0, _history.Count - maxSnapshotDays);
            }

            OnCompanyChanged?.Invoke();
        }

    }
}
