using System.Collections.Generic;
using UnityEngine;
using AntiqueTradingSimulator.Market;

namespace AntiqueTradingSimulator.Economy
{
    /// <summary>
    /// Central coordinator of the economy simulation. Owns the Market instance,
    /// registers every known AntiqueDefinition with it, seeds some initial listings,
    /// and reacts to the passage of time by rolling new listings in. Other systems
    /// (NPC, News, UI) should go through this manager rather than creating their own Market.
    /// </summary>
    [RequireComponent(typeof(Core.TimeManager))]
    public class EconomyManager : MonoBehaviour
    {
        [Header("Initial market conditions (per antique type)")]
        [SerializeField] private float defaultInitialSupply = 5f;
        [SerializeField] private float defaultInitialDemand = 5f;

        [Header("Listing spawning")]
        [SerializeField] private int initialListingCount = 6;
        [SerializeField] private int newListingsPerDay = 1;

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
                Market.RegisterType(definition.Id, defaultInitialSupply, defaultInitialDemand);
            }

            for (int i = 0; i < initialListingCount; i++)
            {
                Market.GenerateListing();
            }

            Debug.Log($"EconomyManager: market initialized with {Market.Listings.Count} listings across {allDefinitions.Count} antique types.");
        }

        private void HandleDayChanged(int newDay)
        {
            for (int i = 0; i < newListingsPerDay; i++)
            {
                var listing = Market.GenerateListing();
                if (listing != null)
                    Debug.Log($"EconomyManager: new listing appeared — {listing}");
            }
        }
    }
}
