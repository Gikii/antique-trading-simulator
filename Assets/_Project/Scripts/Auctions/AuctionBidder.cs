namespace AntiqueTradingSimulator.Auctions
{
    /// <summary>
    /// A participant of one lot's bidding. NPCs bid on their own up to Limit; the player only
    /// bids when they press the button (Limit is unused for them).
    /// </summary>
    public class AuctionBidder
    {
        public string Id;
        public string Name;
        public BidderKind Kind;

        /// <summary>Highest bid this NPC is willing to make right now (can rise with auction fever).</summary>
        public float Limit;

        /// <summary>Hard ceiling from the NPC's budget — fever never pushes the limit past it.</summary>
        public float Cap;

        /// <summary>Chance, when the price passes the limit, to get carried away and raise it once.</summary>
        public float FeverChance;

        /// <summary>How much fever raises the limit (0.1 = +10%).</summary>
        public float FeverRaise;

        /// <summary>0..1 — how much longer the NPC thinks as the price approaches its limit.</summary>
        public float Hesitation;

        /// <summary>False once the bidder dropped out of this lot.</summary>
        public bool Active = true;

        public bool FeverUsed;

        /// <summary>LotBidding's clock time of this NPC's next response (infinity = none pending).</summary>
        internal float RespondAt = float.PositiveInfinity;

        public bool IsPlayer => Kind == BidderKind.Player;
    }
}
