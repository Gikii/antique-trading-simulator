using System;
using System.Collections.Generic;
using UnityEngine;

namespace AntiqueTradingSimulator.Dependency
{
    /// <summary>
    /// Carries a change in one category's interest over to the categories it is linked with.
    /// Market movement is reported during the day, AdvanceDay runs once per day, and the result
    /// lands in each affected type's TempDemandMod. Prices stay with PriceEngine.
    /// </summary>
    public class DependencyEngine
    {
        private DependencyGraphSettings _settings;
        private Market.Market _market;
        private DependencyGraph _graph;

        // Live knock-on effects, newest last.
        private readonly List<DependencyInfluence> _influences = new List<DependencyInfluence>();

        // Signal reported since the last AdvanceDay, summed per category.
        private readonly Dictionary<MarketFacet, float> _pendingSignal = new Dictionary<MarketFacet, float>();

        // What this system has already added to each type's TempDemandMod.
        private readonly Dictionary<string, float> _appliedByDefinition = new Dictionary<string, float>();

        private readonly List<PropagatedInfluence> _propagationBuffer = new List<PropagatedInfluence>();

        /// <summary>Raised after a day has been processed.</summary>
        public event Action<int> OnDayProcessed;

        /// <summary>Works without a market too, which is how the rules are tuned in the editor.</summary>
        public DependencyEngine(DependencyGraphSettings settings, Market.Market market = null)
        {
            _settings = settings;
            _market = market;
            RebuildGraph();
        }

        /// <summary>Writes a line to the console for every demand change produced. Tuning aid.</summary>
        public bool LogChanges { get; set; }

        public DependencyGraphSettings Settings => _settings;

        /// <summary>The links the rules expand into.</summary>
        public DependencyGraph Graph => _graph;

        /// <summary>Every knock-on effect currently in play.</summary>
        public IReadOnlyList<DependencyInfluence> ActiveInfluences => _influences;

        /// <summary>Signal collected since the last AdvanceDay, per category.</summary>
        public IReadOnlyDictionary<MarketFacet, float> PendingSignal => _pendingSignal;

        /// <summary>What this system is currently adding to each antique type's demand.</summary>
        public IReadOnlyDictionary<string, float> CurrentModifiers => _appliedByDefinition;

        public float CurrentInfluenceOn(string definitionId) =>
            !string.IsNullOrEmpty(definitionId) && _appliedByDefinition.TryGetValue(definitionId, out float amount)
                ? amount
                : 0f;

        /// <summary>Live influences on one category, for tooltips like "mirrors are up because of paintings".</summary>
        public List<DependencyInfluence> InfluencesOn(MarketFacet facet) =>
            _influences.FindAll(influence => influence.Facet == facet);

        /// <summary>
        /// Points the system at the market it writes to. The bookkeeping starts over, because a new
        /// market carries none of the modifiers the old one was given.
        /// </summary>
        public void SetMarket(Market.Market market)
        {
            _market = market;
            _appliedByDefinition.Clear();
        }

        /// <summary>Swaps the rule set. Influences already in play are kept.</summary>
        public void SetSettings(DependencyGraphSettings settings)
        {
            _settings = settings;
            RebuildGraph();
        }

        /// <summary>Re-expands the rules into links. Call after editing the asset at runtime.</summary>
        public void RebuildGraph() => _graph = DependencyGraph.Build(_settings);

        // ---------------------------------------------------------------- collecting the signal

        /// <summary>
        /// Turns a market movement on one antique type into a signal on every category it belongs to.
        /// Supply counts with its sign flipped: stock leaving the market reads as interest.
        /// </summary>
        public void ReportPressure(string definitionId, float demandDelta, float supplyDelta)
        {
            if (_settings == null) return;

            float signal = demandDelta * _settings.DemandSignalWeight - supplyDelta * _settings.SupplySignalWeight;
            ReportDefinitionSignal(definitionId, signal);
        }

        /// <summary>An already-weighted signal on one antique type.</summary>
        public void ReportDefinitionSignal(string definitionId, float signal)
        {
            if (string.IsNullOrEmpty(definitionId) || Mathf.Approximately(signal, 0f)) return;

            foreach (var facet in MarketFacets.FacetsOf(definitionId))
                ReportInterest(facet, signal);
        }

        /// <summary>A change pushed straight onto a category — an event, a news item, a passing fashion.</summary>
        public void ReportInterest(MarketFacet facet, float signal)
        {
            if (Mathf.Approximately(signal, 0f)) return;

            _pendingSignal.TryGetValue(facet, out float current);
            _pendingSignal[facet] = current + signal;
        }

        // ---------------------------------------------------------------- the daily cycle

        /// <summary>
        /// One tick: fade what is in play, propagate what was collected, write the result out.
        /// Call once per game day, after the market has done its own daily work.
        /// </summary>
        public void AdvanceDay(int day)
        {
            if (_settings == null) return;

            DecayInfluences();
            PropagatePending(day);
            _pendingSignal.Clear();
            ApplyToMarket();

            OnDayProcessed?.Invoke(day);
        }

        /// <summary>Fades every influence by its own rate and drops the ones left below the cutoff.</summary>
        private void DecayInfluences()
        {
            for (int i = _influences.Count - 1; i >= 0; i--)
            {
                float left = _influences[i].Decay();
                if (Mathf.Abs(left) < _settings.InfluenceCutoff)
                    _influences.RemoveAt(i);
            }
        }

        private void PropagatePending(int day)
        {
            if (_pendingSignal.Count == 0 || _graph == null) return;

            foreach (var pending in _pendingSignal)
            {
                _propagationBuffer.Clear();
                _graph.Propagate(pending.Key, pending.Value, _settings.MaxPropagationDepth,
                                 _settings.DepthDamping, _settings.MinimumPropagatedChange, _propagationBuffer);

                foreach (var propagated in _propagationBuffer)
                    MergeInfluence(pending.Key, propagated, day);
            }
        }

        /// <summary>
        /// Adds a new effect to the live list. The same link repeating adds to the influence it
        /// already produced and refreshes it.
        /// </summary>
        private void MergeInfluence(MarketFacet origin, PropagatedInfluence propagated, int day)
        {
            foreach (var existing in _influences)
            {
                if (existing.Facet != propagated.Facet || existing.Origin != origin || existing.Kind != propagated.Kind)
                    continue;

                existing.Amount += propagated.Amount;
                existing.DecayPerDay = propagated.DecayPerDay;
                existing.Depth = Mathf.Min(existing.Depth, propagated.Depth);
                existing.LastRefreshedOnDay = day;
                return;
            }

            _influences.Add(new DependencyInfluence
            {
                Facet = propagated.Facet,
                Origin = origin,
                Kind = propagated.Kind,
                Amount = propagated.Amount,
                DecayPerDay = propagated.DecayPerDay,
                Depth = propagated.Depth,
                CreatedOnDay = day,
                LastRefreshedOnDay = day
            });
        }

        // ---------------------------------------------------------------- writing the result out

        /// <summary>
        /// Gives every antique type inside a category the full influence aimed at that category,
        /// and caps the total per type.
        /// </summary>
        public Dictionary<string, float> ComputeDesiredModifiers()
        {
            var desired = new Dictionary<string, float>();

            foreach (var influence in _influences)
            {
                if (influence == null) continue;

                foreach (var definitionId in MarketFacets.ResolveDefinitionIds(influence.Facet))
                {
                    desired.TryGetValue(definitionId, out float current);
                    desired[definitionId] = current + influence.Amount;
                }
            }

            float cap = _settings != null ? _settings.MaxInfluencePerDefinition : 3f;
            var keys = new List<string>(desired.Keys);
            foreach (var key in keys)
                desired[key] = Mathf.Clamp(desired[key], -cap, cap);

            return desired;
        }

        /// <summary>A change must reach this size to be written, unless it is the last one.</summary>
        private const float MinimumWrite = 0.0001f;

        // Types whose modifier changed today and therefore need re-pricing.
        private readonly HashSet<string> _repriceQueue = new HashSet<string>();

        /// <summary>
        /// Writes the difference between what each type should feel and what it was last given into
        /// TempDemandMod, so the modifier keeps what event effects put there, and re-prices the
        /// types that moved.
        /// </summary>
        private void ApplyToMarket()
        {
            var desired = ComputeDesiredModifiers();

            if (_market == null)
            {
                // Tuning without a market: the numbers are still readable through CurrentModifiers.
                _appliedByDefinition.Clear();
                foreach (var pair in desired)
                    if (pair.Value != 0f)
                        _appliedByDefinition[pair.Key] = pair.Value;
                return;
            }

            var touched = new HashSet<string>(desired.Keys);
            touched.UnionWith(_appliedByDefinition.Keys);

            var nextApplied = new Dictionary<string, float>(touched.Count);

            foreach (var definitionId in touched)
            {
                desired.TryGetValue(definitionId, out float target);
                _appliedByDefinition.TryGetValue(definitionId, out float applied);

                float delta = target - applied;
                bool releasing = target == 0f;

                // A skipped write keeps the remembered value, so the fraction still sitting in the
                // market stays accounted for. The final release is always written.
                if (!releasing && Mathf.Abs(delta) < MinimumWrite)
                {
                    if (applied != 0f) nextApplied[definitionId] = applied;
                    continue;
                }

                // Remember the new value once the market actually holds it: a type the market does
                // not know keeps nothing.
                bool marketHoldsTarget = delta == 0f || Write(definitionId, delta);
                if (marketHoldsTarget && target != 0f)
                    nextApplied[definitionId] = target;
            }

            _appliedByDefinition.Clear();
            foreach (var pair in nextApplied)
                _appliedByDefinition[pair.Key] = pair.Value;

            Reprice();
        }

        /// <summary>Adds the change to one type's temporary demand. False when the market has no such type.</summary>
        private bool Write(string definitionId, float delta)
        {
            var typeState = _market.GetTypeState(definitionId);
            if (typeState == null) return false;

            typeState.TempDemandMod += delta;
            _repriceQueue.Add(definitionId);

            if (LogChanges)
                Debug.Log($"Dependencies: {definitionId} demand {delta:+0.000;-0.000}");

            return true;
        }

        private void Reprice()
        {
            foreach (var definitionId in _repriceQueue)
                _market.RecalculatePricesForDefinition(definitionId);

            _repriceQueue.Clear();
        }

        /// <summary>Takes every influence back out of the market and forgets it.</summary>
        public void ClearAll()
        {
            _influences.Clear();
            _pendingSignal.Clear();

            if (_market != null)
            {
                foreach (var pair in _appliedByDefinition)
                    if (pair.Value != 0f)
                        Write(pair.Key, -pair.Value);

                Reprice();
            }

            _appliedByDefinition.Clear();
        }

        // ---------------------------------------------------------------- save / load

        public DependencyState CaptureState() => DependencyState.Capture(_influences, _pendingSignal);

        /// <summary>
        /// Rebuilds the live influences from a save and brings the bookkeeping up to date; the
        /// restored market already holds the modifiers they produced. Restore after the economy.
        /// </summary>
        public void RestoreState(DependencyState state)
        {
            _influences.Clear();
            _pendingSignal.Clear();
            _appliedByDefinition.Clear();

            if (state == null) return;

            foreach (var influence in state.RestoreInfluences())
                _influences.Add(influence);

            foreach (var pair in state.RestorePending())
                _pendingSignal[pair.Key] = pair.Value;

            foreach (var pair in ComputeDesiredModifiers())
                if (pair.Value != 0f)
                    _appliedByDefinition[pair.Key] = pair.Value;
        }
    }
}
