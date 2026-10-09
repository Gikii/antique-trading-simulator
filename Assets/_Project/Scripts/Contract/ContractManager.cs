using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Market;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AntiqueTradingSimulator.Contracts
{
    /// <summary>
    /// A class of contract above the standard one (e.g. Premium, Prestige): offered by clients who
    /// only work with dealers of a given reputation, and who pay more. Reputation gives access to
    /// these clients — it never changes the reward of a contract the player could already take.
    /// </summary>
    [Serializable]
    public class ContractClassDefinition
    {
        [Tooltip("Shown on the contract and in the reputation unlocks, e.g. \"Premium\".")]
        public string Name;

        [Tooltip("Who offers these contracts — shown in the reputation unlocks.")]
        public string ClientDescription;

        [Tooltip("Reputation the player needs to accept these contracts. Should match a reputation tier's minimum.")]
        [Min(0)] public int RequiredReputation;

        [Tooltip("Relative chance of generating this class once the player has unlocked it (standard contracts use Standard Contract Weight).")]
        [Min(0f)] public float Weight = 1f;

        [Min(0f)] public float MinRewardMultiplier = 1.1f;
        [Min(0f)] public float MaxRewardMultiplier = 1.6f;

        [Min(1)] public int MinQuantity = 1;
        [Min(1)] public int MaxQuantity = 5;

        public ContractClassDefinition() { }

        public ContractClassDefinition(string name, string clientDescription, int requiredReputation, float weight,
            float minRewardMultiplier, float maxRewardMultiplier, int minQuantity, int maxQuantity)
        {
            Name = name;
            ClientDescription = clientDescription;
            RequiredReputation = requiredReputation;
            Weight = weight;
            MinRewardMultiplier = minRewardMultiplier;
            MaxRewardMultiplier = maxRewardMultiplier;
            MinQuantity = minQuantity;
            MaxQuantity = maxQuantity;
        }
    }

    public class ContractManager : MonoBehaviour
    {
        [Header("References")]
        [SerializeField] private EconomyManager economyManager;
        [SerializeField] private Core.TimeManager timeManager;

        [Header("Generation")]
        [SerializeField] private int contractsGeneratedPerDay = 1;
        [SerializeField] private int maxActiveContracts = 15;
        [SerializeField] private int initialContractCount = 5;

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

        [Header("Reputation-gated contract classes")]
        [Tooltip("Relative chance of a standard contract (uses the Generation/Compensation numbers above).")]
        [Min(0f)] [SerializeField] private float standardContractWeight = 3f;

        [Tooltip("Higher classes of contract. Only the player can take them, and only with enough reputation; " +
                 "the reputation unlock lists are generated from this list.")]
        [SerializeField] private List<ContractClassDefinition> reputationClasses = new()
        {
            new ContractClassDefinition("Premium", "Wealthy private collectors", 500, 1.2f, 1.3f, 1.8f, 1, 5),
            new ContractClassDefinition("Prestige", "Museums and foundations", 1500, 0.8f, 1.6f, 2.2f, 3, 6),
        };

        [Tooltip("Weight multiplier for the lowest class the player hasn't unlocked yet, so a few locked offers " +
                 "show what reputation leads to. 0 = generate unlocked classes only.")]
        [Range(0f, 1f)] [SerializeField] private float lockedClassPreviewWeight = 0.3f;

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

        public event Action OnContractsRestored;

        public IReadOnlyList<ContractClassDefinition> ReputationClasses => reputationClasses;

        /// <summary>
        /// The player's current reputation — decides which contract classes get generated.
        /// Set by CompanyManager; without it only standard contracts and a preview of the
        /// lowest class appear.
        /// </summary>
        public Func<int> PlayerReputationProvider { get; set; }


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

        void Start()
        {
            int count = Mathf.Min(initialContractCount, maxActiveContracts);
            int currentDay = timeManager != null ? timeManager.CurrentDay : 1;

            for (int i = 0; i < count; i++)
            {
                GenerateContract(currentDay);
            }
        }

        private void HandleDayChanged(int newDay)
        {
            ExpireContracts(newDay);
            int activeCount = _contracts.Count(c => c.Status == ContractStatus.Active);
            int newContractCount = Mathf.Min(contractsGeneratedPerDay, maxActiveContracts - activeCount);
            for (int i = 0; i < newContractCount; i++)
            {
                GenerateContract(newDay);
            }
        }

        /// <summary>
        /// Expires only contracts that are still Active and have actually reached their
        /// deadline, applies the exclusive-contract penalty (and frees any antiques the
        /// claiming trader had reserved for it), then drops them from the active list.
        /// </summary>
        private void ExpireContracts(int currentDay)
        {
            var toExpire = _contracts.Where(c => c.Status == ContractStatus.Active && c.DeadlineDay <= currentDay).ToList();

            foreach (var contract in toExpire)
            {
                contract.SetStatusExpired();
                ApplyExpiryConsequences(contract);
                _contracts.Remove(contract);

                Debug.Log($"ContractManager: contract {contract.ContractId} expired unfulfilled on day {currentDay}.");
                OnContractExpired?.Invoke(contract);
            }
        }

        /// <summary>
        /// Applies the cash penalty and releases reserved antiques for a contract that
        /// expired without being fulfilled. Only Exclusive contracts carry a penalty, and
        /// only ones that were actually claimed have a known trader/inventory to apply it to
        /// — Open contracts are never formally claimed, so any NPC informally pursuing one
        /// is responsible for releasing its own reservations once it notices the contract
        /// is no longer Active.
        /// </summary>
        private void ApplyExpiryConsequences(Contract contract)
        {
            if (!contract.IsClaimed) return;
            if (!_inventoriesByTraderId.TryGetValue(contract.ClaimedByTraderId, out var inventory)) return;

            inventory.ReleaseAllReservationsForContract(contract.ContractId);

            if (contract.Penalty > 0f)
            {
                inventory.RemoveCash(contract.Penalty, LedgerCategory.Penalty, $"Penalty for failed contract ({contract.Requirement})");
                Debug.Log($"ContractManager: {contract.ClaimedByTraderId} incurred a {contract.Penalty:F2} penalty for failing exclusive contract {contract.ContractId}.");
            }
        }

        public Contract GenerateContract(int currentDay)
        {
            var market = economyManager != null ? economyManager.Market : null;
            if (market == null) return null;

            var requirement = PickRequirement();
            if (requirement == null) return null;

            float avgReferencePrice = requirement.AverageReferenceUnitPrice(market);
            if (avgReferencePrice <= 0f) return null;

            var contractClass = PickContractClass(); // null = standard
            int minQ = contractClass != null ? contractClass.MinQuantity : minQuantity;
            int maxQ = contractClass != null ? Mathf.Max(contractClass.MinQuantity, contractClass.MaxQuantity) : maxQuantity;
            float minMultiplier = contractClass != null ? contractClass.MinRewardMultiplier : minRewardMultiplier;
            float maxMultiplier = contractClass != null ? Mathf.Max(contractClass.MinRewardMultiplier, contractClass.MaxRewardMultiplier) : maxRewardMultiplier;

            requirement.Quantity = UnityEngine.Random.Range(minQ, maxQ + 1);
            int duration = UnityEngine.Random.Range(minDurationDays, maxDurationDays + 1);

            float baseMultiplier = UnityEngine.Random.Range(minMultiplier, maxMultiplier);
            float urgencyFactor = UrgencyFactor(duration);
            float rewardPerUnit = avgReferencePrice * baseMultiplier * urgencyFactor;

            ContractType type = UnityEngine.Random.value < exclusiveContractChance ? ContractType.Exclusive : ContractType.Open;
            float penalty = type == ContractType.Exclusive
                ? rewardPerUnit * requirement.Quantity * exclusivePenaltyFraction
                : 0f;

            var contract = new Contract(type, requirement, currentDay, duration, maxDurationDays, rewardPerUnit, penalty,
                contractClass != null ? contractClass.RequiredReputation : 0,
                contractClass != null ? contractClass.Name : "");
            _contracts.Add(contract);
            _contractsById[contract.ContractId] = contract;

            Debug.Log($"ContractManager: generated {contract.Type} contract ({contract.ContractId}) on day {currentDay} for {requirement.Quantity} antiques at average price {requirement.AverageReferenceUnitPrice(economyManager.Market)} to be fullfilled by day {contract.DeadlineDay} with total payout {contract.TotalReward}");
            OnContractCreated?.Invoke(contract);
            return contract;
        }

        /// <summary>
        /// Picks the class of the next contract by weight: standard, every class the player has
        /// unlocked, and the lowest locked class at a reduced weight (a preview). Null = standard.
        /// </summary>
        private ContractClassDefinition PickContractClass()
        {
            if (reputationClasses == null || reputationClasses.Count == 0) return null;

            int reputation = PlayerReputationProvider != null ? PlayerReputationProvider() : 0;

            ContractClassDefinition lowestLocked = null;
            foreach (var c in reputationClasses)
                if (c != null && c.RequiredReputation > reputation &&
                    (lowestLocked == null || c.RequiredReputation < lowestLocked.RequiredReputation))
                    lowestLocked = c;

            float total = standardContractWeight;
            foreach (var c in reputationClasses)
                total += ClassWeight(c, reputation, lowestLocked);

            if (total <= 0f) return null;

            float roll = UnityEngine.Random.value * total;
            if (roll < standardContractWeight) return null;
            roll -= standardContractWeight;

            foreach (var c in reputationClasses)
            {
                float weight = ClassWeight(c, reputation, lowestLocked);
                if (roll < weight) return c;
                roll -= weight;
            }

            return null;
        }

        private float ClassWeight(ContractClassDefinition c, int reputation, ContractClassDefinition lowestLocked)
        {
            if (c == null) return 0f;
            if (c.RequiredReputation <= reputation) return c.Weight;
            return c == lowestLocked ? c.Weight * lockedClassPreviewWeight : 0f;
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
            if (string.IsNullOrEmpty(traderId) || inventory == null)
            {
                Debug.Log($"ContractManager: Trader registration failed.");
                return;
            }
            Debug.Log($"ContractManager: Trader {traderId} registered.");
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

                if (listing.IsListedForSale)
                {
                    Debug.LogWarning($"ContractManager: listing {listingId} is listed on the market — cancel the listing before handing it in.");
                    return false;
                }

                if (listing.IsInTransit)
                {
                    Debug.LogWarning($"ContractManager: listing {listingId} is still in transit (arrives day {listing.ArrivalDay}) — it can be handed in once delivered.");
                    return false;
                }

                if (!contract.Requirement.IsSatisfiedBy(listing))
                {
                    Debug.LogWarning($"ContractManager: listing {listingId} does not match contract {contract.ContractId}'s requirement ({contract.Requirement}).");
                    return false;
                }

                if (listing.IsReservedForContract && listing.ReservedForContractId != contract.ContractId)
                {
                    Debug.LogWarning($"ContractManager: listing {listingId} is reserved for a different contract ({listing.ReservedForContractId}).");
                    return false;
                }
            }

            foreach (var listingId in listingIdList)
                inventory.RemoveHolding(listingId);

            contract.SetStatusFulfilled();
            inventory.AddCash(contract.TotalReward, LedgerCategory.Contract, $"Contract payment ({contract.Requirement})");

            inventory.ReleaseAllReservationsForContract(contract.ContractId);

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

        // ---------------------------------------------------------------- save / load

        /// <summary>
        /// Snapshot of every contract, plus which of them are still on the board. Both are needed:
        /// expired contracts leave the board but stay resolvable by Id, and traders still hold
        /// their Ids (reserved antiques, committed-contract lists).
        /// </summary>
        public ContractsState CaptureState()
        {
            var state = new ContractsState();

            foreach (var contract in _contractsById.Values)
            {
                if (contract == null) continue;
                state.Contracts.Add(ContractState.Capture(contract));
            }

            foreach (var contract in _contracts)
            {
                if (contract == null) continue;
                state.ListedContractIds.Add(contract.ContractId);
            }

            return state;
        }

        /// <summary>
        /// Replaces the contract board with the saved one, discarding the contracts that Start
        /// generated for this session. Traders are not re-registered here — PlayerTrader and the
        /// NPCs do that themselves — so this can run before or after the economy is restored.
        /// </summary>
        public void RestoreState(ContractsState state)
        {
            _contracts.Clear();
            _contractsById.Clear();

            if (state == null)
            {
                OnContractsRestored?.Invoke();
                return;
            }

            if (state.Contracts != null)
            {
                foreach (var contractState in state.Contracts)
                {
                    var contract = contractState?.Restore();
                    if (contract == null || string.IsNullOrEmpty(contract.ContractId)) continue;

                    if (_contractsById.ContainsKey(contract.ContractId))
                    {
                        Debug.LogWarning($"ContractManager: duplicate contract Id '{contract.ContractId}' in the save — keeping the first one.");
                        continue;
                    }

                    _contractsById.Add(contract.ContractId, contract);
                }
            }

            if (state.ListedContractIds != null)
            {
                foreach (var contractId in state.ListedContractIds)
                {
                    if (!_contractsById.TryGetValue(contractId, out var contract))
                    {
                        Debug.LogWarning($"ContractManager: contract '{contractId}' was on the board but is missing from the save's contract list — skipped.");
                        continue;
                    }

                    _contracts.Add(contract);
                }
            }

            Debug.Log($"ContractManager: restored {_contracts.Count} listed contract(s) out of {_contractsById.Count} known.");
            OnContractsRestored?.Invoke();
        }

    }
}