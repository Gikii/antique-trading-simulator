using AntiqueTradingSimulator.Market;
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

        public int CreatedWeek { get; }
        public int DurationWeeks { get; set; }
        public int DeadlineWeek => CreatedWeek + DurationWeeks;

        public float RewardPerUnit { get; }
        public float TotalReward => RewardPerUnit * Requirement.Quantity;

        public float Penalty { get; }

        /// <summary>
        /// Reputation the player needs to accept this contract (0 = anyone). Reputation only gives
        /// access to better clients — it never changes a contract's reward. NPCs, who have no
        /// reputation, don't take contracts that require any.
        /// </summary>
        public int RequiredReputation { get; }

        /// <summary>Name of the contract's class ("Premium", "Prestige"), empty for a standard contract.</summary>
        public string ClassName { get; }

        public bool RequiresReputation => RequiredReputation > 0;

        public ContractStatus Status { get; private set; } = ContractStatus.Active;
        public int DeliveredQuantity { get; private set; }
        public int RemainingQuantity => Mathf.Max(0, Requirement.Quantity - DeliveredQuantity);
        public bool IsFulfilled => DeliveredQuantity >= Requirement.Quantity;

        public string ClaimedByTraderId { get; private set; }
        public bool IsClaimed => ClaimedByTraderId != null;
        public bool CanBeClaimed => Type == ContractType.Exclusive && Status == ContractStatus.Active && !IsClaimed;

        public event Action<Contract> OnContractExpired;

        public Contract(ContractType type, ContractRequirement requirement, int createdWeek, int durationWeeks, int maxDurationWeeks, float rewardPerUnit, float penalty,
            int requiredReputation = 0, string className = "")
        {
            ContractId = Guid.NewGuid().ToString("N");
            Type = type;
            Requirement = requirement;
            CreatedWeek = createdWeek;
            DurationWeeks = Mathf.Clamp(durationWeeks, 1, maxDurationWeeks);
            RewardPerUnit = Mathf.Max(0f, rewardPerUnit);
            Penalty = type == ContractType.Exclusive ? Mathf.Max(0f, penalty) : 0f;
            RequiredReputation = Mathf.Max(0, requiredReputation);
            ClassName = className ?? "";
        } 

        public Contract(ContractState state)
        {
            if (state == null)
            {
                Debug.LogError("Contract: restore constructor got null state.");
                Requirement = new ContractRequirement();
                return;
            }

            ContractId = state.ContractId;
            Type = state.Type;
            Requirement = state.Requirement ?? new ContractRequirement();
            CreatedWeek = state.CreatedWeek;
            DurationWeeks = state.DurationWeeks;
            RewardPerUnit = state.RewardPerUnit;
            Penalty = state.Penalty;
            // Saves from before contract classes have neither field: 0 / null → a standard contract.
            RequiredReputation = Mathf.Max(0, state.RequiredReputation);
            ClassName = state.ClassName ?? "";
            Status = state.Status;
            DeliveredQuantity = state.DeliveredQuantity;
            ClaimedByTraderId = state.ClaimedByTraderId;
        }


        public float ReferenceValue(Market.Market market) => Requirement.AverageReferenceUnitPrice(market) * RemainingQuantity;

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