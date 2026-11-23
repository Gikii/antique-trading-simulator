using System.Collections.Generic;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Economy;
using UnityEngine;

namespace AntiqueTradingSimulator.Diagnostics
{
    /// <summary>
    /// Accounts for all eleven rungs of NPCTrader's priority ladder on every action slot, so an
    /// idle slot says which rungs had nothing to do and which had something and passed anyway.
    /// </summary>
    public sealed class ActionGateMirror
    {
        /// <summary>Ladder order. Index is the rank written to the table.</summary>
        public static readonly string[] Rungs =
        {
            "TierContractRescue",
            "TierExecutePlans",
            "TierRumourExit",
            "TierContractWork",
            "TierTakeContract",
            "TierDamageControl",
            "TierNewsBuy",
            "TierReviewHoldings",
            "TierPlantRumour",
            "TierRestoreStock",
            "TierBrowseMarket"
        };

        private static readonly int IxContractRescue = RankOf("TierContractRescue");
        private static readonly int IxExecutePlans = RankOf("TierExecutePlans");
        private static readonly int IxRumourExit = RankOf("TierRumourExit");
        private static readonly int IxContractWork = RankOf("TierContractWork");
        private static readonly int IxTakeContract = RankOf("TierTakeContract");
        private static readonly int IxDamageControl = RankOf("TierDamageControl");
        private static readonly int IxNewsBuy = RankOf("TierNewsBuy");
        private static readonly int IxReviewHoldings = RankOf("TierReviewHoldings");
        private static readonly int IxPlantRumour = RankOf("TierPlantRumour");
        private static readonly int IxRestoreStock = RankOf("TierRestoreStock");
        private static readonly int IxBrowseMarket = RankOf("TierBrowseMarket");

        /// <summary>The two rungs that share the contract work queue, by resolved index.</summary>
        private static readonly int[] ContractWorkRungs = { IxContractRescue, IxContractWork };

        /// <summary>NPCTrader.CommittedBargainPenalty, mirrored.</summary>
        private const float CommittedBargainPenalty = 2f;

        private readonly TelemetryRecorder _host;
        private readonly TelemetryRun _run;

        private readonly int[] _candidates = new int[Rungs.Length];
        private readonly bool[] _allowed = new bool[Rungs.Length];
        private readonly bool[] _observable = new bool[Rungs.Length];
        private readonly string[] _detail = new string[Rungs.Length];
        private bool _captured;

        public ActionGateMirror(TelemetryRecorder host)
        {
            _host = host;
            _run = host.Run;
        }

        public static int RankOf(string rung)
        {
            for (int i = 0; i < Rungs.Length; i++)
                if (Rungs[i] == rung) return i;
            return -1;
        }

        // ---------------------------------------------------------------- capture, before the slot runs

        public void Capture(NPCTrader npc, int week)
        {
            _captured = false;

            var profile = npc != null ? npc.Profile : null;
            var inventory = npc != null ? npc.Inventory : null;
            if (profile == null || inventory == null) return;

            var state = npc.CaptureState();
            if (state == null) return;

            for (int i = 0; i < Rungs.Length; i++)
            {
                _candidates[i] = 0;
                _allowed[i] = true;
                _observable[i] = true;
                _detail[i] = "";
            }

            int slotsAllowed = ContractSlotsAllowed(npc, profile);
            bool slotsLeft = state.ContractSlotsUsed < slotsAllowed;
            int contractWork = CommittedWithMissingPieces(inventory);
            int reactionsDue = ReactionsDue(state, week);

            // TierContractRescue and TierContractWork — same gate, same work queue.
            foreach (int index in ContractWorkRungs)
            {
                _allowed[index] = profile.TakesContracts;
                if (!profile.TakesContracts) _detail[index] = "profile does not take contracts";
                else if (!slotsLeft) _detail[index] = $"contract slots spent ({state.ContractSlotsUsed}/{slotsAllowed})";
                else if (contractWork == 0) _detail[index] = "no committed contract is short of pieces";

                _candidates[index] = profile.TakesContracts && slotsLeft ? contractWork : 0;
            }

            // TierExecutePlans — a plan with a restoration leg to start, or a rumour exit to run.
            int planWork = 0;
            if (state.Acquisitions != null)
            {
                foreach (var plan in state.Acquisitions)
                {
                    if (plan == null) continue;
                    bool hasLeg = !string.IsNullOrEmpty(plan.RestorationOptionId);
                    if (hasLeg || plan.Purpose == PlanPurpose.RumourExit) planWork++;
                }
            }
            _candidates[IxExecutePlans] = planWork;
            if (planWork == 0) _detail[IxExecutePlans] = "no plan has a restoration leg or a rumour exit";

            // TierRumourExit — inside its own planted rumour's dump window, holding something in scope.
            bool exitWindow = state.RumourExitWeek >= 0
                              && week >= state.RumourDumpWeek
                              && week < state.RumourClaimWeek;
            _allowed[IxRumourExit] = !profile.NeverSells;
            if (profile.NeverSells) _detail[IxRumourExit] = "profile never sells";
            else if (!exitWindow) _detail[IxRumourExit] = state.RumourExitWeek < 0
                ? "no rumour of its own is running"
                : $"outside the dump window (weeks {state.RumourDumpWeek}-{state.RumourClaimWeek})";
            _candidates[IxRumourExit] = !profile.NeverSells && exitWindow ? HoldingsInScope(inventory, state) : 0;

            // TierDamageControl, TierNewsBuy — both driven by reactions whose week has come.
            _allowed[IxDamageControl] = !profile.NeverSells;
            if (profile.NeverSells) _detail[IxDamageControl] = "profile never sells";
            else if (reactionsDue == 0) _detail[IxDamageControl] = "no news reaction is due";
            _candidates[IxDamageControl] = !profile.NeverSells ? reactionsDue : 0;

            _candidates[IxNewsBuy] = reactionsDue;
            if (reactionsDue == 0) _detail[IxNewsBuy] = "no news reaction is due";

            // TierReviewHoldings — a resale holding, unencumbered, past its minimum hold.
            _allowed[IxReviewHoldings] = !profile.NeverSells;
            int reviewable = profile.NeverSells ? 0 : Reviewable(inventory, state, profile, week);
            _candidates[IxReviewHoldings] = reviewable;
            if (profile.NeverSells) _detail[IxReviewHoldings] = "profile never sells";
            else if (reviewable == 0) _detail[IxReviewHoldings] = "no unencumbered resale holding is past its minimum hold";

            // TierTakeContract — reputation-eligible contracts not already taken.
            _allowed[IxTakeContract] = profile.TakesContracts;
            int takeable = profile.TakesContracts ? EligibleContracts(npc, inventory) : 0;
            _candidates[IxTakeContract] = takeable;
            if (!profile.TakesContracts) _detail[IxTakeContract] = "profile does not take contracts";
            else if (takeable == 0) _detail[IxTakeContract] = "no active contract clears its reputation gate";

            // TierPlantRumour — the weekly permission flag is private, so this is as far as it goes.
            _observable[IxPlantRumour] = false;
            _candidates[IxPlantRumour] = 0;
            _detail[IxPlantRumour] = profile.RumourPlantChance <= 0f
                ? "profile never plants rumours"
                : $"weekly plant permission is private; budget {state.WeekBudget:0}";
            _allowed[IxPlantRumour] = profile.RumourPlantChance > 0f;

            // TierRestoreStock — a resale holding worth restoring, with a workshop slot free.
            int openJobs = OpenRestorations(inventory);
            bool jobSlotFree = openJobs < profile.MaxConcurrentRestorations;
            _allowed[IxRestoreStock] = profile.Restores;
            int restorable = profile.Restores && jobSlotFree ? RestorableHoldings(inventory, state) : 0;
            _candidates[IxRestoreStock] = restorable;
            if (!profile.Restores) _detail[IxRestoreStock] = "profile does not restore";
            else if (!jobSlotFree) _detail[IxRestoreStock] = $"workshop slots full ({openJobs}/{profile.MaxConcurrentRestorations})";
            else if (restorable == 0) _detail[IxRestoreStock] = "no unencumbered resale holding to restore";

            // TierBrowseMarket — the browse funnel already measures this in full.
            bool hasBudget = state.WeekBudget > 0f;
            _candidates[IxBrowseMarket] = hasBudget ? 1 : 0;
            _detail[IxBrowseMarket] = hasBudget
                ? "see fact_browse_funnel for the gate breakdown"
                : "no discretionary budget left this week";

            _captured = true;
        }

        // ---------------------------------------------------------------- emit, after the slot ran

        public void Emit(string traderId, int slotIndex, string firedRung, List<string> declined)
        {
            if (!_captured) return;

            var table = _run.Table(T.FactActionGateName, T.FactActionGateHeader);

            for (int i = 0; i < Rungs.Length; i++)
            {
                string rung = Rungs[i];
                bool fired = rung == firedRung;
                bool reached = fired || (declined != null && declined.Contains(rung));

                string outcome =
                    fired ? "Fired" :
                    !reached ? "NotReached" :
                    !_allowed[i] ? "BlockedByProfile" :
                    !_observable[i] ? "Unobservable" :
                    _candidates[i] > 0 ? "DeclinedWithCandidate" : "NoCandidate";

                _run.Stamp(table)
                    .S(traderId)
                    .I(slotIndex)
                    .S(rung)
                    .I(i)
                    .B(reached)
                    .B(fired)
                    .B(_allowed[i])
                    .I(_observable[i] ? _candidates[i] : (int?)null)
                    .S(outcome)
                    .S(_detail[i])
                    .End();
            }
        }

        // ---------------------------------------------------------------- mirrored helpers

        /// <summary>NPCTrader.ContractSlotsAllowed, mirrored.</summary>
        private int ContractSlotsAllowed(NPCTrader npc, NpcBehaviorProfile profile)
        {
            int ticksPerPeriod = Mathf.Max(1, _host.Clock != null ? _host.Clock.TicksPerPeriod : 28);
            int full = Mathf.Clamp(Mathf.RoundToInt(profile.ActionsPerPeriod * Mathf.Max(0f, npc.ActivityScale)),
                1, ticksPerPeriod);
            return Mathf.Max(1, Mathf.CeilToInt(full * profile.ContractSlotShare));
        }

        private int CommittedWithMissingPieces(TraderInventory inventory)
        {
            if (_host.Board == null) return 0;

            int count = 0;
            foreach (string contractId in inventory.CommittedContractIds)
            {
                var contract = _host.Board.GetById(contractId);
                if (contract == null || contract.Status != ContractStatus.Active) continue;

                int reserved = ContractMath.ReservedMatching(inventory, contract);
                if (contract.Requirement.Quantity - reserved > 0) count++;
            }
            return count;
        }

        private static int ReactionsDue(NPCTraderState state, int week)
        {
            if (state.PendingReactions == null) return 0;

            int count = 0;
            foreach (var pending in state.PendingReactions)
                if (pending != null && pending.ReactionWeek <= week) count++;
            return count;
        }

        private static int HoldingsInScope(TraderInventory inventory, NPCTraderState state)
        {
            if (state.RumourExitScope == null || state.RumourExitScope.Count == 0) return 0;

            int count = 0;
            foreach (var pair in inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding == null || holding.Definition == null) continue;
                if (!holding.IsAvailable) continue;

                foreach (var scope in state.RumourExitScope)
                {
                    if (scope == null) continue;

                    bool match = scope.targetScope switch
                    {
                        Events.EventEffect.TargetScope.AntiqueType => holding.Definition.Type == scope.AntiqueType,
                        Events.EventEffect.TargetScope.Country => holding.Definition.Country == scope.Country,
                        Events.EventEffect.TargetScope.Century => holding.Definition.Century == scope.Century,
                        _ => false
                    };

                    if (match) { count++; break; }
                }
            }
            return count;
        }

        /// <summary>Holdings TierReviewHoldings would look at: resale, unencumbered, past minimum hold.</summary>
        private static int Reviewable(TraderInventory inventory, NPCTraderState state,
            NpcBehaviorProfile profile, int week)
        {
            int count = 0;
            foreach (var pair in inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding == null) continue;
                if (holding.IsReservedForContract || holding.IsListedForSale) continue;
                if (holding.IsInTransit || holding.IsInRestoration) continue;

                var plan = FindPlan(state, holding.Id);
                if (plan == null || plan.Purpose != PlanPurpose.Resale) continue;
                if (!string.IsNullOrEmpty(plan.RestorationOptionId)) continue;
                if (week - plan.Week < profile.MinHoldingWeeksBeforeSell) continue;

                count++;
            }
            return count;
        }

        private static int RestorableHoldings(TraderInventory inventory, NPCTraderState state)
        {
            int count = 0;
            foreach (var pair in inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding == null || !holding.IsAvailable) continue;
                if (holding.IsReservedForContract || holding.IsListedForSale) continue;

                var plan = FindPlan(state, holding.Id);
                if (plan == null || plan.Purpose != PlanPurpose.Resale) continue;
                if (!string.IsNullOrEmpty(plan.RestorationOptionId)) continue;

                count++;
            }
            return count;
        }

        /// <summary>
        /// Active contracts this trader could reach by reputation and has not taken. A reputation
        /// filter only: the full gauntlet is too heavy to run per slot and is measured weekly in
        /// fact_contract_eligibility_week instead.
        /// </summary>
        private int EligibleContracts(NPCTrader npc, TraderInventory inventory)
        {
            if (_host.Board == null) return 0;

            int reputation = npc.Reputation;
            int count = 0;

            foreach (var contract in _host.Board.ActiveContracts)
            {
                if (contract == null) continue;
                if (contract.RequiredReputation > reputation) continue;
                if (inventory.IsCommittedToContract(contract.ContractId)) continue;
                if (contract.Type == ContractType.Exclusive && !contract.CanBeClaimed) continue;

                count++;
            }
            return count;
        }

        private int OpenRestorations(TraderInventory inventory)
        {
            if (_host.Restorations == null) return 0;

            int count = 0;
            foreach (var job in _host.Restorations.Jobs)
                if (job != null && ReferenceEquals(job.Owner, inventory)) count++;
            return count;
        }

        private static PlanState FindPlan(NPCTraderState state, string antiqueId)
        {
            if (state.Acquisitions == null) return null;

            foreach (var plan in state.Acquisitions)
                if (plan != null && plan.ListingId == antiqueId) return plan;
            return null;
        }
    }
}
