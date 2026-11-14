using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Market;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static AntiqueTradingSimulator.Market.AntiqueEnums;
using UnityEngine.Serialization;

namespace AntiqueTradingSimulator.Contracts
{
    /// <summary>
    /// Variables of a contract that are predefined by the caller instead of rolled randomly. 
    /// All variables left as null will be rolled randomly the same as in standard contract generation
    /// </summary>
    public class ContractOverrides
    {
        public ContractAttributeScope? Scope;

        /// <summary>
        /// Every attribute the requirement must use (e.g. type + century). Each one needs its value set
        /// below — or is rolled among the values that keep at least one type matching. Null = the primary
        /// Scope, plus a randomly rolled second attribute as for any generated contract.
        /// </summary>
        public ContractAttributes? Attributes;

        public AntiqueType? AntiqueType;

        public Country? Country;

        public Century? Century;

        public int? Quantity;

        public float? MinCondition;

        /// <summary>Lowest market value (£) per antique; the reward is priced from it unless RewardPerUnit is set too.</summary>
        public float? MinValue;

        public int? DurationWeeks;

        public ContractType? Type;

        public float? RewardPerUnit;

        public float? Penalty;

        public int? RequiredReputation;

        public string ClassName;
    }

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

        [Tooltip("Range the required minimum condition is rolled from. The reward is based on the value of an antique in exactly that condition.")]
        public ConditionTier MinTier = ConditionTier.Fair;
        public ConditionTier MaxTier = ConditionTier.Excellent;

        [Tooltip("Which matching antique types the client may want, by price: the types are sorted from cheapest (0) " +
                 "to dearest (1) and one is drawn from this range. Its value in the required condition becomes the " +
                 "contract's minimum value per antique — and the base of the reward.")]
        [Range(0f, 1f)] public float MinValueRank = 0f;
        [Range(0f, 1f)] public float MaxValueRank = 1f;

        public ContractClassDefinition() { }

        public ContractClassDefinition(string name, string clientDescription, int requiredReputation, float weight,
            float minRewardMultiplier, float maxRewardMultiplier, int minQuantity, int maxQuantity, ConditionTier minTier, ConditionTier maxTier,
            float minValueRank = 0f, float maxValueRank = 1f)
        {
            MinValueRank = minValueRank;
            MaxValueRank = maxValueRank;
            MinTier = minTier;
            MaxTier = maxTier;
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
        [Tooltip("Transport times decide the shortest deadline a contract can have. Auto-found if empty.")]
        [SerializeField] private Logistics.TransportManager transportManager;

        [Header("Generation")]
        [FormerlySerializedAs("contractsGeneratedPerDay")]
        [SerializeField] private int contractsGeneratedPerWeek = 1;
        [SerializeField] private int maxActiveContracts = 15;
        [SerializeField] private int initialContractCount = 5;

        [SerializeField] private int minQuantity = 1;
        [SerializeField] private int maxQuantity = 5;

        [FormerlySerializedAs("minDurationDays")]
        [SerializeField] private int minDurationWeeks = 1;
        [FormerlySerializedAs("maxDurationDays")]
        [SerializeField] private int maxDurationWeeks = 10;

        [Header("Requirements and feasibility")]
        [Tooltip("Chance a generated contract asks for two attributes at once (e.g. Porcelain · 18th century).")]
        [Range(0f, 1f)] [SerializeField] private float pairedRequirementChance = 0.35f;

        [Tooltip("A pair is only used when at least this many antique types match both attributes.")]
        [Min(1)] [SerializeField] private int minTypesForPair = 2;

        [Tooltip("Share of the market's expected qualifying supply before the deadline that a contract may ask for. " +
                 "The estimate assumes nobody else buys and everything ships by Express, so keep it well below 1.")]
        [Range(0.1f, 1f)] [SerializeField] private float supplyShareForContract = 0.5f;

        [Tooltip("Rolls tried before giving up on generating a contract this time (e.g. when the market is nearly empty).")]
        [Min(1)] [SerializeField] private int maxGenerationAttempts = 8;

        [Header("Client demand")]
        [Tooltip("Demand an open contract adds per antique it still asks for, spread evenly over the antique types " +
                 "that can satisfy it (base demand of a type is 5, so 0.25 on one type ≈ +5% price).")]
        [Min(0f)] [SerializeField] private float demandPerRequestedPiece = 0.25f;

        [Tooltip("Most demand all open contracts together may add to one antique type (1.5 ≈ +30% price, like an exhibition).")]
        [Min(0f)] [SerializeField] private float maxContractDemandPerType = 1.5f;

        [Header("Compensation")]
        [SerializeField] private float minRewardMultiplier = 1.1f;
        [SerializeField] private float maxRewardMultiplier = 1.6f;
        [SerializeField] private float maxUrgencyBonusMultiplier = 1.5f;

        [Header("Condition required")]
        [Tooltip("Range the minimum condition of a standard contract is rolled from. Rewards are based on the value of an antique in exactly that condition.")]
        [SerializeField] private ConditionTier standardMinTier = ConditionTier.Fair;
        [SerializeField] private ConditionTier standardMaxTier = ConditionTier.Excellent;

        [Header("Value required")]
        [Tooltip("Price rank (0 = cheapest matching type, 1 = dearest) of the type a standard contract's minimum " +
                 "value is taken from. The reward is priced from that minimum value.")]
        [Range(0f, 1f)] [SerializeField] private float standardMinValueRank = 0f;
        [Range(0f, 1f)] [SerializeField] private float standardMaxValueRank = 1f;

        [Header("Exclusive contracts")]
        [Range(0f, 1f)]
        [SerializeField] private float exclusiveContractChance = 0.35f;
        [SerializeField] private float exclusivePenaltyFraction = 0.5f;

        [Header("Reputation-gated contract classes")]
        [Tooltip("Relative chance of a standard contract (uses the Generation/Compensation numbers above).")]
        [Min(0f)] [SerializeField] private float standardContractWeight = 3f;

        [Tooltip("Higher classes of contract. Only traders (player or NPC) with enough reputation can take them; " +
                 "the reputation unlock lists are generated from this list.")]
        [SerializeField]
        private List<ContractClassDefinition> reputationClasses = new()
        {
            new ContractClassDefinition("Premium", "Wealthy private collectors", 500, 1.2f, 1.3f, 1.8f, 1, 5,
                ConditionTier.Good, ConditionTier.Excellent, 0.4f, 1f),
            // 1–2 pieces: few listings are both Excellent and of the dearest types, so more is rarely feasible.
            new ContractClassDefinition("Prestige", "Museums and foundations", 1500, 0.8f, 1.6f, 2.2f, 1, 2,
                ConditionTier.Excellent, ConditionTier.Excellent, 0.7f, 1f),
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

        /// <summary>
        /// A contract was delivered; Contract.FulfilledByTraderId says by whom. Raised before the
        /// fulfilling trader drops its own commitment, so listeners must not assume it's gone yet.
        /// </summary>
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

        public ContractFeed Feed { get; } = new ContractFeed();


        void Awake()
        {
            if (economyManager == null) economyManager = FindFirstObjectByType<EconomyManager>();
            if (timeManager == null) timeManager = FindFirstObjectByType<Core.TimeManager>();
            if (transportManager == null) transportManager = FindFirstObjectByType<Logistics.TransportManager>();
        }

        void OnEnable()
        {
            if (timeManager != null)
            {
                timeManager.OnWeekChanged += HandleWeekChanged;
            }
        }

        void OnDisable()
        {
            if (timeManager != null)
                timeManager.OnWeekChanged -= HandleWeekChanged;
        }

        void Start()
        {
            int count = Mathf.Min(initialContractCount, maxActiveContracts);
            int currentWeek = timeManager != null ? timeManager.CurrentWeek : 1;

            for (int i = 0; i < count; i++)
            {
                GenerateContract(currentWeek);
            }
        }

        private void HandleWeekChanged(int newWeek)
        {
            ExpireContracts(newWeek);
            int activeCount = _contracts.Count(c => c.Status == ContractStatus.Active);
            int newContractCount = Mathf.Min(contractsGeneratedPerWeek, maxActiveContracts - activeCount);
            for (int i = 0; i < newContractCount; i++)
            {
                GenerateContract(newWeek);
            }

            // Every change above already refreshed it; this only catches up if something was missed
            // (e.g. contracts generated before the market had registered its types). No change = no-op.
            RefreshContractDemand(report: true);
        }

        // ---------------------------------------------------------------- client demand

        private readonly Dictionary<string, float> _demandScratch = new();

        /// <summary>
        /// Sets each antique type's ContractDemandMod from the open contracts: every contract adds
        /// demandPerRequestedPiece × the pieces it still asks for, spread over the types that can satisfy
        /// it, capped per type. A contract is a buyer waiting outside the market, so it moves demand — and
        /// through it prices — the same way any other buyer interest does; it never sets a price.
        /// Increases are reported as market pressure so related categories follow through the dependency
        /// network, except when rebuilding after a load (report = false): that knock-on is already saved.
        /// </summary>
        private void RefreshContractDemand(bool report)
        {
            var market = economyManager != null ? economyManager.Market : null;
            if (market == null) return;

            _demandScratch.Clear();
            foreach (var contract in _contracts)
            {
                if (contract == null || contract.Status != ContractStatus.Active) continue;

                var types = contract.Requirement.MatchingDefinitions();
                if (types.Count == 0) continue;

                float perType = demandPerRequestedPiece * contract.Requirement.Quantity / types.Count;
                foreach (var def in types)
                    _demandScratch[def.Id] = (_demandScratch.TryGetValue(def.Id, out var sum) ? sum : 0f) + perType;
            }

            foreach (var def in AntiqueDatabase.GetAll())
            {
                if (def == null) continue;

                var typeState = market.GetTypeState(def.Id);
                if (typeState == null) continue;

                float target = _demandScratch.TryGetValue(def.Id, out var wanted)
                    ? Mathf.Min(wanted, maxContractDemandPerType)
                    : 0f;

                float delta = target - typeState.ContractDemandMod;
                if (Mathf.Abs(delta) < 0.0001f) continue;

                typeState.ContractDemandMod = target;

                // Only new interest is reported: the dependency network fades its knock-on by itself, so
                // reporting the drop when a client leaves would push related categories below normal.
                if (report && delta > 0f) market.ReportPressure(def.Id, delta, 0f);
                market.RecalculatePricesForDefinition(def.Id);
            }
        }

        /// <summary>
        /// Expires only contracts that are still Active and have actually reached their
        /// deadline, applies the exclusive-contract penalty (and frees any antiques the
        /// claiming trader had reserved for it), then drops them from the active list.
        /// </summary>
        private void ExpireContracts(int currentWeek)
        {
            var toExpire = _contracts.Where(c => c.Status == ContractStatus.Active && c.DeadlineWeek <= currentWeek).ToList();

            foreach (var contract in toExpire)
            {
                contract.SetStatusExpired();
                float penaltyPaid = ApplyExpiryConsequences(contract);
                _contracts.Remove(contract);

                RecordExpiry(contract, penaltyPaid, currentWeek);

                Debug.Log($"ContractManager: contract {contract.ContractId} expired unfulfilled on week {currentWeek}.");
                OnContractExpired?.Invoke(contract);
            }

            // The clients who gave up stop waiting for antiques.
            if (toExpire.Count > 0) RefreshContractDemand(report: true);
        }

        /// <summary>
        /// History entry for a contract that ran out: "failed" for whoever had committed to it (the claimant
        /// of an exclusive one, anyone who accepted an open one – before OnContractExpired lets them drop
        /// it), otherwise "expired".
        /// </summary>
        private void RecordExpiry(Contract contract, float penaltyPaid, int week)
        {
            if (contract.IsClaimed)
            {
                Feed.AddFailed(contract, contract.ClaimedByTraderId, penaltyPaid, week);
                return;
            }

            bool anyoneCommitted = false;
            foreach (var pair in _inventoriesByTraderId)
            {
                if (pair.Value == null || !pair.Value.IsCommittedToContract(contract.ContractId))
                    continue;

                Feed.AddFailed(contract, pair.Key, 0f, week);
                anyoneCommitted = true;
            }

            if (!anyoneCommitted)
                Feed.AddExpired(contract, week);
        }

        /// <summary>
        /// Applies the cash penalty and releases reserved antiques for a contract that
        /// expired without being fulfilled. Only Exclusive contracts carry a penalty, and
        /// only ones that were actually claimed have a known trader/inventory to apply it to
        /// — Open contracts are never formally claimed, so any NPC informally pursuing one
        /// is responsible for releasing its own reservations once it notices the contract
        /// is no longer Active.
        /// </summary>
        /// <returns>The penalty actually charged (0 when none).</returns>
        private float ApplyExpiryConsequences(Contract contract)
        {
            if (!contract.IsClaimed) return 0f;
            if (!_inventoriesByTraderId.TryGetValue(contract.ClaimedByTraderId, out var inventory)) return 0f;

            inventory.ReleaseAllReservationsForContract(contract.ContractId);

            if (contract.Penalty > 0f)
            {
                inventory.RemoveCash(contract.Penalty, LedgerCategory.Penalty, $"Penalty for failed contract ({contract.Requirement})");
                Debug.Log($"ContractManager: {contract.ClaimedByTraderId} incurred a {contract.Penalty:F2} penalty for failing exclusive contract {contract.ContractId}.");
                return contract.Penalty;
            }

            return 0f;
        }

        public Contract GenerateContract(int currentWeek) => GenerateContract(currentWeek, null);

        public Contract GenerateContract(int currentWeek, ContractOverrides overrides) =>
            Generate(currentWeek, overrides, forceStandard: false);

        /// <param name="forceStandard">Skip the class roll — the retry after a higher class proved infeasible.</param>
        private Contract Generate(int currentWeek, ContractOverrides overrides, bool forceStandard)
        {
            var market = economyManager != null ? economyManager.Market : null;
            if (market == null) return null;

            var contractClass = forceStandard ? null : ResolveContractClass(overrides); // null = standard
            int minQ = contractClass != null ? contractClass.MinQuantity : minQuantity;
            int maxQ = contractClass != null ? Mathf.Max(contractClass.MinQuantity, contractClass.MaxQuantity) : maxQuantity;
            float minMultiplier = contractClass != null ? contractClass.MinRewardMultiplier : minRewardMultiplier;
            float maxMultiplier = contractClass != null ? Mathf.Max(contractClass.MinRewardMultiplier, contractClass.MaxRewardMultiplier) : maxRewardMultiplier;
            float minRank = contractClass != null ? contractClass.MinValueRank : standardMinValueRank;
            float maxRank = contractClass != null ? contractClass.MaxValueRank : standardMaxValueRank;

            int? fixedQuantity = overrides?.Quantity is int q ? Mathf.Max(1, q) : (int?)null;
            int neededQuantity = fixedQuantity ?? minQ;
            int shortestDuration = Mathf.Max(minDurationWeeks, ShortestFeasibleDuration);

            // Roll whole candidates (attributes, condition, value floor, deadline) until one the market can
            // actually supply comes up. Contracts nobody could fulfil are never put on the board.
            ContractRequirement requirement = null;
            int duration = 0;
            float supportable = -1f; // qualifying antiques the contract may count on; -1 = not estimated
            ContractRequirement fallback = null;
            int fallbackDuration = 0;

            for (int attempt = 0; attempt < maxGenerationAttempts; attempt++)
            {
                var candidate = PickRequirement(overrides);
                if (candidate == null) continue;

                candidate.MinCondition = overrides?.MinCondition is float fixedCondition
                    ? Antique.QuantizeCondition(fixedCondition)
                    : contractClass != null
                        ? RollRequiredCondition(contractClass.MinTier, contractClass.MaxTier, contractClass.Name)
                        : RollRequiredCondition(standardMinTier, standardMaxTier, null);

                candidate.MinValue = overrides?.MinValue is float fixedValue
                    ? Mathf.Max(0f, fixedValue)
                    : RollMinValue(candidate, market, minRank, maxRank);

                // No matching type has a market price yet — nothing to price the contract from.
                if (!candidate.HasMinValue) continue;

                int candidateDuration = overrides?.DurationWeeks is int fixedDuration
                    ? Mathf.Max(1, fixedDuration)
                    : UnityEngine.Random.Range(shortestDuration, Mathf.Max(shortestDuration, maxDurationWeeks) + 1);

                float expected = ContractFeasibility.ExpectedSupply(candidate, market, currentWeek,
                    currentWeek + candidateDuration, economyManager.ExpectedNewListingsPerWeek, ExpressWeeks);
                float candidateSupportable = expected * supplyShareForContract;

                if (candidateSupportable >= neededQuantity)
                {
                    requirement = candidate;
                    duration = candidateDuration;
                    supportable = candidateSupportable;
                    break;
                }

                if (fallback == null)
                {
                    fallback = candidate;
                    fallbackDuration = candidateDuration;
                }
            }

            if (requirement == null)
            {
                // A higher class the market can't supply right now (Prestige asks for rare pieces):
                // the client doesn't show up this week, a standard one does.
                if (overrides == null && contractClass != null && !forceStandard)
                {
                    Debug.Log($"ContractManager: no feasible {contractClass.Name} contract on week {currentWeek} — trying a standard one.");
                    return Generate(currentWeek, null, forceStandard: true);
                }

                // Randomly generated: skip this time, the market may offer more next week.
                // Asked for by an event: the designer's call — create it, but say it may be impossible.
                if (overrides == null || fallback == null)
                {
                    Debug.Log($"ContractManager: no feasible contract found in {maxGenerationAttempts} attempts on week {currentWeek} — none generated.");
                    return null;
                }

                Debug.LogWarning($"ContractManager: the market is unlikely to supply {neededQuantity}x {fallback.AttributesLabel()} in time — creating the requested contract anyway.");
                requirement = fallback;
                duration = fallbackDuration;
            }

            // Never ask for more than the market should offer; a class's minimum is guaranteed by the check above.
            int maxFeasibleQ = supportable >= 0f ? Mathf.Max(minQ, Mathf.FloorToInt(supportable)) : maxQ;
            requirement.Quantity = fixedQuantity ?? UnityEngine.Random.Range(minQ, Mathf.Min(maxQ, maxFeasibleQ) + 1);

            // Priced from the value floor: the cheapest antique that qualifies is worth about MinValue,
            // so the multiplier really is the client's premium over the market. A dearer antique is the
            // supplier's choice; a cheaper one doesn't count.
            float baseMultiplier = UnityEngine.Random.Range(minMultiplier, maxMultiplier);
            float urgencyFactor = UrgencyFactor(duration);
            float rewardPerUnit = overrides?.RewardPerUnit is float fixedReward
                ? Mathf.Max(0f, fixedReward)
                : requirement.MinValue * baseMultiplier * urgencyFactor;

            ContractType type = overrides?.Type
                ?? (UnityEngine.Random.value < exclusiveContractChance ? ContractType.Exclusive : ContractType.Open);
            float penalty = overrides?.Penalty is float fixedPenalty
                ? Mathf.Max(0f, fixedPenalty)
                : (type == ContractType.Exclusive
                    ? rewardPerUnit * requirement.Quantity * exclusivePenaltyFraction
                    : 0f);

            int durationCap = Mathf.Max(maxDurationWeeks, duration);

            var contract = new Contract(type, requirement, currentWeek, duration, durationCap, rewardPerUnit, penalty,
                overrides?.RequiredReputation ?? (contractClass != null ? contractClass.RequiredReputation : 0),
                contractClass != null ? contractClass.Name : "");

            _contracts.Add(contract);
            _contractsById[contract.ContractId] = contract;

            // The new client starts looking for antiques: demand for the matching types rises.
            RefreshContractDemand(report: true);

            Debug.Log($"ContractManager: generated {contract.Type} contract ({contract.ContractId}) on week {currentWeek} for {requirement.Quantity} antiques worth at least {requirement.MinValue:F0} each, to be fulfilled by week {contract.DeadlineWeek} with total payout {contract.TotalReward:F0}");
            OnContractCreated?.Invoke(contract);
            Feed.AddListed(contract, currentWeek);
            return contract;
        }

        private ContractClassDefinition ResolveContractClass(ContractOverrides overrides)
        {
            string name = overrides?.ClassName;
            if (name == null) return PickContractClass();
            if (name.Length == 0) return null; // explicitly standard

            var named = reputationClasses?.FirstOrDefault(c =>
                c != null && string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase));

            if (named == null)
                Debug.LogWarning($"ContractManager: no contract class named '{name}' — generating a standard contract instead.");

            return named;
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

        /// <summary>
        /// Rolls what the client asks for: a primary attribute and its value, then — with
        /// pairedRequirementChance, or always when overrides.Attributes asks for more — a second attribute.
        /// A rolled second value always narrows the choice but keeps at least minTypesForPair types matching.
        /// Values fixed in the overrides are used as they are. Null when nothing matches.
        /// </summary>
        private ContractRequirement PickRequirement(ContractOverrides overrides = null)
        {
            var all = AntiqueDatabase.GetAll();
            if (all == null || all.Count == 0) return null;

            var primary = overrides?.Scope
                ?? (ContractAttributeScope)UnityEngine.Random.Range(0, Enum.GetValues(typeof(ContractAttributeScope)).Length);

            var requirement = new ContractRequirement { Scope = primary, Attributes = primary.ToFlag() };
            if (!AssignValue(requirement, primary, overrides, all)) return null;

            if (overrides?.Attributes is ContractAttributes requested && requested.AttributeCount() > 1)
            {
                foreach (var scope in ContractAttributesExtensions.DisplayOrder)
                {
                    if (scope == primary || !requested.Includes(scope)) continue;

                    // Unset values are rolled among the types still matching, so the result is never empty.
                    if (!AssignValue(requirement, scope, overrides, requirement.MatchingDefinitions())) return null;
                    requirement.Attributes |= scope.ToFlag();
                }

                return requirement.MatchingDefinitions().Count > 0 ? requirement : null;
            }

            if (overrides?.Attributes == null && UnityEngine.Random.value < pairedRequirementChance)
                TryAddSecondAttribute(requirement);

            return requirement;
        }

        /// <summary>
        /// Narrows the requirement by a second attribute: picks one value, among those shared by at least
        /// minTypesForPair of the types matching so far, that leaves out at least one of them (a second
        /// attribute every matching type already has would only lengthen the name). Leaves it single otherwise.
        /// </summary>
        private void TryAddSecondAttribute(ContractRequirement requirement)
        {
            var matching = requirement.MatchingDefinitions();
            if (matching.Count < Mathf.Max(2, minTypesForPair)) return;

            var options = new List<(ContractAttributeScope Scope, int Value)>();
            foreach (var scope in ContractAttributesExtensions.DisplayOrder)
            {
                if (requirement.Uses(scope)) continue;

                foreach (var group in matching.GroupBy(def => ValueOf(def, scope)))
                {
                    if (IsPlaceholder(scope, group.Key)) continue;
                    int count = group.Count();
                    if (count >= minTypesForPair && count < matching.Count)
                        options.Add((scope, group.Key));
                }
            }

            if (options.Count == 0) return;

            var (chosenScope, chosenValue) = options[UnityEngine.Random.Range(0, options.Count)];
            SetValue(requirement, chosenScope, chosenValue);
            requirement.Attributes |= chosenScope.ToFlag();
        }

        /// <summary>
        /// Sets the requirement's value for one attribute: the one fixed in the overrides, or a random one
        /// among the values the given types have (placeholders like "Other" only if nothing else is there).
        /// </summary>
        private static bool AssignValue(ContractRequirement requirement, ContractAttributeScope scope,
            ContractOverrides overrides, List<AntiqueDefinition> pool)
        {
            int? fixedValue = scope switch
            {
                ContractAttributeScope.AntiqueType => overrides?.AntiqueType is AntiqueType t ? (int)t : (int?)null,
                ContractAttributeScope.Country => overrides?.Country is Country c ? (int)c : (int?)null,
                ContractAttributeScope.Century => overrides?.Century is Century y ? (int)y : (int?)null,
                _ => null
            };

            if (fixedValue.HasValue)
            {
                SetValue(requirement, scope, fixedValue.Value);
                return true;
            }

            if (pool == null || pool.Count == 0) return false;

            var values = pool.Where(def => def != null).Select(def => ValueOf(def, scope)).Distinct().ToList();
            var real = values.Where(v => !IsPlaceholder(scope, v)).ToList();
            if (real.Count > 0) values = real;
            if (values.Count == 0) return false;

            SetValue(requirement, scope, values[UnityEngine.Random.Range(0, values.Count)]);
            return true;
        }

        private static int ValueOf(AntiqueDefinition def, ContractAttributeScope scope) => scope switch
        {
            ContractAttributeScope.AntiqueType => (int)def.Type,
            ContractAttributeScope.Country => (int)def.Country,
            ContractAttributeScope.Century => (int)def.Century,
            _ => 0
        };

        private static void SetValue(ContractRequirement requirement, ContractAttributeScope scope, int value)
        {
            switch (scope)
            {
                case ContractAttributeScope.AntiqueType: requirement.AntiqueType = (AntiqueType)value; break;
                case ContractAttributeScope.Country: requirement.Country = (Country)value; break;
                case ContractAttributeScope.Century: requirement.Century = (Century)value; break;
            }
        }

        /// <summary>"Other" type/country and "Unknown" century — catch-alls a client wouldn't ask for by name.</summary>
        private static bool IsPlaceholder(ContractAttributeScope scope, int value) => scope switch
        {
            ContractAttributeScope.AntiqueType => value == (int)AntiqueType.Other,
            ContractAttributeScope.Country => value == (int)Country.Other,
            ContractAttributeScope.Century => value == (int)Century.Unknown,
            _ => false
        };

        /// <summary>Minimum condition required for the contract.</summary>
        private static float RollRequiredCondition(ConditionTier minTier, ConditionTier maxTier, string className)
        {
            // A class saved before these fields existed can come back Unset. Fall back to the full
            // range of tiers rather than refusing to generate, and say so once so it gets fixed.
            if (!minTier.IsSet() && !maxTier.IsSet())
            {
                if (_warnedUnsetTiers.Add(className ?? string.Empty))
                    Debug.LogWarning($"ContractManager: the {className ?? "standard"} contract class has no quality tiers set. Default values used.");

                minTier = ConditionTiers.Lowest;
                maxTier = ConditionTiers.Highest;
            }

            return Antique.QuantizeCondition(ConditionTiers.Random(minTier, maxTier).Threshold());
        }

        private static readonly HashSet<string> _warnedUnsetTiers = new();

        /// <summary>
        /// The kind of object the client wants: the matching types are sorted by current reference
        /// price, one is drawn from the [minRank, maxRank] slice (0 = cheapest, 1 = dearest), and the
        /// floor is what that type is worth in exactly the required condition. Rounded down, so an
        /// average specimen of that type qualifies. 0 when no matching type has a price.
        /// </summary>
        private static float RollMinValue(ContractRequirement requirement, Market.Market market, float minRank, float maxRank)
        {
            if (requirement == null || market == null) return 0f;

            var prices = new List<float>();
            foreach (var def in requirement.MatchingDefinitions())
            {
                if (def == null || def.BasePrice <= 0f) continue;

                float price = PriceEngine.CalculateReferencePrice(def.BasePrice, market.GetTypeState(def.Id));
                if (price > 0f) prices.Add(price);
            }

            if (prices.Count == 0) return 0f;
            prices.Sort();

            float lo = Mathf.Clamp01(Mathf.Min(minRank, maxRank));
            float hi = Mathf.Clamp01(Mathf.Max(minRank, maxRank));
            int last = prices.Count - 1;
            int from = Mathf.FloorToInt(lo * last);
            int to = Mathf.CeilToInt(hi * last);

            float reference = prices[UnityEngine.Random.Range(from, to + 1)];
            float condition = requirement.HasMinCondition ? requirement.MinCondition : ConditionTiers.Lowest.Threshold();

            return RoundDownToNice(reference * condition);
        }

        /// <summary>537 → 500 (steps of 10 below 200 £, 50 below 2 000 £, 100 above).</summary>
        private static float RoundDownToNice(float value)
        {
            float step = value < 200f ? 10f : value < 2000f ? 50f : 100f;
            if (value < step) return Mathf.Max(1f, Mathf.Floor(value));
            return Mathf.Floor(value / step) * step;
        }

        /// <summary>
        /// Full bonus for the shortest deadline a purchase can still make, none for the longest. Measured
        /// from the shortest feasible deadline, not from 1 week: a deadline nobody can meet by buying
        /// isn't "urgent", it's impossible — and is no longer generated.
        /// </summary>
        private float UrgencyFactor(int duration)
        {
            int shortest = Mathf.Max(minDurationWeeks, ShortestFeasibleDuration);
            int span = Mathf.Max(1, maxDurationWeeks - shortest);
            float t = Mathf.Clamp01((float)(duration - shortest) / span);
            return Mathf.Lerp(maxUrgencyBonusMultiplier, 1f, t);
        }

        /// <summary>Base Express transit time from a zone, in weeks (1 week if transport isn't set up).</summary>
        private int ExpressWeeks(Logistics.ShippingZone zone) =>
            transportManager != null && transportManager.Settings != null
                ? transportManager.Settings.GetBaseDurationWeeks(zone, Logistics.TransportOption.Express)
                : 1;

        /// <summary>
        /// The shortest deadline a contract can have and still be met by buying: an item bought on the
        /// first week must arrive (local Express) before the week the contract expires.
        /// </summary>
        public int ShortestFeasibleDuration => ExpressWeeks(Logistics.ShippingZone.Local) + 1;

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

                if (listing.IsInRestoration)
                {
                    Debug.LogWarning($"ContractManager: listing {listingId} is being restored (ready week {listing.RestorationEndWeek}) — it can be handed in once the work is done.");
                    return false;
                }

                if (listing.IsInTransit)
                {
                    Debug.LogWarning($"ContractManager: listing {listingId} is still in transit (arrives week {listing.ArrivalWeek}) — it can be handed in once delivered.");
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

            var market = economyManager != null ? economyManager.Market : null;

            foreach (var listingId in listingIdList)
            {
                // The antiques leave the game with the client — a sale the market hears about.
                if (market != null) market.RecordContractDelivery(inventory.GetHolding(listingId), traderId, contract.RewardPerUnit);
                inventory.RemoveHolding(listingId);
            }

            contract.SetStatusFulfilled(traderId);
            inventory.AddCash(contract.TotalReward, LedgerCategory.Contract, $"Contract payment ({contract.Requirement})");

            inventory.ReleaseAllReservationsForContract(contract.ContractId);

            // Leaves the board like an expired one; stays resolvable by Id in _contractsById.
            _contracts.Remove(contract);

            // The client got what they wanted and stops waiting: their demand goes.
            RefreshContractDemand(report: true);

            Debug.Log($"ContractManager: {traderId} fulfilled {contract.Type} contract {contract.ContractId} in full for {contract.TotalReward:F2} on week {CurrentWeek}.");
            OnContractFulfilled?.Invoke(contract);
            Feed.AddFulfilled(contract, traderId, CurrentWeek);

            return true;
        }
        public bool ClaimContract(string contractId, string traderId)
        {
            var contract = GetById(contractId);
            if (contract == null || string.IsNullOrEmpty(traderId)) return false;

            if (!contract.TryClaim(traderId)) return false;

            Debug.Log($"ContractManager: contract {contract.ContractId} claimed by {traderId}.");
            OnContractClaimed?.Invoke(contract, traderId);
            Feed.AddTakenOn(contract, traderId, CurrentWeek);
            return true;
        }

        /// <summary>
        /// Notes in the contract history that a trader committed to an open contract. Open contracts are
        /// never claimed (anyone may deliver), so ClaimContract doesn't see these – the player's accept does.
        /// </summary>
        public void RecordTakenOn(Contract contract, string traderId)
        {
            if (contract == null || string.IsNullOrEmpty(traderId)) return;
            Feed.AddTakenOn(contract, traderId, CurrentWeek);
        }

        private int CurrentWeek => timeManager != null ? timeManager.CurrentWeek : 1;

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

            state.FeedEntries = Feed.CaptureEntries();
            state.FeedPlayerTotals = Feed.CaptureTotals();

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
                Feed.Clear();
                RefreshContractDemand(report: false);
                OnContractsRestored?.Invoke();
                return;
            }

            Feed.RestoreState(state.FeedEntries, state.FeedPlayerTotals);

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

                    // Older saves kept fulfilled contracts on the board; only active ones belong there.
                    if (contract.Status != ContractStatus.Active) continue;

                    _contracts.Add(contract);
                }
            }

            Debug.Log($"ContractManager: restored {_contracts.Count} listed contract(s) out of {_contractsById.Count} known.");

            // Client demand isn't saved: rebuild it from the restored board (its knock-on through the
            // dependency network is part of the saved dependency state, so it isn't reported again).
            RefreshContractDemand(report: false);

            OnContractsRestored?.Invoke();
        }

    }
}