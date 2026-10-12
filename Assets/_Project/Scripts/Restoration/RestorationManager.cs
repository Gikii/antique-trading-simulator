using System;
using System.Collections.Generic;
using System.Linq;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Market;
using UnityEngine;

namespace AntiqueTradingSimulator.Restoration
{
    /// <summary>
    /// Sends the player's antiques to a conservator's workshop and brings them back restored.
    ///
    /// Quote → StartRestoration (fee paid, outcome rolled and kept hidden, antique marked
    /// InRestoration) → on its end day the antique's condition changes, its price is recalculated
    /// and it is available again. While away it stays in the collection and keeps its warehouse
    /// slot, but can't be sold, listed or handed in.
    ///
    /// Player-only for now: NPCs don't restore. The Conservators upgrade lowers the fee and the
    /// time (TraderInventory.Modifiers); better workshops need reputation (RestorationOption).
    /// </summary>
    public class RestorationManager : MonoBehaviour
    {
        [SerializeField] private RestorationSettings settings;

        [Header("References (auto-found if empty)")]
        [SerializeField] private EconomyManager economyManager;
        [SerializeField] private Core.TimeManager timeManager;
        [SerializeField] private PlayerTrader playerTrader;

        private readonly List<RestorationJob> _jobs = new();
        private readonly List<RestorationRecord> _completed = new();

        public RestorationSettings Settings
        {
            get
            {
                EnsureSettings();
                return settings;
            }
        }

        public IReadOnlyList<RestorationOption> Options => Settings.Options;
        public IReadOnlyList<RestorationJob> Jobs => _jobs;

        /// <summary>Finished restorations, oldest first.</summary>
        public IReadOnlyList<RestorationRecord> Completed => _completed;

        public event Action<RestorationJob> OnRestorationStarted;
        public event Action<RestorationRecord> OnRestorationCompleted;
        public event Action OnRestorationsRestored;

        private int CurrentDay => timeManager != null ? timeManager.CurrentDay : 0;
        private Market.Market CurrentMarket => economyManager != null ? economyManager.Market : null;

        private void Awake()
        {
            if (economyManager == null) economyManager = FindFirstObjectByType<EconomyManager>();
            if (timeManager == null)
                timeManager = economyManager != null && economyManager.TimeManager != null
                    ? economyManager.TimeManager
                    : FindFirstObjectByType<Core.TimeManager>();
            if (playerTrader == null) playerTrader = FindFirstObjectByType<PlayerTrader>();
            EnsureSettings();
        }

        private void EnsureSettings()
        {
            if (settings != null) return;
            settings = ScriptableObject.CreateInstance<RestorationSettings>();
        }

        private void OnEnable()
        {
            if (timeManager != null)
                timeManager.OnDayChanged += HandleDayChanged;
        }

        private void OnDisable()
        {
            if (timeManager != null)
                timeManager.OnDayChanged -= HandleDayChanged;
        }

        // ---------------------------------------------------------------- player context

        private TraderInventory PlayerInventory => playerTrader != null ? playerTrader.Inventory : null;

        private int PlayerReputation =>
            playerTrader != null && playerTrader.Company != null ? playerTrader.Company.Reputation.Reputation : 0;

        /// <summary>Tier name for a reputation requirement ("Known Dealer"), empty if unknown.</summary>
        public string TierNameFor(int reputation) =>
            playerTrader != null && playerTrader.Company != null ? playerTrader.Company.TierNameFor(reputation) : "";

        // ---------------------------------------------------------------- quoting

        /// <summary>
        /// Why this antique can't go to any workshop right now (in transit, listed, ...), or null.
        /// Workshop-specific reasons (reputation, cash, condition) are in each quote's BlockReason.
        /// </summary>
        public string ItemBlockReason(Antique antique)
        {
            var inventory = PlayerInventory;
            if (antique == null || inventory == null || !inventory.Owns(antique.Id))
                return "You don't own this antique.";
            if (antique.IsInRestoration)
                return $"Already being restored — ready on day {antique.RestorationEndDay}.";
            if (antique.IsInTransit)
                return "In transit — it can be restored once it reaches your warehouse.";
            if (antique.IsListedForSale)
                return "Listed on the market — cancel the listing first.";
            if (antique.IsReservedForContract)
                return "Reserved for a contract.";

            float best = 0f;
            foreach (var option in Options)
                if (option != null)
                    best = Mathf.Max(best, option.MaxCondition);
            if (antique.Condition >= best - ConditionEpsilon)
                return "Already in better condition than any workshop can achieve.";

            return null;
        }

        private const float ConditionEpsilon = 0.005f;

        /// <summary>
        /// Fee, time, possible results and expected profit of restoring this antique at this
        /// workshop, for the player. Pure — changes nothing. Null if there is no antique or option.
        /// </summary>
        public RestorationQuote Quote(Antique antique, RestorationOption option)
        {
            if (antique == null || option == null) return null;

            var inventory = PlayerInventory;
            var modifiers = inventory?.Modifiers;

            float start = antique.Condition;
            float target = Mathf.Clamp(option.MaxCondition, Antique.MinCondition, Antique.MaxCondition);
            float gain = Mathf.Max(0f, target - start);
            float partial = start + gain * 0.5f;
            float worst = Mathf.Max(Antique.MinCondition, start - Mathf.Max(option.FailureLossMin, option.FailureLossMax));

            // Value the antique would have in perfect condition, at today's prices.
            float perfectValue = start > 0f ? antique.CurrentPrice / start : 0f;

            float cost = Mathf.Max(option.MinimumFee, perfectValue * gain * option.CostRate);
            if (modifiers != null)
                cost *= 1f - modifiers.RestorationCostReduction;

            int days = option.DurationDays;
            if (modifiers != null)
                days = Mathf.RoundToInt(days * (1f - modifiers.RestorationTimeReduction));
            days = Mathf.Max(1, days);

            string reason = ItemBlockReason(antique);
            if (reason == null && gain <= ConditionEpsilon)
                reason = "This workshop can't improve it any further.";
            if (reason == null && option.RequiredReputation > PlayerReputation)
            {
                string tier = TierNameFor(option.RequiredReputation);
                reason = $"Requires {option.RequiredReputation} reputation" + (string.IsNullOrEmpty(tier) ? "." : $" ({tier}).");
            }
            if (reason == null && inventory != null && cost > inventory.Cash)
                reason = $"Not enough cash — {Mathf.CeilToInt(cost - inventory.Cash)} € short.";

            return new RestorationQuote(option, start, target, partial, worst, cost, CurrentDay, days, perfectValue, reason);
        }

        public RestorationQuote Quote(Antique antique, string optionId) => Quote(antique, Settings.Get(optionId));

        // ---------------------------------------------------------------- starting

        /// <summary>
        /// Sends the player's antique to the workshop: pays the fee (ledger: Restoration, also added
        /// to the antique's cost basis), rolls the outcome and marks the antique InRestoration.
        /// </summary>
        public bool StartRestoration(Antique antique, string optionId)
        {
            var option = Settings.Get(optionId);
            var quote = Quote(antique, option);
            if (quote == null || !quote.CanStart)
            {
                Debug.Log($"RestorationManager: can't restore {antique?.Name} at {option?.DisplayName ?? optionId} — {quote?.BlockReason ?? "unknown workshop"}");
                return false;
            }

            var inventory = PlayerInventory;
            int today = CurrentDay;

            var (outcome, result) = RollOutcome(quote);

            inventory.RemoveCash(quote.Cost, LedgerCategory.Restoration, $"Restoration of \"{antique.Name}\" ({option.DisplayName})");
            antique.AddRestorationCost(quote.Cost, today);
            antique.MarkInRestoration(quote.EndDay);

            var job = new RestorationJob
            {
                Owner = inventory,
                AntiqueId = antique.Id,
                AntiqueName = antique.Name,
                OptionId = option.Id,
                OptionName = option.DisplayName,
                StartDay = today,
                EndDay = quote.EndDay,
                Cost = quote.Cost,
                StartCondition = quote.StartCondition,
                TargetCondition = quote.TargetCondition,
                Outcome = outcome,
                ResultCondition = result
            };
            _jobs.Add(job);

            inventory.NotifyHoldingUpdated(antique.Id);
            Debug.Log($"RestorationManager: {antique.Name} sent to {option.DisplayName} until day {job.EndDay} for {quote.Cost:F2}.");
            OnRestorationStarted?.Invoke(job);
            return true;
        }

        /// <summary>Rolled once, when the job starts — reloading a save can't change it.</summary>
        private static (RestorationOutcome, float) RollOutcome(RestorationQuote quote)
        {
            var option = quote.Option;
            float roll = UnityEngine.Random.value;

            if (roll < option.SuccessChance)
                return (RestorationOutcome.Success, quote.TargetCondition);

            if (roll < option.SuccessChance + option.PartialChance)
                return (RestorationOutcome.Partial, quote.PartialCondition);

            float min = Mathf.Min(option.FailureLossMin, option.FailureLossMax);
            float max = Mathf.Max(option.FailureLossMin, option.FailureLossMax);
            float loss = UnityEngine.Random.Range(min, max);
            return (RestorationOutcome.Failure, Mathf.Max(Antique.MinCondition, quote.StartCondition - loss));
        }

        // ---------------------------------------------------------------- completing

        private void HandleDayChanged(int newDay)
        {
            var due = _jobs.Where(j => j.EndDay <= newDay).ToList();
            foreach (var job in due)
            {
                _jobs.Remove(job);
                Complete(job, newDay);
            }
        }

        private void Complete(RestorationJob job, int day)
        {
            var antique = job.Owner?.GetHolding(job.AntiqueId);
            if (antique == null)
            {
                Debug.LogWarning($"RestorationManager: {job.AntiqueName} ({job.AntiqueId}) is no longer owned — restoration dropped.");
                return;
            }

            float before = antique.Condition;
            float valueBefore = antique.CurrentPrice;

            antique.Condition = Mathf.Clamp(job.ResultCondition, Antique.MinCondition, Antique.MaxCondition);
            antique.CompleteRestoration();

            // Condition affects price — re-value before anyone is notified.
            if (CurrentMarket != null)
                CurrentMarket.RecalculatePrice(antique);

            antique.AppendHistory(HistoryLine(job, before, antique.Condition, day));

            var record = new RestorationRecord
            {
                AntiqueId = job.AntiqueId,
                AntiqueName = job.AntiqueName,
                OptionName = job.OptionName,
                Day = day,
                Outcome = job.Outcome,
                StartCondition = before,
                ResultCondition = antique.Condition,
                ValueChange = antique.CurrentPrice - valueBefore
            };
            AddRecord(record);

            job.Owner.NotifyHoldingUpdated(antique.Id);
            Debug.Log($"RestorationManager: {job.AntiqueName} back from {job.OptionName} — {job.Outcome}, condition {before:F2} → {antique.Condition:F2}.");
            OnRestorationCompleted?.Invoke(record);
        }

        private static string HistoryLine(RestorationJob job, float before, float after, int day)
        {
            string change = $"condition {Mathf.RoundToInt(before * 100f)}% → {Mathf.RoundToInt(after * 100f)}%";
            return job.Outcome switch
            {
                RestorationOutcome.Success => $"Restored by {job.OptionName} (day {day}), {change}.",
                RestorationOutcome.Partial => $"Partly restored by {job.OptionName} (day {day}), {change}.",
                _ => $"Damaged during restoration by {job.OptionName} (day {day}), {change}."
            };
        }

        private void AddRecord(RestorationRecord record)
        {
            _completed.Add(record);
            int max = Mathf.Max(5, Settings.MaxCompletedLog);
            if (_completed.Count > max)
                _completed.RemoveRange(0, _completed.Count - max);
        }

        // ---------------------------------------------------------------- queries

        public RestorationJob GetJob(string antiqueId) => _jobs.FirstOrDefault(j => j.AntiqueId == antiqueId);

        public IEnumerable<RestorationJob> GetJobsFor(TraderInventory owner) => _jobs.Where(j => j.Owner == owner);

        /// <summary>Restorations finished on or after the given day, newest first.</summary>
        public IEnumerable<RestorationRecord> GetCompletedSince(int sinceDay) =>
            _completed.Where(r => r.Day >= sinceDay).Reverse();

        // ---------------------------------------------------------------- save / load

        public RestorationState CaptureState()
        {
            var state = new RestorationState();

            foreach (var job in _jobs)
            {
                string ownerId = job.Owner != null ? job.Owner.OwnerId : null;
                if (string.IsNullOrEmpty(ownerId))
                    Debug.LogWarning($"RestorationManager: no owner Id for '{job.AntiqueName}' — this restoration will not survive the load.");
                state.Jobs.Add(RestorationJobState.Capture(job, ownerId));
            }

            state.Completed.AddRange(_completed);
            return state;
        }

        /// <summary>
        /// Call after EconomyManager.RestoreState — the antiques must already be in their owners'
        /// inventories. Also repairs antiques left InRestoration without a job (e.g. a save made
        /// before restoration existed can't have any, but a damaged save might).
        /// </summary>
        public void RestoreState(RestorationState state)
        {
            _jobs.Clear();
            _completed.Clear();

            if (state?.Jobs != null)
            {
                foreach (var jobState in state.Jobs)
                {
                    if (jobState == null) continue;

                    TraderInventory owner = null;
                    if (economyManager != null && !string.IsNullOrEmpty(jobState.OwnerId))
                        economyManager.InventoriesByOwnerId.TryGetValue(jobState.OwnerId, out owner);

                    var antique = owner?.GetHolding(jobState.AntiqueId);
                    if (antique == null)
                    {
                        Debug.LogWarning($"RestorationManager: '{jobState.AntiqueName}' was being restored, but it isn't in its owner's inventory after loading — restoration dropped.");
                        continue;
                    }

                    if (!antique.IsInRestoration)
                        antique.MarkInRestoration(jobState.EndDay);

                    _jobs.Add(jobState.Restore(owner));
                }
            }

            if (state?.Completed != null)
                foreach (var record in state.Completed)
                    if (record != null)
                        _completed.Add(record);

            ReleaseOrphans();
            OnRestorationsRestored?.Invoke();
        }

        // An antique marked InRestoration with no job would be stuck forever — make it available.
        private void ReleaseOrphans()
        {
            if (economyManager == null) return;

            foreach (var inventory in economyManager.Inventories)
            {
                if (inventory == null) continue;

                foreach (var antique in inventory.Holdings.Values)
                {
                    if (antique == null || !antique.IsInRestoration || GetJob(antique.Id) != null) continue;

                    Debug.LogWarning($"RestorationManager: {antique.Name} was marked as being restored but had no restoration job — returned to the warehouse unchanged.");
                    antique.CompleteRestoration();
                    inventory.NotifyHoldingUpdated(antique.Id);
                }
            }
        }

#if UNITY_EDITOR
        // ---------------------------------------------------------------- debug (editor only)

        [ContextMenu("Debug/Finish all restorations now")]
        private void DebugFinishAll()
        {
            int today = CurrentDay;
            foreach (var job in _jobs.ToList())
            {
                _jobs.Remove(job);
                Complete(job, today);
            }
        }
#endif
    }
}
