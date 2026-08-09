using System.Collections.Generic;
using UnityEngine;
using AntiqueTradingSimulator.Market;

namespace AntiqueTradingSimulator.Economy
{
    /// <summary>
    /// Central coordinator of the economy simulation. Owns the Market instance,
    /// initializes it from all known AntiqueDefinitions, and reacts to the
    /// passage of time. Other systems (NPC, News, UI) should go through this
    /// manager rather than creating their own Market.
    /// </summary>
    [RequireComponent(typeof(Core.TimeManager))]
    public class EconomyManager : MonoBehaviour
    {
        [SerializeField] private float defaultInitialSupply = 10f;
        [SerializeField] private float defaultInitialDemand = 5f;

        public Market.Market Market { get; private set; }

        private Core.TimeManager _timeManager;

        void Awake()
        {
            _timeManager = GetComponent<Core.TimeManager>();
            InitializeMarket();
        }

        void OnEnable()
        {
            _timeManager.OnDayChanged += HandleDayChanged;
        }

        void OnDisable()
        {
            _timeManager.OnDayChanged -= HandleDayChanged;
        }

        private void InitializeMarket()
        {
            Market = new Market.Market();

            List<AntiqueDefinition> allDefinitions = AntiqueDatabase.GetAll();

            foreach (var definition in allDefinitions)
            {
                var antique = new Antique(definition.Id, defaultInitialSupply, defaultInitialDemand);
                Market.AddAntique(antique);
            }

            Debug.Log($"EconomyManager: market initialized with {Market.Antiques.Count} antiques.");
        }

        private void HandleDayChanged(int newDay)
        {
            // Placeholder for future logic: NPC decisions, news events, dependency effects, etc.
            // For now this is just the hook point future systems will subscribe to.
        }
    }
}