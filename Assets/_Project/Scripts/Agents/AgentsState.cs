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

        public List<string> CommittedContractIds = new List<string>();

        public TraderRecordState Record;

        public int PlanWeek;

        public int ListingRevisionWeek;

        public List<int> SlotTicks = new List<int>();

        public float WeekBudget;

        public int BudgetWeek;

        public float ContractReserve;

        public float PlanReserve;

        public int ContractSlotsUsed;

        public int RumourExitWeek;
        public int RumourClaimWeek;
        public int RumourDumpWeek;
        public List<NewsEventData> RumourExitScope;

        public List<BeliefState> Beliefs = new List<BeliefState>();

        public List<MagnitudeEntryState> MagnitudeMemory = new List<MagnitudeEntryState>();

        public List<float> SourceTrust = new List<float>();
    }

    public class BeliefState
    {
        public string NewsId;
        public string OfficialName;
        public NewsEventData Facet;
        public int Direction;
        public float EstimatedMove;
        public float Spread;
        public int StartWeek;
        public int EndWeek;
        public float Trust;
        public int HeardWeek;
        public int ActsFromWeek;
        public int Source;
        public bool Checked;
        public bool Learned;
        public float PriceAtStart;
        public float RealisedMove;
    }

    public class MagnitudeEntryState
    {
        public string OfficialName;
        public float Mean;
        public float Spread;
        public int Observations;
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
        public int OfferedSinceWeek;

        public float EntryOutlook;
        public int ExpectedPeakWeek;
        public int ExpectedFadeWeek;
        public float TargetPrice;
        public float StopPrice;
        public string[] BasedOnNewsIds;
        public int DistressSince;
    }
}
