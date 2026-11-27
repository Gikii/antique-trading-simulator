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
    /// Capture and restore. Beliefs, learned magnitudes and positions are saved; the agenda is derived.
    /// One part of NPCTrader, which is a partial class.
    /// </summary>
    public partial class NPCTrader
    {
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
                ListingRevisionWeek = _lastListingRevisionWeek,
                BudgetWeek = _budgetWeek,
                SlotTicks = new List<int>(_slotTicks),
                WeekBudget = _weekBudget,
                ContractReserve = _contractReserve,
                PlanReserve = _planReserve,
                ContractSlotsUsed = _contractSlotsUsed
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
                    DumpAfterWeek = plan.DumpAfterWeek,
                    OfferedSinceWeek = plan.OfferedSinceWeek,
                    EntryOutlook = plan.EntryOutlook,
                    ExpectedPeakWeek = plan.ExpectedPeakWeek,
                    ExpectedFadeWeek = plan.ExpectedFadeWeek,
                    TargetPrice = plan.TargetPrice,
                    StopPrice = plan.StopPrice,
                    BasedOnNewsIds = plan.BasedOnNewsIds,
                    DistressSince = plan.DistressSince
                });
            }

            CaptureWorldView(state);
            return state;
        }

        /// <summary>
        /// Saves what the trader believes and what it has learned. Both are hidden state: two
        /// traders differ by what they have heard and lived through, which cannot be recomputed.
        /// </summary>
        private void CaptureWorldView(NPCTraderState state)
        {
            if (WorldView == null) return;

            var beliefs = WorldView.Beliefs;
            for (int i = 0; i < beliefs.Count; i++)
            {
                var belief = beliefs[i];
                state.Beliefs.Add(new BeliefState
                {
                    NewsId = belief.NewsId,
                    OfficialName = belief.OfficialName,
                    Facet = belief.Facet,
                    Direction = belief.Direction,
                    EstimatedMove = belief.EstimatedMove,
                    Spread = belief.Spread,
                    StartWeek = belief.StartWeek,
                    EndWeek = belief.EndWeek,
                    Trust = belief.Trust,
                    HeardWeek = belief.HeardWeek,
                    ActsFromWeek = belief.ActsFromWeek,
                    Source = (int)belief.Source,
                    Checked = belief.Checked,
                    Learned = belief.Learned,
                    PriceAtStart = belief.PriceAtStart,
                    RealisedMove = belief.RealisedMove
                });
            }

            foreach (var pair in WorldView.Memory.Entries)
            {
                state.MagnitudeMemory.Add(new MagnitudeEntryState
                {
                    OfficialName = pair.Key,
                    Mean = pair.Value.Mean,
                    Spread = pair.Value.Spread,
                    Observations = pair.Value.Observations
                });
            }

            for (int i = 0; i < BeliefSourceCount; i++)
                state.SourceTrust.Add(WorldView.StandingTrustIn((BeliefSource)i));
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

                    _plans[planState.ListingId] = new Position
                    {
                        CostBasis = planState.PurchasePrice,
                        AcquiredWeek = planState.Week,
                        Purpose = planState.Purpose,
                        ContractId = planState.ContractId,
                        RestorationOptionId = planState.RestorationOptionId,
                        StartRestoreByWeek = planState.StartRestoreByWeek,
                        AssumedFee = planState.AssumedFee,
                        ExitAskingPrice = planState.ExitAskingPrice,
                        DumpAfterWeek = planState.DumpAfterWeek,
                        OfferedSinceWeek = planState.OfferedSinceWeek,
                        EntryOutlook = planState.EntryOutlook,
                        ExpectedPeakWeek = planState.ExpectedPeakWeek,
                        ExpectedFadeWeek = planState.ExpectedFadeWeek,
                        TargetPrice = planState.TargetPrice,
                        StopPrice = planState.StopPrice,
                        BasedOnNewsIds = planState.BasedOnNewsIds,
                        DistressSince = planState.DistressSince != 0 ? planState.DistressSince : -1
                    };
                }
            }

            // The period in progress. A save from before intra-period acting carries PlanWeek 0,
            // which leaves _planWeek at -1 and makes the next tick plan the period from scratch.
            _planWeek = state.PlanWeek > 0 ? state.PlanWeek : -1;
            _weekBudget = state.WeekBudget;
            _budgetWeek = state.BudgetWeek > 0 ? state.BudgetWeek : _planWeek;
            _contractReserve = state.ContractReserve;
            _planReserve = state.PlanReserve;
            _contractSlotsUsed = state.ContractSlotsUsed;
            _lastObligationTick = -1;
            _lastListingRevisionWeek = state.ListingRevisionWeek > 0 ? state.ListingRevisionWeek : -1;
            _rng = new System.Random(SeedFor(Mathf.Max(1, _planWeek)));

            _slotTicks.Clear();
            if (state.SlotTicks != null)
            {
                _slotTicks.AddRange(state.SlotTicks);
                _slotTicks.Sort();
            }

            RestoreWorldView(state);

            // The agenda is derived, so it is not saved: the first tick of the week reviews again.
            _agenda.Clear();

            // CommittedContractIds is not restored here: the inventory restores its own commitments
            // when the economy is loaded (after the NPCs).
        }

        /// <summary>Rebuilds the beliefs and the learned magnitudes. Older saves simply have none.</summary>
        private void RestoreWorldView(NPCTraderState state)
        {
            if (WorldView == null) return;

            WorldView.Clear();

            if (state.Beliefs != null)
            {
                foreach (var belief in state.Beliefs)
                {
                    if (belief == null || belief.Facet == null) continue;

                    WorldView.AddRestored(new Belief
                    {
                        NewsId = belief.NewsId,
                        OfficialName = belief.OfficialName,
                        Facet = belief.Facet,
                        Direction = belief.Direction,
                        EstimatedMove = belief.EstimatedMove,
                        Spread = belief.Spread,
                        StartWeek = belief.StartWeek,
                        EndWeek = belief.EndWeek,
                        Trust = belief.Trust,
                        HeardWeek = belief.HeardWeek,
                        ActsFromWeek = belief.ActsFromWeek > 0 ? belief.ActsFromWeek : belief.HeardWeek,
                        Source = (BeliefSource)belief.Source,
                        Checked = belief.Checked,
                        Learned = belief.Learned,
                        PriceAtStart = belief.PriceAtStart,
                        RealisedMove = belief.RealisedMove
                    });
                }
            }

            if (state.SourceTrust != null)
            {
                for (int i = 0; i < state.SourceTrust.Count && i < BeliefSourceCount; i++)
                    WorldView.SetStandingTrust((BeliefSource)i, state.SourceTrust[i]);
            }

            if (state.MagnitudeMemory == null) return;

            foreach (var entry in state.MagnitudeMemory)
                if (entry != null) WorldView.Memory.Set(entry.OfficialName, entry.Mean, entry.Spread, entry.Observations);
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
