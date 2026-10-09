using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.Market;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

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

        [Header("Mean reversion")]
        [Range(0f, 1f)]
        [SerializeField] private float supplyReversionRate = 0.05f;
        [Range(0f, 1f)]
        [SerializeField] private float demandReversionRate = 0.05f;


        [Header("Listing spawning")]
        [SerializeField] private int initialListingCount = 6;
        [SerializeField] private int newListingsPerDay = 1;

        public Market.Market Market { get; private set; }

        // Field-initialised so traders can register from their own Awake regardless
        // of script execution order; the Market reads this same list.
        private readonly List<TraderInventory> _inventories = new List<TraderInventory>();

        /// <summary>Every registered trader inventory (player and NPCs).</summary>
        public IReadOnlyList<TraderInventory> Inventories => _inventories;

        private readonly Dictionary<string, TraderInventory> _inventoriesByOwnerId = new Dictionary<string, TraderInventory>();
        public IReadOnlyDictionary<string, TraderInventory> InventoriesByOwnerId => _inventoriesByOwnerId;

        public void RegisterInventory(TraderInventory inventory) => RegisterInventory(inventory, null);

        public void RegisterInventory(TraderInventory inventory, string ownerId)
        {
            if (inventory == null) return;

            if (!_inventories.Contains(inventory))
                _inventories.Add(inventory);

            if (!string.IsNullOrEmpty(ownerId))
            {
                if (_inventoriesByOwnerId.TryGetValue(ownerId, out var existing) && existing != inventory)
                    Debug.LogWarning($"EconomyManager: owner Id '{ownerId}' was already registered to another inventory — overwriting. Owner Ids must be unique or saves will mix traders up.");

                _inventoriesByOwnerId[ownerId] = inventory;

                inventory.SetOwnerId(ownerId);
            }

            // Ledger entries need the game day; read lazily since this may run before our Awake.
            if (inventory.DayProvider == null)
                inventory.DayProvider = () => _timeManager != null ? _timeManager.CurrentDay : 1;
        }


        /// <summary>
        /// Removes the inventory from both lookups. Leaving it in _inventoriesByOwnerId would make
        /// removed traders (e.g. the NPCs replaced on load) reappear in the next save.
        /// </summary>
        public void UnregisterInventory(TraderInventory inventory)
        {
            if (inventory == null) return;

            _inventories.Remove(inventory);

            var staleOwnerIds = new List<string>();
            foreach (var pair in _inventoriesByOwnerId)
                if (pair.Value == inventory)
                    staleOwnerIds.Add(pair.Key);

            foreach (var ownerId in staleOwnerIds)
                _inventoriesByOwnerId.Remove(ownerId);
        }

        private Core.TimeManager _timeManager;
        public Core.TimeManager TimeManager => _timeManager;

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
            Market = new Market.Market(_inventories);

            List<AntiqueDefinition> allDefinitions = AntiqueDatabase.GetAll();

            foreach (var definition in allDefinitions)
            {
                Market.RegisterType(definition.Id, defaultInitialSupply, defaultInitialDemand);
            }

            for (int i = 0; i < initialListingCount; i++)
            {
                Market.GenerateListing(_timeManager.CurrentDay);
            }

            Debug.Log($"EconomyManager: market initialized with {Market.Listings.Count} listings across {allDefinitions.Count} antique types.");
        }

        private void HandleDayChanged(int newDay)
        {
            Market.ApplyMeanReversion(supplyReversionRate, demandReversionRate);
            Market.RecordDailyPrices(newDay);

            for (int i = 0; i < newListingsPerDay; i++)
            {
                var listing = Market.GenerateListing(newDay);
                if (listing != null)
                    Debug.Log($"EconomyManager: new listing appeared — {listing}");
            }
        }


        // ---------------------------------------------------------------- save / load
        public EconomyState CaptureState()
        {
            var state = new EconomyState { Market = Market != null ? Market.CaptureState() : null };

            foreach (var pair in _inventoriesByOwnerId)
                state.Inventories[pair.Key] = pair.Value.CaptureState();

            int unsaveable = _inventories.Count - _inventoriesByOwnerId.Count;
            if (unsaveable > 0)
                Debug.LogWarning($"EconomyManager: {unsaveable} inventory/ies registered without an owner Id and were left out of the save.");

            return state;
        }

        /// <summary>
        /// Pushes saved state into the inventories currently registered. Call AFTER every trader
        /// has registered (so each owner Id resolves) and after the clock has been restored, so
        /// ledger entries made afterwards get the right day.
        /// </param>
        public void RestoreState(EconomyState state, Func<string, WarehouseState, Warehouse> warehouseFactory = null)
        {
            if (state == null) return;

            // Inventories first: the market re-links owner-set listings to the Antique objects
            // these inventories hold, so they have to exist before Market.RestoreState runs.
            if (state.Inventories != null)
            {
                foreach (var pair in state.Inventories)
                {
                    if (!_inventoriesByOwnerId.TryGetValue(pair.Key, out var inventory))
                    {
                        Debug.LogWarning($"EconomyManager: save contains an inventory for owner '{pair.Key}', but no such trader is registered — skipped. Restore the traders (NPCManager.RestoreState) before the economy.");
                        continue;
                    }

                    inventory.RestoreState(
                        pair.Value,
                        warehouseFactory != null ? warehouseState => warehouseFactory(pair.Key, warehouseState) : (Func<WarehouseState, Warehouse>)null);
                }
            }

            if (Market == null)
                Debug.LogError("EconomyManager: RestoreState ran before Awake built the Market — load after the scene is initialised.");
            else if (state.Market != null)
                Market.RestoreState(state.Market);
            else
                Debug.LogWarning("EconomyManager: the save has no market state — keeping the freshly generated market from InitializeMarket.");
        }

    }
}
