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
    public class NPCTrader : IInformationReceiver, IRestorationClient
    {
        public string Id { get; private set; }
        public string ProfileId { get; }
        public string TraderName { get; }
        public TraderInventory Inventory { get; }

        private readonly EconomyManager _economyManager;
        private readonly ContractManager _contractManager;
        private readonly TransportManager _transportManager;
        private readonly RestorationManager _restorationManager;

        // NPCs always ship with the standard service.
        private const TransportOption NpcTransportOption = TransportOption.Standard;
        private NpcBehaviorProfile _profileCache;
        public NpcBehaviorProfile Profile => _profileCache ??= NpcProfileDatabase.GetById(ProfileId);

        public InfoAccessLevel AccessLevel => Profile != null ? Profile.AccessLevel : InfoAccessLevel.LocalPress;
        public string ReceiverId => Id;

        private const int NewsActionWindowWeeks = 2;

        /// <summary>Exclusive contracts must leave at least this share of the reward as profit (they carry a penalty).</summary>
        private const float ExclusiveMinProfitShare = 0.15f;

        /// <summary>Share of cash an NPC may tie up in contract purchases at once.</summary>
        private const float ContractCashShare = 0.5f;

        /// <summary>A discretionary purchase may not take cash below the contract reserve times this variable.</summary>
        private const float ReserveSafetyMargin = 1.25f;

        private const float CommittedBargainPenalty = 2f;

        /// <summary>Expected seasonal rise (fall) beyond which a far-sighted NPC holds (sells) an antique.</summary>
        private const float SeasonalHoldThreshold = 0.03f;

        private const float ConditionEpsilon = 0.005f;

        /// <summary>Weeks after an antique arrives that a planned resale restoration stays worth starting.</summary>
        private const int RestorationLegGraceWeeks = 4;

        /// <summary>Weeks of buying allowance an NPC may save up when its profile does not say.</summary>
        private const int DefaultBudgetAccrualWeeks = 4;

        public float ActivityScale { get; set; } = 1f;

        private struct PendingReaction
        {
            public NewsItem News;
            public int ReactionWeek;
        }

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

        private readonly List<PendingReaction> _pendingReactions = new();

        /// <summary>Scope of the rumour this NPC planted, to sell into. Empty when it has none running.</summary>
        private readonly List<NewsEventData> _rumourExitScope = new();

        /// <summary>First week to start selling into its own rumour; -1 = no rumour running.</summary>
        private int _rumourExitWeek = -1;

        /// <summary>The rumour's claimed week: selling after it is pointless, the hangover has landed.</summary>
        private int _rumourClaimWeek = -1;

        /// <summary>First week the exit stops asking its price and sells to the market; -1 = no rumour running.</summary>
        private int _rumourDumpWeek = -1;

        /// <summary>Set by PlanPeriod once a week when the profile's roll comes up; spent by TierPlantRumour.</summary>
        private bool _mayPlantRumour;

        private readonly Dictionary<string, Plan> _plans = new();

        private int _planWeek = -1;
        private readonly List<int> _slotTicks = new();
        private float _weekBudget;
        private int _budgetWeek = -1;
        private float _contractReserve;
        private float _planReserve;
        private int _contractSlotsUsed;
        private int _lastObligationTick = -1;
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
            Id = Guid.NewGuid().ToString("N");
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

            if (_planWeek != week) PlanPeriod(week, tick, profile);

            if (_lastObligationTick != tick)
            {
                _lastObligationTick = tick;
                RunObligations(week, profile);
            }

            while (_slotTicks.Count > 0 && _slotTicks[0] <= tick)
            {
                _slotTicks.RemoveAt(0);
                RunOneBudgetedAction(week, profile);
            }
        }

        public void FlushRemainingSlots()
        {
            var profile = Profile;
            if (profile == null || _economyManager.Market == null || _planWeek < 0) return;

            while (_slotTicks.Count > 0)
            {
                _slotTicks.RemoveAt(0);
                RunOneBudgetedAction(_planWeek, profile);
            }
        }

        public void EvaluateWeek(int currentWeek)
        {
            var profile = Profile;
            if (profile == null || _economyManager.Market == null) return;

            PlanPeriod(currentWeek, 0, profile);
            RunObligations(currentWeek, profile);
            FlushRemainingSlots();
        }

        // ---------------------------------------------------------------- planning

        private void PlanPeriod(int week, int fromTick, NpcBehaviorProfile profile)
        {
            _planWeek = week;
            _contractSlotsUsed = 0;
            _lastObligationTick = -1;
            _rng = new System.Random(SeedFor(week));

            DropInactiveContracts();
            DropExpiredReactions(week);
            DropFinishedRumourExit(week);
            DropDeadPlans(week);

            _mayPlantRumour = profile.RumourPlantChance > 0f && !profile.NeverSells &&
                              UnityEngine.Random.value < profile.RumourPlantChance;

            _contractReserve = CalculateContractReserve();
            _planReserve = CalculatePlanReserve();
            GrantWeekBudget(week, profile);

            RollSlotTicks(fromTick, profile);
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

        private int SeedFor(int week)
        {
            unchecked
            {
                int hash = (int)2166136261;
                string id = Id ?? TraderName ?? "npc";
                for (int i = 0; i < id.Length; i++)
                    hash = (hash ^ id[i]) * 16777619;
                return hash ^ (week * 397);
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
        private Plan PlanFor(string antiqueId) =>
            antiqueId != null && _plans.TryGetValue(antiqueId, out var plan) ? plan : null;

        /// <summary>The plan for an owned antique, creating a plain resale one if it has none.</summary>
        private Plan PlanForOrCreate(Antique antique, int week) =>
            PlanFor(antique.Id) ?? RecordPlan(antique.Id, antique.PurchasePrice, week, PlanPurpose.Resale);

        private Plan RecordPlan(string antiqueId, float costBasis, int week, PlanPurpose purpose, string contractId = null)
        {
            var plan = new Plan
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

                if (plan.Purpose == PlanPurpose.RumourExit && (_rumourExitWeek < 0 || week >= _rumourClaimWeek))
                    plan.FallBackToResale();

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

        // ---------------------------------------------------------------- restoration bar

        /// <summary>A workshop worth sending one antique to, and what that is worth per week.</summary>
        private readonly struct RestorationLeg
        {
            public readonly RestorationOption Option;
            public readonly float Fee;
            public readonly int Weeks;
            /// <summary>Risk-adjusted value the antique is worth on its return, at today's prices.</summary>
            public readonly float ExpectedValue;
            public readonly float WeeklyReturn;

            public RestorationLeg(RestorationOption option, float fee, int weeks, float expectedValue, float weeklyReturn)
            {
                Option = option;
                Fee = fee;
                Weeks = weeks;
                ExpectedValue = expectedValue;
                WeeklyReturn = weeklyReturn;
            }

            public bool Exists => Option != null;
        }

        /// <summary>How long this trader usually holds a piece before selling it.</summary>
        private int HoldWeeks(NpcBehaviorProfile profile) =>
            profile.PatientSellAfterWeeks > 0 ? profile.PatientSellAfterWeeks : Mathf.Max(1, profile.MinHoldingWeeksBeforeSell);

        /// <summary>
        /// The return per week this trader expects of its own capital: its profit target over its
        /// usual hold. A collector that never sells has no such target, so adding value is enough.
        /// </summary>
        private float TargetWeeklyReturn(NpcBehaviorProfile profile) =>
            profile.NeverSells ? 0f : Mathf.Max(0f, profile.ProfitTargetMultiplier - 1f) / HoldWeeks(profile);

        /// <summary>
        /// What the antique is worth at a condition, with the profile's condition premium and season,
        /// rounded as the market would price it — so a gain too small to show is read as no gain.
        /// </summary>
        private float ValueAtCondition(Antique antique, float perfectValue, float condition, NpcBehaviorProfile profile) =>
            Core.Money.RoundToShilling(perfectValue * condition
                * (1f + profile.ConditionPremium * Mathf.Max(0f, condition - 0.5f))
                * (1f + SeasonalOutlook(antique.DefinitionId, profile)));

        /// <summary>
        /// What one workshop is worth for one antique: the value it is expected to add, less the fee,
        /// the downside the profile fears and the upkeep, over the capital and weeks it ties up.
        /// </summary>
        private RestorationLeg EvaluateLeg(Antique antique, RestorationQuote quote, NpcBehaviorProfile profile, int delayWeeks)
        {
            var option = quote.Option;
            float perfectValue = quote.StartCondition > 0f ? antique.CurrentPrice / quote.StartCondition : 0f;
            if (perfectValue <= 0f) return default;

            float averageFailure = Mathf.Max(quote.WorstCondition,
                quote.StartCondition - (option.FailureLossMin + option.FailureLossMax) * 0.5f);

            float valueNow = ValueAtCondition(antique, perfectValue, quote.StartCondition, profile);
            float expected =
                option.SuccessChance * ValueAtCondition(antique, perfectValue, quote.TargetCondition, profile) +
                option.PartialChance * ValueAtCondition(antique, perfectValue, quote.PartialCondition, profile) +
                option.FailureChance * ValueAtCondition(antique, perfectValue, averageFailure, profile);

            expected -= (1f - profile.RiskTolerance) * option.FailureChance
                        * Mathf.Max(0f, valueNow - ValueAtCondition(antique, perfectValue, quote.WorstCondition, profile));

            int weeks = Mathf.Max(1, quote.DurationWeeks + Mathf.Max(0, delayWeeks));
            float capital = quote.Cost + valueNow;
            if (capital <= 0f) return default;

            float weekly = (expected - valueNow - quote.Cost - weeks * MarginalUpkeepPerItem()) / (capital * weeks);
            return new RestorationLeg(option, quote.Cost, quote.DurationWeeks, expected, weekly);
        }

        /// <summary>
        /// Best workshop for an antique, by risk-adjusted return per pound per week. The cheap gates
        /// run first so a quote is only built for workshops that could take the job.
        /// </summary>
        private RestorationLeg BestLegFor(Antique antique, NpcBehaviorProfile profile, bool owned, int delayWeeks, float barWeeklyReturn)
        {
            if (antique == null || _restorationManager == null || !profile.Restores) return default;
            if (OpenRestorationCount() >= profile.MaxConcurrentRestorations) return default;

            int reputation = Record.Reputation.Reputation;
            RestorationLeg best = default;

            foreach (var option in _restorationManager.Options)
            {
                if (option == null || option.RequiredReputation > reputation) continue;
                if (option.TargetCondition <= antique.Condition + ConditionEpsilon) continue;

                var quote = owned
                    ? _restorationManager.Quote(this, antique, option)
                    : _restorationManager.QuoteProspective(this, antique, option);
                if (quote == null) continue;
                if (owned && !quote.CanStart) continue;
                if (quote.DurationWeeks > profile.MaxRestorationWeeks) continue;
                if (!Core.Money.Covers(Inventory.Cash * profile.MaxRestorationShareOfCash, quote.Cost)) continue;
                if (!PassesCashFloor(quote.Cost)) continue;

                var leg = EvaluateLeg(antique, quote, profile, delayWeeks);
                if (!leg.Exists || leg.WeeklyReturn <= 0f || leg.WeeklyReturn < barWeeklyReturn) continue;

                if (!best.Exists || leg.WeeklyReturn > best.WeeklyReturn) best = leg;
            }

            return best;
        }

        /// <summary>Return per pound per week of buying a listing, restoring it and holding it to sell.</summary>
        private float BuyAndRestoreWeeklyReturn(float buyCost, RestorationLeg leg, NpcBehaviorProfile profile)
        {
            int weeks = Mathf.Max(1, leg.Weeks + HoldWeeks(profile));
            float capital = buyCost + leg.Fee;
            if (capital <= 0f) return 0f;

            return (leg.ExpectedValue - capital - weeks * MarginalUpkeepPerItem()) / (capital * weeks);
        }

        /// <summary>Return per pound per week of buying a listing and selling it on as it stands.</summary>
        private float BuyAsIsWeeklyReturn(Antique listing, float cost, NpcBehaviorProfile profile) =>
            cost > 0f ? BargainScore(listing, cost, profile) / HoldWeeks(profile) : 0f;

        /// <summary>Highest condition any workshop this NPC may use can reach.</summary>
        private float BestAchievableCondition()
        {
            if (_restorationManager == null) return 0f;

            float best = 0f;
            int reputation = Record.Reputation.Reputation;
            foreach (var option in _restorationManager.Options)
                if (option != null && option.RequiredReputation <= reputation)
                    best = Mathf.Max(best, option.TargetCondition);

            return best;
        }

        /// <summary>
        /// A listing the NPC would buy in order to restore: below its condition floor is allowed when
        /// a workshop can bring it back over.
        /// </summary>
        private bool CanConsiderForRestoration(Antique listing, NpcBehaviorProfile profile)
        {
            if (listing == null || Inventory.Owns(listing.Id)) return false;
            if (listing.Condition >= profile.MinConditionToBuy) return true;
            return BestAchievableCondition() >= profile.MinConditionToBuy;
        }

        /// <summary>Chance a finished restoration leaves the antique meeting a contract's requirement.</summary>
        private static float RestorationConfidence(ContractRequirement requirement, Antique antique, RestorationQuote quote)
        {
            var option = quote.Option;
            float confidence = 0f;

            if (requirement.IsSatisfiedAt(antique, quote.TargetCondition)) confidence += option.SuccessChance;
            if (requirement.IsSatisfiedAt(antique, quote.PartialCondition)) confidence += option.PartialChance;
            if (requirement.IsSatisfiedAt(antique, quote.WorstCondition)) confidence += option.FailureChance;

            return confidence;
        }

        /// <summary>How sure of the outcome the profile has to be before it restores for a contract.</summary>
        private static float RequiredConfidence(NpcBehaviorProfile profile) =>
            Mathf.Lerp(0.95f, 0.60f, profile.RiskTolerance);

        /// <summary>
        /// Cheapest workshop that would bring an antique up to a contract's requirement, in time from
        /// <paramref name="fromWeek"/> and with enough confidence for the profile. Null if none would.
        /// </summary>
        private RestorationQuote BestContractQuote(Contract contract, Antique antique, int fromWeek, NpcBehaviorProfile profile, bool owned)
        {
            if (_restorationManager == null || !profile.Restores || !profile.RestoresForContracts) return null;
            if (OpenRestorationCount() >= profile.MaxConcurrentRestorations) return null;

            int latestArrival = LatestUsefulArrivalWeek(contract);
            int reputation = Record.Reputation.Reputation;
            float required = RequiredConfidence(profile);
            RestorationQuote best = null;

            foreach (var option in _restorationManager.Options)
            {
                if (option == null || option.RequiredReputation > reputation) continue;
                if (option.TargetCondition <= antique.Condition + ConditionEpsilon) continue;

                var quote = owned
                    ? _restorationManager.Quote(this, antique, option)
                    : _restorationManager.QuoteProspective(this, antique, option);
                if (quote == null) continue;
                if (owned && !quote.CanStart) continue;
                if (quote.DurationWeeks > profile.MaxRestorationWeeks) continue;

                // A failure leaves the slot empty, so the work has to finish with time to find a replacement.
                if (fromWeek + quote.DurationWeeks + profile.ContractPanicWeeks > latestArrival) continue;
                if (!Core.Money.Covers(Inventory.Cash * profile.MaxRestorationShareOfCash, quote.Cost)) continue;
                if (RestorationConfidence(contract.Requirement, antique, quote) < required) continue;

                if (best == null || quote.Cost < best.Cost) best = quote;
            }

            return best;
        }

        // ---------------------------------------------------------------- non-tick actions

        private void RunObligations(int week, NpcBehaviorProfile profile)
        {
            DropInactiveContracts();
            ReserveMatchingHoldings();
            DropUnfullfillableOpenContracts(week, profile);
            FulfillReadyContracts(week);
            RunStopLoss(profile);
            WithdrawStaleListings(week, profile);
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

        private void DropExpiredReactions(int week)
        {
            for (int i = _pendingReactions.Count - 1; i >= 0; i--)
                if (week > _pendingReactions[i].ReactionWeek + NewsActionWindowWeeks)
                    _pendingReactions.RemoveAt(i);
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

        private void RunStopLoss(NpcBehaviorProfile profile)
        {
            if (profile.NeverSells || profile.StopLossMultiplier <= 0f) return;

            string victim = null;
            float paid = 0f;

            foreach (var pair in Inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding.IsReservedForContract || holding.IsListedForSale) continue;
                if (holding.IsInTransit || holding.IsInRestoration) continue;
                if (!_plans.TryGetValue(holding.Id, out var plan)) continue;
                if (plan.Purpose != PlanPurpose.Resale || plan.HasRestorationLeg) continue;
                if (holding.CurrentPrice >= plan.CostBasis * profile.StopLossMultiplier) continue;

                victim = holding.Id;
                paid = plan.CostBasis;
                break;
            }

            if (victim == null) return;

            var sold = Inventory.GetHolding(victim);
            Debug.Log($"{TraderName} panic sells {sold?.Name}: worth {Core.Money.Format(sold?.CurrentPrice ?? 0f)}, paid {Core.Money.Format(paid)}.");
            if (SellListing(victim)) _plans.Remove(victim);
        }

        /// <summary>Withdraws one of its own listings that has sat unsold past RelistAfterWeeks.</summary>
        private void WithdrawStaleListings(int week, NpcBehaviorProfile profile)
        {
            if (!profile.ListsOnMarket) return;

            string stale = null;
            foreach (var pair in Inventory.Holdings)
            {
                var holding = pair.Value;
                if (!holding.IsListedForSale) continue;
                if (holding.MarketListedOnWeek < 0) continue;
                if (week - holding.MarketListedOnWeek < Mathf.Max(1, profile.RelistAfterWeeks)) continue;

                stale = holding.Id;
                break;
            }

            if (stale != null) Inventory.CancelListing(_economyManager.Market, stale);
        }

        // ---------------------------------------------------------------- plan tick actions

        /// <summary>
        /// Carries out the next step of a plan already committed to: starting a restoration costed
        /// when the antique was acquired, or listing and then dumping stock into a planted rumour.
        /// </summary>
        private bool TierExecutePlans(int week, NpcBehaviorProfile profile)
        {
            if (_plans.Count == 0) return false;

            _planScratch.Clear();
            _planScratch.AddRange(_plans.Keys);

            for (int i = 0; i < _planScratch.Count; i++)
            {
                string antiqueId = _planScratch[i];
                var plan = PlanFor(antiqueId);
                var antique = Inventory.GetHolding(antiqueId);
                if (plan == null || antique == null) continue;

                if (plan.HasRestorationLeg && StartPlannedRestoration(antique, plan, profile)) return true;
                if (plan.Purpose == PlanPurpose.RumourExit && RunRumourExitStep(antique, plan, week, profile)) return true;
            }

            return false;
        }

        /// <summary>
        /// Re-quotes a plan's restoration at today's prices and starts it, or clears the leg when it
        /// no longer pays. The fee joins the antique's cost basis so later sales price it in.
        /// </summary>
        private bool StartPlannedRestoration(Antique antique, Plan plan, NpcBehaviorProfile profile)
        {
            if (_restorationManager == null)
            {
                plan.ClearRestorationLeg();
                return false;
            }

            if (IsRestoring(antique.Id)) return false;
            if (!antique.IsAvailable || antique.IsListedForSale || antique.IsReservedForContract) return false;

            var option = _restorationManager.Settings.Get(plan.RestorationOptionId);
            var quote = option != null ? _restorationManager.Quote(this, antique, option) : null;

            bool viable = quote != null && quote.CanStart && PassesCashFloor(quote.Cost);

            if (viable)
                viable = plan.Purpose == PlanPurpose.Contract
                    ? RestorationStillServesContract(antique, plan, quote, profile)
                    : ClearsResaleBar(EvaluateLeg(antique, quote, profile, 0), profile);

            if (!viable)
            {
                plan.ClearRestorationLeg();
                _planReserve = CalculatePlanReserve();
                return false;
            }

            if (!_restorationManager.StartRestoration(this, antique, option.Id))
            {
                plan.ClearRestorationLeg();
                _planReserve = CalculatePlanReserve();
                return false;
            }

            plan.CostBasis = Core.Money.RoundToShilling(plan.CostBasis + quote.Cost);
            _planReserve = CalculatePlanReserve();
            Debug.Log($"{TraderName} sent {antique.Name} to {option.DisplayName} for {Core.Money.Format(quote.Cost)} ({plan.Purpose}).");
            return true;
        }

        /// <summary>Whether a leg is worth paying for on its own merits, with no contract behind it.</summary>
        private bool ClearsResaleBar(RestorationLeg leg, NpcBehaviorProfile profile) =>
            leg.Exists && leg.WeeklyReturn > 0f
            && leg.WeeklyReturn >= TargetWeeklyReturn(profile) * profile.RestorationHurdle;

        /// <summary>True while the plan's contract is still live and the work would still land in time.</summary>
        private bool RestorationStillServesContract(Antique antique, Plan plan, RestorationQuote quote, NpcBehaviorProfile profile)
        {
            var contract = _contractManager != null ? _contractManager.GetById(plan.ContractId) : null;
            if (contract == null || contract.Status != ContractStatus.Active) return false;
            if (CurrentWeek + quote.DurationWeeks + profile.ContractPanicWeeks > LatestUsefulArrivalWeek(contract)) return false;

            return RestorationConfidence(contract.Requirement, antique, quote) >= RequiredConfidence(profile);
        }

        /// <summary>
        /// Lists a rumour-scope holding at the exit price while the pump runs, then cancels and sells
        /// to the market from the plan's dump week.
        /// </summary>
        private bool RunRumourExitStep(Antique antique, Plan plan, int week, NpcBehaviorProfile profile)
        {
            if (profile.NeverSells) return false;
            if (antique.IsReservedForContract || antique.IsInTransit || antique.IsInRestoration) return false;

            if (week >= plan.DumpAfterWeek)
            {
                if (antique.IsListedForSale)
                {
                    // A standing tag that still nets more than the market pays is left to run.
                    if (Inventory.Modifiers.ListingProceeds(antique.AskingPrice) > antique.CurrentPrice) return false;
                    Inventory.CancelListing(_economyManager.Market, antique.Id);
                }

                if (!SellListing(antique.Id)) return false;

                _plans.Remove(antique.Id);
                Debug.Log($"{TraderName} sold {antique.Name} to the market, into the rumour it planted.");
                return true;
            }

            if (!profile.ListsIntoOwnRumour || !profile.ListsOnMarket) return false;
            if (plan.ExitAskingPrice <= 0f) return false;
            if (antique.IsListedForSale && antique.AskingPrice >= plan.ExitAskingPrice) return false;
            if (!antique.IsAvailable) return false;

            if (antique.IsListedForSale) Inventory.CancelListing(_economyManager.Market, antique.Id);
            if (!Inventory.ListForSale(_economyManager.Market, antique.Id, plan.ExitAskingPrice, week)) return false;

            Debug.Log($"{TraderName} listed {antique.Name} at {Core.Money.Format(plan.ExitAskingPrice)} into its own rumour.");
            return true;
        }

        /// <summary>A finished restoration ends the leg; the plan's remaining purpose runs from the next slot.</summary>
        private void HandleRestorationCompleted(RestorationRecord record)
        {
            if (record == null || !Inventory.Owns(record.AntiqueId)) return;

            var plan = PlanFor(record.AntiqueId);
            if (plan == null) return;

            plan.ClearRestorationLeg();
            _planReserve = CalculatePlanReserve();
        }

        // ---------------------------------------------------------------- tick action priority ladder
        private void RunOneBudgetedAction(int week, NpcBehaviorProfile profile)
        {
            Diagnostics.Telemetry.NpcActionBegin(this);
            string tier = RunOneBudgetedActionCore(week, profile);
            Diagnostics.Telemetry.NpcActionEnd(this, week, tier);
        }
        private string RunOneBudgetedActionCore(int week, NpcBehaviorProfile profile)
        {
            if (TierContractRescue(week, profile)) return nameof(TierContractRescue);
            Diagnostics.Telemetry.TierDeclined(this, nameof(TierContractRescue));

            if (TierExecutePlans(week, profile)) return nameof(TierExecutePlans);
            Diagnostics.Telemetry.TierDeclined(this, nameof(TierExecutePlans));

            if (TierRumourExit(week, profile)) return nameof(TierRumourExit);
            Diagnostics.Telemetry.TierDeclined(this, nameof(TierRumourExit));

            if (TierDamageControl(week, profile)) return nameof(TierDamageControl);
            Diagnostics.Telemetry.TierDeclined(this, nameof(TierDamageControl));

            if (TierNewsBuy(week, profile)) return nameof(TierNewsBuy);
            Diagnostics.Telemetry.TierDeclined(this, nameof(TierNewsBuy));

            if (TierContractWork(week, profile)) return nameof(TierContractWork);
            Diagnostics.Telemetry.TierDeclined(this, nameof(TierContractWork));

            if (TierReviewHoldings(week, profile)) return nameof(TierReviewHoldings);
            Diagnostics.Telemetry.TierDeclined(this, nameof(TierReviewHoldings));

            if (TierTakeContract(week, profile)) return nameof(TierTakeContract);
            Diagnostics.Telemetry.TierDeclined(this, nameof(TierTakeContract));

            if (TierPlantRumour(week, profile)) return nameof(TierPlantRumour);
            Diagnostics.Telemetry.TierDeclined(this, nameof(TierPlantRumour));

            if (TierRestoreStock(week, profile)) return nameof(TierRestoreStock);
            Diagnostics.Telemetry.TierDeclined(this, nameof(TierRestoreStock));

            if (TierBrowseMarket(week, profile)) return nameof(TierBrowseMarket);
            Diagnostics.Telemetry.TierDeclined(this, nameof(TierBrowseMarket));

            return null;
        }

        private int ContractSlotsAllowed(NpcBehaviorProfile profile)
        {
            int full = Mathf.Clamp(Mathf.RoundToInt(profile.ActionsPerPeriod * Mathf.Max(0f, ActivityScale)), 1, Mathf.Max(1, TicksPerPeriod));
            return Mathf.Max(1, Mathf.CeilToInt(full * profile.ContractSlotShare));
        }

        // ---------------------------------------------------------------- contract tick actions

        private bool TierContractRescue(int week, NpcBehaviorProfile profile) =>
            WorkOnContracts(week, profile, urgentOnly: true);

        private bool TierContractWork(int week, NpcBehaviorProfile profile)
        {
            if (_contractSlotsUsed >= ContractSlotsAllowed(profile)) return false;
            return WorkOnContracts(week, profile, urgentOnly: false);
        }

        private bool WorkOnContracts(int week, NpcBehaviorProfile profile, bool urgentOnly)
        {
            if (_contractManager == null) return false;

            CollectActiveContracts(week, profile);
            if (_contractScratch.Count == 0) return false;

            foreach (var contract in _contractScratch)
            {
                int missing = MissingFor(contract);
                if (missing <= 0) continue;

                var urgency = UrgencyOf(contract, week, profile);
                if (urgentOnly && urgency == ContractUrgency.Normal) continue;
                if (!urgentOnly && urgency != ContractUrgency.Normal) continue;

                var offer = BestOfferFor(contract, week, profile, urgency);

                // Restoring stock already in the warehouse costs only the fee plus the resale value given up.
                if (PlanStockRestoration(contract, week, profile, offer))
                {
                    if (!urgentOnly) _contractSlotsUsed++;
                    return true;
                }

                if (offer.Listing == null) continue;

                float cost = offer.Cost;
                if (!Core.Money.Covers(Inventory.Cash, cost)) continue;

                if (!BuyListing(offer.Listing.Id, offer.Option)) continue;

                RecordPlan(offer.Listing.Id, offer.Listing.PurchasePrice, week, PlanPurpose.Contract, contract.ContractId);

                if (offer.NeedsRestoration)
                    AttachRestorationLeg(offer.Listing.Id, offer.RestorationOptionId, offer.RestorationFee,
                        LatestUsefulArrivalWeek(contract) - offer.RestorationWeeks);
                else
                    Inventory.ReserveForContract(offer.Listing.Id, contract.ContractId);

                _contractReserve = CalculateContractReserve();
                if (!urgentOnly) _contractSlotsUsed++;

                Debug.Log($"{TraderName} bought {offer.Listing.Name} for contract {contract.ContractId} ({urgency}, {offer.Option} transport, ready week {offer.ArrivalWeek}).");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Pledges the cheapest held antique a workshop could bring up to a contract's requirement in
        /// time, when that beats buying one. TierExecutePlans starts the work.
        /// </summary>
        private bool PlanStockRestoration(Contract contract, int week, NpcBehaviorProfile profile, Offer offer)
        {
            if (_restorationManager == null || !profile.Restores || !profile.RestoresForContracts) return false;

            float buyCost = offer.Listing != null ? offer.Cost : float.MaxValue;

            Antique chosen = null;
            RestorationQuote chosenQuote = null;
            float chosenCost = buyCost;

            foreach (var pair in Inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding == null || !holding.IsAvailable) continue;
                if (holding.IsReservedForContract || holding.IsListedForSale) continue;

                var existing = PlanFor(holding.Id);
                if (existing != null && (existing.HasRestorationLeg || existing.Purpose == PlanPurpose.RumourExit)) continue;
                if (!contract.Requirement.MatchesAttributes(holding.Type, holding.Country, holding.Century)) continue;
                if (contract.Requirement.IsSatisfiedBy(holding)) continue;

                var quote = BestContractQuote(contract, holding, week, profile, owned: true);
                if (quote == null) continue;

                float cost = quote.Cost + holding.CurrentPrice;
                if (cost >= chosenCost) continue;

                chosen = holding;
                chosenQuote = quote;
                chosenCost = cost;
            }

            if (chosen == null) return false;

            var plan = PlanForOrCreate(chosen, week);
            plan.Purpose = PlanPurpose.Contract;
            plan.ContractId = contract.ContractId;
            AttachRestorationLeg(chosen.Id, chosenQuote.Option.Id, chosenQuote.Cost,
                LatestUsefulArrivalWeek(contract) - chosenQuote.DurationWeeks);

            Debug.Log($"{TraderName} will restore {chosen.Name} at {chosenQuote.Option.DisplayName} " +
                      $"for contract {contract.ContractId} ({Core.Money.Format(chosenQuote.Cost)}).");
            return true;
        }

        private void CollectActiveContracts(int week, NpcBehaviorProfile profile)
        {
            _contractScratch.Clear();
            if (_contractManager == null) return;

            foreach (var contractId in Inventory.CommittedContractIds)
            {
                var contract = _contractManager.GetById(contractId);
                if (contract == null || contract.Status != ContractStatus.Active) continue;
                _contractScratch.Add(contract);
            }

            // Tightest deadline first.
            _contractScratch.Sort((a, b) => a.DeadlineWeek.CompareTo(b.DeadlineWeek));
        }

        private bool TierTakeContract(int week, NpcBehaviorProfile profile)
        {
            if (!profile.TakesContracts || _contractManager == null) return false;
            return ConsiderNewContract(week, profile);
        }

        // ---------------------------------------------------------------- news tick actions

        private bool TierDamageControl(int week, NpcBehaviorProfile profile)
        {
            if (profile.NeverSells) return false;

            for (int i = _pendingReactions.Count - 1; i >= 0; i--)
            {
                var pending = _pendingReactions[i];
                if (pending.ReactionWeek > week) continue;
                if (SellOnNews(pending.News, week, profile)) return true;
            }

            return false;
        }

        /// <summary>
        /// Sells into the rumour this NPC planted, while the price is still carrying the believers'
        /// purchases. Nothing left to sell leaves the exit open for the next slot.
        /// </summary>
        private bool TierRumourExit(int week, NpcBehaviorProfile profile)
        {
            if (profile.NeverSells || _rumourExitWeek < 0) return false;
            if (week < _rumourDumpWeek || week >= _rumourClaimWeek) return false;

            return SellMatching(_rumourExitScope, fallingOnly: false, "into the rumour it planted");
        }

        /// <summary>
        /// Plants a fake rumour to pump stock this NPC already holds, when one pays for itself
        /// (NpcRumourPlanner). The spend comes out of its cash like any other purchase.
        /// </summary>
        private bool TierPlantRumour(int week, NpcBehaviorProfile profile)
        {
            if (!_mayPlantRumour) return false;

            var mill = RumourMill.Current;
            if (mill == null || !mill.CanPlant(this, week))
            {
                _mayPlantRumour = false;
                return false;
            }

            _mayPlantRumour = false;

            if (!NpcRumourPlanner.TryFindPlan(mill, this, week, out var plan)) return false;

            if (plan.Spend > _weekBudget || !PassesCashFloor(plan.Spend)) return false;

            if (!mill.PlantFor(this, plan)) return false;

            _weekBudget = Mathf.Max(0f, _weekBudget - plan.Spend);
            return true;
        }


        private bool TierNewsBuy(int week, NpcBehaviorProfile profile)
        {
            for (int i = _pendingReactions.Count - 1; i >= 0; i--)
            {
                var pending = _pendingReactions[i];
                if (pending.ReactionWeek > week) continue;
                if (BuyOnNews(pending.News, week, profile)) return true;
            }

            return false;
        }

        private bool SellOnNews(NewsItem news, int week, NpcBehaviorProfile profile) =>
            SellMatching(news.NewsData, fallingOnly: true,
                $"ahead of {news.Type} news from week {news.WeekPublished}");

        /// <summary>
        /// Sells the first free holding matching any of <paramref name="effects"/>. With
        /// <paramref name="fallingOnly"/> only effects predicting a fall count (reacting to bad news);
        /// without it every matching scope counts (selling into a rumour this NPC planted).
        /// </summary>
        private bool SellMatching(IReadOnlyList<NewsEventData> effects, bool fallingOnly, string reason)
        {
            if (effects == null) return false;

            foreach (var effect in effects)
            {
                if (effect == null || effect.targetScope == TargetScope.Other) continue;
                if (fallingOnly && effect.affectsPriceUp) continue;

                string victim = null;
                string victimName = null;
                bool wasListed = false;

                foreach (var pair in Inventory.Holdings)
                {
                    var holding = pair.Value;
                    if (holding.IsReservedForContract || holding.IsInTransit || holding.IsInRestoration) continue;
                    if (!MatchesScope(holding, effect)) continue;

                    victim = holding.Id;
                    victimName = holding.Name;
                    wasListed = holding.IsListedForSale;
                    break;
                }

                if (victim == null) continue;

                if (wasListed) Inventory.CancelListing(_economyManager.Market, victim);

                if (!SellListing(victim)) continue;

                _plans.Remove(victim);
                Debug.Log($"{TraderName} sold {victimName} {reason}.");
                return true;
            }

            return false;
        }

        private bool BuyOnNews(NewsItem news, int week, NpcBehaviorProfile profile)
        {
            foreach (var effect in news.NewsData)
            {
                if (effect.targetScope == TargetScope.Other || !effect.affectsPriceUp) continue;

                var listings = ListingsInScope(effect);
                if (listings == null) continue;

                for (int i = 0; i < listings.Count; i++)
                {
                    var listing = listings[i];
                    if (!CanConsider(listing, profile)) continue;

                    float cost = EstimateTotalCost(listing, NpcTransportOption);
                    if (cost > _weekBudget || !IsAcceptablePrice(listing, profile)) continue;
                    if (!PassesCashFloor(cost)) continue;

                    if (!TryBuyForStock(listing, week, cost)) continue;

                    Debug.Log($"{TraderName} bought {listing.Name} on {news.Type} news from week {news.WeekPublished}.");
                    return true;
                }
            }

            return false;
        }

        private List<Antique> ListingsInScope(NewsEventData effect)
        {
            var market = _economyManager.Market;
            if (market == null) return null;

            return effect.targetScope switch
            {
                TargetScope.AntiqueType => market.GetByType(effect.AntiqueType),
                TargetScope.Country => market.GetByCountry(effect.Country),
                TargetScope.Century => market.GetByCentury(effect.Century),
                _ => null
            };
        }

        private static bool MatchesScope(Antique antique, NewsEventData effect) => effect.targetScope switch
        {
            TargetScope.AntiqueType => antique.Definition != null && antique.Definition.Type == effect.AntiqueType,
            TargetScope.Country => antique.Definition != null && antique.Definition.Country == effect.Country,
            TargetScope.Century => antique.Definition != null && antique.Definition.Century == effect.Century,
            _ => false
        };

        // ---------------------------------------------------------------- sell holdings tick actions

        private bool TierReviewHoldings(int week, NpcBehaviorProfile profile)
        {
            if (profile.NeverSells) return false;

            string chosen = null;
            string chosenName = null;
            float askingPrice = 0f;
            float estimatedValue = 0f;

            foreach (var pair in Inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding.IsReservedForContract || holding.IsListedForSale) continue;
                if (holding.IsInTransit || holding.IsInRestoration) continue;
                if (!_plans.TryGetValue(holding.Id, out var plan)) continue;
                if (plan.Purpose != PlanPurpose.Resale || plan.HasRestorationLeg) continue;

                int held = week - plan.AcquiredWeek;
                if (held < profile.MinHoldingWeeksBeforeSell) continue;

                float value = holding.CurrentPrice;
                bool patient = profile.PatientSellAfterWeeks > 0 && held >= profile.PatientSellAfterWeeks;
                float target = patient
                    ? Mathf.Min(profile.PatientProfitMultiplier, profile.ProfitTargetMultiplier)
                    : profile.ProfitTargetMultiplier;

                // A far-sighted trader waits for a coming season and cashes in before one ends.
                float seasonal = SeasonalOutlook(holding.DefinitionId, profile);
                if (seasonal >= SeasonalHoldThreshold && !patient) continue;
                if (seasonal <= -SeasonalHoldThreshold)
                    target = Mathf.Min(target, profile.PatientProfitMultiplier);

                if (value < plan.CostBasis * target) continue;

                chosen = holding.Id;
                chosenName = holding.Name;
                estimatedValue = value;
                askingPrice = Core.Money.RoundToShilling(Mathf.Max(value * profile.ListingMarkup, plan.CostBasis * target));
                break;
            }

            if (chosen == null) return false;

            if (profile.ListsOnMarket)
            {
                if (!Inventory.ListForSale(_economyManager.Market, chosen, askingPrice, week)) return false;

                Debug.Log($"{TraderName} listed {chosenName} for {Core.Money.Format(askingPrice)} (value {Core.Money.Format(estimatedValue)}).");
                return true;
            }

            if (!SellListing(chosen)) return false;

            _plans.Remove(chosen);
            return true;
        }

        // ---------------------------------------------------------------- buy from market tick actions

        /// <summary>
        /// Puts a workshop to stock whose plan has run out: a collector's piece below the condition
        /// it collects at, or a seller's stock that has sat unsold past its patience.
        /// </summary>
        private bool TierRestoreStock(int week, NpcBehaviorProfile profile)
        {
            if (_restorationManager == null || !profile.Restores) return false;
            if (OpenRestorationCount() >= profile.MaxConcurrentRestorations) return false;

            float bar = TargetWeeklyReturn(profile) * profile.RestorationHurdle;
            Antique chosen = null;
            RestorationLeg chosenLeg = default;

            foreach (var pair in Inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding == null || !holding.IsAvailable) continue;
                if (holding.IsReservedForContract || holding.IsListedForSale) continue;

                var plan = PlanFor(holding.Id);
                if (plan == null || plan.Purpose != PlanPurpose.Resale || plan.HasRestorationLeg) continue;

                bool spent = profile.NeverSells
                    ? holding.Condition < profile.MinConditionToBuy
                    : week - plan.AcquiredWeek >= HoldWeeks(profile);
                if (!spent) continue;

                var leg = BestLegFor(holding, profile, owned: true, delayWeeks: 0, barWeeklyReturn: bar);
                if (!leg.Exists || leg.WeeklyReturn <= 0f) continue;
                if (chosenLeg.Exists && leg.WeeklyReturn <= chosenLeg.WeeklyReturn) continue;

                chosen = holding;
                chosenLeg = leg;
            }

            if (chosen == null) return false;

            AttachRestorationLeg(chosen.Id, chosenLeg.Option.Id, chosenLeg.Fee, week + RestorationLegGraceWeeks);
            Debug.Log($"{TraderName} will restore {chosen.Name} at {chosenLeg.Option.DisplayName} ({Core.Money.Format(chosenLeg.Fee)}).");
            return true;
        }

        private bool TierBrowseMarket(int week, NpcBehaviorProfile profile)
        {
            if (_weekBudget <= 0f) return false;

            var market = _economyManager.Market;
            if (market == null) return false;

            var listings = market.Listings;
            int holdWeeks = HoldWeeks(profile);
            float threshold = BargainThreshold(profile) / holdWeeks;
            float restorationBar = TargetWeeklyReturn(profile) * profile.RestorationHurdle;

            Antique bestAsIs = null;
            float bestAsIsWeekly = threshold;
            float bestAsIsCost = 0f;

            Antique bestRestore = null;
            float bestRestoreWeekly = threshold;
            float bestRestoreCost = 0f;
            RestorationLeg bestLeg = default;

            for (int i = 0; i < listings.Count; i++)
            {
                var listing = listings[i];
                if (listing == null || Inventory.Owns(listing.Id)) continue;
                if (!IsInterestedIn(listing.Definition, profile)) continue;
                if (!IsAcceptablePrice(listing, profile)) continue;

                float cost = EstimateTotalCost(listing, NpcTransportOption);
                if (cost <= 0f || !Core.Money.Covers(_weekBudget, cost)) continue;
                if (!PassesCashFloor(cost)) continue;

                if (CanConsider(listing, profile))
                {
                    float weekly = BuyAsIsWeeklyReturn(listing, cost, profile);
                    if (weekly >= bestAsIsWeekly)
                    {
                        bestAsIs = listing;
                        bestAsIsWeekly = weekly;
                        bestAsIsCost = cost;
                    }
                }

                if (!profile.Restores || !profile.BuysToRestore) continue;
                if (!CanConsiderForRestoration(listing, profile)) continue;

                var leg = BestLegFor(listing, profile, owned: false, delayWeeks: TransitWeeksFor(listing, NpcTransportOption),
                    barWeeklyReturn: restorationBar);
                if (!leg.Exists) continue;
                if (!Core.Money.Covers(_weekBudget, cost + leg.Fee) || !PassesCashFloor(cost + leg.Fee)) continue;

                float restoreWeekly = BuyAndRestoreWeeklyReturn(cost, leg, profile);
                if (restoreWeekly < bestRestoreWeekly) continue;

                bestRestore = listing;
                bestRestoreWeekly = restoreWeekly;
                bestRestoreCost = cost;
                bestLeg = leg;
            }

            bool takeRestore = bestRestore != null &&
                               (bestAsIs == null || bestRestoreWeekly >= bestAsIsWeekly * profile.RestorationHurdle);

            if (takeRestore)
            {
                int arrival = week + TransitWeeksFor(bestRestore, NpcTransportOption);
                if (!TryBuyForStock(bestRestore, week, bestRestoreCost)) return false;

                AttachRestorationLeg(bestRestore.Id, bestLeg.Option.Id, bestLeg.Fee, arrival + RestorationLegGraceWeeks);
                Debug.Log($"{TraderName} bought {bestRestore.Name} to restore at {bestLeg.Option.DisplayName} " +
                          $"(return {bestRestoreWeekly:P1} a week, cost {Core.Money.Format(bestRestoreCost + bestLeg.Fee)}).");
                return true;
            }

            if (bestAsIs == null) return false;
            if (!TryBuyForStock(bestAsIs, week, bestAsIsCost)) return false;

            Debug.Log($"{TraderName} bought {bestAsIs.Name} on the open market (return {bestAsIsWeekly:P1} a week, cost {Core.Money.Format(bestAsIsCost)}).");
            return true;
        }

        private int TransitWeeksFor(Antique listing, TransportOption option)
        {
            var quote = QuoteFor(listing, option);
            return quote != null ? quote.DurationWeeks : 0;
        }


        // ---------------------------------------------------------------- valuation

        private float BargainScore(Antique listing, float totalCost, NpcBehaviorProfile profile)
        {
            if (totalCost <= 0f) return 0f;

            float fair = FairValue(listing, profile);
            int holdPeriods = profile.PatientSellAfterWeeks > 0
                ? profile.PatientSellAfterWeeks
                : Mathf.Max(1, profile.MinHoldingWeeksBeforeSell);

            float carry = holdPeriods * MarginalUpkeepPerItem();
            return (fair - totalCost - carry) / totalCost;
        }

        private float BargainThreshold(NpcBehaviorProfile profile)
        {
            float bar = profile.MinBargainScore;
            return HasActiveCommitments() ? bar * CommittedBargainPenalty : bar;
        }

        /// <summary>
        /// What one more item adds to this period's warehouse upkeep. Zero for NPCs until they are
        /// given warehouses; written against the real curve so it starts working when they are.
        /// </summary>
        private float MarginalUpkeepPerItem()
        {
            var warehouse = Inventory.Warehouse;
            if (warehouse == null) return 0f;

            int used = Inventory.UsedSlots;
            return Mathf.Max(0f, warehouse.CalculateWeeklyUpkeep(used + 1) - warehouse.CalculateWeeklyUpkeep(used));
        }

        /// <summary>
        /// What the antique is worth in its condition, with the profile's condition premium — and, for a
        /// far-sighted trader, the part of the coming seasonal price change it believes in.
        /// </summary>
        private float FairValue(Antique listing, NpcBehaviorProfile profile)
        {
            var typeState = _economyManager.Market?.GetTypeState(listing.DefinitionId);
            float baseValue = PriceEngine.CalculateReferencePrice(listing.BasePrice, typeState) * listing.Condition;
            return baseValue * (1f + profile.ConditionPremium * Mathf.Max(0f, listing.Condition - 0.5f))
                             * (1f + SeasonalOutlook(listing.DefinitionId, profile));
        }

        /// <summary>
        /// The seasonal price change this trader expects for a type over its horizon, weighted by its
        /// foresight: 0.06 = expects (and prices in) +6%. 0 for traders who ignore the calendar.
        /// </summary>
        private float SeasonalOutlook(string definitionId, NpcBehaviorProfile profile)
        {
            if (profile.SeasonalForesight <= 0f || profile.SeasonalHorizonWeeks <= 0 || _economyManager == null) return 0f;

            float ratio = _economyManager.SeasonalPriceOutlook(definitionId, profile.SeasonalHorizonWeeks);
            return profile.SeasonalForesight * (ratio - 1f);
        }
        private bool IsAcceptablePrice(Antique listing, NpcBehaviorProfile profile) =>
            listing.SalePrice <= FairValue(listing, profile) * profile.MaxPriceMultiplierWillingToPay;

        /// <summary>A regular purchase may never eat into the cash a taken contract needs.</summary>
        private bool PassesCashFloor(float cost) =>
            Core.Money.Covers(Inventory.Cash - (_contractReserve + _planReserve) * ReserveSafetyMargin, cost);

        private bool HasActiveCommitments()
        {
            if (_contractManager == null) return false;

            foreach (var contractId in Inventory.CommittedContractIds)
            {
                var contract = _contractManager.GetById(contractId);
                if (contract != null && contract.Status == ContractStatus.Active && MissingFor(contract) > 0)
                    return true;
            }

            return false;
        }

        private bool CanConsider(Antique listing, NpcBehaviorProfile profile)
        {
            if (listing == null) return false;
            if (Inventory.Owns(listing.Id)) return false;              // its own listing
            if (listing.Condition < profile.MinConditionToBuy) return false;
            return true;
        }

        private bool IsInterestedIn(AntiqueDefinition def, NpcBehaviorProfile profile)
        {
            if (def == null) return false;
            bool typeOk = profile.PreferredTypes.Count == 0 || profile.PreferredTypes.Contains(def.Type);
            bool countryOk = profile.PreferredCountries.Count == 0 || profile.PreferredCountries.Contains(def.Country);
            bool periodOk = profile.PreferredCenturies.Count == 0 || profile.PreferredCenturies.Contains(def.Century);
            return typeOk && countryOk && periodOk;
        }

        // ---------------------------------------------------------------- contracts

        /// <summary>
        /// Latest day an item may arrive and still be handed in.
        /// </summary>
        private static int LatestUsefulArrivalWeek(Contract contract) => contract.DeadlineWeek - 1;

        /// <summary>Items still needed: quantity minus what is reserved and still qualifies.</summary>
        private int MissingFor(Contract contract) =>
            Mathf.Max(0, contract.Requirement.Quantity - ReservedCountFor(contract) - RestoringForCount(contract));

        /// <summary>Reserved for this contract and still matching In transit included.</summary>
        private int ReservedCountFor(Contract contract)
        {
            int count = 0;
            foreach (var pair in Inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding.ReservedForContractId != contract.ContractId) continue;
                if (!contract.Requirement.IsSatisfiedBy(holding)) continue;
                count++;
            }
            return count;
        }

        /// <summary>
        /// Held antiques promised to this contract through a restoration plan. They cannot be reserved
        /// until they qualify, so they are counted here to stop the NPC buying a replacement as well.
        /// </summary>
        private int RestoringForCount(Contract contract)
        {
            int count = 0;
            foreach (var pair in _plans)
            {
                var plan = pair.Value;
                if (plan.Purpose != PlanPurpose.Contract || plan.ContractId != contract.ContractId) continue;
                if (!plan.HasRestorationLeg) continue;

                var holding = Inventory.GetHolding(pair.Key);
                if (holding == null || holding.IsReservedForContract) continue;
                count++;
            }
            return count;
        }

        /// <summary>Reserved, arrived and still matching. Only items that can be currently handed in.</summary>
        private int ReadyCountFor(Contract contract)
        {
            int count = 0;
            foreach (var pair in Inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding.ReservedForContractId != contract.ContractId) continue;
                if (!holding.IsAvailable) continue;
                if (!contract.Requirement.IsSatisfiedBy(holding)) continue;
                count++;
            }
            return count;
        }

        /// <summary>
        /// Slack is the deadline minus today minus the worst transit still ahead of this contract —
        /// both the shipments already flying and the trips the missing items have yet to make.
        /// </summary>
        private ContractUrgency UrgencyOf(Contract contract, int week, NpcBehaviorProfile profile)
        {
            if (MissingFor(contract) <= 0) return ContractUrgency.Normal;

            int slack = LatestUsefulArrivalWeek(contract) - week - WorstTransitAhead(contract);

            if (slack <= 0) return ContractUrgency.Critical;
            if (slack <= profile.ContractPanicWeeks) return ContractUrgency.Pressing;
            return ContractUrgency.Normal;
        }

        private int WorstTransitAhead(Contract contract)
        {
            int worst = 1;

            foreach (var pair in Inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding.ReservedForContractId != contract.ContractId) continue;
                if (!holding.IsInTransit) continue;
                worst = Mathf.Max(worst, holding.ArrivalWeek - CurrentWeek);
            }

            foreach (var pair in _plans)
            {
                var plan = pair.Value;
                if (plan.Purpose != PlanPurpose.Contract || plan.ContractId != contract.ContractId) continue;
                if (!plan.HasRestorationLeg) continue;

                var holding = Inventory.GetHolding(pair.Key);
                if (holding == null) continue;

                int ready = holding.IsInRestoration
                    ? holding.RestorationEndWeek - CurrentWeek
                    : plan.StartRestoreByWeek - CurrentWeek;
                worst = Mathf.Max(worst, ready);
            }

            if (MissingFor(contract) > 0 && _transportManager != null && _transportManager.Settings != null)
                worst = Mathf.Max(worst, _transportManager.Settings.GetBaseDurationWeeks(ShippingZone.Domestic, NpcTransportOption));

            return Mathf.Max(1, worst);
        }

        /// <summary>
        /// Cheapest matching listing that can still land in time, re-quoted now. The transport option
        /// escalates with urgency and with how little condition margin the item has to lose.
        /// </summary>
        private Offer BestOfferFor(Contract contract, int week, NpcBehaviorProfile profile, ContractUrgency urgency)
        {
            var market = _economyManager.Market;
            if (market == null) return default;

            int latestArrival = LatestUsefulArrivalWeek(contract);
            var listings = market.Listings;

            Offer best = default;
            float bestCost = float.MaxValue;

            for (int i = 0; i < listings.Count; i++)
            {
                var listing = listings[i];
                if (Inventory.Owns(listing.Id)) continue;
                if (!IsAcceptablePrice(listing, profile)) continue;

                var option = TransportOptionFor(listing, contract, urgency);
                var quote = QuoteFor(listing, option);
                int arrival = quote != null ? quote.ArrivalWeek : week;
                int duration = quote != null ? quote.DurationWeeks : 0;
                float shipped = listing.SalePrice + (quote != null ? quote.Cost : 0f);

                if (contract.Requirement.IsSatisfiedBy(listing))
                {
                    if (arrival > latestArrival) continue;
                    if (shipped > contract.RewardPerUnit || shipped >= bestCost) continue;

                    best = new Offer(listing, option, shipped, arrival, duration);
                    bestCost = shipped;
                    continue;
                }

                if (!profile.Restores || !profile.RestoresForContracts || !profile.BuysToRestore) continue;

                var restoration = BestContractQuote(contract, listing, arrival, profile, owned: false);
                if (restoration == null) continue;

                int ready = arrival + restoration.DurationWeeks;
                if (ready > latestArrival) continue;

                float total = shipped + restoration.Cost;
                if (total > contract.RewardPerUnit || total >= bestCost) continue;

                best = new Offer(listing, option, total, ready, duration + restoration.DurationWeeks,
                    restoration.Option.Id, restoration.Cost, restoration.DurationWeeks);
                bestCost = total;
            }

            return best;
        }

        private TransportOption TransportOptionFor(Antique listing, Contract contract, ContractUrgency urgency)
        {
            if (contract == null) return NpcTransportOption;
            if (urgency != ContractUrgency.Normal) return TransportOption.Express;

            var requirement = contract.Requirement;
            if (!requirement.HasMinCondition && !requirement.HasMinValue) return NpcTransportOption;

            float maxLoss = _transportManager != null && _transportManager.Settings != null
                ? _transportManager.Settings.MaxConditionLoss
                : 0f;

            // Damage lowers condition, and the price falls with it (price is linear in condition), so
            // an item close to either floor may arrive no longer qualifying.
            bool conditionAtRisk = requirement.HasMinCondition && listing.Condition - requirement.MinCondition < maxLoss;
            bool valueAtRisk = requirement.HasMinValue && listing.Condition > 0f &&
                               listing.CurrentPrice * (listing.Condition - maxLoss) / listing.Condition < requirement.MinAcceptedValue;

            return conditionAtRisk || valueAtRisk ? TransportOption.Express : NpcTransportOption;
        }

        private TransportQuote QuoteFor(Antique listing, TransportOption option) =>
            _transportManager != null ? _transportManager.Quote(listing, option, buyer: Inventory) : null;


        private bool IsStillReachable(Contract contract, int missing, int week, NpcBehaviorProfile profile)
        {
            int perPeriod = Mathf.Max(1, ContractSlotsAllowed(profile));
            int latestArrival = LatestUsefulArrivalWeek(contract);

            BuildOfferPlan(contract, week, profile, missing);
            if (_offerScratch.Count < missing) return false;

            for (int k = 0; k < missing; k++)
            {
                int buyWeek = week + k / perPeriod;
                if (buyWeek + _offerScratch[k].DurationWeeks > latestArrival) return false;
            }

            return true;
        }

        private void BuildOfferPlan(Contract contract, int week, NpcBehaviorProfile profile, int missing)
        {
            _offerScratch.Clear();

            var market = _economyManager.Market;
            if (market == null || missing <= 0) return;

            int latestArrival = LatestUsefulArrivalWeek(contract);
            var listings = market.Listings;
            var urgency = ContractUrgency.Normal;

            for (int i = 0; i < listings.Count; i++)
            {
                var listing = listings[i];
                if (Inventory.Owns(listing.Id)) continue;
                if (!contract.Requirement.IsSatisfiedBy(listing)) continue;
                if (!IsAcceptablePrice(listing, profile)) continue;

                var option = TransportOptionFor(listing, contract, urgency);
                var quote = QuoteFor(listing, option);
                int arrival = quote != null ? quote.ArrivalWeek : week;
                if (arrival > latestArrival) continue;

                float cost = listing.SalePrice + (quote != null ? quote.Cost : 0f);
                if (cost > contract.RewardPerUnit) continue;

                _offerScratch.Add(new Offer(listing, option, cost, arrival, quote != null ? quote.DurationWeeks : 0));
            }

            _offerScratch.Sort((a, b) => a.Cost.CompareTo(b.Cost));
            if (_offerScratch.Count > missing) _offerScratch.RemoveRange(missing, _offerScratch.Count - missing);

            _offerScratch.Sort((a, b) => b.DurationWeeks.CompareTo(a.DurationWeeks));
        }

        /// <summary>
        /// Takes the most profitable contract the NPC can actually finish.
        /// </summary>
        private bool ConsiderNewContract(int week, NpcBehaviorProfile profile)
        {
            int reputation = Record.Reputation.Reputation;

            var candidates = _contractManager.OpenContracts
                .Where(c => c != null && !Inventory.IsCommittedToContract(c.ContractId))
                .Concat(_contractManager.ExclusiveContracts.Where(c => c != null && c.CanBeClaimed))
                .Where(c => c.RequiredReputation <= reputation && c.Status == ContractStatus.Active)
                .ToList();

            if (candidates.Count == 0) return false;

            float cashLimit = Inventory.Cash * ContractCashShare - _contractReserve;
            if (cashLimit <= 0f) return false;

            Contract chosen = null;
            float bestProfit = 0f;

            foreach (var contract in candidates)
            {
                int missing = contract.Requirement.Quantity - ReservedCountFor(contract);
                if (missing <= 0) continue;

                BuildOfferPlan(contract, week, profile, missing);
                if (_offerScratch.Count < missing) continue;

                float purchaseCost = 0f;
                for (int k = 0; k < _offerScratch.Count; k++) purchaseCost += _offerScratch[k].Cost;
                if (purchaseCost > cashLimit) continue;

                if (!IsStillReachable(contract, missing, week, profile)) continue;

                float minProfit = contract.Type == ContractType.Exclusive
                    ? contract.TotalReward * ExclusiveMinProfitShare
                    : 0f;

                float profit = contract.TotalReward - purchaseCost;
                if (profit <= minProfit) continue;

                if (chosen == null || profit > bestProfit)
                {
                    chosen = contract;
                    bestProfit = profit;
                }
            }

            if (chosen == null) return false;

            if (chosen.Type == ContractType.Exclusive && !_contractManager.ClaimContract(chosen.ContractId, Id))
                return false;

            Inventory.CommitToContract(chosen.ContractId);
            Record.ContractAccepted();

            // Exclusive contracts are recorded by ClaimContract above; open ones are never claimed.
            if (chosen.Type != ContractType.Exclusive)
                _contractManager.RecordTakenOn(chosen, Id);

            _contractReserve = CalculateContractReserve();
            _weekBudget = Mathf.Min(_weekBudget, Mathf.Max(0f, Inventory.Cash - _contractReserve));

            Debug.Log($"{TraderName} picked up {chosen.Type} contract {chosen.ContractId} — needs {chosen.Requirement.Quantity}, due week {chosen.DeadlineWeek}, expected profit {bestProfit:F0}.");
            return true;
        }

        // ---------------------------------------------------------------- deliveries 

        private void HandleShipmentDelivered(Shipment shipment, Antique antique, float conditionLost)
        {
            if (shipment == null || antique == null) return;
            if (!ReferenceEquals(shipment.Recipient, Inventory)) return;
            if (conditionLost <= 0f) return;

            string contractId = antique.ReservedForContractId;
            if (string.IsNullOrEmpty(contractId) || _contractManager == null) return;

            var contract = _contractManager.GetById(contractId);
            if (contract == null || contract.Requirement.IsSatisfiedBy(antique)) return;

            Inventory.ReleaseReservation(antique.Id);

            if (PlanRescueRestoration(contract, antique))
            {
                Debug.Log($"{TraderName}: {antique.Name} arrived damaged (-{conditionLost:F2}) and goes to a workshop for contract {contractId}.");
                return;
            }


            Debug.Log($"{TraderName}: {antique.Name} arrived damaged (-{conditionLost:F2}) and no longer meets contract {contractId} — reservation released, looking for a replacement.");
        }
        private bool PlanRescueRestoration(Contract contract, Antique antique)
        {
            var profile = Profile;
            if (profile == null) return false;

            var quote = BestContractQuote(contract, antique, CurrentWeek, profile, owned: true);
            if (quote == null) return false;

            var plan = PlanForOrCreate(antique, CurrentWeek);
            plan.Purpose = PlanPurpose.Contract;
            plan.ContractId = contract.ContractId;
            AttachRestorationLeg(antique.Id, quote.Option.Id, quote.Cost,
                LatestUsefulArrivalWeek(contract) - quote.DurationWeeks);
            return true;
        }

        // ---------------------------------------------------------------- receiving nes

        public void ReceiveNews(NewsItem news, int deliveryWeek)
        {
            var profile = Profile;
            if (profile == null || _economyManager == null || _economyManager.TimeManager == null) return;

            if (news?.NewsData == null || news.NewsData.Count == 0) return;

            float baseTrust = news.Type switch
            {
                NewsType.Official => 1f,
                NewsType.Leak => profile.LeakTrust,
                NewsType.Rumor or NewsType.Fake => profile.RumorTrust,
            };

            float trust = news.Type is NewsType.Rumor or NewsType.Fake
                ? baseTrust * (1f - RumourMill.CurrentSkepticism)
                : baseTrust;

            float expertChance = float.NaN;
            if (news.Type == NewsType.Fake && AccessLevel >= InfoAccessLevel.Expert)
            {
                expertChance = FakeRumourSettings.Active.ExpertDetection * (1f - news.Credibility);
                if (UnityEngine.Random.value < expertChance)
                {
                    Debug.Log($"Trader {TraderName} spotted the fake rumour '{news.OfficialName}'.");
                    Diagnostics.Telemetry.NewsReception(this, news, deliveryWeek, "DetectedFake",
                        baseTrust, trust, float.NaN, float.NaN, expertChance, -1, -1);
                    return;
                }
            }

            float actionChance = trust * news.Credibility * (0.5f + profile.RiskTolerance);
            float trustRoll = UnityEngine.Random.value;

            if (trustRoll > actionChance)
            {
                Debug.Log($"Trader {TraderName} dismissed the {news.Type} news from Week {news.WeekPublished} based on actionChance");
                Diagnostics.Telemetry.NewsReception(this, news, deliveryWeek, "DismissedByTrust",
                    baseTrust, trust, actionChance, trustRoll, expertChance, -1, -1);
                return;
            }

            int delay = UnityEngine.Random.Range(profile.MinReactionDelayWeeks, profile.MaxReactionDelayWeeks + 1);
            int reactionWeek = Mathf.Max(deliveryWeek, _economyManager.TimeManager.CurrentWeek) + delay;

            if (news.EventTriggerWeek <= reactionWeek)
            {
                Debug.Log($"Trader {TraderName} dismissed the {news.Type} news from Week {news.WeekPublished} because the week it would react ({reactionWeek}) is later than the week the event starts ({news.EventTriggerWeek})");
                Diagnostics.Telemetry.NewsReception(this, news, deliveryWeek, "DismissedTooLate",
                    baseTrust, trust, actionChance, trustRoll, expertChance, delay, reactionWeek);
                return;
            }

            Diagnostics.Telemetry.NewsReception(this, news, deliveryWeek, "Queued",
                baseTrust, trust, actionChance, trustRoll, expertChance, delay, reactionWeek);
            _pendingReactions.Add(new PendingReaction { News = news, ReactionWeek = reactionWeek });
        }

        /// <summary>Drops planned reactions to a news item (a debunked rumour).</summary>
        public void ForgetNews(string newsId)
        {
            if (string.IsNullOrEmpty(newsId)) return;

            int removed = _pendingReactions.RemoveAll(p => p.News != null && p.News.Id == newsId);
            if (removed > 0) Debug.Log($"Trader {TraderName} dropped {removed} reaction(s) to debunked news.");
        }

        /// <summary>
        /// Starts selling into a rumour this NPC planted: from <paramref name="fromWeek"/> (once the
        /// believers' purchases have moved the price) until <paramref name="claimWeek"/>, when the
        /// rumour is debunked. Called by RumourMill when the rumour is published.
        /// </summary>
        public void BeginRumourExit(IReadOnlyList<NewsEventData> scope, int fromWeek, int claimWeek, float pumpMultiple = 1f)
        {
            _rumourExitScope.Clear();
            if (scope == null) return;

            foreach (var data in scope)
                if (data != null && data.targetScope != TargetScope.Other)
                    _rumourExitScope.Add(data);

            if (_rumourExitScope.Count == 0) return;

            var profile = Profile;
            int dumpLead = profile != null ? Mathf.Max(1, profile.ExitDumpLeadWeeks) : 1;

            _rumourExitWeek = fromWeek;
            _rumourClaimWeek = claimWeek;
            _rumourDumpWeek = Mathf.Max(fromWeek + 1, claimWeek - dumpLead);

            MarkRumourExitPlans(pumpMultiple, profile);
        }

        private void MarkRumourExitPlans(float pumpMultiple, NpcBehaviorProfile profile)
        {
            if (profile == null) return;

            int week = CurrentWeek;

            foreach (var pair in Inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding == null || holding.IsReservedForContract) continue;
                if (!MatchesAnyExitScope(holding)) continue;

                var plan = PlanForOrCreate(holding, week);
                if (plan.HasRestorationLeg) continue;

                plan.Purpose = PlanPurpose.RumourExit;
                plan.ContractId = null;
                plan.DumpAfterWeek = _rumourDumpWeek;
                plan.ExitAskingPrice = ExitAskingPrice(holding, pumpMultiple, profile);
            }
        }

        private float ExitAskingPrice(Antique antique, float pumpMultiple, NpcBehaviorProfile profile)
        {
            float believed = 1f + (Mathf.Max(1f, pumpMultiple) - 1f) * Mathf.Clamp01(profile.ExitPumpConfidence);
            float asking = Core.Money.RoundToNice(antique.CurrentPrice * believed * Mathf.Max(1f, profile.ExitTargetRatio));

            float floor = antique.CurrentPrice / Mathf.Max(0.01f, 1f - Inventory.Modifiers.ListingFeeRate);

            return asking > floor ? asking : 0f;
        }

        private bool MatchesAnyExitScope(Antique antique)
        {
            for (int i = 0; i < _rumourExitScope.Count; i++)
                if (MatchesScope(antique, _rumourExitScope[i])) return true;
            return false;
        }

        private void DropFinishedRumourExit(int week)
        {
            if (_rumourExitWeek < 0 || week < _rumourClaimWeek) return;

            _rumourExitScope.Clear();
            _rumourExitWeek = -1;
            _rumourClaimWeek = -1;
            _rumourDumpWeek = -1;
        }

        // ---------------------------------------------------------------- transactions

        private bool TryBuyForStock(Antique listing, int week, float cost)
        {
            if (!BuyListing(listing.Id, NpcTransportOption)) return false;

            RecordPlan(listing.Id, listing.PurchasePrice, week, PlanPurpose.Resale);
            _weekBudget = Mathf.Max(0f, _weekBudget - cost);
            return true;
        }

        private float EstimateTotalCost(Antique listing, TransportOption option) =>
            TraderHelper.EstimateTotalCost(listing, _transportManager, option, Inventory);

        public bool BuyListing(string listingId) => BuyListing(listingId, NpcTransportOption);

        public bool BuyListing(string listingId, TransportOption option) =>
            TraderHelper.BuyListing(Inventory, _economyManager.Market, listingId, TraderName, CurrentWeek, _transportManager, option);

        public bool SellListing(string listingId) =>
            TraderHelper.SellListing(Inventory, _economyManager.Market, listingId, TraderName, CurrentWeek);

        // ---------------------------------------------------------------- auctions

        /// <summary>
        /// Most this NPC can bid on one lot (hammer price): its free cash after the contract reserve,
        /// times the profile's auction budget share, leaving <paramref name="costAllowance"/> for the
        /// buyer's premium and transport. Recomputed for every lot, so winning one lowers the next.
        /// </summary>
        public float AuctionBudget(float costAllowance)
        {
            var profile = Profile;
            if (profile == null) return 0f;

            float free = Mathf.Max(0f, Inventory.Cash - CalculateContractReserve() * ReserveSafetyMargin);
            return free * profile.AuctionBudgetShare / (1f + Mathf.Max(0f, costAllowance));
        }

        /// <summary>
        /// This NPC's paddle for a lot, or null if it doesn't want it (outside its interests, below its
        /// condition standard, no budget). The limit starts from the same fair value used on the market,
        /// scaled by the profile's auction multiplier, the lot's extra appeal (<paramref name="valuePremium"/>,
        /// e.g. provenance) and the news it believes about this kind of antique — capped by its budget.
        /// </summary>
        public AuctionBidder CreateAuctionBidder(Antique antique, float valuePremium, float costAllowance, System.Random rng)
        {
            var profile = Profile;
            if (profile == null || antique == null || antique.Definition == null || _economyManager.Market == null) return null;
            if (!IsInterestedIn(antique.Definition, profile)) return null;
            if (antique.Condition < profile.MinConditionToBuy) return null;

            float cap = AuctionBudget(costAllowance);
            if (cap <= 0f) return null;

            float jitter = rng != null ? 0.9f + 0.2f * (float)rng.NextDouble() : 1f;
            float value = FairValue(antique, profile)
                          * profile.AuctionValueMultiplier
                          * (1f + Mathf.Max(0f, valuePremium))
                          * (1f + AuctionNewsBias(antique, profile))
                          * jitter;

            float limit = Mathf.Min(value, cap);
            if (limit <= 0f) return null;

            return new AuctionBidder
            {
                Id = Id,
                Name = TraderName,
                Kind = BidderKind.Npc,
                Limit = limit,
                Cap = cap,
                FeverChance = profile.AuctionFeverChance,
                FeverRaise = profile.AuctionFeverRaise,
                Hesitation = profile.AuctionHesitation
            };
        }

        /// <summary>Remembers a won lot like a market purchase, so the usual selling rules apply to it.</summary>
        public void RecordAuctionPurchase(Antique antique, int week)
        {
            if (antique == null) return;
            RecordPlan(antique.Id, antique.PurchasePrice, week, PlanPurpose.Resale);
        }

        /// <summary>
        /// +premium per believed piece of good news matching the antique, −premium per bad one. Only news
        /// the NPC already reacts to (passed its trust roll, reaction day reached) counts.
        /// </summary>
        private float AuctionNewsBias(Antique antique, NpcBehaviorProfile profile)
        {
            if (profile.AuctionNewsPremium <= 0f) return 0f;

            int week = CurrentWeek;
            float bias = 0f;

            foreach (var pending in _pendingReactions)
            {
                if (pending.ReactionWeek > week || pending.News?.NewsData == null) continue;

                foreach (var effect in pending.News.NewsData)
                {
                    if (effect == null || effect.targetScope == TargetScope.Other) continue;
                    if (!MatchesScope(antique, effect)) continue;

                    bias += effect.affectsPriceUp ? profile.AuctionNewsPremium : -profile.AuctionNewsPremium;
                }
            }

            return Mathf.Clamp(bias, -0.5f, 0.5f);
        }

        // ---------------------------------------------------------------- save / load

        public NPCTraderState CaptureState()
        {
            var state = new NPCTraderState
            {
                Id = Id,
                ProfileId = ProfileId,
                TraderName = TraderName,
                // Informational only: the inventory saves and restores the commitments (EconomyState).
                CommittedContractIds = new List<string>(Inventory.CommittedContractIds),
                Record = Record.CaptureState(),
                PlanWeek = _planWeek,
                BudgetWeek = _budgetWeek,
                SlotTicks = new List<int>(_slotTicks),
                WeekBudget = _weekBudget,
                ContractReserve = _contractReserve,
                PlanReserve = _planReserve,
                ContractSlotsUsed = _contractSlotsUsed,
                RumourExitWeek = _rumourExitWeek,
                RumourClaimWeek = _rumourClaimWeek,
                RumourExitScope = new List<NewsEventData>(_rumourExitScope)
            };

            foreach (var pair in _plans)
            {
                var plan = pair.Value;
                state.Acquisitions.Add(new PlanState
                {
                    ListingId = pair.Key,
                    PurchasePrice = plan.CostBasis,
                    Week = plan.AcquiredWeek,
                    Purpose = plan.Purpose,
                    ContractId = plan.ContractId,
                    RestorationOptionId = plan.RestorationOptionId,
                    StartRestoreByWeek = plan.StartRestoreByWeek,
                    AssumedFee = plan.AssumedFee,
                    ExitAskingPrice = plan.ExitAskingPrice,
                    DumpAfterWeek = plan.DumpAfterWeek
                });
            }

            foreach (var pending in _pendingReactions)
            {
                state.PendingReactions.Add(new PendingReactionState
                {
                    ReactionWeek = pending.ReactionWeek,
                    News = NewsItemState.Capture(pending.News)
                });
            }

            return state;
        }

        public void RestoreState(NPCTraderState state)
        {
            if (state == null) return;

            if (!string.IsNullOrEmpty(state.Id))
                Id = state.Id;

            if (!string.IsNullOrEmpty(state.ProfileId) && state.ProfileId != ProfileId)
                Debug.LogWarning($"NPCTrader: '{TraderName}' was constructed with profile '{ProfileId}' but the save says '{state.ProfileId}'. Construct the NPC from the saved ProfileId.");

            // Saves from before NPC records start them fresh.
            Record.RestoreState(state.Record);

            _plans.Clear();
            if (state.Acquisitions != null)
            {
                foreach (var planState in state.Acquisitions)
                {
                    if (planState == null || string.IsNullOrEmpty(planState.ListingId)) continue;

                    _plans[planState.ListingId] = new Plan
                    {
                        CostBasis = planState.PurchasePrice,
                        AcquiredWeek = planState.Week,
                        Purpose = planState.Purpose,
                        ContractId = planState.ContractId,
                        RestorationOptionId = planState.RestorationOptionId,
                        StartRestoreByWeek = planState.StartRestoreByWeek,
                        AssumedFee = planState.AssumedFee,
                        ExitAskingPrice = planState.ExitAskingPrice,
                        DumpAfterWeek = planState.DumpAfterWeek
                    };
                }
            }

            _pendingReactions.Clear();
            if (state.PendingReactions != null)
            {
                foreach (var pending in state.PendingReactions)
                {
                    var news = pending?.News?.Restore();
                    if (news == null) continue;

                    _pendingReactions.Add(new PendingReaction { News = news, ReactionWeek = pending.ReactionWeek });
                }
            }

            // The period in progress. A save from before intra-period acting carries PlanWeek 0,
            // which leaves _planWeek at -1 and makes the next tick plan the period from scratch.
            _rumourExitScope.Clear();
            if (state.RumourExitScope != null) _rumourExitScope.AddRange(state.RumourExitScope);
            _rumourExitWeek = _rumourExitScope.Count > 0 ? state.RumourExitWeek : -1;
            _rumourDumpWeek = state.RumourDumpWeek > 0 ? state.RumourDumpWeek : _rumourExitWeek;
            _rumourClaimWeek = state.RumourClaimWeek;
            _planWeek = state.PlanWeek > 0 ? state.PlanWeek : -1;
            _weekBudget = state.WeekBudget;
            _budgetWeek = state.BudgetWeek > 0 ? state.BudgetWeek : _planWeek;
            _contractReserve = state.ContractReserve;
            _planReserve = state.PlanReserve;
            _contractSlotsUsed = state.ContractSlotsUsed;
            _rumourDumpWeek = state.RumourDumpWeek > 0 ? state.RumourDumpWeek : _rumourExitWeek;
            _lastObligationTick = -1;
            _rng = new System.Random(SeedFor(Mathf.Max(1, _planWeek)));

            _slotTicks.Clear();
            if (state.SlotTicks != null)
            {
                _slotTicks.AddRange(state.SlotTicks);
                _slotTicks.Sort();
            }

            // CommittedContractIds is not restored here: the inventory restores its own commitments
            // when the economy is loaded (after the NPCs).
        }

    }

    public enum ContractUrgency
    {
        /// <summary> There are more days left than ContractPanicWeeks.</summary>
        Normal,
        /// <summary>Equal or fewer days than ContractPanicWeeks. Handling contracts is done by every slot.</summary>
        Pressing,
        /// <summary>The contract may already be unfinishable.</summary>
        Critical
    }
}