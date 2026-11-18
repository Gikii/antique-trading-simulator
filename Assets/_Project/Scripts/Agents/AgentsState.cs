using System.Collections.Generic;
using AntiqueTradingSimulator.Company;
using AntiqueTradingSimulator.News;

namespace AntiqueTradingSimulator.Agents
{
    public class NPCManagerState
    {
        public List<NPCTraderState> Npcs = new List<NPCTraderState>();
    }

    public class NPCTraderState
    {
        public string Id;

        public string ProfileId;
        public string TraderName;

        public List<PlanState> Acquisitions = new List<PlanState>();

        public List<PendingReactionState> PendingReactions = new List<PendingReactionState>();

        public List<string> CommittedContractIds = new List<string>();

        /// <summary>Reputation, contract counts and daily history. Null in saves from before NPC records.</summary>
        public TraderRecordState Record;

        public int PlanWeek;

        public List<int> SlotTicks = new List<int>();

        public float WeekBudget;

        public float ContractReserve;

        public float PlanReserve;

        public int ContractSlotsUsed;

        public int RumourExitWeek;
        public int RumourClaimWeek;
        public int RumourDumpWeek;
        public List<NewsEventData> RumourExitScope;
    }

    public class AcquisitionState
    {
        public string ListingId;
        public float PurchasePrice;
        public int Week;
    }
    public class PlanState
    {
        public string ListingId;
        public float PurchasePrice;
        public int Week;

        public PlanPurpose Purpose;
        public string ContractId;
        public string RestorationOptionId;
        public int StartRestoreByWeek;
        public float AssumedFee;
        public float ExitAskingPrice;
        public int DumpAfterWeek;
    }

    public class PendingReactionState
    {
        public int ReactionWeek;
        public NewsItemState News;
    }
}
