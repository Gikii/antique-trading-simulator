using System;
using System.Collections.Generic;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Economy;
using UnityEngine;

namespace AntiqueTradingSimulator.Company
{
    /// <summary>
    /// A trader's standing over the campaign — the player's company and every NPC keep one, by
    /// the same rules: reputation and credibility (market sales and contracts move them), the
    /// contract counts, and one snapshot per day for charts and the final ranking.
    /// Plain C#: the owner feeds it sales, contract results and days.
    /// </summary>
    public class TraderRecord
    {
        public const int DefaultMaxHistoryDays = 120;

        private readonly List<CompanySnapshot> _history = new List<CompanySnapshot>();

        // Market sales are turned into reputation once per day, as one history entry.
        private float _pendingSalesValue;
        private int _pendingSalesCount;

        public TraderRecord(ReputationSettings settings, int maxHistoryDays = DefaultMaxHistoryDays)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            Reputation = new CompanyReputation(settings);
            Reputation.OnChanged += _ => Changed?.Invoke();
            MaxHistoryDays = Math.Max(1, maxHistoryDays);
        }

        public CompanyReputation Reputation { get; }

        public ReputationSettings Settings => Reputation.Settings;

        public int ContractsAccepted { get; private set; }
        public int ContractsFulfilled { get; private set; }
        public int ContractsFailed { get; private set; }

        /// <summary>One snapshot per day, oldest first.</summary>
        public IReadOnlyList<CompanySnapshot> History => _history;

        public int MaxHistoryDays { get; set; }

        /// <summary>Reputation, credibility or a contract count changed.</summary>
        public event Action Changed;

        // ---------------------------------------------------------------- sales

        /// <summary>A market sale; its reputation is granted at the end of the day (<see cref="FlushSales"/>).</summary>
        public void RecordSale(float amount)
        {
            if (amount <= 0f) return;

            _pendingSalesValue += amount;
            _pendingSalesCount++;
        }

        /// <summary>Turns the day's sales into reputation, as one entry dated <paramref name="day"/>.</summary>
        public void FlushSales(int day)
        {
            int points = Mathf.RoundToInt(_pendingSalesValue * Settings.ReputationPerSaleValue);
            int count = _pendingSalesCount;
            _pendingSalesValue = 0f;
            _pendingSalesCount = 0;

            if (points > 0)
                Reputation.AddReputation(points, count == 1 ? "Antique sold on the market" : $"{count} antiques sold on the market", day);
        }

        // ---------------------------------------------------------------- contracts

        public void ContractAccepted()
        {
            ContractsAccepted++;
            Changed?.Invoke();
        }

        public void ContractFulfilled(Contract contract, int day)
        {
            ContractsFulfilled++;
            if (contract == null)
            {
                Changed?.Invoke();
                return;
            }

            var s = Settings;
            bool exclusive = contract.Type == ContractType.Exclusive;

            int reputation = s.ContractFulfilledReputation
                             + Mathf.RoundToInt(contract.TotalReward * s.ReputationPerContractReward)
                             + (exclusive ? s.ExclusiveContractBonusReputation : 0);
            float credibility = s.ContractFulfilledCredibility + (exclusive ? s.ExclusiveContractBonusCredibility : 0f);

            Reputation.AddReputation(reputation, exclusive ? $"Exclusive contract fulfilled ({contract.Requirement})" : $"Contract fulfilled ({contract.Requirement})", day);
            Reputation.AddCredibility(credibility, $"Contract delivered on time ({contract.Requirement})", day);
        }

        public void ContractFailed(Contract contract, int day)
        {
            ContractsFailed++;

            var s = Settings;
            string what = contract != null ? $" ({contract.Requirement})" : "";
            Reputation.AddReputation(s.ContractFailedReputation, "Failed to deliver contract" + what, day);
            Reputation.AddCredibility(s.ContractFailedCredibility, "Contract deadline missed" + what, day);
        }

        // ---------------------------------------------------------------- snapshots

        /// <summary>The trader's numbers right now.</summary>
        public CompanySnapshot Capture(int day, TraderInventory inventory, float marketShare) =>
            new CompanySnapshot(
                day,
                inventory?.Cash ?? 0f,
                inventory?.Wealth ?? 0f,
                inventory?.TotalHoldingsValue ?? 0f,
                Reputation.Reputation,
                Reputation.Credibility,
                marketShare,
                ContractsFulfilled);

        /// <summary>Keeps <paramref name="snapshot"/> as the day's entry (replacing an earlier one of the same day).</summary>
        public void Record(CompanySnapshot snapshot)
        {
            _history.RemoveAll(s => s.Day == snapshot.Day);
            _history.Add(snapshot);
            if (_history.Count > MaxHistoryDays)
                _history.RemoveRange(0, _history.Count - MaxHistoryDays);
        }

        /// <summary>
        /// The latest snapshot at least <paramref name="days"/> days before <paramref name="currentDay"/>,
        /// the oldest one if history doesn't reach that far back, or null with no history at all.
        /// </summary>
        public CompanySnapshot? SnapshotDaysAgo(int currentDay, int days)
        {
            if (_history.Count == 0) return null;

            int day = currentDay - days;
            for (int i = _history.Count - 1; i >= 0; i--)
                if (_history[i].Day <= day)
                    return _history[i];

            return _history[0];
        }

        // ---------------------------------------------------------------- save / load

        public TraderRecordState CaptureState() => new TraderRecordState
        {
            ContractsAccepted = ContractsAccepted,
            ContractsFulfilled = ContractsFulfilled,
            ContractsFailed = ContractsFailed,
            PendingSalesValue = _pendingSalesValue,
            PendingSalesCount = _pendingSalesCount,
            History = new List<CompanySnapshot>(_history),
            Reputation = Reputation.CaptureState()
        };

        /// <summary>Replaces everything with the saved record. A null state (a save from before records) starts it empty.</summary>
        public void RestoreState(TraderRecordState state)
        {
            ContractsAccepted = state?.ContractsAccepted ?? 0;
            ContractsFulfilled = state?.ContractsFulfilled ?? 0;
            ContractsFailed = state?.ContractsFailed ?? 0;
            _pendingSalesValue = state?.PendingSalesValue ?? 0f;
            _pendingSalesCount = state?.PendingSalesCount ?? 0;

            Reputation.RestoreState(state?.Reputation ?? new ReputationState());

            _history.Clear();
            if (state?.History != null)
            {
                _history.AddRange(state.History);
                if (_history.Count > MaxHistoryDays)
                    _history.RemoveRange(0, _history.Count - MaxHistoryDays);
            }

            Changed?.Invoke();
        }
    }

    /// <summary>A <see cref="TraderRecord"/> in a save file.</summary>
    public class TraderRecordState
    {
        public int ContractsAccepted;
        public int ContractsFulfilled;
        public int ContractsFailed;

        public float PendingSalesValue;
        public int PendingSalesCount;

        public List<CompanySnapshot> History = new List<CompanySnapshot>();

        public ReputationState Reputation = new ReputationState();
    }

    public static class TraderStats
    {
        /// <summary>
        /// <paramref name="inventory"/>'s share of all market trading volume (purchases + sales of
        /// every trader in <paramref name="allTraders"/>), 0..1, from <paramref name="fromDay"/> on.
        /// </summary>
        public static float MarketShare(TraderInventory inventory, IEnumerable<TraderInventory> allTraders, int fromDay = int.MinValue)
        {
            if (inventory == null || allTraders == null) return 0f;

            float total = 0f;
            foreach (var trader in allTraders)
                if (trader != null)
                    total += trader.Ledger.TradeVolume(fromDay, int.MaxValue);

            return total > 0f ? inventory.Ledger.TradeVolume(fromDay, int.MaxValue) / total : 0f;
        }
    }
}
