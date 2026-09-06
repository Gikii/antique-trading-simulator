using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Market;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AntiqueTradingSimulator.Orders
{
    public class OrderManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private EconomyManager economyManager;
        [SerializeField] private Core.TimeManager timeManager;

        [Header("Generation")]
        [SerializeField] private int ordersGeneratedPerDay = 1;
        [SerializeField] private int maxActiveOrders = 15;

        [SerializeField] private int minQuantity = 1;
        [SerializeField] private int maxQuantity = 5;

        [SerializeField] private int minDurationDays = 1;
        [SerializeField] private int maxDurationDays = 10;

        [Header("Compensation")]
        [SerializeField] private float minRewardMultiplier = 1.1f;
        [SerializeField] private float maxRewardMultiplier = 1.6f;
        [SerializeField] private float maxUrgencyBonusMultiplier = 1.5f;

        [Header("Exclusive orders")]
        [Range(0f, 1f)]
        [SerializeField] private float exclusiveOrderChance = 0.35f;
        [SerializeField] private float exclusivePenaltyFraction = 0.5f;

        private readonly List<Order> _orders = new();
        private readonly Dictionary<string, Order> _ordersById = new();

        public IReadOnlyList<Order> AllOrders => _orders;
        public List<Order> ActiveOrders => _orders.Where(o => o.Status == OrderStatus.Active).ToList();
        public List<Order> OpenOrders => _orders.Where(o => o.Type == OrderType.Open && o.Status == OrderStatus.Active).ToList();
        public List<Order> ExclusiveOrders => _orders.Where(o => o.Type == OrderType.Exclusive && o.Status == OrderStatus.Active).ToList();

        private readonly Dictionary<string, TraderInventory> _inventoriesByTraderId = new();

        public event Action<Order> OnOrderCreated;
        public event Action<Order, string> OnOrderClaimed;
        public event Action<Order> OnOrderFulfilled;
        public event Action<Order> OnOrderExpired;


        void Awake()
        {
            if (economyManager == null) economyManager = FindFirstObjectByType<EconomyManager>();
            if (timeManager == null) timeManager = FindFirstObjectByType<Core.TimeManager>();
        }

        void OnEnable()
        {
            if (timeManager != null)
            {
                timeManager.OnDayChanged += HandleDayChanged;
            }

        }

        private void HandleDayChanged(int newDay)
        {
            ExpireOrders();
            int newOrderCount = Mathf.Min(ordersGeneratedPerDay, maxActiveOrders - _orders.Count);
            for (int i = 0; i < newOrderCount; i++)
            {
                GenerateOrder(newDay);
            }
        }

        private void ExpireOrders()
        {
            foreach(var order in _orders.ToList())
            {
                if(order.DeadlineDay == timeManager.CurrentDay) {
                    order.SetStatusExpired();
                }
                _orders.Remove(order);
                OnOrderExpired?.Invoke(order);
            }
        }

        public Order GenerateOrder(int currentDay)
        {
            var market = economyManager != null ? economyManager.Market : null;
            if (market == null) return null;

            var requirement = PickRequirement();
            if (requirement == null) return null;

            var matchingDefs = requirement.MatchingDefinitions();
            float avgReferencePrice = AverageReferencePrice(matchingDefs, market);
            if (avgReferencePrice <= 0f) return null;

            requirement.Quantity = UnityEngine.Random.Range(minQuantity, maxQuantity + 1);
            int duration = UnityEngine.Random.Range(minDurationDays, maxDurationDays + 1);

            float baseMultiplier = UnityEngine.Random.Range(minRewardMultiplier, maxRewardMultiplier);
            float urgencyFactor = UrgencyFactor(duration);
            float rewardPerUnit = avgReferencePrice * baseMultiplier * urgencyFactor;

            OrderType type = UnityEngine.Random.value < exclusiveOrderChance ? OrderType.Exclusive : OrderType.Open;
            float penalty = type == OrderType.Exclusive
                ? rewardPerUnit * requirement.Quantity * exclusivePenaltyFraction
                : 0f;

            var order = new Order(type, requirement, currentDay, duration, maxDurationDays, rewardPerUnit, penalty);
            _orders.Add(order);
            _ordersById[order.OrderId] = order;

            Debug.Log($"OrderManager: generated {order}");
            OnOrderCreated?.Invoke(order);
            return order;
        }

        private OrderRequirement PickRequirement()
        {
            if (AntiqueDatabase.GetAll().Count == 0) return null;

            var scope = (OrderAttributeScope)UnityEngine.Random.Range(0, Enum.GetValues(typeof(OrderAttributeScope)).Length);

            switch (scope)
            {
                case OrderAttributeScope.AntiqueType:
                    var types = AntiqueDatabase.GetAvailableTypes();
                    return new OrderRequirement { Scope = scope, AntiqueType = types[UnityEngine.Random.Range(0, types.Count)] };

                case OrderAttributeScope.Country:
                    var countries = AntiqueDatabase.GetAvailableCountries();
                    return new OrderRequirement { Scope = scope, Country = countries[UnityEngine.Random.Range(0, countries.Count)] };

                case OrderAttributeScope.TimePeriod:
                    var periods = AntiqueDatabase.GetAvailableTimePeriods();
                    return new OrderRequirement { Scope = scope, TimePeriod = periods[UnityEngine.Random.Range(0, periods.Count)] };
                default:
                    return null;
            }
        }

        private static float AverageReferencePrice(List<AntiqueDefinition> definitions, Market.Market market)
        {
            if (definitions == null || definitions.Count == 0) return 0f;

            float total = 0f;
            int counted = 0;

            foreach (var def in definitions)
            {
                if (def == null || def.BasePrice <= 0f) continue;

                var typeState = market.GetTypeState(def.Id);
                total += PriceEngine.CalculateReferencePrice(def.BasePrice, typeState);
                counted++;
            }

            return counted > 0 ? total / counted : 0f;
        }

        private float UrgencyFactor(int duration)
        {
            int span = Mathf.Max(1, maxDurationDays - 1);
            float t = Mathf.Clamp01((float)(duration - 1) / span);
            return Mathf.Lerp(maxUrgencyBonusMultiplier, 1f, t);
        }


        public Order GetById(string orderId)
        {
            _ordersById.TryGetValue(orderId, out var order);
            return order;
        }



        public void RegisterTrader(string traderId, TraderInventory inventory)
        {
            if (string.IsNullOrEmpty(traderId) || inventory == null) return;
            _inventoriesByTraderId[traderId] = inventory;
        }

        public void UnregisterTrader(string traderId) => _inventoriesByTraderId.Remove(traderId);

        public bool FulfillOrder(string orderId, string traderId, TraderInventory inventory, IEnumerable<string> listingIds)
        {
            var listingIdList = listingIds?.Distinct().ToList() ?? new List<string>();
            var order = GetById(orderId);

            if (order == null)
            {
                Debug.LogWarning("OrderManager: order not found.");
                return false;
            }

            if (inventory == null)
            {
                Debug.LogWarning("OrderManager: no inventory provided.");
                return false;
            }

            if (!order.CanBeFulfilledBy(traderId))
            {
                Debug.LogWarning($"OrderManager: order {order.OrderId} cannot be fulfilled by {traderId}.");
                return false;
            }

            if (listingIdList.Count != order.Requirement.Quantity)
            {
                Debug.LogWarning($"OrderManager: order {order.OrderId} requires {order.Requirement.Quantity} antiques. {listingIdList.Count} were offered.");
                return false;
            }


            foreach (var listingId in listingIdList)
            {
                var listing = inventory.GetHolding(listingId);
                if (listing == null)
                {
                    Debug.LogWarning($"OrderManager: listing {listingId} is not owned by {traderId}.");
                    return false;
                }

                if (!order.Requirement.IsSatisfiedBy(listing))
                {
                    Debug.LogWarning($"OrderManager: listing {listingId} does not match order {order.OrderId}'s requirement ({order.Requirement}).");
                    return false;
                }
            }

            foreach (var listingId in listingIdList)
                inventory.RemoveHolding(listingId);

            order.SetStatusFulfilled();

            Debug.Log($"OrderManager: {traderId} fulfilled order {order.OrderId} in full for {order.TotalReward:F2}.");
            OnOrderFulfilled?.Invoke(order);

            return true;
        }
        public bool ClaimOrder(string orderId, string traderId)
        {
            var order = GetById(orderId);
            if (order == null || string.IsNullOrEmpty(traderId)) return false;

            if (!order.TryClaim(traderId)) return false;

            Debug.Log($"OrderManager: order {order.OrderId} claimed by {traderId}.");
            OnOrderClaimed?.Invoke(order, traderId);
            return true;
        }

    }
}