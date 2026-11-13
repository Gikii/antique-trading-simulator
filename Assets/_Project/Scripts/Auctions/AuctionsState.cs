using AntiqueTradingSimulator.Market;
using System.Collections.Generic;

namespace AntiqueTradingSimulator.Auctions
{
    /// <summary>Everything AuctionManager writes to a save: the upcoming catalogs and the results history.</summary>
    public class AuctionsState
    {
        /// <summary>Index (0, 1, 2, …) of the next auction day that has no auction created yet.</summary>
        public int NextScheduleIndex;

        public List<AuctionState> Upcoming = new List<AuctionState>();
        public List<AuctionState> History = new List<AuctionState>();
    }

    public class AuctionState
    {
        public string Id;
        public int Number;
        public int Week;
        public int StartTick;
        public int AnnouncedOnWeek;
        public AuctionStatus Status;
        public bool PlayerJoined;
        public List<AuctionLotState> Lots = new List<AuctionLotState>();

        public static AuctionState Capture(Auction auction)
        {
            if (auction == null) return null;

            var state = new AuctionState
            {
                Id = auction.Id,
                Number = auction.Number,
                Week = auction.Week,
                StartTick = auction.StartTick,
                AnnouncedOnWeek = auction.AnnouncedOnWeek,
                Status = auction.Status,
                PlayerJoined = auction.PlayerJoined
            };

            foreach (var lot in auction.Lots)
                if (lot != null) state.Lots.Add(AuctionLotState.Capture(lot));

            return state;
        }

        public Auction Restore()
        {
            var auction = new Auction
            {
                Id = Id,
                Number = Number,
                Week = Week,
                StartTick = StartTick,
                AnnouncedOnWeek = AnnouncedOnWeek,
                Status = Status,
                PlayerJoined = PlayerJoined
            };

            if (Lots != null)
                foreach (var lotState in Lots)
                {
                    var lot = lotState?.Restore();
                    if (lot != null) auction.Lots.Add(lot);
                }

            return auction;
        }
    }

    /// <summary>
    /// A lot with its antique. A sold lot's antique now belongs to the winner, so the copy kept here
    /// is only a record for the results list — the winner's inventory holds the real one.
    /// </summary>
    public class AuctionLotState
    {
        public int LotNumber;
        public AntiqueState Antique;
        public float EstimateLow;
        public float EstimateHigh;
        public float StartPrice;
        public bool HasProvenance;
        public LotStatus Status;
        public string WinnerId;
        public string WinnerName;
        public float HammerPrice;
        public float MarketValueAtSale;
        public int BidCount;

        public static AuctionLotState Capture(AuctionLot lot) => new AuctionLotState
        {
            LotNumber = lot.LotNumber,
            Antique = AntiqueState.Capture(lot.Antique),
            EstimateLow = lot.EstimateLow,
            EstimateHigh = lot.EstimateHigh,
            StartPrice = lot.StartPrice,
            HasProvenance = lot.HasProvenance,
            Status = lot.Status,
            WinnerId = lot.WinnerId,
            WinnerName = lot.WinnerName,
            HammerPrice = lot.HammerPrice,
            MarketValueAtSale = lot.MarketValueAtSale,
            BidCount = lot.BidCount
        };

        public AuctionLot Restore()
        {
            var antique = Antique?.Restore();
            if (antique == null) return null;

            return new AuctionLot
            {
                LotNumber = LotNumber,
                Antique = antique,
                EstimateLow = EstimateLow,
                EstimateHigh = EstimateHigh,
                StartPrice = StartPrice,
                HasProvenance = HasProvenance,
                Status = Status,
                WinnerId = WinnerId,
                WinnerName = WinnerName,
                HammerPrice = HammerPrice,
                MarketValueAtSale = MarketValueAtSale,
                BidCount = BidCount
            };
        }
    }
}
