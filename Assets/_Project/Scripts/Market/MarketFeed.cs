using System;
using System.Collections.Generic;

namespace AntiqueTradingSimulator.Market
{
    /// <summary>What happened on the market, for the market history.</summary>
    public enum MarketFeedKind
    {
        /// <summary>New offers on the market – one entry per week, counted in <see cref="MarketFeedEntry.Count"/>.</summary>
        Listed,

        /// <summary>A trader (or the player) bought a listing – an anonymous offer, or another owner's listing.</summary>
        Purchase,

        /// <summary>A private collector bought an owner's listing (CollectorDemand).</summary>
        CollectorPurchase,

        /// <summary>An owner sold an antique to the market instantly.</summary>
        SoldToMarket,

        AuctionSold,
        AuctionUnsold,

        /// <summary>A trader (or the player) handed an antique over to a contract's client. Last, so saved values keep their meaning.</summary>
        ContractDelivery
    }

    /// <summary>One line of the market history. Plain data, saved as is.</summary>
    [Serializable]
    public class MarketFeedEntry
    {
        public int Week;
        public MarketFeedKind Kind;
        public string AntiqueName = "";

        /// <summary>Antique.PlayerOwnerId, an NPC id, or empty (collector, auction house, market).</summary>
        public string BuyerId = "";

        /// <summary>The owner who sold it: Antique.PlayerOwnerId, an NPC id, or empty (an anonymous offer / the auction house).</summary>
        public string SellerId = "";

        /// <summary>Price paid (hammer price for auctions, start price for unsold lots).</summary>
        public float Price;

        /// <summary>Listed only: how many offers appeared that week.</summary>
        public int Count = 1;

        public bool BoughtByPlayer => BuyerId == Antique.PlayerOwnerId;
        public bool SoldByPlayer => SellerId == Antique.PlayerOwnerId;

        /// <summary>A real sale to a buyer (counts toward the week's turnover).</summary>
        public bool IsSale => Kind == MarketFeedKind.Purchase || Kind == MarketFeedKind.CollectorPurchase ||
                              Kind == MarketFeedKind.AuctionSold || Kind == MarketFeedKind.ContractDelivery;
    }

    /// <summary>The player's trading over the whole campaign, kept even after old entries drop out.</summary>
    [Serializable]
    public class MarketFeedTotals
    {
        public int Bought;
        public float Spent;
        public int Sold;
        public float Earned;
    }

    /// <summary>Market activity in one week: sales and turnover, for the "this week on the market" line.</summary>
    [Serializable]
    public class MarketWeekStats
    {
        public int Week;
        public int Sales;
        public float Turnover;
    }

    /// <summary>
    /// History of the market: who bought and sold what, auction results, and how many new offers
    /// appeared each week. Newest entry first. Shown by TransactionHistoryUI in the Market view's side
    /// panel, saved with the market. Entries are data, not sentences, so the UI can group them by week
    /// and colour the player's own trades.
    /// </summary>
    public class MarketFeed
    {
        public const int MaxEntries = 200;

        private readonly List<MarketFeedEntry> _entries = new List<MarketFeedEntry>(MaxEntries);

        public IReadOnlyList<MarketFeedEntry> Entries => _entries;

        public MarketFeedTotals PlayerTotals { get; private set; } = new MarketFeedTotals();

        /// <summary>The latest week with recorded activity (compare its Week with the current one).</summary>
        public MarketWeekStats LatestWeek { get; private set; } = new MarketWeekStats();

        /// <summary>
        /// The current game week. Set by EconomyManager; the market itself has no clock and some
        /// actions (a purchase, a collector buying) don't carry the week.
        /// </summary>
        public Func<int> WeekProvider { get; set; }

        /// <summary>Raised after any change (new entry, a Listed count going up, clear, restore).</summary>
        public event Action OnChanged;

        private int CurrentWeek => WeekProvider != null ? WeekProvider() : 1;

        public void Clear()
        {
            _entries.Clear();
            PlayerTotals = new MarketFeedTotals();
            LatestWeek = new MarketWeekStats();
            OnChanged?.Invoke();
        }

        // ---------------------------------------------------------------- recording

        /// <summary>Counts a new offer into the week's "N new listings" line.</summary>
        public void AddListed()
        {
            int week = CurrentWeek;

            // The week's Listed entry, if any: entries of one week sit together at the top.
            for (int i = 0; i < _entries.Count && _entries[i].Week == week; i++)
            {
                if (_entries[i].Kind != MarketFeedKind.Listed) continue;

                _entries[i].Count++;
                OnChanged?.Invoke();
                return;
            }

            Add(new MarketFeedEntry { Week = week, Kind = MarketFeedKind.Listed });
        }

        /// <param name="sellerId">The listing's owner, or empty for an anonymous market offer.</param>
        public void AddPurchase(string antiqueName, string buyerId, string sellerId, float price) =>
            Add(new MarketFeedEntry
            {
                Kind = MarketFeedKind.Purchase, AntiqueName = antiqueName ?? "",
                BuyerId = buyerId ?? "", SellerId = sellerId ?? "", Price = price
            });

        public void AddCollectorPurchase(string antiqueName, string sellerId, float price) =>
            Add(new MarketFeedEntry
            {
                Kind = MarketFeedKind.CollectorPurchase, AntiqueName = antiqueName ?? "",
                SellerId = sellerId ?? "", Price = price
            });

        public void AddSoldToMarket(string antiqueName, string sellerId, float price) =>
            Add(new MarketFeedEntry
            {
                Kind = MarketFeedKind.SoldToMarket, AntiqueName = antiqueName ?? "",
                SellerId = sellerId ?? "", Price = price
            });

        public void AddAuctionSold(string antiqueName, string buyerId, float hammerPrice) =>
            Add(new MarketFeedEntry
            {
                Kind = MarketFeedKind.AuctionSold, AntiqueName = antiqueName ?? "",
                BuyerId = buyerId ?? "", Price = hammerPrice
            });

        /// <param name="price">What the client paid for this one antique (the contract's reward per unit).</param>
        public void AddContractDelivery(string antiqueName, string supplierId, float price) =>
            Add(new MarketFeedEntry
            {
                Kind = MarketFeedKind.ContractDelivery, AntiqueName = antiqueName ?? "",
                SellerId = supplierId ?? "", Price = price
            });

        public void AddAuctionUnsold(string antiqueName, float startPrice) =>
            Add(new MarketFeedEntry
            {
                Kind = MarketFeedKind.AuctionUnsold, AntiqueName = antiqueName ?? "", Price = startPrice
            });

        private void Add(MarketFeedEntry entry)
        {
            if (entry.Kind != MarketFeedKind.Listed && string.IsNullOrWhiteSpace(entry.AntiqueName))
                return;

            if (entry.Week == 0)
                entry.Week = CurrentWeek;

            Count(entry);

            _entries.Insert(0, entry);

            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);

            OnChanged?.Invoke();
        }

        /// <summary>Keeps the player's totals and the week's market stats.</summary>
        private void Count(MarketFeedEntry entry)
        {
            if (entry.BoughtByPlayer)
            {
                PlayerTotals.Bought++;
                PlayerTotals.Spent += entry.Price;
            }

            if (entry.SoldByPlayer)
            {
                PlayerTotals.Sold++;
                PlayerTotals.Earned += entry.Price;
            }

            if (!entry.IsSale)
                return;

            if (entry.Week != LatestWeek.Week)
                LatestWeek = new MarketWeekStats { Week = entry.Week };

            LatestWeek.Sales++;
            LatestWeek.Turnover += entry.Price;
        }

        // ---------------------------------------------------------------- save / load

        public List<MarketFeedEntry> CaptureEntries() => new List<MarketFeedEntry>(_entries);

        public MarketFeedTotals CaptureTotals() => new MarketFeedTotals
        {
            Bought = PlayerTotals.Bought,
            Spent = PlayerTotals.Spent,
            Sold = PlayerTotals.Sold,
            Earned = PlayerTotals.Earned
        };

        public MarketWeekStats CaptureWeekStats() => new MarketWeekStats
        {
            Week = LatestWeek.Week,
            Sales = LatestWeek.Sales,
            Turnover = LatestWeek.Turnover
        };

        /// <summary>
        /// Replaces the history with the saved one. Saves from before the structured history carry
        /// no entries – the history then simply starts empty.
        /// </summary>
        public void RestoreState(List<MarketFeedEntry> entries, MarketFeedTotals totals, MarketWeekStats weekStats)
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

            PlayerTotals = totals ?? new MarketFeedTotals();
            LatestWeek = weekStats ?? new MarketWeekStats();
            OnChanged?.Invoke();
        }
    }
}
