using System.Collections.Generic;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.Market;
using UnityEngine;

namespace AntiqueTradingSimulator.Agents
{
    /// <summary>
    /// The player's trading agent. Makes no decisions on its own — UI code
    /// (button onClick handlers, etc.) calls the inherited BuyListing/SellListing
    /// directly with the Id of whatever listing was clicked.
    /// </summary>
    public class PlayerTrader : TraderAgent
    {
        [SerializeField] private ContractManager contractManager;

        [Header("Warehouse")]
        [SerializeField] private WarehouseSettings warehouseSettings;
        [SerializeField] private int startingCapacityLevel = 0;
        [SerializeField] private int startingSecurityLevel = 0;

        private Core.TimeManager _timeManager;

        public Warehouse Warehouse => Inventory.Warehouse;

        protected override void Awake()
        {
            base.Awake();

            if (contractManager == null)
                contractManager = FindFirstObjectByType<ContractManager>();

            contractManager?.RegisterTrader(Antique.PlayerOwnerId, Inventory);

            if (warehouseSettings == null)
            {
                Debug.LogWarning("PlayerTrader: no WarehouseSettings assigned — using default values.");
                warehouseSettings = ScriptableObject.CreateInstance<WarehouseSettings>();
            }
            Inventory.AttachWarehouse(new Warehouse(warehouseSettings, startingCapacityLevel, startingSecurityLevel));
        }

        // Start, not OnEnable: EconomyManager sets up its TimeManager in its own Awake.
        private void Start()
        {
            _timeManager = economyManager != null ? economyManager.TimeManager : FindFirstObjectByType<Core.TimeManager>();
            if (_timeManager != null)
                _timeManager.OnDayChanged += HandleDayChanged;
        }

        protected override void OnDestroy()
        {
            if (_timeManager != null)
                _timeManager.OnDayChanged -= HandleDayChanged;

            base.OnDestroy();
        }

        private void HandleDayChanged(int newDay)
        {
            float upkeep = Inventory.ChargeWarehouseUpkeep();
            if (upkeep > 0f)
                Debug.Log($"PlayerTrader: warehouse upkeep {upkeep:F2} € charged on day {newDay}. Cash: {Inventory.Cash:F2}");
        }

        public bool UpgradeWarehouseCapacity() => Inventory.TryUpgradeWarehouseCapacity();

        public bool UpgradeWarehouseSecurity() => Inventory.TryUpgradeWarehouseSecurity();

        public bool AcceptContract(string contractId)
        {
            if (string.IsNullOrEmpty(contractId) || Inventory.IsCommittedToContract(contractId))
                return false;

            var contract = contractManager != null ? contractManager.GetById(contractId) : null;
            if (contract == null || contract.Status != ContractStatus.Active)
                return false;

            if (contract.Type == ContractType.Exclusive &&
                !contractManager.ClaimContract(contractId, Antique.PlayerOwnerId))
                return false;

            return Inventory.CommitToContract(contractId);
        }

        public bool FulfillContract(string contractId, IEnumerable<string> listingIds)
        {
            if (string.IsNullOrEmpty(contractId) || contractManager == null)
                return false;

            if (!Inventory.IsCommittedToContract(contractId))
                return false;

            if (!contractManager.FulfillContract(contractId, Antique.PlayerOwnerId, Inventory, listingIds))
                return false;

            Inventory.ReleaseCommittedContract(contractId);
            return true;
        }
    }
}
