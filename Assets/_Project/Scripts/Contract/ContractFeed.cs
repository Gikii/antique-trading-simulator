using System;
using System.Collections.Generic;
using AntiqueTradingSimulator.Market;

namespace AntiqueTradingSimulator.Contracts
{
    /// <summary>What happened to a contract, for the contract history.</summary>
    public enum ContractFeedKind
    {
        /// <summary>New contracts on the board – one entry per week, counted in <see cref="ContractFeedEntry.Count"/>.</summary>
        Listed,

        /// <summary>A trader (or the player) committed to a contract: claimed an exclusive one, or the player accepted an open one.</summary>
        TakenOn,

        Fulfilled,

        /// <summary>A claimed contract ran out unfulfilled; <see cref="ContractFeedEntry.Amount"/> is the penalty paid.</summary>
        Failed,

        /// <summary>An unclaimed contract left the board without being fulfilled.</summary>
        Expired
    }

    /// <summary>One line of the contract history. Plain data, saved as is.</summary>
    [Serializable]
    public class ContractFeedEntry
    {
        public int Week;
        public ContractFeedKind Kind;

        /// <summary>"3x Porcelain" (empty for Listed).</summary>
        public string ContractName = "";
        public ContractType Type;

        /// <summary>Who did it: Antique.PlayerOwnerId, an NPC id, or empty when nobody did (Expired, Listed).</summary>
        public string TraderId = "";

        /// <summary>Reward (Fulfilled) or penalty (Failed); 0 otherwise.</summary>
        public float Amount;

        /// <summary>Listed only: how many contracts appeared that week.</summary>
        public int Count = 1;

        public bool IsPlayer => TraderId == Antique.PlayerOwnerId;
    }

    /// <summary>The player's running contract record, kept even after old entries drop out of the history.</summary>
    [Serializable]
    public class ContractFeedTotals
    {
        public int Fulfilled;
        public int Failed;
        public float Earned;
        public float PenaltiesPaid;
    }

    /// <summary>
    /// History of the contract board: who took on, fulfilled or failed which contract, what expired,
    /// and how many new contracts appeared each week. Newest entry first.
    ///
    /// Owned by ContractManager, shown by ContractFeedUI in the Contracts view's side panel whenever
    /// no contract is selected, and saved with the board so loading a game keeps the history.
    /// Entries are data (week, kind, amount…), not finished sentences, so the UI can group them by
    /// week and colour them by outcome.
    /// </summary>
    public class ContractFeed
    {
        public const int MaxEntries = 100;

        private readonly List<ContractFeedEntry> _entries = new List<ContractFeedEntry>(MaxEntries);

        public IReadOnlyList<ContractFeedEntry> Entries => _entries;

        /// <summary>The player's record over the whole campaign.</summary>
        public ContractFeedTotals PlayerTotals { get; private set; } = new ContractFeedTotals();

        /// <summary>Raised after any change (new entry, a Listed count going up, clear, restore).</summary>
        public event Action OnChanged;

        public void Clear()
        {
            _entries.Clear();
            PlayerTotals = new ContractFeedTotals();
            OnChanged?.Invoke();
        }

        // ---------------------------------------------------------------- recording

        /// <summary>Counts a new contract into the week's "N new contracts listed" line.</summary>
        public void AddListed(Contract contract, int week)
        {
            if (contract == null) return;

            // The week's Listed entry, if any: entries of one week sit together at the top.
            for (int i = 0; i < _entries.Count && _entries[i].Week == week; i++)
            {
                if (_entries[i].Kind != ContractFeedKind.Listed) continue;

                _entries[i].Count++;
                OnChanged?.Invoke();
                return;
            }

            Add(new ContractFeedEntry { Week = week, Kind = ContractFeedKind.Listed, Count = 1 });
        }

        public void AddTakenOn(Contract contract, string traderId, int week) =>
            Add(Entry(contract, ContractFeedKind.TakenOn, traderId, 0f, week));

        public void AddFulfilled(Contract contract, string traderId, int week)
        {
            var entry = Entry(contract, ContractFeedKind.Fulfilled, traderId, contract != null ? contract.TotalReward : 0f, week);
            if (entry != null && entry.IsPlayer)
            {
                PlayerTotals.Fulfilled++;
                PlayerTotals.Earned += entry.Amount;
            }

            Add(entry);
        }

        /// <summary>A claimed contract that ran out; <paramref name="penalty"/> is what the trader paid (0 = none).</summary>
        public void AddFailed(Contract contract, string traderId, float penalty, int week)
        {
            var entry = Entry(contract, ContractFeedKind.Failed, traderId, penalty, week);
            if (entry != null && entry.IsPlayer)
            {
                PlayerTotals.Failed++;
                PlayerTotals.PenaltiesPaid += entry.Amount;
            }

            Add(entry);
        }

        public void AddExpired(Contract contract, int week) =>
            Add(Entry(contract, ContractFeedKind.Expired, "", 0f, week));

        private static ContractFeedEntry Entry(Contract contract, ContractFeedKind kind, string traderId, float amount, int week)
        {
            if (contract == null) return null;

            return new ContractFeedEntry
            {
                Week = week,
                Kind = kind,
                ContractName = contract.DisplayName,
                Type = contract.Type,
                TraderId = traderId ?? "",
                Amount = amount
            };
        }

        private void Add(ContractFeedEntry entry)
        {
            if (entry == null) return;

            _entries.Insert(0, entry);

            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);

            OnChanged?.Invoke();
        }

        // ---------------------------------------------------------------- save / load

        public List<ContractFeedEntry> CaptureEntries() => new List<ContractFeedEntry>(_entries);

        public ContractFeedTotals CaptureTotals() => new ContractFeedTotals
        {
            Fulfilled = PlayerTotals.Fulfilled,
            Failed = PlayerTotals.Failed,
            Earned = PlayerTotals.Earned,
            PenaltiesPaid = PlayerTotals.PenaltiesPaid
        };

        /// <summary>
        /// Replaces the history with the saved one. Saves from before the structured history carry
        /// no entries – the history then simply starts empty.
        /// </summary>
        public void RestoreState(List<ContractFeedEntry> entries, ContractFeedTotals totals)
        {
            _entries.Clear();

            if (entries != null)
            {
                foreach (var entry in entries)
                {
                    if (entry != null)
                        _entries.Add(entry);
                }

                if (_entries.Count > MaxEntries)
                    _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
            }

            PlayerTotals = totals ?? new ContractFeedTotals();
            OnChanged?.Invoke();
        }
    }
}
