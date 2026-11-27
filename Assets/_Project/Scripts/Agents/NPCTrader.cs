using AntiqueTradingSimulator.Auctions;
using AntiqueTradingSimulator.Company;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Events;
using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.News;
using AntiqueTradingSimulator.Restoration;
using AntiqueTradingSimulator.Rumours;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static AntiqueTradingSimulator.Events.EventEffect;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.Agents
{
    /// <summary>
    /// Runtime instance of an NPC trader — mirrors the Antique/AntiqueDefinition split.
    /// Holds only what's dynamic (cash, holdings, pending reactions) plus a string
    /// ProfileId, never a direct ScriptableObject reference, so instances stay
    /// serializable for save/load. Plain C# on purpose: NPCs have no scene presence.
    /// </summary>
    [Serializable]
    public partial class NPCTrader : IInformationReceiver, IRestorationClient
    {
        public string Id { get; private set; }
        public string ProfileId { get; }
        public string TraderName { get; }
        public TraderInventory Inventory { get; }

        private readonly EconomyManager _economyManager;
        private readonly ContractManager _contractManager;
        private readonly TransportManager _transportManager;
        private readonly RestorationManager _restorationManager;

        /// <summary>The service a trader falls back to when it cannot quote, and the contract default.</summary>
        private const TransportOption NpcTransportOption = TransportOption.Standard;

        private NpcBehaviorProfile _profileCache;
        public NpcBehaviorProfile Profile => _profileCache ??= NpcProfileDatabase.GetById(ProfileId);

        public InfoAccessLevel AccessLevel => Profile != null ? Profile.AccessLevel : InfoAccessLevel.LocalPress;
        public string ReceiverId => Id;

        /// <summary>The decision rules this trader and the telemetry mirrors both read.</summary>
        private static readonly TraderPolicy Policy = TraderPolicy.Default;

        /// <summary>
        /// Most pieces one buying intention may take in a week, so a cheap type with a strong view
        /// cannot be cornered in a single slot.
        /// </summary>
        private const int MaxPiecesPerPosition = 4;

        /// <summary>Kinds of source a trader keeps a standing opinion of; BeliefSource's member count.</summary>
        private const int BeliefSourceCount = 4;

        // The six shared decision constants, read off Policy so the agents, the mirrors and the
        // contract recorder all judge a trade by one set of rules.
        private static float ExclusiveMinProfitShare => Policy.ExclusiveMinProfitShare;
        private static float ContractRiskPremium => Policy.ContractRiskPremium;
        private static float ReserveSafetyMargin => Policy.ReserveSafetyMargin;

        private const float ConditionEpsilon = 0.005f;

        /// <summary>Weeks ahead a trader looks when pricing a listing or valuing a purchase.</summary>
        private const int PricingHorizonWeeks = 8;

        /// <summary>Believed fall at which a trader gets out of a plain holding.</summary>
        private const float BadNewsThreshold = 0.04f;

        /// <summary>Believed rise at which a trader keeps waiting instead of taking its profit.</summary>
        private const float GoodNewsThreshold = 0.04f;

        /// <summary>Weeks after an antique arrives that a planned resale restoration stays worth starting.</summary>
        private const int RestorationLegGraceWeeks = 4;

        /// <summary>Weeks of buying allowance an NPC may save up when its profile does not say.</summary>
        private const int DefaultBudgetAccrualWeeks = 4;

        public float ActivityScale { get; set; } = 1f;

        private readonly struct Offer
        {
            public readonly Antique Listing;
            public readonly TransportOption Option;
            public readonly float Cost;
            public readonly int ArrivalWeek;
            public readonly int DurationWeeks;
            public readonly string RestorationOptionId;
            public readonly float RestorationFee;
            public readonly int RestorationWeeks;

            public Offer(Antique listing, TransportOption option, float cost, int arrivalWeek, int durationWeeks,
                string restorationOptionId = null, float restorationFee = 0f, int restorationWeeks = 0)
            {
                Listing = listing;
                Option = option;
                Cost = cost;
                ArrivalWeek = arrivalWeek;
                DurationWeeks = durationWeeks;
                RestorationOptionId = restorationOptionId;
                RestorationFee = restorationFee;
                RestorationWeeks = restorationWeeks;
            }

            public bool NeedsRestoration => !string.IsNullOrEmpty(RestorationOptionId);
        }

        /// <summary>Everything this trader believes about where prices are going.</summary>
        public TraderWorldView WorldView { get; private set; }

        /// <summary>This week's intentions, built by Review and spent by the action slots.</summary>
        private readonly TraderAgenda _agenda = new();

        /// <summary>
        /// The week's intentions, for the diagnostics. Read on the week change, when the slots are
        /// spent and the next Review has not yet cleared it.
        /// </summary>
        public IReadOnlyList<AgendaItem> Agenda => _agenda.Items;

        /// <summary>The position behind one held antique, or null. Read-only; for the diagnostics.</summary>
        public Position PositionFor(string antiqueId) => PlanFor(antiqueId);

        /// <summary>Every antique this trader holds a position on, for the one-position invariant check.</summary>
        public IEnumerable<string> PositionAntiqueIds => _plans.Keys;

        /// <summary>What is left of the week's allowance, per reason for spending it.</summary>
        private float _speculationBudget;
        private float _bargainBudget;
        private float _restorationBudget;

        /// <summary>Cash the week's committed costs are short of; 0 when there is none.</summary>
        private float _cashShortfall;

        /// <summary>
        /// Types already given a buying intention this week. A set, because the market's listings
        /// are in insertion order rather than grouped by type.
        /// </summary>
        private readonly HashSet<string> _seenDefinitions = new HashSet<string>(System.StringComparer.Ordinal);

        /// <summary>Scratch list for matching a planted rumour's scopes against the holdings.</summary>
        private readonly List<NewsEventData> _scopeScratch = new();

        /// <summary>Set once a week by the review when the profile's roll comes up; spent by a slot.</summary>
        private bool _mayPlantRumour;

        private readonly Dictionary<string, Position> _plans = new();

        private int _planWeek = -1;
        private readonly List<int> _slotTicks = new();
        private float _weekBudget;
        private int _budgetWeek = -1;
        private float _contractReserve;
        private float _planReserve;
        private int _contractSlotsUsed;
        private int _lastObligationTick = -1;
        private int _lastListingRevisionWeek = -1;
        private System.Random _rng;

        private readonly List<Contract> _contractScratch = new();
        private readonly List<Offer> _offerScratch = new();
        private readonly List<string> _planScratch = new();

        /// <summary>Contracts this NPC has taken — kept by its inventory (saved with the economy).</summary>
        public IReadOnlyCollection<string> CommittedContractIds => Inventory.CommittedContractIds;

        /// <summary>
        /// Reputation, credibility, contract counts and daily history — kept by the same rules as
        /// the player's company. Reputation decides which contracts this NPC may take.
        /// </summary>
        public TraderRecord Record { get; }

        /// <param name="reputationSettings">The reputation rules; the player's are passed in so both play by the same ones.</param>
        public NPCTrader(string traderName, string profileId, float startingCash, EconomyManager economyManager,
            ContractManager contractManager = null, TransportManager transportManager = null,
            ReputationSettings reputationSettings = null, RestorationManager restorationManager = null)
        {
            Id = Core.IdFactory.NewId();
            TraderName = traderName;
            ProfileId = profileId;
            Inventory = new TraderInventory(startingCash);
            _economyManager = economyManager;
            _contractManager = contractManager;
            _transportManager = transportManager;
            _restorationManager = restorationManager;

            Record = new TraderRecord(reputationSettings != null
                ? reputationSettings
                : ScriptableObject.CreateInstance<ReputationSettings>());

            // The inventory lives exactly as long as this NPC, so the subscription never needs removing.
            Inventory.Ledger.OnEntryAdded += entry =>
            {
                if (entry.Category == LedgerCategory.Sale) Record.RecordSale(entry.Amount);
            };

            if (_restorationManager != null)
                _restorationManager.OnRestorationCompleted += HandleRestorationCompleted;

            WorldView = new TraderWorldView(Id, economyManager);
        }

        /// <summary>
        /// Gives the trader the same storage, events and dependency graph the player has. Called
        /// right after construction, before any news can arrive.
        /// </summary>
        public void AttachEnvironment(WarehouseSettings warehouseSettings, int startingCapacityLevel,
            Events.EventManager eventManager = null, Dependency.DependencyManager dependencyManager = null)
        {
            if (warehouseSettings != null)
                Inventory.AttachWarehouse(new Warehouse(warehouseSettings, startingCapacityLevel));

            WorldView = new TraderWorldView(Id, _economyManager, eventManager, dependencyManager);
        }

        public void Detach()
        {
            if (_transportManager != null)
                _transportManager.OnShipmentDelivered -= HandleShipmentDelivered;
            if (_restorationManager != null)
                _restorationManager.OnRestorationCompleted -= HandleRestorationCompleted;
        }

        public int Reputation => Record.Reputation.Reputation;

        private int CurrentWeek => _economyManager != null && _economyManager.TimeManager != null
            ? _economyManager.TimeManager.CurrentWeek
            : 0;

        private int TicksPerPeriod => _economyManager != null && _economyManager.TimeManager != null
            ? _economyManager.TimeManager.TicksPerPeriod
            : Core.TimeManager.DefaultTicksPerPeriod;

        // ---------------------------------------------------------------- actions on tick

        public void RunScheduledActions(int week, int tick)
        {
            var profile = Profile;
            if (profile == null || _economyManager.Market == null) return;

            if (_planWeek != week) Review(week, tick, profile);

            if (_lastObligationTick != tick)
            {
                _lastObligationTick = tick;
                RunObligations(week, profile);
            }

            while (_slotTicks.Count > 0 && _slotTicks[0] <= tick)
            {
                _slotTicks.RemoveAt(0);
                RunOneBudgetedAction(week, tick, profile);
            }
        }

        public void FlushRemainingSlots()
        {
            var profile = Profile;
            if (profile == null || _economyManager.Market == null || _planWeek < 0) return;

            while (_slotTicks.Count > 0)
            {
                int tick = _slotTicks[0];
                _slotTicks.RemoveAt(0);
                RunOneBudgetedAction(_planWeek, tick, profile);
            }
        }

        public void EvaluateWeek(int currentWeek)
        {
            var profile = Profile;
            if (profile == null || _economyManager.Market == null) return;

            Review(currentWeek, 0, profile);
            RunObligations(currentWeek, profile);
            FlushRemainingSlots();
        }

        // ---------------------------------------------------------------- planning

        /// <summary>
        /// The week's one decision point: ages what the trader believes, pays its costs, prices its
        /// listings and settles what it means to do, so the slots only carry the decisions out.
        /// Runs on the first tick of the week, after the events and news of that week have landed.
        /// </summary>
        private void Review(int week, int fromTick, NpcBehaviorProfile profile)
        {
            _planWeek = week;
            _contractSlotsUsed = 0;
            _lastObligationTick = -1;
            _rng = new System.Random(SeedFor(week));

            WorldView?.AdvanceWeek(week);
            PayEndowment(profile);

            DropInactiveContracts();
            DropDeadPlans(week);

            _mayPlantRumour = profile.RumourPlantChance > 0f && !profile.NeverSells &&
                              _rng.NextDouble() < profile.RumourPlantChance;

            _contractReserve = CalculateContractReserve();
            _planReserve = CalculatePlanReserve();
            GrantWeekBudget(week, profile);
            SplitWeekBudget(profile);

            RollSlotTicks(fromTick, profile);

            RepriceListings(week, profile);
            BuildAgenda(week, profile);
        }

        /// <summary>Pays a collector's weekly endowment, the only income a trader that never sells has.</summary>
        private void PayEndowment(NpcBehaviorProfile profile)
        {
            if (profile.WeeklyEndowment <= 0f) return;
            Inventory.AddCash(profile.WeeklyEndowment, LedgerCategory.Other, "Endowment");
        }

        /// <summary>
        /// Divides the week's allowance between the reasons for spending it, so buying into a
        /// believed move cannot take the money set aside for the open board.
        /// </summary>
        private void SplitWeekBudget(NpcBehaviorProfile profile)
        {
            float speculation = Mathf.Max(0f, profile.SpeculationBudgetShare);
            float bargain = Mathf.Max(0f, profile.BargainBudgetShare);
            float restoration = Mathf.Max(0f, profile.RestorationBudgetShare);

            float total = speculation + bargain + restoration;
            if (total <= 0f)
            {
                _speculationBudget = 0f;
                _bargainBudget = _weekBudget;
                _restorationBudget = 0f;
                return;
            }

            _speculationBudget = _weekBudget * speculation / total;
            _bargainBudget = _weekBudget * bargain / total;
            _restorationBudget = _weekBudget * restoration / total;
        }

        /// <summary>What is left this week for one reason to spend, capped by the whole allowance.</summary>
        private float BudgetFor(Intent intent)
        {
            float envelope = intent switch
            {
                Intent.OpenPosition => _speculationBudget,
                Intent.TakeBargain => _bargainBudget,
                Intent.SendToWorkshop => _restorationBudget,
                _ => _weekBudget
            };

            return Mathf.Min(_weekBudget, Mathf.Max(0f, envelope));
        }

        /// <summary>Takes a spend out of both its envelope and the week's allowance.</summary>
        private void SpendFrom(Intent intent, float amount)
        {
            _weekBudget = Mathf.Max(0f, _weekBudget - amount);

            switch (intent)
            {
                case Intent.OpenPosition:
                    _speculationBudget = Mathf.Max(0f, _speculationBudget - amount);
                    break;
                case Intent.TakeBargain:
                    _bargainBudget = Mathf.Max(0f, _bargainBudget - amount);
                    break;
                case Intent.SendToWorkshop:
                    _restorationBudget = Mathf.Max(0f, _restorationBudget - amount);
                    break;
            }
        }

        private void GrantWeekBudget(int week, NpcBehaviorProfile profile)
        {
            int accrualWeeks = profile.BudgetAccrualWeeks > 0
                ? profile.BudgetAccrualWeeks
                : DefaultBudgetAccrualWeeks;

            float spendable = Mathf.Max(0f, Inventory.Cash - _contractReserve - _planReserve);
            float allowance = spendable * profile.WeeklyBudgetFraction;
            float ceiling = Mathf.Min(spendable, allowance * accrualWeeks);

            float granted = _budgetWeek == week
                ? Mathf.Max(_weekBudget, allowance)
                : Mathf.Max(0f, _weekBudget) + allowance;

            _weekBudget = Mathf.Min(ceiling, granted);
            _budgetWeek = week;
        }

        private void RollSlotTicks(int fromTick, NpcBehaviorProfile profile)
        {
            _slotTicks.Clear();

            int ticks = Mathf.Max(1, TicksPerPeriod);
            fromTick = Mathf.Clamp(fromTick, 0, ticks - 1);
            int available = ticks - fromTick;

            int full = Mathf.Clamp(Mathf.RoundToInt(profile.ActionsPerPeriod * Mathf.Max(0f, ActivityScale)), 1, ticks);

            int slots = fromTick == 0
                ? full
                : Mathf.Clamp(Mathf.RoundToInt(full * available / (float)ticks), 1, available);

            var taken = new bool[ticks];
            int placed = 0;
            int guard = 0;

            while (placed < slots && guard++ < ticks * 4)
            {
                int t = _rng.Next(fromTick, ticks);
                if (taken[t]) continue;
                taken[t] = true;
                placed++;
            }

            for (int t = fromTick; t < ticks && placed < slots; t++)
            {
                if (taken[t]) continue;
                taken[t] = true;
                placed++;
            }

            for (int t = 0; t < ticks; t++)
                if (taken[t]) _slotTicks.Add(t);
        }

        private int SeedFor(int week) => Fnv1a(Id ?? TraderName ?? "npc") ^ (week * 397);

        /// <summary>
        /// The stream for judging one news item, seeded from the trader, the item and the week, so
        /// the verdict is a pure function of the three whatever order items arrive in.
        /// </summary>
        private System.Random ReceptionRng(string newsId, int deliveryWeek) =>
            new System.Random(Fnv1a(Id ?? TraderName ?? "npc") ^ Fnv1a(newsId) ^ (deliveryWeek * 397));

        /// <summary>FNV-1a, which is stable across runs and platforms where GetHashCode is not.</summary>
        private static int Fnv1a(string text)
        {
            unchecked
            {
                int hash = (int)2166136261;
                if (text == null) return hash;

                for (int i = 0; i < text.Length; i++)
                    hash = (hash ^ text[i]) * 16777619;

                return hash;
            }
        }

        private float CalculateContractReserve()
        {
            if (_contractManager == null) return 0f;

            var market = _economyManager.Market;
            float reserve = 0f;

            foreach (var contractId in Inventory.CommittedContractIds)
            {
                var contract = _contractManager.GetById(contractId);
                if (contract == null || contract.Status != ContractStatus.Active) continue;

                int missing = MissingFor(contract);
                if (missing <= 0) continue;

                reserve += contract.Requirement.ExpectedUnitCost(market) * missing;
            }

            return reserve;
        }

        // ---------------------------------------------------------------- plans

        /// <summary>The plan for an owned antique, or null.</summary>
        private Position PlanFor(string antiqueId) =>
            antiqueId != null && _plans.TryGetValue(antiqueId, out var plan) ? plan : null;

        /// <summary>The plan for an owned antique, creating a plain resale one if it has none.</summary>
        private Position PlanForOrCreate(Antique antique, int week) =>
            PlanFor(antique.Id) ?? RecordPlan(antique.Id, antique.PurchasePrice, week, PlanPurpose.Resale);

        private Position RecordPlan(string antiqueId, float costBasis, int week, PlanPurpose purpose, string contractId = null)
        {
            var plan = new Position
            {
                CostBasis = Core.Money.RoundToShilling(costBasis),
                AcquiredWeek = week,
                Purpose = purpose,
                ContractId = contractId
            };
            _plans[antiqueId] = plan;
            return plan;
        }

        /// <summary>Records on a plan which workshop its antique is meant to go to, and by when.</summary>
        private void AttachRestorationLeg(string antiqueId, string optionId, float fee, int startRestoreByWeek)
        {
            var plan = PlanFor(antiqueId);
            if (plan == null || string.IsNullOrEmpty(optionId)) return;

            plan.RestorationOptionId = optionId;
            plan.AssumedFee = fee;
            plan.StartRestoreByWeek = startRestoreByWeek;
            _planReserve = CalculatePlanReserve();
        }

        /// <summary>
        /// Drops plans for antiques the NPC no longer owns, and returns the rest to plain stock once
        /// their contract, rumour or restoration window is gone.
        /// </summary>
        private void DropDeadPlans(int week)
        {
            List<string> gone = null;

            foreach (var pair in _plans)
            {
                var plan = pair.Value;

                if (!Inventory.Owns(pair.Key))
                {
                    (gone ??= new List<string>()).Add(pair.Key);
                    continue;
                }

                if (plan.Purpose == PlanPurpose.Contract && _contractManager != null)
                {
                    var contract = _contractManager.GetById(plan.ContractId);
                    if (contract == null || contract.Status != ContractStatus.Active) plan.FallBackToResale();
                }

                // RumourExit is a retired purpose: anything carrying it falls back to plain stock.
                if (plan.Purpose == PlanPurpose.RumourExit) plan.FallBackToResale();

                if (plan.HasRestorationLeg && week > plan.StartRestoreByWeek && !IsRestoring(pair.Key))
                    plan.ClearRestorationLeg();
            }

            if (gone == null) return;
            foreach (var antiqueId in gone) _plans.Remove(antiqueId);
        }

        private bool IsRestoring(string antiqueId) =>
            _restorationManager != null && _restorationManager.GetJob(antiqueId) != null;

        private int OpenRestorationCount() =>
            _restorationManager != null ? _restorationManager.GetJobsFor(Inventory).Count() : 0;

        /// <summary>Cash held back for restoration fees the NPC has planned but not yet paid.</summary>
        private float CalculatePlanReserve()
        {
            if (_restorationManager == null) return 0f;

            float reserve = 0f;
            foreach (var pair in _plans)
            {
                var plan = pair.Value;
                if (!plan.HasRestorationLeg || IsRestoring(pair.Key)) continue;
                reserve += plan.AssumedFee;
            }

            return Core.Money.RoundToShilling(reserve);
        }

        // ---------------------------------------------------------------- non-tick actions

        private void RunObligations(int week, NpcBehaviorProfile profile)
        {
            DropInactiveContracts();
            ReserveMatchingHoldings();
            DropUnfullfillableOpenContracts(week, profile);
            FulfillReadyContracts(week);
        }

        private void DropInactiveContracts()
        {
            if (_contractManager == null || Inventory.CommittedContractIds.Count == 0) return;

            List<string> dead = null;
            foreach (var contractId in Inventory.CommittedContractIds)
            {
                var contract = _contractManager.GetById(contractId);
                if (contract != null && contract.Status == ContractStatus.Active) continue;
                (dead ??= new List<string>()).Add(contractId);
            }

            if (dead == null) return;

            foreach (var contractId in dead)
            {
                Inventory.ReleaseAllReservationsForContract(contractId);
                Inventory.ReleaseCommittedContract(contractId);
            }
        }

        /// <summary>
        /// A reserved antique can stop qualifying while it waits — its market value fell below the
        /// contract's floor. It no longer counts toward the contract (MissingFor), so free it: otherwise
        /// it would sit reserved forever, unsellable. ReserveMatchingHoldings picks it up again if the
        /// market recovers.
        /// </summary>
        private void ReleaseNoLongerMatching()
        {
            if (_contractManager == null) return;

            List<string> stale = null;
            foreach (var pair in Inventory.Holdings)
            {
                var holding = pair.Value;
                if (!holding.IsReservedForContract) continue;

                var contract = _contractManager.GetById(holding.ReservedForContractId);
                if (contract == null || contract.Requirement.IsSatisfiedBy(holding)) continue;

                (stale ??= new List<string>()).Add(holding.Id);
            }

            if (stale == null) return;

            foreach (var listingId in stale)
                Inventory.ReleaseReservation(listingId);

            Debug.Log($"{TraderName}: released {stale.Count} reserved antique(s) that no longer meet their contract.");
        }

        private void ReserveMatchingHoldings()
        {
            if (_contractManager == null) return;

            ReleaseNoLongerMatching();

            foreach (var contractId in Inventory.CommittedContractIds)
            {
                var contract = _contractManager.GetById(contractId);
                if (contract == null || contract.Status != ContractStatus.Active) continue;

                int missing = MissingFor(contract);
                if (missing <= 0) continue;

                string pick = null;
                foreach (var pair in Inventory.Holdings)
                {
                    var holding = pair.Value;
                    if (holding.IsReservedForContract || holding.IsListedForSale || holding.IsInRestoration) continue;
                    if (!contract.Requirement.IsSatisfiedBy(holding)) continue;
                    pick = holding.Id;
                    break;
                }

                if (pick != null) Inventory.ReserveForContract(pick, contractId);
            }
        }

        private void FulfillReadyContracts(int week)
        {
            if (_contractManager == null || Inventory.CommittedContractIds.Count == 0) return;

            Contract ready = null;
            foreach (var contractId in Inventory.CommittedContractIds)
            {
                var contract = _contractManager.GetById(contractId);
                if (contract == null || contract.Status != ContractStatus.Active) continue;
                if (ReadyCountFor(contract) < contract.Requirement.Quantity) continue;

                ready = contract;
                break;
            }

            if (ready != null) TryFulfillContract(ready, week);
        }

        private void TryFulfillContract(Contract contract, int week)
        {
            var listingIds = new List<string>(contract.Requirement.Quantity);

            foreach (var pair in Inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding.ReservedForContractId != contract.ContractId) continue;
                if (!holding.IsAvailable) continue;
                if (!contract.Requirement.IsSatisfiedBy(holding)) continue;

                listingIds.Add(holding.Id);
                if (listingIds.Count >= contract.Requirement.Quantity) break;
            }

            if (listingIds.Count < contract.Requirement.Quantity) return;

            if (_contractManager.FulfillContract(contract.ContractId, Id, Inventory, listingIds))
            {
                foreach (var listingId in listingIds)
                    _plans.Remove(listingId);

                Inventory.ReleaseCommittedContract(contract.ContractId);
                Record.ContractFulfilled(contract, week);
            }
        }

        private void DropUnfullfillableOpenContracts(int week, NpcBehaviorProfile profile)
        {
            if (_contractManager == null || Inventory.CommittedContractIds.Count == 0) return;

            Contract hopeless = null;
            foreach (var contractId in Inventory.CommittedContractIds)
            {
                var contract = _contractManager.GetById(contractId);
                if (contract == null || contract.Status != ContractStatus.Active) continue;
                if (contract.Type != ContractType.Open) continue;

                int missing = MissingFor(contract);
                if (missing <= 0) continue;
                if (UrgencyOf(contract, week, profile) != ContractUrgency.Critical) continue;
                if (IsStillReachable(contract, missing, week, profile)) continue;

                hopeless = contract;
                break;
            }

            if (hopeless == null) return;

            Debug.Log($"{TraderName} dropped open contract {hopeless.ContractId}. They cannot aquire missing antiques in remaining time.");
            Inventory.ReleaseAllReservationsForContract(hopeless.ContractId);
            Inventory.ReleaseCommittedContract(hopeless.ContractId);
            Record.ContractFailed(hopeless, week);
        }

        /// <summary>
        /// Walks every listing's asking price one step down its patience curve, and withdraws one
        /// that has sat past its relist window at its reservation price. Once a week.
        /// </summary>
        private void RepriceListings(int week, NpcBehaviorProfile profile)
        {
            if (!profile.ListsOnMarket) return;
            if (_lastListingRevisionWeek == week) return;
            _lastListingRevisionWeek = week;

            var market = _economyManager.Market;
            if (market == null) return;

            int relistAfter = Mathf.Max(1, profile.RelistAfterWeeks);
            string stale = null;

            foreach (var pair in Inventory.Holdings)
            {
                var holding = pair.Value;
                if (!holding.IsListedForSale || holding.MarketListedOnWeek < 0) continue;

                int weeksListed = week - holding.MarketListedOnWeek;
                if (weeksListed < 1) continue;

                float worth = TraderValuation.Worth(holding, market, WorldView, profile, PricingHorizonWeeks);
                float ask = TraderValuation.AskFor(holding, PlanFor(holding.Id), weeksListed, worth,
                    Inventory, market, profile);

                if (ask > 0f && ask < holding.AskingPrice - 0.01f &&
                    Inventory.RepriceListing(market, holding.Id, ask))
                {
                    Debug.Log($"{TraderName} priced {holding.Name} at {Core.Money.Format(ask)} " +
                              $"(worth {Core.Money.Format(worth)}), listed {weeksListed} weeks.");
                    continue;
                }

                if (stale == null && weeksListed >= relistAfter) stale = holding.Id;
            }

            if (stale != null) Inventory.CancelListing(market, stale);
        }

        // ---------------------------------------------------------------- the week's intentions

        /// <summary>
        /// Settles what the trader means to do this week, with every candidate in view at once, so
        /// a slot carries out a decision instead of making one.
        /// </summary>
        private void BuildAgenda(int week, NpcBehaviorProfile profile)
        {
            _agenda.Clear();

            if (HasUrgentContract(week, profile))
                _agenda.Add(Intent.FillContract, null, _weekBudget, week, 100f, "a contract is running out of time");

            if (HasPlanStep(week))
                _agenda.Add(Intent.ExecutePlan, null, _weekBudget, week, 90f, "a planned step is due");

            _cashShortfall = CashShortfall();
            if (_cashShortfall > 0f)
                _agenda.Add(Intent.RaiseCash, null, 0f, week, 80f,
                    $"short {Core.Money.Format(_cashShortfall)} of committed costs");

            if (Inventory.IsOverCapacity)
                _agenda.Add(Intent.FreeSpace, null, 0f, week, 70f,
                    $"warehouse over capacity ({Inventory.UsedSlots}/{Inventory.Capacity})");

            AddExitItems(week, profile);

            if (profile.TakesContracts)
                _agenda.Add(Intent.TakeContract, null, _weekBudget, week, 10f, "looking at the contract board");

            AddPositionItems(week, profile);

            if (_bargainBudget > 0f)
                _agenda.Add(Intent.TakeBargain, null, _bargainBudget, week, 1f, "looking for a bargain");

            if (profile.Restores && _restorationBudget > 0f)
                _agenda.Add(Intent.SendToWorkshop, null, _restorationBudget, week, 1f, "looking for restoration work");

            if (WantsMoreSpace(profile))
                _agenda.Add(Intent.Invest, null, _weekBudget, week, 0.5f, "the warehouse is nearly full");

            if (_mayPlantRumour)
                _agenda.Add(Intent.PlantRumour, null, _weekBudget, week, 0.5f, "a rumour may pay for itself");

            _agenda.Sort();
        }

        /// <summary>One exit item per holding the trader has a reason to be rid of.</summary>
        private void AddExitItems(int week, NpcBehaviorProfile profile)
        {
            if (profile.NeverSells || _economyManager.Market == null) return;

            foreach (var pair in Inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding.IsReservedForContract || holding.IsInTransit || holding.IsInRestoration) continue;

                var plan = PlanFor(holding.Id);
                if (plan == null || plan.HasRestorationLeg) continue;
                if (plan.Purpose == PlanPurpose.Contract) continue;

                var reason = ReasonToSell(holding, plan, week, profile);

                // Back above its stop, so the distress clock is cleared and starts again from
                // scratch if the value falls back under.
                if (reason != DisposalReason.StopLoss) plan.DistressSince = -1;

                if (reason == DisposalReason.None) continue;

                _agenda.Add(Intent.ExitPosition, holding.Id, 0f, week, ExitWeightOf(reason),
                    $"{holding.Name}: {RationaleFor(reason, holding, plan, week)}", reason);
            }
        }

        /// <summary>
        /// Why this holding should go, if it should. The first reason that applies wins, so a
        /// thesis that has broken is acted on before one that has merely matured.
        /// </summary>
        private DisposalReason ReasonToSell(Antique holding, Position plan, int week, NpcBehaviorProfile profile)
        {
            float value = holding.CurrentPrice;

            if (plan.HasThesis)
            {
                if (ThesisBroken(plan, holding, profile)) return DisposalReason.ThesisInvalidated;

                if (plan.StopPrice > 0f && value < plan.StopPrice) return DisposalReason.StopLoss;

                if (plan.TargetPrice > 0f && value >= plan.TargetPrice) return DisposalReason.TargetReached;

                // A fade that is a hard deadline (a planted rumour's debunk) will not wait for a
                // buyer, so it dumps; an estimated fade only means it is time to be listed.
                if (plan.DumpAfterWeek > 0 && week >= plan.DumpAfterWeek) return DisposalReason.RumourDump;

                // Past the peak the move is as good as it gets, so the piece goes on offer at what
                // the thesis says it is worth rather than waiting for the fade.
                bool mayOffer = plan.DumpAfterWeek > 0 ? profile.ListsIntoOwnRumour : profile.ListsOnMarket;
                if (mayOffer && plan.ExpectedPeakWeek > 0 && week >= plan.ExpectedPeakWeek)
                    return DisposalReason.TargetReached;

                int lead = Mathf.Max(0, profile.ExitLeadWeeks);
                if (week >= plan.ExpectedFadeWeek - lead) return DisposalReason.FadeApproaching;

                return DisposalReason.None;
            }

            int held = week - plan.AcquiredWeek;

            if (profile.StopLossMultiplier > 0f && plan.CostBasis > 0f)
            {
                float expected = value * (1f + Mathf.Min(0f, TrendOutlook(holding.DefinitionId, profile)));
                if (expected < plan.CostBasis * profile.StopLossMultiplier) return DisposalReason.StopLoss;
            }

            if (held < profile.MinHoldingWeeksBeforeSell) return DisposalReason.None;

            var outlook = WorldView != null
                ? WorldView.OutlookFor(holding.DefinitionId, HoldWeeks(profile), profile)
                : Outlook.None;

            // A believed fall is worth getting ahead of, whatever the piece has made so far.
            if (outlook.Conviction <= -BadNewsThreshold) return DisposalReason.BadNews;

            bool patient = profile.PatientSellAfterWeeks > 0 && held >= profile.PatientSellAfterWeeks;
            float target = patient
                ? Mathf.Min(profile.PatientProfitMultiplier, profile.ProfitTargetMultiplier)
                : profile.ProfitTargetMultiplier;

            // A believed rise is worth waiting for, unless patience has already run out.
            if (outlook.Conviction >= GoodNewsThreshold && !patient) return DisposalReason.None;

            if (value >= plan.CostBasis * target) return DisposalReason.TargetReached;
            if (patient) return DisposalReason.Patience;

            return DisposalReason.None;
        }

        /// <summary>True once the news a thesis rested on is gone or has turned against it.</summary>
        private bool ThesisBroken(Position plan, Antique holding, NpcBehaviorProfile profile)
        {
            if (WorldView == null) return true;

            if (plan.BasedOnNewsIds != null)
            {
                bool anyHeld = false;
                for (int i = 0; i < plan.BasedOnNewsIds.Length; i++)
                    if (WorldView.Holds(plan.BasedOnNewsIds[i])) { anyHeld = true; break; }

                if (!anyHeld) return true;
            }

            var outlook = WorldView.OutlookFor(holding.DefinitionId, HoldWeeks(profile), profile);
            return outlook.Conviction * plan.EntryOutlook < 0f;
        }

        /// <summary>Obligations and broken theses are dealt with before matured ones.</summary>
        private static float ExitWeightOf(DisposalReason reason) => reason switch
        {
            DisposalReason.CashNeed => 60f,
            DisposalReason.RumourDump => 55f,
            DisposalReason.ThesisInvalidated => 50f,
            DisposalReason.StopLoss => 45f,
            DisposalReason.BadNews => 40f,
            DisposalReason.FadeApproaching => 35f,
            DisposalReason.Capacity => 30f,
            DisposalReason.TargetReached => 25f,
            DisposalReason.Patience => 20f,
            _ => 15f
        };

        private string RationaleFor(DisposalReason reason, Antique holding, Position plan, int week) => reason switch
        {
            DisposalReason.ThesisInvalidated => "the view it was bought on is gone",
            DisposalReason.StopLoss => $"worth {Core.Money.Format(holding.CurrentPrice)} against {Core.Money.Format(plan.CostBasis)} paid",
            DisposalReason.BadNews => "believed news says this is falling",
            DisposalReason.FadeApproaching => $"the move it was bought for fades in week {plan.ExpectedFadeWeek}",
            DisposalReason.TargetReached => $"reached {Core.Money.Format(holding.CurrentPrice)}",
            DisposalReason.Patience => $"held {week - plan.AcquiredWeek} weeks",
            DisposalReason.CashNeed => "cash is needed now",
            DisposalReason.Capacity => "the warehouse is full",
            _ => "time to move it on"
        };

        /// <summary>
        /// One buying intention per antique type the trader's view says is cheap, sized by how much
        /// of that view it is confident in and whether a purchase can arrive before the move peaks.
        /// </summary>
        private void AddPositionItems(int week, NpcBehaviorProfile profile)
        {
            if (WorldView == null || _speculationBudget <= 0f) return;

            var market = _economyManager.Market;
            if (market == null) return;

            var listings = market.Listings;
            _seenDefinitions.Clear();

            for (int i = 0; i < listings.Count; i++)
            {
                var listing = listings[i];
                if (listing == null || Inventory.Owns(listing.Id)) continue;
                if (string.IsNullOrEmpty(listing.DefinitionId)) continue;
                if (_seenDefinitions.Contains(listing.DefinitionId)) continue;
                if (!IsInterestedIn(listing.Definition, profile)) continue;

                var outlook = WorldView.OutlookFor(listing.DefinitionId, HoldWeeks(profile), profile);
                if (outlook.Conviction < Mathf.Max(0.01f, profile.MinBargainScore)) continue;

                // No point buying into a move that lands before the piece does.
                int arrival = week + TransitWeeksFor(listing, NpcTransportOption);
                if (outlook.PeakWeek > 0 && arrival > outlook.PeakWeek) continue;

                _seenDefinitions.Add(listing.DefinitionId);

                float share = Mathf.Clamp01(outlook.Conviction / Mathf.Max(0.01f, profile.FullConvictionAt));
                float ceiling = _speculationBudget * share;
                if (ceiling <= 0f) continue;

                _agenda.Add(Intent.OpenPosition, listing.DefinitionId, ceiling, outlook.PeakWeek,
                    outlook.Conviction,
                    $"{listing.Definition.DisplayName}: {outlook.Supporting} source(s), " +
                    $"{outlook.Expected:P0} expected, peaks week {outlook.PeakWeek}");
            }
        }

        /// <summary>Cash the week's committed costs are short of, after the safety margin.</summary>
        private float CashShortfall()
        {
            float needed = (_contractReserve + _planReserve) * ReserveSafetyMargin + WeeklyFixedCosts();
            return Mathf.Max(0f, Core.Money.RoundToShilling(needed - Inventory.Cash));
        }

        /// <summary>What the trader owes every week whatever it trades.</summary>
        private float WeeklyFixedCosts()
        {
            var warehouse = Inventory.Warehouse;
            return warehouse != null ? warehouse.CalculateWeeklyUpkeep(Inventory.UsedSlots) : 0f;
        }

        private bool HasUrgentContract(int week, NpcBehaviorProfile profile)
        {
            if (_contractManager == null) return false;

            foreach (var contractId in Inventory.CommittedContractIds)
            {
                var contract = _contractManager.GetById(contractId);
                if (contract == null || contract.Status != ContractStatus.Active) continue;
                if (MissingFor(contract) <= 0) continue;
                if (UrgencyOf(contract, week, profile) != ContractUrgency.Normal) return true;
            }

            return false;
        }

        private bool HasPlanStep(int week)
        {
            foreach (var pair in _plans)
            {
                var plan = pair.Value;
                if (plan.HasRestorationLeg && !IsRestoring(pair.Key)) return true;
            }

            return false;
        }

        private bool WantsMoreSpace(NpcBehaviorProfile profile)
        {
            var warehouse = Inventory.Warehouse;
            if (warehouse == null || !warehouse.CanUpgradeCapacity) return false;
            if (Inventory.FreeSlots > 1) return false;

            float cost = warehouse.NextCapacityUpgradeCost;
            return cost > 0f && Core.Money.Covers(_weekBudget, cost) && PassesCashFloor(cost);
        }

        // ---------------------------------------------------------------- spending a slot

        private void RunOneBudgetedAction(int week, int tick, NpcBehaviorProfile profile)
        {
            Diagnostics.Telemetry.NpcActionBegin(this);
            var item = RunOneBudgetedActionCore(week, tick, profile);
            Diagnostics.Telemetry.NpcActionEnd(this, week, item);
        }

        /// <summary>
        /// Takes the next intention off the week's agenda and carries it out. Items that can no
        /// longer be acted on are retired with the reason, and the slot moves to the next one.
        /// </summary>
        private AgendaItem RunOneBudgetedActionCore(int week, int tick, NpcBehaviorProfile profile)
        {
            while (true)
            {
                var item = _agenda.NextFor(week);
                if (item == null) return null;

                if (!Execute(item, week, profile))
                {
                    Diagnostics.Telemetry.TierDeclined(this, item.Intent.ToString());
                    _agenda.Retire(item, false, item.SkippedReason ?? "nothing to act on");
                    continue;
                }

                _agenda.Retire(item, true, tick: tick);
                return item;
            }
        }

        /// <summary>Carries out one intention. False means it could not be done this week.</summary>
        private bool Execute(AgendaItem item, int week, NpcBehaviorProfile profile)
        {
            switch (item.Intent)
            {
                case Intent.FillContract:
                    if (WorkOnContracts(week, profile, urgentOnly: true)) return true;
                    if (_contractSlotsUsed >= ContractSlotsAllowed(profile))
                    {
                        item.SkippedReason = "contract slots spent";
                        return false;
                    }
                    return WorkOnContracts(week, profile, urgentOnly: false);

                case Intent.ExecutePlan:
                    return TierExecutePlans(week, profile);

                case Intent.RaiseCash:
                    return RaiseCash(item, week, profile);

                case Intent.FreeSpace:
                    return FreeSpace(item, week, profile);

                case Intent.ExitPosition:
                    return ExitPosition(item, week, profile);

                case Intent.TakeContract:
                    return TierTakeContract(week, profile);

                case Intent.OpenPosition:
                    return OpenPosition(item, week, profile);

                case Intent.TakeBargain:
                    return TierBrowseMarket(week, profile);

                case Intent.SendToWorkshop:
                    return TierRestoreStock(week, profile);

                case Intent.Invest:
                    return Invest(item, profile);

                case Intent.PlantRumour:
                    return TierPlantRumour(week, profile);

                default:
                    return false;
            }
        }

        /// <summary>
        /// Sells to the market until the shortfall is covered, cheapest holding first. The only
        /// route to an instant sale that is not already a lost cause.
        /// </summary>
        private bool RaiseCash(AgendaItem item, int week, NpcBehaviorProfile profile)
        {
            if (profile.NeverSells) { item.SkippedReason = "profile never sells"; return false; }

            float shortfall = CashShortfall();
            if (shortfall <= 0f) { item.SkippedReason = "no longer short"; return false; }

            string victim = CheapestFreeHolding(out string victimName);
            if (victim == null) { item.SkippedReason = "nothing free to sell"; return false; }

            if (!DisposeOf(victim, DisposalReason.CashNeed, week, profile)) return false;

            Debug.Log($"{TraderName} sold {victimName} to the market, short {Core.Money.Format(shortfall)}.");

            // Still short: keep the intention alive for the next slot.
            if (CashShortfall() > 0f) _agenda.Add(Intent.RaiseCash, null, 0f, week, 80f, "still short of committed costs");
            return true;
        }

        /// <summary>Lists the cheapest free holding to get back under the warehouse ceiling.</summary>
        private bool FreeSpace(AgendaItem item, int week, NpcBehaviorProfile profile)
        {
            if (profile.NeverSells) { item.SkippedReason = "profile never sells"; return false; }
            if (!Inventory.IsOverCapacity) { item.SkippedReason = "back within capacity"; return false; }

            string victim = CheapestFreeHolding(out string victimName);
            if (victim == null) { item.SkippedReason = "nothing free to move on"; return false; }

            if (!DisposeOf(victim, DisposalReason.Capacity, week, profile)) return false;

            Debug.Log($"{TraderName} moved {victimName} on: warehouse full ({Inventory.UsedSlots}/{Inventory.Capacity}).");
            return true;
        }

        private bool ExitPosition(AgendaItem item, int week, NpcBehaviorProfile profile)
        {
            if (string.IsNullOrEmpty(item.TargetId) || !Inventory.Owns(item.TargetId))
            {
                item.SkippedReason = "no longer held";
                return false;
            }

            return DisposeOf(item.TargetId, item.Reason, week, profile);
        }

        /// <summary>
        /// Buys into a believed move, taking the best-returning listings of that type while the
        /// item's ceiling covers another piece, so conviction sets the size of the position.
        /// </summary>
        private bool OpenPosition(AgendaItem item, int week, NpcBehaviorProfile profile)
        {
            var market = _economyManager.Market;
            if (market == null || string.IsNullOrEmpty(item.TargetId)) return false;

            float ceiling = Mathf.Min(BudgetFor(Intent.OpenPosition), item.BudgetCeiling);
            if (ceiling <= 0f) { item.SkippedReason = "envelope spent"; return false; }

            var outlook = WorldView.OutlookFor(item.TargetId, HoldWeeks(profile), profile);
            if (outlook.Conviction <= 0f) { item.SkippedReason = "the view has gone"; return false; }

            int bought = 0;
            float spent = 0f;

            while (bought < MaxPiecesPerPosition)
            {
                float envelope = ceiling - spent;
                if (envelope <= 0f) break;

                if (!BuyOnePosition(item, week, profile, market, outlook, envelope, out float paid)) break;

                spent += paid;
                bought++;
            }

            if (bought == 0)
            {
                item.SkippedReason ??= "nothing affordable of that type";
                return false;
            }

            if (bought > 1)
                Debug.Log($"{TraderName} took {bought} pieces into its view of {item.TargetId} " +
                          $"for {Core.Money.Format(spent)} of a {Core.Money.Format(ceiling)} ceiling.");

            return true;
        }

        /// <summary>
        /// Buys the best-returning listing of the item's type that fits the envelope and writes the
        /// thesis onto it. False when nothing left is worth buying.
        /// </summary>
        private bool BuyOnePosition(AgendaItem item, int week, NpcBehaviorProfile profile, Market.Market market,
            Outlook outlook, float envelope, out float paid)
        {
            paid = 0f;

            var listings = market.Listings;
            int holdWeeks = HoldWeeks(profile);

            Antique best = null;
            float bestCost = 0f;
            float bestReturn = 0f;
            TransportOption bestOption = NpcTransportOption;

            for (int i = 0; i < listings.Count; i++)
            {
                var listing = listings[i];
                if (listing == null || listing.DefinitionId != item.TargetId) continue;
                if (Inventory.Owns(listing.Id)) continue;
                if (!CanConsider(listing, profile)) continue;

                float worth = TraderValuation.Worth(listing, market, WorldView, profile, holdWeeks);

                var shipping = ChooseTransportFor(listing, worth, holdWeeks);
                float cost = shipping.TotalCost;
                if (cost <= 0f || !Core.Money.Covers(envelope, cost) || !PassesCashFloor(cost)) continue;

                float weekly = TraderValuation.WeeklyReturn(cost, worth, holdWeeks, MarginalUpkeepPerItem());
                if (weekly <= 0f || weekly < bestReturn) continue;

                best = listing;
                bestCost = cost;
                bestReturn = weekly;
                bestOption = shipping.Option;
            }

            if (best == null)
            {
                item.SkippedReason ??= "nothing affordable of that type";
                return false;
            }

            string name = best.Name;
            if (!TryBuyForStock(best, week, bestCost, Intent.OpenPosition, bestOption)) return false;

            RecordThesis(best.Id, outlook, bestCost, week, profile);
            paid = bestCost;
            Debug.Log($"{TraderName} bought {name} on its view of the market ({item.Rationale}).");
            return true;
        }

        /// <summary>Writes the view that justified a purchase onto its position, with the prices that end it.</summary>
        private void RecordThesis(string antiqueId, Outlook outlook, float cost, int week, NpcBehaviorProfile profile)
        {
            var plan = PlanFor(antiqueId);
            if (plan == null) return;

            plan.Purpose = PlanPurpose.NewsThesis;
            plan.EntryOutlook = outlook.Expected;
            plan.ExpectedPeakWeek = outlook.PeakWeek;
            plan.ExpectedFadeWeek = outlook.FadeWeek > 0 ? outlook.FadeWeek : week + HoldWeeks(profile);
            plan.TargetPrice = Core.Money.RoundToShilling(cost * (1f + Mathf.Max(0f, outlook.Conviction)));
            plan.StopPrice = Core.Money.RoundToShilling(cost * StopShareOf(profile));
            plan.BasedOnNewsIds = NewsIdsBehind(antiqueId, profile);
        }

        /// <summary>Where a thesis is wrong, as a share of what was paid.</summary>
        private static float StopShareOf(NpcBehaviorProfile profile) =>
            profile.StopLossMultiplier > 0f ? profile.StopLossMultiplier : 0.8f;

        /// <summary>The news items currently behind a view of this antique's type.</summary>
        private string[] NewsIdsBehind(string antiqueId, NpcBehaviorProfile profile)
        {
            var antique = Inventory.GetHolding(antiqueId);
            if (antique == null || WorldView == null) return null;

            var definition = antique.Definition;
            if (definition == null) return null;

            List<string> ids = null;
            var beliefs = WorldView.Beliefs;

            for (int i = 0; i < beliefs.Count; i++)
            {
                var belief = beliefs[i];
                if (!TraderWorldView.Matches(definition, belief.Facet)) continue;
                if (ids != null && ids.Contains(belief.NewsId)) continue;

                (ids ??= new List<string>()).Add(belief.NewsId);
            }

            return ids?.ToArray();
        }

        private bool Invest(AgendaItem item, NpcBehaviorProfile profile)
        {
            if (!WantsMoreSpace(profile)) { item.SkippedReason = "no longer worth it"; return false; }

            float cost = Inventory.Warehouse.NextCapacityUpgradeCost;
            if (!Inventory.TryUpgradeWarehouseCapacity()) return false;

            SpendFrom(Intent.Invest, cost);
            Debug.Log($"{TraderName} upgraded its warehouse to {Inventory.Capacity} slots.");
            return true;
        }

        private int ContractSlotsAllowed(NpcBehaviorProfile profile)
        {
            int full = Mathf.Clamp(Mathf.RoundToInt(profile.ActionsPerPeriod * Mathf.Max(0f, ActivityScale)), 1, Mathf.Max(1, TicksPerPeriod));
            return Mathf.Max(1, Mathf.CeilToInt(full * profile.ContractSlotShare));
        }

        // ---------------------------------------------------------------- transactions

        private bool TryBuyForStock(Antique listing, int week, float cost, Intent intent = Intent.TakeBargain,
            TransportOption option = NpcTransportOption)
        {
            if (!BuyListing(listing.Id, option)) return false;

            RecordPlan(listing.Id, listing.PurchasePrice, week, PlanPurpose.Resale);
            SpendFrom(intent, cost);
            return true;
        }

        private float EstimateTotalCost(Antique listing, TransportOption option) =>
            TraderHelper.EstimateTotalCost(listing, _transportManager, option, Inventory);

        public bool BuyListing(string listingId) => BuyListing(listingId, NpcTransportOption);

        public bool BuyListing(string listingId, TransportOption option) =>
            TraderHelper.BuyListing(Inventory, _economyManager.Market, listingId, TraderName, CurrentWeek, _transportManager, option);

        public bool SellListing(string listingId) =>
            TraderHelper.SellListing(Inventory, _economyManager.Market, listingId, TraderName, CurrentWeek);

    }
}