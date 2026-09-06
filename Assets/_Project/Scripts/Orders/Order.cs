using System;
using UnityEngine;

namespace AntiqueTradingSimulator.Orders
{
    [Serializable]
    public class Order
    {
        public string OrderId { get; }
        public OrderType Type { get; }
        public OrderRequirement Requirement { get; }

        public int CreatedDay { get; }
        public int DurationDays { get; set; }
        public int DeadlineDay => CreatedDay + DurationDays;

        public float RewardPerUnit { get; }
        public float TotalReward => RewardPerUnit * Requirement.Quantity;

        public float Penalty { get; }

        public OrderStatus Status { get; private set; } = OrderStatus.Active;
        public int DeliveredQuantity { get; private set; }
        public int RemainingQuantity => Mathf.Max(0, Requirement.Quantity - DeliveredQuantity);
        public bool IsFulfilled => DeliveredQuantity >= Requirement.Quantity;

        public string ClaimedByTraderId { get; private set; }
        public bool IsClaimed => ClaimedByTraderId != null;
        public bool CanBeClaimed => Type == OrderType.Exclusive && Status == OrderStatus.Active && !IsClaimed;

        public event Action<Order> OnOrderExpired;

        public Order(OrderType type, OrderRequirement requirement, int createdDay, int durationDays, int maxDurationDays, float rewardPerUnit, float penalty)
        {
            OrderId = Guid.NewGuid().ToString("N");
            Type = type;
            Requirement = requirement;
            CreatedDay = createdDay;
            DurationDays = Mathf.Clamp(durationDays, 1, maxDurationDays);
            RewardPerUnit = Mathf.Max(0f, rewardPerUnit);
            Penalty = type == OrderType.Exclusive ? Mathf.Max(0f, penalty) : 0f;
        }

        public bool CanBeFulfilledBy(string traderId)
        {
            if (Status != OrderStatus.Active) return false;
            if (Type == OrderType.Open) return true;
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
            Status = OrderStatus.Fulfilled;
        }
        
        public void SetStatusExpired()
        {
            Status = OrderStatus.Expired;
        }
    }
}
