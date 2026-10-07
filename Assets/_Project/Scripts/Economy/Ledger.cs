using System;
using System.Collections.Generic;

namespace AntiqueTradingSimulator.Economy
{
    /// <summary>
    /// What a cash movement was for. The sign of LedgerEntry.Amount says whether it was
    /// income or an expense; the category says why. Kept fine-grained so the Finances
    /// screen can group them however it likes.
    /// </summary>
    public enum LedgerCategory
    {
        Sale,         // antique sold on the market (instant sale or own listing, gross price)
        Contract,     // contract reward
        Auction,      // auction proceeds (no auctions yet)
        Purchase,     // antique bought on the market (price only)
        Transport,    // shipping paid together with a purchase
        Warehouse,    // daily warehouse upkeep
        Commission,   // market fee kept from an own listing
        Upgrade,      // one-off price of a company upgrade
        Upkeep,       // daily upkeep of company upgrades (staff, experts, ...)
        Penalty,      // failed contract penalty
        Other
    }

    public static class LedgerCategoryExtensions
    {
        /// <summary>Counts toward market trading volume (used for market share).</summary>
        public static bool IsTrade(this LedgerCategory category) =>
            category == LedgerCategory.Sale ||
            category == LedgerCategory.Purchase ||
            category == LedgerCategory.Auction;

        public static string DisplayName(this LedgerCategory category) => category switch
        {
            LedgerCategory.Sale => "Sales (Market)",
            LedgerCategory.Contract => "Contracts",
            LedgerCategory.Auction => "Auctions",
            LedgerCategory.Purchase => "Purchases (Market)",
            LedgerCategory.Transport => "Transport",
            LedgerCategory.Warehouse => "Warehouse Maintenance",
            LedgerCategory.Commission => "Market Commissions",
            LedgerCategory.Upgrade => "Upgrades",
            LedgerCategory.Upkeep => "Company Upkeep",
            LedgerCategory.Penalty => "Penalties",
            _ => "Other"
        };
    }

    /// <summary>One cash movement. Amount is signed: positive = income, negative = expense.</summary>
    public readonly struct LedgerEntry
    {
        public readonly int Day;
        public readonly LedgerCategory Category;
        public readonly float Amount;
        public readonly string Description;

        public LedgerEntry(int day, LedgerCategory category, float amount, string description)
        {
            Day = day;
            Category = category;
            Amount = amount;
            Description = description ?? "";
        }

        public bool IsIncome => Amount > 0f;
        public bool IsExpense => Amount < 0f;
    }

    /// <summary>
    /// Chronological record of every cash movement of one TraderInventory (player and NPCs
    /// alike). TraderInventory writes to it; the Finances screen and market share read it.
    /// Entries are never removed — a 90-day campaign stays small.
    /// </summary>
    public class Ledger
    {
        private readonly List<LedgerEntry> _entries = new List<LedgerEntry>();

        /// <summary>Oldest first.</summary>
        public IReadOnlyList<LedgerEntry> Entries => _entries;

        public event Action<LedgerEntry> OnEntryAdded;

        public void Add(LedgerEntry entry)
        {
            if (Math.Abs(entry.Amount) < 0.005f) return;

            _entries.Add(entry);
            OnEntryAdded?.Invoke(entry);
        }

        /// <summary>Entries from <paramref name="fromDay"/> to <paramref name="toDay"/> inclusive.</summary>
        public IEnumerable<LedgerEntry> Between(int fromDay, int toDay)
        {
            foreach (var entry in _entries)
                if (entry.Day >= fromDay && entry.Day <= toDay)
                    yield return entry;
        }

        /// <summary>Newest first, at most <paramref name="count"/> entries.</summary>
        public List<LedgerEntry> Latest(int count)
        {
            var result = new List<LedgerEntry>(Math.Max(0, count));
            for (int i = _entries.Count - 1; i >= 0 && result.Count < count; i--)
                result.Add(_entries[i]);
            return result;
        }

        public float Income(int fromDay, int toDay)
        {
            float sum = 0f;
            foreach (var entry in Between(fromDay, toDay))
                if (entry.IsIncome) sum += entry.Amount;
            return sum;
        }

        /// <summary>Total expenses as a positive number.</summary>
        public float Expenses(int fromDay, int toDay)
        {
            float sum = 0f;
            foreach (var entry in Between(fromDay, toDay))
                if (entry.IsExpense) sum -= entry.Amount;
            return sum;
        }

        /// <summary>Signed sum of one category.</summary>
        public float Sum(LedgerCategory category, int fromDay, int toDay)
        {
            float sum = 0f;
            foreach (var entry in Between(fromDay, toDay))
                if (entry.Category == category) sum += entry.Amount;
            return sum;
        }

        /// <summary>Absolute value of all sales and purchases — this trader's market volume.</summary>
        public float TradeVolume(int fromDay, int toDay)
        {
            float sum = 0f;
            foreach (var entry in Between(fromDay, toDay))
                if (entry.Category.IsTrade()) sum += Math.Abs(entry.Amount);
            return sum;
        }

        // ---------------------------------------------------------------- save / load

        public LedgerState CaptureState() => new LedgerState { Entries = new List<LedgerEntry>(_entries) };
        public void RestoreState(LedgerState state)
        {
            _entries.Clear();
            if (state?.Entries == null) return;

            _entries.AddRange(state.Entries);
        }

    }
}
