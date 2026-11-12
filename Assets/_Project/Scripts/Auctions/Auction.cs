using AntiqueTradingSimulator.Market;
using System.Collections.Generic;
using System.Linq;

namespace AntiqueTradingSimulator.Auctions
{
    /// <summary>
    /// One item in an auction catalog. The antique is a fine piece consigned by the auction house:
    /// it belongs to nobody (and is not on the regular market) until someone wins it.
    /// </summary>
    public class AuctionLot
    {
        public int LotNumber;
        public Antique Antique;

        public float EstimateLow;
        public float EstimateHigh;
        public float StartPrice;
        public bool HasProvenance;

        public LotStatus Status = LotStatus.Pending;

        // Result — set when the hammer falls.
        public string WinnerId;
        public string WinnerName;
        public float HammerPrice;
        public float MarketValueAtSale;
        public int BidCount;

        public bool IsPending => Status == LotStatus.Pending;
        public bool IsSold => Status == LotStatus.Sold;
        public string Name => Antique != null ? Antique.Name : "Unknown";
    }

    /// <summary>One auction session: a day, a start hour and a catalog of lots auctioned one after another.</summary>
    public class Auction
    {
        public string Id;
        public int Number;
        public int Day;
        public int StartTick;
        public int AnnouncedOnDay;
        public AuctionStatus Status = AuctionStatus.Announced;

        /// <summary>True once the player joined the live session (kept for the results).</summary>
        public bool PlayerJoined;

        public List<AuctionLot> Lots = new List<AuctionLot>();

        public bool IsFinished => Status == AuctionStatus.Finished;
        public int SoldCount => Lots.Count(l => l.IsSold);
        public float TotalHammer => Lots.Where(l => l.IsSold).Sum(l => l.HammerPrice);
        public AuctionLot FirstPendingLot => Lots.FirstOrDefault(l => l.IsPending);
    }
}
