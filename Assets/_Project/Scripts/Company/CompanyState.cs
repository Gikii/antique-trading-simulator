using System.Collections.Generic;

namespace AntiqueTradingSimulator.Company
{
    public class CompanyState
    {
        public int ContractsAccepted;
        public int ContractsFulfilled;
        public int ContractsFailed;

        public float PendingSalesValue;
        public int PendingSalesCount;

        public List<CompanySnapshot> History = new List<CompanySnapshot>();

        public ReputationState Reputation = new ReputationState();

        public Dictionary<CompanyUpgradeType, int> UpgradeLevels = new Dictionary<CompanyUpgradeType, int>();
    }

    public class ReputationState
    {
        public int Reputation;
        public float Credibility;

        public List<ReputationChange> History = new List<ReputationChange>();
    }
}
