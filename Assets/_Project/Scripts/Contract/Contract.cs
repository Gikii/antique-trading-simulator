using System;
using UnityEngine;

namespace AntiqueTradingSimulator.Contracts
{
    [Serializable]
    public class Contract
    {
        public string ContractId { get; }
        public ContractType Type { get; }
        public ContractRequirement Requirement { get; }

        public int CreatedDay { get; }
        public int DurationDays { get; set; }
        public int DeadlineDay => CreatedDay + DurationDays;

        public float RewardPerUnit { get; }
        public float TotalReward => RewardPerUnit * Requirement.Quantity;

        public float Penalty { get; }

        public ContractStatus Status { get; private set; } = ContractStatus.Active;
        public int DeliveredQuantity { get; private set; }
        public int RemainingQuantity => Mathf.Max(0, Requirement.Quantity - DeliveredQuantity);
        public bool IsFulfilled => DeliveredQuantity >= Requirement.Quantity;

        public string ClaimedByTraderId { get; private set; }
        public bool IsClaimed => ClaimedByTraderId != null;
        public bool CanBeClaimed => Type == ContractType.Exclusive && Status == ContractStatus.Active && !IsClaimed;

        public event Action<Contract> OnContractExpired;

        public Contract(ContractType type, ContractRequirement requirement, int createdDay, int durationDays, int maxDurationDays, float rewardPerUnit, float penalty)
        {
            ContractId = Guid.NewGuid().ToString("N");
            Type = type;
            Requirement = requirement;
            CreatedDay = createdDay;
            DurationDays = Mathf.Clamp(durationDays, 1, maxDurationDays);
            RewardPerUnit = Mathf.Max(0f, rewardPerUnit);
            Penalty = type == ContractType.Exclusive ? Mathf.Max(0f, penalty) : 0f;
        }

        public bool CanBeFulfilledBy(string traderId)
        {
            if (Status != ContractStatus.Active) return false;
            if (Type == ContractType.Open) return true;
            return IsClaimed && ClaimedByTraderId == traderId;
        }

        public bool TryClaim(string traderId)
        {
            if (!CanBeClaimed) return false;
            ClaimedByTraderId = traderId;
            return true;
        }

        public void SetStatusFulfilled()
        {
            Status = ContractStatus.Fulfilled;
        }
        
        public void SetStatusExpired()
        {
            Status = ContractStatus.Expired;
        }
    }
}
