namespace AntiqueTradingSimulator.Agents
{
    /// <summary>Why a trader is holding an antique, and what it means to do with it.</summary>
    public enum PlanPurpose
    {
        Resale,
        Contract,
        RumourExit,
        NewsThesis
    }

    /// <summary>What ends a position, chosen once a week and acted on by a slot.</summary>
    public enum DisposalReason
    {
        None,
        /// <summary>The profit target has been reached.</summary>
        TargetReached,
        /// <summary>Held past the patience window; price it to clear.</summary>
        Patience,
        /// <summary>Believed news says this facet is falling.</summary>
        BadNews,
        /// <summary>Value has fallen below cost times the stop-loss multiple.</summary>
        StopLoss,
        /// <summary>The thesis it was bought on is about to fade.</summary>
        FadeApproaching,
        /// <summary>The thesis was debunked or has flipped sign.</summary>
        ThesisInvalidated,
        /// <summary>Cash is needed now for a committed cost.</summary>
        CashNeed,
        /// <summary>The warehouse is full.</summary>
        Capacity,
        /// <summary>The trader's own rumour is about to be debunked.</summary>
        RumourDump
    }

    /// <summary>
    /// One owned antique's cost basis, the intent attached to it and, when it was bought on a
    /// view of the market, the thesis that justified it and the prices that end it.
    /// </summary>
    public class Position
    {
        /// <summary>Everything paid for the antique so far: purchase, transport and restoration fees.</summary>
        public float CostBasis;
        public int AcquiredWeek;

        public PlanPurpose Purpose;

        /// <summary>The contract this antique is meant for, when Purpose is Contract.</summary>
        public string ContractId;

        /// <summary>Workshop the antique is meant to go to once it is available, or null.</summary>
        public string RestorationOptionId;

        /// <summary>Last week the restoration may begin and still be useful.</summary>
        public int StartRestoreByWeek;

        /// <summary>Fee the decision was costed at; reserved until the job starts.</summary>
        public float AssumedFee;

        /// <summary>Price to ask while the planted rumour runs, when Purpose is RumourExit. 0 = do not list.</summary>
        public float ExitAskingPrice;

        /// <summary>Week from which an unsold rumour-exit holding is cancelled and sold to the market.</summary>
        public int DumpAfterWeek;

        /// <summary>Week this antique was first put up for sale by its owner, or 0 if it never has been.</summary>
        public int OfferedSinceWeek;

        // ---------------------------------------------------------------- thesis

        /// <summary>The outlook that justified buying, when Purpose is NewsThesis.</summary>
        public float EntryOutlook;

        /// <summary>Week the believed move is strongest.</summary>
        public int ExpectedPeakWeek;

        /// <summary>Week the believed move is spent; the position is listed before it.</summary>
        public int ExpectedFadeWeek;

        /// <summary>Price the thesis says the antique should reach. 0 = no target.</summary>
        public float TargetPrice;

        /// <summary>Price below which the thesis is wrong. 0 = no stop.</summary>
        public float StopPrice;

        /// <summary>News items the thesis rests on, so a debunk invalidates it directly.</summary>
        public string[] BasedOnNewsIds;

        /// <summary>First week the value sat under the stop; -1 while it has not.</summary>
        public int DistressSince = -1;

        public bool HasBeenOffered => OfferedSinceWeek > 0;

        public bool HasRestorationLeg => !string.IsNullOrEmpty(RestorationOptionId);

        public bool HasThesis => Purpose == PlanPurpose.NewsThesis && ExpectedFadeWeek > 0;

        public void ClearRestorationLeg()
        {
            RestorationOptionId = null;
            StartRestoreByWeek = 0;
            AssumedFee = 0f;
        }

        /// <summary>Returns the position to plain stock, keeping only its cost basis.</summary>
        public void FallBackToResale()
        {
            Purpose = PlanPurpose.Resale;
            ContractId = null;
            ExitAskingPrice = 0f;
            DumpAfterWeek = 0;
            EntryOutlook = 0f;
            ExpectedPeakWeek = 0;
            ExpectedFadeWeek = 0;
            TargetPrice = 0f;
            StopPrice = 0f;
            BasedOnNewsIds = null;
        }
    }
}
