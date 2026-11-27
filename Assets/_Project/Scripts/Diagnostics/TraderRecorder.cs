using System;
using System.Collections.Generic;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Company;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Economy;
using UnityEngine;
using Antique = AntiqueTradingSimulator.Market.Antique;

namespace AntiqueTradingSimulator.Diagnostics
{
    /// <summary>
    /// Everything keyed on a trader: the ledger, ownership chain, contract reservations, NPC
    /// plans, NPC action slots and reputation.
    ///
    /// Ownership, reservations and plans come from diffing state once a tick rather than from
    /// events, because several paths that change them raise nothing (an NPC dropping an open
    /// contract, a reservation released when an antique falls below a value floor). The market
    /// events supply the reason, matched back to the diff by antique id; a row with source=Diff is
    /// one no event explained.
    /// </summary>
    public sealed class TraderRecorder
    {
        private readonly TelemetryRecorder _host;
        private readonly TelemetryRun _run;

        private sealed class TraderView
        {
            public string Id;
            public string Name;
            public TraderInventory Inventory;
            public NPCTrader Npc;
            public TraderRecord Record;

            public readonly Dictionary<string, string> Reservations = new Dictionary<string, string>();
            public readonly HashSet<string> Holdings = new HashSet<string>();
            public readonly HashSet<string> Committed = new HashSet<string>();
            public readonly Dictionary<string, PlanView> Plans = new Dictionary<string, PlanView>();

            public readonly List<string> Declined = new List<string>();
            public int SlotIndex;
            public float CashBefore;
            public float BudgetBefore;
            public int LastPlanWeek = -1;
        }

        /// <summary>What other recorders need to know about a plan, without exposing the diff state.</summary>
        public struct PlanInfo
        {
            public PlanPurpose Purpose;
            public string ContractId;
            public string OptionId;
        }

        /// <summary>The plan an NPC has for one antique, or null.</summary>
        public PlanInfo? PlanFor(string traderId, string antiqueId)
        {
            if (string.IsNullOrEmpty(traderId) || string.IsNullOrEmpty(antiqueId)) return null;
            if (!_views.TryGetValue(traderId, out var view)) return null;
            if (!view.Plans.TryGetValue(antiqueId, out var plan)) return null;

            return new PlanInfo { Purpose = plan.Purpose, ContractId = plan.ContractId, OptionId = plan.OptionId };
        }

        private struct PlanView
        {
            public PlanPurpose Purpose;
            public string ContractId;
            public string OptionId;
            public float AssumedFee;
            public int StartRestoreByWeek;
            public float ExitAskingPrice;
            public int DumpAfterWeek;
            public float CostBasis;
            public int AcquiredWeek;
        }

        /// <summary>What a market event says about an antique changing hands, until the diff claims it.</summary>
        private struct PathHint
        {
            public string Path;
            public string BuyerId;
            public string SellerId;
            public float Price;
            public float AskingPrice;
            public int Week;
        }

        private readonly Dictionary<string, TraderView> _views = new Dictionary<string, TraderView>();
        private readonly Dictionary<string, PathHint> _hints = new Dictionary<string, PathHint>();
        private readonly List<string> _scratch = new List<string>();
        private readonly List<Action> _detach = new List<System.Action>();

        /// <summary>Diff-only ownership rows this week, reported by the weekly integrity check.</summary>
        public int UnexplainedOwnershipRows { get; private set; }

        public void ResetWeeklyCounters() => UnexplainedOwnershipRows = 0;

        public TraderRecorder(TelemetryRecorder host)
        {
            _host = host;
            _run = host.Run;
        }

        public IEnumerable<string> TraderIds => _views.Keys;

        public TraderInventory InventoryOf(string traderId) =>
            _views.TryGetValue(traderId, out var view) ? view.Inventory : null;

        public NPCTrader NpcOf(string traderId) =>
            _views.TryGetValue(traderId, out var view) ? view.Npc : null;

        public TraderRecord RecordOf(string traderId) =>
            _views.TryGetValue(traderId, out var view) ? view.Record : null;

        public string NameOf(string traderId) =>
            _views.TryGetValue(traderId, out var view) ? view.Name : traderId;

        // ---------------------------------------------------------------- registration

        public void AttachPlayer(PlayerTrader player)
        {
            if (player == null || player.Inventory == null) return;

            var view = Register(Antique.PlayerOwnerId,
                player.Company != null ? player.Company.CompanyName : "Player",
                player.Inventory, null,
                player.Company != null ? player.Company.Record : null);

            _host.Dimensions.WritePlayer(player);
            Seed(view);
        }

        public void NpcAdded(NPCTrader npc)
        {
            if (npc == null || npc.Inventory == null) return;
            if (_views.ContainsKey(npc.Id)) return;

            var view = Register(npc.Id, npc.TraderName, npc.Inventory, npc, npc.Record);
            _host.Dimensions.WriteNpc(npc);
            Seed(view);
        }

        public void NpcRemoved(NPCTrader npc)
        {
            if (npc != null) _views.Remove(npc.Id);
        }

        private TraderView Register(string id, string name, TraderInventory inventory, NPCTrader npc, TraderRecord record)
        {
            var view = new TraderView { Id = id, Name = name, Inventory = inventory, Npc = npc, Record = record };
            _views[id] = view;

            // Inventories and company records outlive the recorder, so these two subscriptions are
            // tracked and released when the session ends. Left attached, they would fire into a
            // closed session — a handler reaching game code is exactly what must not happen.
            Action<LedgerEntry> onLedgerEntry = entry => LedgerEntryAdded(view, entry);
            inventory.Ledger.OnEntryAdded += onLedgerEntry;
            _detach.Add(() => inventory.Ledger.OnEntryAdded -= onLedgerEntry);

            if (record != null && record.Reputation != null)
            {
                var reputation = record.Reputation;
                Action<ReputationChange> onReputationChanged = change => ReputationChanged(view, change);
                reputation.OnChanged += onReputationChanged;
                _detach.Add(() => reputation.OnChanged -= onReputationChanged);
            }

            return view;
        }

        /// <summary>Releases every per-trader subscription and drops the diff state.</summary>
        public void Detach()
        {
            for (int i = _detach.Count - 1; i >= 0; i--)
            {
                try { _detach[i](); }
                catch { /* the publisher is already gone */ }
            }

            _detach.Clear();
            _views.Clear();
            _hints.Clear();
        }

        /// <summary>Takes the opening state so the first diff reports changes, not the whole inventory.</summary>
        private void Seed(TraderView view)
        {
            foreach (var pair in view.Inventory.Holdings)
            {
                view.Holdings.Add(pair.Key);
                _host.Dimensions.EnsureAntique(pair.Value, "InitialStock");
                if (pair.Value != null && pair.Value.IsReservedForContract)
                    view.Reservations[pair.Key] = pair.Value.ReservedForContractId;
            }

            foreach (string contractId in view.Inventory.CommittedContractIds)
                view.Committed.Add(contractId);

            CapturePlans(view, emit: false);
        }

        public void Reseed()
        {
            foreach (var pair in _views) Seed(pair.Value);
        }

        // ---------------------------------------------------------------- fact_ledger

        private void LedgerEntryAdded(TraderView view, LedgerEntry entry)
        {
            _run.Safe(T.FactLedgerName, "OnEntryAdded", () =>
            {
                var table = _run.Table(T.FactLedgerName, T.FactLedgerHeader);
                _run.Stamp(table)
                    .S(view.Id)
                    .E(entry.Category)
                    .F(entry.Amount)
                    .B(entry.IsIncome)
                    .S(entry.Description)
                    .F(view.Inventory.Cash)
                    .End();

                _host.WeeklyLog.CountLedger(entry);
            });
        }

        // ---------------------------------------------------------------- path hints

        /// <summary>Records what a market event said, for the next tick's ownership diff to use.</summary>
        public void NotePath(string antiqueId, string path, string buyerId, string sellerId, float price, float askingPrice)
        {
            if (string.IsNullOrEmpty(antiqueId)) return;

            _hints[antiqueId] = new PathHint
            {
                Path = path,
                BuyerId = buyerId ?? "",
                SellerId = sellerId ?? "",
                Price = price,
                AskingPrice = askingPrice,
                Week = _run.Week
            };
        }

        // ---------------------------------------------------------------- tick pass

        public void TickPass(int ticksElapsed)
        {
            foreach (var pair in _views)
            {
                var view = pair.Value;
                if (view.Inventory == null) continue;

                DiffHoldings(view, ticksElapsed);
                DiffReservations(view, ticksElapsed);
                DiffCommitments(view);
                CapturePlans(view, emit: true, ticksElapsed: ticksElapsed);
            }

            // Hints older than a week are never going to be claimed.
            _scratch.Clear();
            foreach (var pair in _hints)
                if (_run.Week - pair.Value.Week > 1) _scratch.Add(pair.Key);
            foreach (string key in _scratch) _hints.Remove(key);
        }

        private void DiffHoldings(TraderView view, int ticksElapsed)
        {
            var inventory = view.Inventory;

            // Acquisitions.
            foreach (var pair in inventory.Holdings)
            {
                if (!view.Holdings.Add(pair.Key)) continue;
                _host.Dimensions.EnsureAntique(pair.Value, "Acquired");
                WriteOwnership(view, pair.Value, pair.Key, "Acquired", ticksElapsed);
            }

            // Disposals.
            _scratch.Clear();
            foreach (string id in view.Holdings)
                if (!inventory.Owns(id)) _scratch.Add(id);

            foreach (string id in _scratch)
            {
                view.Holdings.Remove(id);
                WriteOwnership(view, null, id, "Released", ticksElapsed);
            }
        }

        private void WriteOwnership(TraderView view, Antique antique, string antiqueId, string eventName, int ticksElapsed)
        {
            bool acquired = eventName == "Acquired";
            string path = "Unknown";
            string counterparty = "";
            float price = float.NaN;
            float asking = float.NaN;
            bool explained = false;

            if (_hints.TryGetValue(antiqueId, out var hint))
            {
                bool matches = acquired ? hint.BuyerId == view.Id : hint.SellerId == view.Id;
                if (matches)
                {
                    path = hint.Path;
                    counterparty = acquired ? hint.SellerId : hint.BuyerId;
                    price = hint.Price;
                    asking = hint.AskingPrice;
                    explained = true;
                    // Not removed: a sale between two traders produces a Released row and an
                    // Acquired row, and both need this hint. Hints expire by age instead.
                }
            }

            if (!explained && acquired && antique != null && !antique.HasPurchaseRecord)
                path = "Grant";

            if (!explained) UnexplainedOwnershipRows++;

            var plan = view.Plans.TryGetValue(antiqueId, out var p) ? (PlanView?)p : null;

            var table = _run.Table(T.FactOwnershipName, T.FactOwnershipHeader);
            var r = _run.Stamp(table)
                .I(ticksElapsed)
                .S(view.Id)
                .S(antiqueId)
                .S(antique != null ? antique.DefinitionId : "")
                .S(eventName)
                .S(path)
                .S(counterparty)
                .F(float.IsNaN(price) ? (float?)null : price)
                .F(float.IsNaN(asking) ? (float?)null : asking);

            if (antique != null)
            {
                r.F(antique.PurchasePrice)
                    .F(antique.TransportCost)
                    .F(antique.RestorationCost)
                    .F(antique.Condition)
                    .F(antique.CurrentPrice)
                    .E(antique.Status)
                    .E(antique.ShippingZone);
            }
            else
            {
                r.N(7);
            }

            if (plan.HasValue)
            {
                r.E(plan.Value.Purpose).S(plan.Value.ContractId).S(plan.Value.OptionId);
            }
            else
            {
                r.N(3);
            }

            int acquiredWeek = plan.HasValue ? plan.Value.AcquiredWeek : (antique != null ? antique.PurchasedOnWeek : -1);
            float costBasis = plan.HasValue ? plan.Value.CostBasis : (antique != null ? antique.PurchasePrice : 0f);

            r.I(acquiredWeek >= 0 ? acquiredWeek : (int?)null);

            if (!acquired && acquiredWeek >= 0)
            {
                r.I(_run.Week - acquiredWeek);
                r.F(float.IsNaN(price) ? (float?)null : price - costBasis);
            }
            else
            {
                r.N(2);
            }

            r.S(explained ? "Both" : "Diff").End();
        }

        // ---------------------------------------------------------------- fact_reservation

        private void DiffReservations(TraderView view, int ticksElapsed)
        {
            var inventory = view.Inventory;
            var table = _run.Table(T.FactReservationName, T.FactReservationHeader);

            foreach (var pair in inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding == null) continue;

                string now = holding.IsReservedForContract ? holding.ReservedForContractId : null;
                view.Reservations.TryGetValue(pair.Key, out string before);
                if (now == before) continue;

                if (before != null) WriteReservation(table, view, holding, before, "Released", ticksElapsed);
                if (now != null) WriteReservation(table, view, holding, now, "Reserved", ticksElapsed);

                if (now == null) view.Reservations.Remove(pair.Key);
                else view.Reservations[pair.Key] = now;
            }

            // An antique that left the inventory takes its reservation with it.
            _scratch.Clear();
            foreach (var pair in view.Reservations)
                if (!inventory.Owns(pair.Key)) _scratch.Add(pair.Key);

            foreach (string id in _scratch)
            {
                WriteReservation(table, view, null, view.Reservations[id], "Released", ticksElapsed, id);
                view.Reservations.Remove(id);
            }
        }

        private void WriteReservation(CsvTable table, TraderView view, Antique antique, string contractId,
            string eventName, int ticksElapsed, string antiqueId = null)
        {
            var contract = _host.Board != null ? _host.Board.GetById(contractId) : null;
            var requirement = contract != null ? contract.Requirement : null;

            bool satisfies = antique != null && requirement != null && requirement.IsSatisfiedBy(antique);

            string reason = "";
            if (eventName == "Released")
            {
                if (antique == null) reason = "Fulfilled";
                else if (contract == null || contract.Status != ContractStatus.Active) reason = "ContractInactive";
                else if (!satisfies) reason = "StoppedQualifying";
                else reason = "Unknown";
            }

            float? minCondition = requirement != null && requirement.HasMinCondition ? requirement.MinCondition : (float?)null;
            float? minValue = requirement != null && requirement.HasMinValue ? requirement.MinValue : (float?)null;

            _run.Stamp(table)
                .I(ticksElapsed)
                .S(view.Id)
                .S(antique != null ? antique.Id : antiqueId)
                .S(antique != null ? antique.DefinitionId : "")
                .S(contractId)
                .S(eventName)
                .B(satisfies)
                .S(reason)
                .F(antique != null ? antique.Condition : (float?)null)
                .F(antique != null ? antique.CurrentPrice : (float?)null)
                .F(minCondition)
                .F(minValue)
                .F(antique != null && minCondition.HasValue ? antique.Condition - minCondition.Value : (float?)null)
                .F(antique != null && minValue.HasValue ? antique.CurrentPrice - minValue.Value : (float?)null)
                .End();
        }

        // ---------------------------------------------------------------- commitments

        private void DiffCommitments(TraderView view)
        {
            var committed = view.Inventory.CommittedContractIds;

            foreach (string contractId in committed)
                if (view.Committed.Add(contractId))
                    _host.ContractLog.CommitmentChanged(view.Id, contractId, "Committed");

            _scratch.Clear();
            foreach (string contractId in view.Committed)
                if (!view.Inventory.IsCommittedToContract(contractId)) _scratch.Add(contractId);

            foreach (string contractId in _scratch)
            {
                view.Committed.Remove(contractId);
                _host.ContractLog.CommitmentChanged(view.Id, contractId, "Released");
            }
        }

        public bool IsCommitted(string traderId, string contractId) =>
            _views.TryGetValue(traderId, out var view) && view.Committed.Contains(contractId);

        /// <summary>
        /// Held antiques promised to a contract through a restoration plan. They cannot be
        /// reserved until they qualify, so they count separately from reserved_matching.
        /// </summary>
        public int RestoringFor(string traderId, string contractId)
        {
            if (!_views.TryGetValue(traderId, out var view)) return 0;

            int count = 0;
            foreach (var pair in view.Plans)
            {
                var plan = pair.Value;
                if (plan.Purpose != PlanPurpose.Contract || plan.ContractId != contractId) continue;
                if (string.IsNullOrEmpty(plan.OptionId)) continue;

                var holding = view.Inventory.GetHolding(pair.Key);
                if (holding == null || holding.IsReservedForContract) continue;
                count++;
            }
            return count;
        }

        // ---------------------------------------------------------------- fact_plan

        private void CapturePlans(TraderView view, bool emit, int ticksElapsed = 1)
        {
            if (view.Npc == null) return;

            var state = view.Npc.CaptureState();
            if (state == null || state.Acquisitions == null) return;

            var table = emit ? _run.Table(T.FactPlanName, T.FactPlanHeader) : null;
            var seen = new HashSet<string>();

            foreach (var plan in state.Acquisitions)
            {
                if (plan == null || string.IsNullOrEmpty(plan.ListingId)) continue;
                seen.Add(plan.ListingId);

                var now = new PlanView
                {
                    Purpose = plan.Purpose,
                    ContractId = plan.ContractId,
                    OptionId = plan.RestorationOptionId,
                    AssumedFee = plan.AssumedFee,
                    StartRestoreByWeek = plan.StartRestoreByWeek,
                    ExitAskingPrice = plan.ExitAskingPrice,
                    DumpAfterWeek = plan.DumpAfterWeek,
                    CostBasis = plan.PurchasePrice,
                    AcquiredWeek = plan.Week
                };

                bool existed = view.Plans.TryGetValue(plan.ListingId, out var before);
                view.Plans[plan.ListingId] = now;
                if (!emit) continue;

                if (!existed)
                {
                    WritePlan(table, view, plan.ListingId, "Created", now, null, ticksElapsed);
                    continue;
                }

                if (before.Purpose != now.Purpose || before.ContractId != now.ContractId)
                    WritePlan(table, view, plan.ListingId, "PurposeChanged", now, before, ticksElapsed);

                if (string.IsNullOrEmpty(before.OptionId) && !string.IsNullOrEmpty(now.OptionId))
                    WritePlan(table, view, plan.ListingId, "RestorationLegAttached", now, before, ticksElapsed);
                else if (!string.IsNullOrEmpty(before.OptionId) && string.IsNullOrEmpty(now.OptionId))
                    WritePlan(table, view, plan.ListingId, "RestorationLegCleared", now, before, ticksElapsed);

                if (!Mathf.Approximately(before.ExitAskingPrice, now.ExitAskingPrice))
                    WritePlan(table, view, plan.ListingId, "ExitPriceSet", now, before, ticksElapsed);
            }

            _scratch.Clear();
            foreach (var pair in view.Plans)
                if (!seen.Contains(pair.Key)) _scratch.Add(pair.Key);

            foreach (string id in _scratch)
            {
                var gone = view.Plans[id];
                view.Plans.Remove(id);
                if (emit) WritePlan(table, view, id, "Dropped", gone, gone, ticksElapsed);
            }
        }

        private void WritePlan(CsvTable table, TraderView view, string antiqueId, string eventName,
            PlanView now, PlanView? before, int ticksElapsed)
        {
            var holding = view.Inventory.GetHolding(antiqueId);

            _run.Stamp(table)
                .I(ticksElapsed)
                .S(view.Id)
                .S(antiqueId)
                .S(holding != null ? holding.DefinitionId : "")
                .S(eventName)
                .E(now.Purpose)
                .E(before.HasValue ? (System.Enum)before.Value.Purpose : null)
                .S(now.ContractId)
                .S(before.HasValue ? before.Value.ContractId : null)
                .S(now.OptionId)
                .F(now.AssumedFee)
                .I(now.StartRestoreByWeek)
                .F(now.ExitAskingPrice)
                .I(now.DumpAfterWeek)
                .F(now.CostBasis)
                .I(now.AcquiredWeek)
                .End();
        }

        // ---------------------------------------------------------------- fact_npc_action

        public void NpcActionBegin(NPCTrader npc)
        {
            if (npc == null || !_views.TryGetValue(npc.Id, out var view)) return;

            _run.Safe(T.FactNpcActionName, "NpcActionBegin", () =>
            {
                var state = npc.CaptureState();
                view.CashBefore = npc.Inventory.Cash;
                view.BudgetBefore = state != null ? state.WeekBudget : 0f;

                int planWeek = state != null ? state.PlanWeek : _run.Week;
                if (planWeek != view.LastPlanWeek)
                {
                    view.LastPlanWeek = planWeek;
                    view.SlotIndex = 0;
                }

                view.Declined.Clear();
                _host.WeeklyLog.Gates.Capture(npc, _run.Week);

                if (Telemetry.Mirrors) _host.WeeklyLog.Mirror.CaptureBrowse(npc, view.Id);
            });
        }

        public void NpcActionEnd(NPCTrader npc, int week, string tier)
        {
            if (npc == null || !_views.TryGetValue(npc.Id, out var view)) return;

            _run.Safe(T.FactNpcActionName, "NpcActionEnd", () =>
            {
                var state = npc.CaptureState();
                var table = _run.Table(T.FactNpcActionName, T.FactNpcActionHeader);

                int slot = view.SlotIndex++;

                _run.Stamp(table)
                    .S(view.Id)
                    .I(slot)
                    .S(string.IsNullOrEmpty(tier) ? "None" : tier)
                    .I(TierRank(tier))
                    .B(!string.IsNullOrEmpty(tier))
                    .N()                                            // result_antique_id, by seq adjacency
                    .N()                                            // result_contract_id, by seq adjacency
                    .F(view.CashBefore)
                    .F(npc.Inventory.Cash)
                    .F(view.BudgetBefore)
                    .F(state != null ? state.WeekBudget : 0f)
                    .F(state != null ? state.ContractReserve : 0f)
                    .F(state != null ? state.PlanReserve : 0f)
                    .I(state != null ? state.ContractSlotsUsed : 0)
                    .I(npc.Inventory.UsedSlots)
                    .I(OpenRestorations(npc.Inventory))
                    .I(npc.Inventory.CommittedContractIds.Count)
                    .I(state != null && state.PendingReactions != null ? state.PendingReactions.Count : 0)
                    .F(npc.ActivityScale)
                    .End();

                _host.WeeklyLog.Gates.Emit(view.Id, slot, tier, view.Declined);

                if (Telemetry.Mirrors) _host.WeeklyLog.Mirror.EmitBrowse(npc, view.Id, tier);
            });
        }

        /// <summary>Records a rung the ladder reached and declined, in ladder order.</summary>
        public void TierDeclined(NPCTrader npc, string tier)
        {
            if (npc == null || string.IsNullOrEmpty(tier)) return;
            if (!_views.TryGetValue(npc.Id, out var view)) return;

            view.Declined.Add(tier);
        }

        /// <summary>Order the week's intentions are attempted in. Lower goes first.</summary>
        public static int TierRank(string tier) => tier switch
        {
            "FillContract" => 0,
            "ExecutePlan" => 1,
            "RaiseCash" => 2,
            "FreeSpace" => 3,
            "ExitPosition" => 4,
            "TakeContract" => 5,
            "OpenPosition" => 6,
            "TakeBargain" => 7,
            "SendToWorkshop" => 8,
            "Invest" => 9,
            "PlantRumour" => 10,
            _ => -1
        };

        private int OpenRestorations(TraderInventory inventory)
        {
            if (_host.Restorations == null || inventory == null) return 0;

            int count = 0;
            foreach (var job in _host.Restorations.Jobs)
                if (job != null && ReferenceEquals(job.Owner, inventory)) count++;
            return count;
        }

        // ---------------------------------------------------------------- fact_reputation_change

        private void ReputationChanged(TraderView view, ReputationChange change)
        {
            _run.Safe(T.FactReputationChangeName, "OnReputationChanged", () =>
            {
                var reputation = view.Record != null ? view.Record.Reputation : null;
                if (reputation == null) return;

                float after = reputation.Value(change.Kind);
                float before = after - change.Delta;
                int tierAfter = reputation.TierIndex(change.Kind);
                int tierBefore = reputation.TierIndexFor(change.Kind, before);
                var tier = reputation.CurrentTier(change.Kind);

                var table = _run.Table(T.FactReputationChangeName, T.FactReputationChangeHeader);
                _run.Stamp(table)
                    .S(view.Id)
                    .E(change.Kind)
                    .F(change.Delta)
                    .S(change.Reason)
                    .F(before)
                    .F(after)
                    .I(tierBefore)
                    .I(tierAfter)
                    .S(tier != null ? tier.Name : "")
                    .B(tierAfter != tierBefore)
                    .F(reputation.ProgressToNextTier(change.Kind))
                    .I(UnlockedContractClasses(Mathf.RoundToInt(after)))
                    .I(UnlockedWorkshops(Mathf.RoundToInt(after)))
                    .N()                                            // source_event_seq, by seq adjacency
                    .End();
            });
        }

        private int UnlockedContractClasses(int reputation)
        {
            if (_host.Board == null) return 0;

            int count = 0;
            foreach (var definition in _host.Board.ReputationClasses)
                if (definition != null && definition.RequiredReputation <= reputation) count++;
            return count;
        }

        private int UnlockedWorkshops(int reputation)
        {
            if (_host.Restorations == null) return 0;

            int count = 0;
            foreach (var option in _host.Restorations.Options)
                if (option != null && option.RequiredReputation <= reputation) count++;
            return count;
        }
    }
}
