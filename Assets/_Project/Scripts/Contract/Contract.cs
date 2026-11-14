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

        public string DisplayName => Requirement != null ? Requirement.ToString() : "";


        public int CreatedWeek { get; }
        public int DurationWeeks { get; }
        public int DeadlineWeek => CreatedWeek + DurationWeeks;

        public float RewardPerUnit { get; }
        public float TotalReward => RewardPerUnit * Requirement.Quantity;

        public float Penalty { get; }

        /// <summary>
        /// Reputation a trader needs to accept this contract (0 = anyone). Applies to the player and
        /// to NPCs alike (each has its own TraderRecord). Reputation only gives access to better
        /// clients — it never changes a contract's reward.
        /// </summary>
        public int RequiredReputation { get; }

        /// <summary>Name of the contract's class ("Premium", "Prestige"), empty for a standard contract.</summary>
        public string ClassName { get; }

        public bool RequiresReputation => RequiredReputation > 0;

        public ContractStatus Status { get; private set; } = ContractStatus.Active;

        public string ClaimedByTraderId { get; private set; }
        public bool IsClaimed => ClaimedByTraderId != null;
        public bool CanBeClaimed => Type == ContractType.Exclusive && Status == ContractStatus.Active && !IsClaimed;

        /// <summary>
        /// Who delivered the contract (Antique.PlayerOwnerId or an NPC id); null while it is active
        /// or when it expired. Lets everyone else who was working on an open contract tell that they lost the race.
        /// </summary>
        public string FulfilledByTraderId { get; private set; }

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
            ClaimedByTraderId = state.ClaimedByTraderId;
            // Saves from before this field existed: null (the fulfiller is unknown).
            FulfilledByTraderId = state.FulfilledByTraderId;
        }


        /// <summary>
        /// How much the payout is above the market value of the cheapest antiques that qualify
        /// (MinValue × quantity) — what the contract earns over simply selling them. Null for
        /// contracts without a value floor (older saves).
        /// </summary>
        public float? Premium => Requirement.HasMinValue
            ? TotalReward - Requirement.MinValue * Requirement.Quantity
            : (float?)null;

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

        public void SetStatusFulfilled(string traderId)
        {
            Status = ContractStatus.Fulfilled;
            FulfilledByTraderId = traderId;
        }

        public void SetStatusExpired()
        {
            Status = ContractStatus.Expired;
        }
    }
}