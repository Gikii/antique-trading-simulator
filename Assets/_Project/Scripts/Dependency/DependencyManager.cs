using System;
using System.Collections.Generic;
using AntiqueTradingSimulator.Economy;
using UnityEngine;

namespace AntiqueTradingSimulator.Dependency
{
    /// <summary>
    /// Runs the dependency network in a scene. Put it on the same GameObject as EconomyManager: it
    /// listens for the market's supply/demand changes, advances the network once per day, and writes
    /// the knock-on effects into each affected type's temporary demand.
    /// </summary>
    [DisallowMultipleComponent]
    public class DependencyManager : MonoBehaviour
    {
        [Tooltip("Left empty: found in the scene at Awake.")]
        [SerializeField] private EconomyManager economyManager;

        [Tooltip("The authored dependency network. Left empty and 'Load From Resources' on: loaded " +
                 "by name from Resources (Data/Resources/DependencyGraph.asset).")]
        [SerializeField] private DependencyGraphSettings settings;

        [Tooltip("Look the settings asset up in Resources when the field above is empty.")]
        [SerializeField] private bool loadSettingsFromResources = true;

        [Tooltip("Write every demand change this system produces to the console. Tuning aid.")]
        [SerializeField] private bool logInfluences;

        private DependencyEngine _engine;
        private Market.Market _boundMarket;
        private bool _started;
        private bool _subscribed;

        /// <summary>Raised after a day has been processed, for UI listing the live effects.</summary>
        public event Action OnInfluencesChanged;

        /// <summary>Built on first use, so it also works in edit mode.</summary>
        public DependencyEngine Engine
        {
            get
            {
                if (_engine == null) BuildEngine();
                return _engine;
            }
        }

        public DependencyGraphSettings Settings => settings;
        public DependencyGraph Graph => Engine.Graph;
        public IReadOnlyList<DependencyInfluence> ActiveInfluences => Engine.ActiveInfluences;

        private Market.Market MarketInstance => economyManager != null ? economyManager.Market : null;

        // ---------------------------------------------------------------- lifecycle

        private void Awake()
        {
            if (economyManager == null) economyManager = FindFirstObjectByType<EconomyManager>();
            if (economyManager == null)
                Debug.LogError("DependencyManager: no EconomyManager in the scene — category links will stay idle.");

            if (_engine == null) BuildEngine();
        }

        // Start, so EconomyManager has built its Market in Awake whatever the execution order.
        private void Start()
        {
            _started = true;
            Subscribe();
        }

        private void OnEnable()
        {
            if (_started) Subscribe();
        }

        private void OnDisable() => Unsubscribe();

        private void BuildEngine()
        {
            if (settings == null && loadSettingsFromResources)
                settings = Resources.Load<DependencyGraphSettings>(DependencyGraphSettings.DefaultResourcesPath);

            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<DependencyGraphSettings>();
                Debug.LogWarning($"DependencyManager: no DependencyGraphSettings assigned and none found at " +
                                 $"Resources/{DependencyGraphSettings.DefaultResourcesPath} — running with an empty " +
                                 $"graph, so nothing will propagate. Create one with Tools → Antique Trading " +
                                 $"Simulator → Economy → Create Example Dependency Graph.");
            }

            _engine = new DependencyEngine(settings, MarketInstance) { LogChanges = logInfluences };
            _engine.OnWeekProcessed += HandleWeekProcessed;
        }

        private void Subscribe()
        {
            if (_subscribed) return;

            var market = MarketInstance;
            if (market == null) return;

            // Only on a different market: SetMarket starts the bookkeeping over, and re-enabling the
            // component would otherwise orphan the modifiers already sitting in this one.
            if (!ReferenceEquals(_boundMarket, market))
            {
                Engine.SetMarket(market);
                _boundMarket = market;
            }

            market.OnPressureApplied += Engine.ReportPressure;

            if (economyManager.TimeManager != null)
                economyManager.TimeManager.OnWeekChanged += Engine.AdvanceWeek;

            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;

            var market = MarketInstance;
            if (market != null)
                market.OnPressureApplied -= Engine.ReportPressure;

            if (economyManager != null && economyManager.TimeManager != null)
                economyManager.TimeManager.OnWeekChanged -= Engine.AdvanceWeek;

            _subscribed = false;
        }

        private void HandleWeekProcessed(int week) => OnInfluencesChanged?.Invoke();

        // ---------------------------------------------------------------- forwarding

        /// <summary>A change pushed straight onto a category — news, an event, a passing fashion.</summary>
        public void ReportInterest(MarketFacet facet, float signal) => Engine.ReportInterest(facet, signal);

        public void ReportPressure(string definitionId, float demandDelta, float supplyDelta) =>
            Engine.ReportPressure(definitionId, demandDelta, supplyDelta);

        public void ReportCategoryPressure(MarketFacet facet, float demandDelta, float supplyDelta) =>
            Engine.ReportCategoryPressure(facet, demandDelta, supplyDelta);


        public void AdvanceWeek(int week) => Engine.AdvanceWeek(week);

        public float CurrentInfluenceOn(string definitionId) => Engine.CurrentInfluenceOn(definitionId);

        public List<DependencyInfluence> InfluencesOn(MarketFacet facet) => Engine.InfluencesOn(facet);

        /// <summary>Re-reads the rule asset. Call after editing rules while the game runs.</summary>
        public void RebuildGraph() => Engine.RebuildGraph();

        /// <summary>Takes every influence back out of the market and forgets it.</summary>
        public void ClearAll()
        {
            Engine.ClearAll();
            OnInfluencesChanged?.Invoke();
        }

        // ---------------------------------------------------------------- save / load

        public DependencyState CaptureState() => Engine.CaptureState();

        /// <summary>Restore after the economy; this rebuilds the bookkeeping only.</summary>
        public void RestoreState(DependencyState state)
        {
            Engine.RestoreState(state);
            OnInfluencesChanged?.Invoke();
        }

        // ---------------------------------------------------------------- inspector helpers

        [ContextMenu("Log graph summary")]
        private void LogGraphSummary()
        {
            var graph = Engine.Graph;
            Debug.Log($"DependencyManager: {graph.EdgeCount} link(s) between categories, " +
                      $"{graph.SelfLinkCount} self-link(s), {Engine.ActiveInfluences.Count} live influence(s).");
        }

        [ContextMenu("Log live influences")]
        private void LogInfluences()
        {
            var influences = Engine.ActiveInfluences;
            if (influences.Count == 0)
            {
                Debug.Log("DependencyManager: nothing in play.");
                return;
            }

            foreach (var influence in influences)
                Debug.Log("DependencyManager: " + influence);
        }
    }
}
