namespace AntiqueTradingSimulator.Auctions
{
    public enum AuctionStatus
    {
        Announced = 0,   // catalog published, waiting for the start hour
        JoinWindow = 1,  // the session is starting — the player has a few seconds to join
        Live = 2,        // lots are being auctioned, with the player in the room
        Finished = 3
    }

    public enum LotStatus
    {
        Pending = 0,
        Sold = 1,
        Unsold = 2
    }

    public enum BidderKind
    {
        Player = 0,
        Npc = 1
    }

    /// <summary>What the auctioneer announces while nobody raises the bid.</summary>
    public enum AuctioneerCall
    {
        None = 0,
        GoingOnce = 1,
        GoingTwice = 2,
        Closed = 3
    }
}
