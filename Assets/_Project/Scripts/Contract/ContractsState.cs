using System.Collections.Generic;

namespace AntiqueTradingSimulator.Contracts
{
    public class ContractsState
    {
        public List<ContractState> Contracts = new List<ContractState>();

        public List<string> ListedContractIds = new List<string>();

        /// <summary>Contract history, newest first. (Saves from before it was structured had FeedMessages – ignored.)</summary>
        public List<ContractFeedEntry> FeedEntries = new List<ContractFeedEntry>();

        public ContractFeedTotals FeedPlayerTotals = new ContractFeedTotals();
    }

    public class ContractState
    {
        public string ContractId;
        public ContractType Type;

        public ContractRequirement Requirement;

        public int CreatedWeek;
        public int DurationWeeks;

        public float RewardPerUnit;
        public float Penalty;

        public int RequiredReputation;
        public string ClassName;

        public ContractStatus Status;
        public int DeliveredQuantity;

        public string ClaimedByTraderId;

        public static ContractState Capture(Contract contract)
        {
            if (contract == null) return null;

            return new ContractState
            {
                ContractId = contract.ContractId,
                Type = contract.Type,
                Requirement = contract.Requirement,
                CreatedWeek = contract.CreatedWeek,
                DurationWeeks = contract.DurationWeeks,
                RewardPerUnit = contract.RewardPerUnit,
                Penalty = contract.Penalty,
                RequiredReputation = contract.RequiredReputation,
                ClassName = contract.ClassName,
                Status = contract.Status,
                DeliveredQuantity = contract.DeliveredQuantity,
                ClaimedByTraderId = contract.ClaimedByTraderId
            };
        }

        public Contract Restore() => new Contract(this);
    }
}
