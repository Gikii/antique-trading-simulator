using System;
using System.Collections.Generic;
using AntiqueTradingSimulator.Company;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Events;
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

        public override string OwnerId => Antique.PlayerOwnerId;

        /// <summary>
        /// Derived from the Information Network upgrade level — the only source of truth, so it is
        /// restored together with the upgrade levels. The serialized accessLevel field is ignored.
        /// </summary>
        public override InfoAccessLevel AccessLevel
        {
            get
            {
                var upgrades = Company != null ? Company.Upgrades : null;
                return upgrades != null ? upgrades.AccessLevel : InfoAccessLevel.LocalPress;
            }
        }

        /// <summary>The player's company (reputation, upgrades, statistics). Added automatically if missing.</summary>
        public CompanyManager Company { get; private set; }

        // Contract lifecycle from the player's point of view — CompanyManager turns these
        // into statistics, reputation and credibility.
        public event Action<Contract> OnContractAccepted;
        public event Action<Contract> OnContractFulfilled;
        public event Action<Contract> OnContractFailed;

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

            // After the warehouse is attached: CompanyManager reads it for the upgrades.
            Company = GetComponent<CompanyManager>();
            if (Company == null)
                Company = gameObject.AddComponent<CompanyManager>();
        }

        // Start, not OnEnable: EconomyManager sets up its TimeManager in its own Awake.
        private void Start()
        {
            _timeManager = economyManager != null ? economyManager.TimeManager : FindFirstObjectByType<Core.TimeManager>();
            if (_timeManager != null)
                _timeManager.OnDayChanged += HandleDayChanged;

            if (contractManager != null)
                contractManager.OnContractExpired += HandleContractExpired;
        }

        protected override void OnDestroy()
        {
            if (_timeManager != null)
                _timeManager.OnDayChanged -= HandleDayChanged;

            if (contractManager != null)
                contractManager.OnContractExpired -= HandleContractExpired;

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

        // ---------------------------------------------------------------- contracts

        /// <summary>Contracts the player has accepted and not yet fulfilled or failed.</summary>
        public int ActiveContractCount => Inventory.CommittedContractIds.Count;

        /// <summary>Limit from the Staff upgrade; CompanyUpgrades.UnlimitedContracts = no limit.</summary>
        public int MaxActiveContracts
        {
            get
            {
                var upgrades = Company != null ? Company.Upgrades : null;
                return upgrades != null ? upgrades.MaxActiveContracts : CompanyUpgrades.UnlimitedContracts;
            }
        }

        public bool HasFreeContractSlot => MaxActiveContracts < 0 || ActiveContractCount < MaxActiveContracts;

        /// <summary>
        /// True if the player's reputation is high enough for this contract (reputation only
        /// gives access — it never changes the reward).
        /// </summary>
        public bool MeetsReputationFor(Contract contract)
        {
            if (contract == null || contract.RequiredReputation <= 0) return true;
            return Company != null && Company.Reputation.Reputation >= contract.RequiredReputation;
        }

        /// <summary>
        /// Whether the player can accept this contract now, and if not, why (for tooltips).
        /// AcceptContract runs the same checks.
        /// </summary>
        public bool CanAcceptContract(Contract contract, out string reason)
        {
            reason = "";

            if (contract == null || contract.Status != ContractStatus.Active)
            {
                reason = "This contract is no longer available.";
                return false;
            }

            if (Inventory.IsCommittedToContract(contract.ContractId))
            {
                reason = "You have already accepted this contract.";
                return false;
            }

            if (contract.Type == ContractType.Exclusive && !contract.CanBeClaimed)
            {
                reason = "Another trader has already claimed this exclusive contract.";
                return false;
            }

            if (!MeetsReputationFor(contract))
            {
                string tier = Company != null ? Company.TierNameFor(contract.RequiredReputation) : "";
                reason = $"Requires {contract.RequiredReputation} reputation" +
                         (string.IsNullOrEmpty(tier) ? "." : $" ({tier}).") +
                         " Complete contracts and sell antiques to grow your reputation.";
                return false;
            }

            if (!HasFreeContractSlot)
            {
                reason = $"You already have {ActiveContractCount}/{MaxActiveContracts} active contracts. " +
                         "Fulfill one or upgrade Staff to take on more.";
                return false;
            }

            return true;
        }

        public bool AcceptContract(string contractId)
        {
            if (string.IsNullOrEmpty(contractId))
                return false;

            var contract = contractManager != null ? contractManager.GetById(contractId) : null;
            if (!CanAcceptContract(contract, out string reason))
            {
                if (contract != null)
                    Debug.Log($"PlayerTrader: cannot accept contract {contractId} — {reason}");
                return false;
            }

            if (contract.Type == ContractType.Exclusive &&
                !contractManager.ClaimContract(contractId, Antique.PlayerOwnerId))
                return false;

            if (!Inventory.CommitToContract(contractId))
                return false;

            OnContractAccepted?.Invoke(contract);
            return true;
        }

        public bool FulfillContract(string contractId, IEnumerable<string> listingIds)
        {
            if (string.IsNullOrEmpty(contractId) || contractManager == null)
                return false;

            if (!Inventory.IsCommittedToContract(contractId))
                return false;

            var contract = contractManager.GetById(contractId);
            if (!contractManager.FulfillContract(contractId, Antique.PlayerOwnerId, Inventory, listingIds))
                return false;

            Inventory.ReleaseCommittedContract(contractId);
            OnContractFulfilled?.Invoke(contract);
            return true;
        }

        /// <summary>
        /// A contract the player accepted ran out unfulfilled: drop the commitment and free
        /// the antiques reserved for it (ContractManager only does that for claimed exclusive
        /// contracts), then report the failure.
        /// </summary>
        private void HandleContractExpired(Contract contract)
        {
            if (contract == null || !Inventory.IsCommittedToContract(contract.ContractId))
                return;

            Inventory.ReleaseAllReservationsForContract(contract.ContractId);
            Inventory.ReleaseCommittedContract(contract.ContractId);
            OnContractFailed?.Invoke(contract);
        }

        // ---------------------------------------------------------------- save / load


        public Func<string, WarehouseState, Warehouse> WarehouseFactory => BuildWarehouse;

        private Warehouse BuildWarehouse(string ownerId, WarehouseState state)
        {
            if (ownerId != OwnerId || state == null) return null;

            if (warehouseSettings == null)
            {
                Debug.LogError("PlayerTrader: cannot rebuild the warehouse from the save — no WarehouseSettings assigned.");
                return null;
            }

            return new Warehouse(warehouseSettings, state.CapacityLevel, state.SecurityLevel);
        }

    }
}
