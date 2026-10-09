using System.Collections.Generic;
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

        public List<AcquisitionState> Acquisitions = new List<AcquisitionState>();

        public List<PendingReactionState> PendingReactions = new List<PendingReactionState>();

        public List<string> CommittedContractIds = new List<string>();
    }

    public class AcquisitionState
    {
        public string ListingId;
        public float PurchasePrice;
        public int Day;
    }

    public class PendingReactionState
    {
        public int ReactionDay;
        public NewsItemState News;
    }
}
