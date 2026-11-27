namespace AntiqueTradingSimulator.Agents
{
    /// <summary>What an NPC bought an antique for, or what it has since decided to do with it.</summary>
    public enum PlanPurpose
    {
        Resale,
        Contract,
        RumourExit
    }

    /// <summary>
    /// One owned antique's cost basis and the intent attached to it, kept from the moment the NPC
    /// decided to acquire it until it leaves the inventory. Carried out by NPCTrader.TierExecutePlans.
    /// </summary>
    public class Plan
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

        /// <summary> Week this antique was first put up for sale by its owner, or 0 if it never has been.</summary>
        public int OfferedSinceWeek;
        public bool HasBeenOffered => OfferedSinceWeek > 0;

        public bool HasRestorationLeg => !string.IsNullOrEmpty(RestorationOptionId);

        public void ClearRestorationLeg()
        {
            RestorationOptionId = null;
            StartRestoreByWeek = 0;
            AssumedFee = 0f;
        }

        /// <summary>Returns the plan to plain stock after its contract or rumour is gone.</summary>
        public void FallBackToResale()
        {
            Purpose = PlanPurpose.Resale;
            ContractId = null;
            ExitAskingPrice = 0f;
            DumpAfterWeek = 0;
        }
    }
}
