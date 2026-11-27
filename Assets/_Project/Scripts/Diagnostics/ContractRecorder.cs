using System.Collections.Generic;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Logistics;
using UnityEngine;

namespace AntiqueTradingSimulator.Diagnostics
{
    /// <summary>
    /// The contract tables.
    /// </summary>
    public sealed class ContractRecorder
    {
        private readonly TelemetryRecorder _host;
        private readonly TelemetryRun _run;

        /// <summary>The agents' own decision constants, so both sides judge a contract by their rules.</summary>
        private static TraderPolicy Policy => TraderPolicy.Default;

        private static float ExclusiveMinProfitShare => Policy.ExclusiveMinProfitShare;

        private static float ReserveSafetyMargin => Policy.ReserveSafetyMargin;

        private readonly List<float> _offers = new List<float>();

        public ContractRecorder(TelemetryRecorder host)
        {
            _host = host;
            _run = host.Run;
        }

        public void Reseed()
        {
            if (_host.Board == null) return;
            foreach (var contract in _host.Board.AllContracts)
                _host.Dimensions.EnsureContract(contract, "InitialSeed");
        }

        public void TickPass()
        {
            // Commitment diffs are driven by TraderRecorder, which calls CommitmentChanged.
        }

        // ---------------------------------------------------------------- lifecycle

        public void ContractCreated(Contract contract)
        {
            _run.Safe(T.FactContractStateName, "OnContractCreated", () =>
            {
                _host.Dimensions.EnsureContract(contract, _run.Week <= 1 ? "InitialSeed" : "WeeklyRoll");
                Write(contract, null, "Created", "Event", 0f, 0f);
                _host.WeeklyLog.CountContract("Created", false);
            });
        }

        public void ContractClaimed(Contract contract, string traderId)
        {
            _run.Safe(T.FactContractStateName, "OnContractClaimed",
                () => Write(contract, traderId, "Claimed", "Event", 0f, 0f));
        }

        public void ContractFulfilled(Contract contract)
        {
            _run.Safe(T.FactContractStateName, "OnContractFulfilled", () =>
            {
                Write(contract, contract.FulfilledByTraderId, "Fulfilled", "Event", contract.TotalReward, 0f);
                _host.WeeklyLog.CountContract("Fulfilled", true);

                // Everyone else working on an open contract has just lost the race.
                foreach (string traderId in _host.Traders.TraderIds)
                {
                    if (traderId == contract.FulfilledByTraderId) continue;
                    if (!_host.Traders.IsCommitted(traderId, contract.ContractId)) continue;
                    Write(contract, traderId, "Lost", "Diff", 0f, 0f);
                }
            });
        }

        public void ContractExpired(Contract contract)
        {
            _run.Safe(T.FactContractStateName, "OnContractExpired", () =>
            {
                bool anyone = false;
                foreach (string traderId in _host.Traders.TraderIds)
                {
                    if (!_host.Traders.IsCommitted(traderId, contract.ContractId)) continue;
                    anyone = true;

                    bool penalised = contract.IsClaimed && contract.ClaimedByTraderId == traderId && contract.Penalty > 0f;
                    Write(contract, traderId, "Expired", "Event", 0f, penalised ? contract.Penalty : 0f);
                    if (penalised) Write(contract, traderId, "PenaltyPaid", "Event", 0f, contract.Penalty);
                }

                if (!anyone) Write(contract, null, "Expired", "Event", 0f, 0f);
                _host.WeeklyLog.CountContract("Expired", anyone);
            });
        }

        /// <summary>Called by TraderRecorder when CommittedContractIds gained or lost an entry.</summary>
        public void CommitmentChanged(string traderId, string contractId, string eventName)
        {
            _run.Safe(T.FactContractStateName, "CommitmentDiff", () =>
            {
                var contract = _host.Board != null ? _host.Board.GetById(contractId) : null;
                if (contract == null) return;

                _host.Dimensions.EnsureContract(contract, "WeeklyRoll");
                Write(contract, traderId, eventName, "Diff", 0f, 0f);
            });
        }

        private void Write(Contract contract, string traderId, string eventName, string source,
            float rewardPaid, float penaltyPaid)
        {
            var inventory = traderId != null ? _host.Traders.InventoryOf(traderId) : null;
            var requirement = contract.Requirement;

            int reserved = ContractMath.ReservedMatching(inventory, contract);
            int ready = ContractMath.ReadyCount(inventory, contract);
            int restoring = traderId != null ? _host.Traders.RestoringFor(traderId, contract.ContractId) : 0;
            int quantity = requirement != null ? requirement.Quantity : 0;

            var table = _run.Table(T.FactContractStateName, T.FactContractStateHeader);
            _run.Stamp(table)
                .S(contract.ContractId)
                .S(traderId)
                .S(eventName)
                .S(source)
                .E(contract.Status)
                .I(quantity)
                .I(inventory != null ? reserved : (int?)null)
                .I(inventory != null ? ready : (int?)null)
                .I(inventory != null ? Mathf.Max(0, quantity - reserved - restoring) : (int?)null)
                .I(contract.DeadlineWeek - _run.Week)
                .F(rewardPaid > 0f ? rewardPaid : (float?)null)
                .F(penaltyPaid > 0f ? penaltyPaid : (float?)null)
                .F(inventory != null ? inventory.Cash : (float?)null)
                .I(ContractMath.CommittedCount(_host, contract.ContractId, traderId))
                .B(eventName == "Fulfilled" || eventName == "Expired")
                .End();
        }

        // ---------------------------------------------------------------- weekly progress

        public void WeeklyProgress()
        {
            if (_host.Board == null) return;

            var table = _run.Table(T.FactContractProgressName, T.FactContractProgressHeader);

            foreach (var contract in _host.Board.ActiveContracts)
            {
                if (contract == null) continue;

                foreach (string traderId in _host.Traders.TraderIds)
                {
                    if (!_host.Traders.IsCommitted(traderId, contract.ContractId)) continue;
                    WriteProgress(table, contract, traderId);
                }
            }
        }

        private void WriteProgress(CsvTable table, Contract contract, string traderId)
        {
            var inventory = _host.Traders.InventoryOf(traderId);
            var npc = _host.Traders.NpcOf(traderId);
            var profile = npc != null ? npc.Profile : null;
            var requirement = contract.Requirement;
            if (inventory == null || requirement == null) return;

            int quantity = requirement.Quantity;
            int reserved = ContractMath.ReservedMatching(inventory, contract);
            int restoring = _host.Traders.RestoringFor(traderId, contract.ContractId);
            int inTransit = ContractMath.ReservedInTransit(inventory, contract);
            int ready = ContractMath.ReadyCount(inventory, contract);
            int missing = Mathf.Max(0, quantity - reserved - restoring);

            int latest = ContractMath.LatestUsefulArrivalWeek(contract);
            var supply = ContractMath.Measure(_host, requirement, latest);

            var state = npc != null ? npc.CaptureState() : null;
            float contractReserve = state != null ? state.ContractReserve : 0f;
            float planReserve = state != null ? state.PlanReserve : 0f;
            float budget = state != null ? state.WeekBudget : 0f;
            int panic = profile != null ? profile.ContractPanicWeeks : 0;

            bool profitable = supply.HasCheapest && supply.CheapestTotal <= contract.RewardPerUnit;
            bool affordable = supply.HasCheapest &&
                              Core.Money.Covers(inventory.Cash - (contractReserve + planReserve) * ReserveSafetyMargin,
                                  supply.CheapestTotal);

            _run.StampWeek(table)
                .S(contract.ContractId)
                .S(traderId)
                .I(quantity)
                .I(reserved)
                .I(restoring)
                .I(inTransit)
                .I(ready)
                .I(missing)
                .I(contract.DeadlineWeek - _run.Week)
                .I(latest)
                .S(ContractMath.Urgency(contract, _run.Week, missing, 1, panic))
                .I(supply.Qualifying)
                .I(supply.Reachable)
                .F(supply.HasCheapest ? supply.CheapestPrice : (float?)null)
                .F(supply.HasCheapest ? supply.CheapestTotal : (float?)null)
                .B(profitable)
                .F(_host.Market != null ? requirement.ExpectedUnitCost(_host.Market) : (float?)null)
                .F(contract.RewardPerUnit)
                .F(inventory.Cash)
                .F(npc != null ? contractReserve : (float?)null)
                .F(npc != null ? planReserve : (float?)null)
                .F(npc != null ? budget : (float?)null)
                .B(affordable)
                .F(ContractMath.ExpectedSupply(_host, requirement, contract.DeadlineWeek))
                .End();
        }

        // ---------------------------------------------------------------- weekly eligibility

        /// <summary>
        /// Replays ConsiderNewContract's gates for every NPC that has not taken the contract, so a
        /// contract nobody picks up says which gate turned everyone away rather than just going
        /// unclaimed.
        /// </summary>
        public void WeeklyEligibility()
        {
            if (_host.Board == null || _host.Npcs == null) return;

            var table = _run.Table(T.FactContractEligibilityWeekName, T.FactContractEligibilityWeekHeader);

            foreach (var contract in _host.Board.ActiveContracts)
            {
                if (contract == null || contract.Requirement == null) continue;

                foreach (var npc in _host.Npcs.NPCs)
                {
                    if (npc == null) continue;
                    WriteEligibility(table, contract, npc);
                }
            }
        }

        private void WriteEligibility(CsvTable table, Contract contract, NPCTrader npc)
        {
            var profile = npc.Profile;
            var inventory = npc.Inventory;
            var requirement = contract.Requirement;

            bool committed = inventory.IsCommittedToContract(contract.ContractId);
            int reputation = npc.Reputation;
            bool meetsReputation = contract.RequiredReputation <= reputation;
            bool claimable = contract.Type == ContractType.Open || contract.CanBeClaimed;

            var state = npc.CaptureState();
            float contractReserve = state != null ? state.ContractReserve : 0f;
            float cashLimit = inventory.Cash * ContractMath.ContractCashShareFor(profile) - contractReserve;

            int reserved = ContractMath.ReservedMatching(inventory, contract);
            int needed = Mathf.Max(0, requirement.Quantity - reserved);

            int latest = ContractMath.LatestUsefulArrivalWeek(contract);
            var supply = ContractMath.Measure(_host, requirement, latest);

            BuildOfferPlan(contract, needed, npc, profile, out int rejectedOnPrice);
            int found = _offers.Count;
            float planned = 0f;
            for (int i = 0; i < _offers.Count; i++) planned += _offers[i];

            bool affordable = found >= needed && planned <= cashLimit;
            bool reachable = found >= needed;
            float profit = contract.TotalReward - planned;
            float minProfit = contract.Type == ContractType.Exclusive
                ? contract.TotalReward * ExclusiveMinProfitShare
                : 0f;
            bool attractive = found >= needed && profit > minProfit;

            // A profile that never takes contracts is blocked by that, not unblocked, and an
            // exclusive already claimed by someone else is AlreadyClaimed rather than Committed.
            // Both used to report the wrong reason, which buried the real signal in this column.

            string blocked =
                profile == null || !profile.TakesContracts ? "DoesNotTakeContracts" :
                committed ? "Committed" :
                !meetsReputation ? "Reputation" :
                !claimable ? "AlreadyClaimed" :
                cashLimit <= 0f ? "Cash" :
                found < needed ? (rejectedOnPrice > 0 ? "PriceCeiling" : "Supply") :
                !affordable ? "Cash" :
                !reachable ? "Timing" :
                !attractive ? "Profit" : "NotBlocked";

            _run.StampWeek(table)
                .S(contract.ContractId)
                .S(npc.Id)
                .B(profile != null && profile.TakesContracts)
                .B(committed)
                .B(meetsReputation)
                .I(reputation)
                .I(contract.RequiredReputation)
                .B(claimable)
                .F(cashLimit)
                .I(needed)
                .I(found)
                .F(found > 0 ? planned : (float?)null)
                .B(affordable)
                .B(reachable)
                .F(found >= needed ? profit : (float?)null)
                .F(minProfit)
                .B(attractive)
                .S(blocked)
                .I(supply.Qualifying)
                .I(supply.Reachable)
                .F(supply.HasCheapest ? supply.CheapestTotal : (float?)null)
                .F(contract.RewardPerUnit)
                .I(ContractMath.CommittedCount(_host, contract.ContractId, npc.Id))
                .I(contract.DeadlineWeek - _run.Week)
                .I(rejectedOnPrice)
                .End();
        }

        /// <summary>
        /// The cheapest qualifying listings that can arrive in time, clear this profile's contract
        /// price ceiling and cost no more than the reward per unit — NPCTrader.BuildOfferPlan, using
        /// only public API.
        /// </summary>
        private void BuildOfferPlan(Contract contract, int needed, NPCTrader npc,
            NpcBehaviorProfile profile, out int rejectedOnPrice)
        {
            _offers.Clear();
            rejectedOnPrice = 0;

            var market = _host.Market;
            var requirement = contract.Requirement;
            if (market == null || requirement == null || needed <= 0) return;

            var inventory = npc != null ? npc.Inventory : null;
            int week = _run.Week;
            int latest = ContractMath.LatestUsefulArrivalWeek(contract);
            var settings = _host.Transport != null ? _host.Transport.Settings : null;
            var express = ContractMath.ExpressWeeks(_host);

            var listings = market.Listings;
            for (int i = 0; i < listings.Count; i++)
            {
                var listing = listings[i];
                if (listing == null || !requirement.IsSatisfiedBy(listing)) continue;

                if (inventory != null && inventory.Owns(listing.Id)) continue;

                if (week + express(listing.ShippingZone) > latest) continue;

                if (!ContractMath.IsAcceptableContractPrice(_host, listing, profile))
                {
                    rejectedOnPrice++;
                    continue;
                }

                float shipping = settings != null
                    ? settings.GetCost(listing.SalePrice, listing.ShippingZone, TransportOption.Express)
                    : 0f;
                float cost = listing.SalePrice + shipping;
                if (cost > contract.RewardPerUnit) continue;

                _offers.Add(cost);
            }

            _offers.Sort();
            if (_offers.Count > needed) _offers.RemoveRange(needed, _offers.Count - needed);
        }
    }
}
