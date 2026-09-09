using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Market;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AntiqueTradingSimulator.Contracts
{
    public class ContractManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private EconomyManager economyManager;
        [SerializeField] private Core.TimeManager timeManager;

        [Header("Generation")]
        [SerializeField] private int contractsGeneratedPerDay = 1;
        [SerializeField] private int maxActiveContracts = 15;

        [SerializeField] private int minQuantity = 1;
        [SerializeField] private int maxQuantity = 5;
        
        [SerializeField] private int minDurationDays = 1;
        [SerializeField] private int maxDurationDays = 10;

        [Header("Compensation")]
        [SerializeField] private float minRewardMultiplier = 1.1f;
        [SerializeField] private float maxRewardMultiplier = 1.6f;
        [SerializeField] private float maxUrgencyBonusMultiplier = 1.5f;

        [Header("Exclusive contracts")]
        [Range(0f, 1f)]
        [SerializeField] private float exclusiveContractChance = 0.35f;
        [SerializeField] private float exclusivePenaltyFraction = 0.5f;

        private readonly List<Contract> _contracts = new();
        private readonly Dictionary<string, Contract> _contractsById = new();

        public IReadOnlyList<Contract> AllContracts => _contracts;
        public List<Contract> ActiveContracts => _contracts.Where(o => o.Status == ContractStatus.Active).ToList();
        public List<Contract> OpenContracts => _contracts.Where(o => o.Type == ContractType.Open && o.Status == ContractStatus.Active).ToList();
        public List<Contract> ExclusiveContracts => _contracts.Where(o => o.Type == ContractType.Exclusive && o.Status == ContractStatus.Active).ToList();

        private readonly Dictionary<string, TraderInventory> _inventoriesByTraderId = new();

        public event Action<Contract> OnContractCreated;
        public event Action<Contract, string> OnContractClaimed;
        public event Action<Contract> OnContractFulfilled;
        public event Action<Contract> OnContractExpired;


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
            ExpireContracts();
            int newContractCount = Mathf.Min(contractsGeneratedPerDay, maxActiveContracts - _contracts.Count);
            for (int i = 0; i < newContractCount; i++)
            {
                GenerateContract(newDay);
            }
        }

        private void ExpireContracts()
        {
            foreach(var contract in _contracts.ToList())
            {
                if(contract.DeadlineDay == timeManager.CurrentDay) {
                    contract.SetStatusExpired();
                }
                _contracts.Remove(contract);
                OnContractExpired?.Invoke(contract);
            }
        }

        public Contract GenerateContract(int currentDay)
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

            ContractType type = UnityEngine.Random.value < exclusiveContractChance ? ContractType.Exclusive : ContractType.Open;
            float penalty = type == ContractType.Exclusive
                ? rewardPerUnit * requirement.Quantity * exclusivePenaltyFraction
                : 0f;

            var contract = new Contract(type, requirement, currentDay, duration, maxDurationDays, rewardPerUnit, penalty);
            _contracts.Add(contract);
            _contractsById[contract.ContractId] = contract;

            Debug.Log($"ContractManager: generated {contract.Type} contract ({contract.ContractId}) on day {currentDay} for {requirement.Quantity} antiques to be fullfilled by day {contract.DeadlineDay}");
            OnContractCreated?.Invoke(contract);
            return contract;
        }

        private ContractRequirement PickRequirement()
        {
            if (AntiqueDatabase.GetAll().Count == 0) return null;

            var scope = (ContractAttributeScope)UnityEngine.Random.Range(0, Enum.GetValues(typeof(ContractAttributeScope)).Length);

            switch (scope)
            {
                case ContractAttributeScope.AntiqueType:
                    var types = AntiqueDatabase.GetAvailableTypes();
                    return new ContractRequirement { Scope = scope, AntiqueType = types[UnityEngine.Random.Range(0, types.Count)] };

                case ContractAttributeScope.Country:
                    var countries = AntiqueDatabase.GetAvailableCountries();
                    return new ContractRequirement { Scope = scope, Country = countries[UnityEngine.Random.Range(0, countries.Count)] };

                case ContractAttributeScope.Century:
                    var centuries = AntiqueDatabase.GetAvailableCenturies();
                    return new ContractRequirement { Scope = scope, Century = centuries[UnityEngine.Random.Range(0, centuries.Count)] };
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


        public Contract GetById(string contractId)
        {
            _contractsById.TryGetValue(contractId, out var contract);
            return contract;
        }



        public void RegisterTrader(string traderId, TraderInventory inventory)
        {
            if (string.IsNullOrEmpty(traderId) || inventory == null) return;
            _inventoriesByTraderId[traderId] = inventory;
        }

        public void UnregisterTrader(string traderId) => _inventoriesByTraderId.Remove(traderId);

        public bool FulfillContract(string contractId, string traderId, TraderInventory inventory, IEnumerable<string> listingIds)
        {
            var listingIdList = listingIds?.Distinct().ToList() ?? new List<string>();
            var contract = GetById(contractId);

            if (contract == null)
            {
                Debug.LogWarning("ContractManager: contract not found.");
                return false;
            }

            if (inventory == null)
            {
                Debug.LogWarning("ContractManager: no inventory provided.");
                return false;
            }

            if (!contract.CanBeFulfilledBy(traderId))
            {
                Debug.LogWarning($"ContractManager: contract {contract.ContractId} cannot be fulfilled by {traderId}.");
                return false;
            }

            if (listingIdList.Count != contract.Requirement.Quantity)
            {
                Debug.LogWarning($"ContractManager: contract {contract.ContractId} requires {contract.Requirement.Quantity} antiques. {listingIdList.Count} were offered.");
                return false;
            }


            foreach (var listingId in listingIdList)
            {
                var listing = inventory.GetHolding(listingId);
                if (listing == null)
                {
                    Debug.LogWarning($"ContractManager: listing {listingId} is not owned by {traderId}.");
                    return false;
                }

                if (!contract.Requirement.IsSatisfiedBy(listing))
                {
                    Debug.LogWarning($"ContractManager: listing {listingId} does not match contract {contract.ContractId}'s requirement ({contract.Requirement}).");
                    return false;
                }
            }

            foreach (var listingId in listingIdList)
                inventory.RemoveHolding(listingId);

            contract.SetStatusFulfilled();

            Debug.Log($"ContractManager: {traderId} fulfilled contract {contract.ContractId} in full for {contract.TotalReward:F2}.");
            OnContractFulfilled?.Invoke(contract);

            return true;
        }
        public bool ClaimContract(string contractId, string traderId)
        {
            var contract = GetById(contractId);
            if (contract == null || string.IsNullOrEmpty(traderId)) return false;

            if (!contract.TryClaim(traderId)) return false;

            Debug.Log($"ContractManager: contract {contract.ContractId} claimed by {traderId}.");
            OnContractClaimed?.Invoke(contract, traderId);
            return true;
        }

    }
}