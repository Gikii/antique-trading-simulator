using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Market;

namespace AntiqueTradingSimulator.UI
{
    public enum ContractsTab
    {
        Available,
        Mine
    }


    public class ContractsView : UIView
    {
        [Header("Dependencies")]
        [SerializeField] private ContractManager contractManager;
        [SerializeField] private PlayerTrader playerTrader;
        [SerializeField] private TimeManager timeManager;

        [Header("Tabs")]
        [SerializeField] private Button availableTabButton;
        [SerializeField] private Button myContractsTabButton;
        [SerializeField] private GameObject availableTabSelectedHighlight;
        [SerializeField] private GameObject myContractsTabSelectedHighlight;

        [Header("Panels")]
        [SerializeField] private ContractListPanelUI listPanel;
        [SerializeField] private ContractDetailsUI detailsUI;

        [Header("Modals")]
        [Tooltip("Auto-found if empty — the modal lives under Canvas/Modals and starts inactive, " +
                 "so it's looked up including inactive objects.")]
        [SerializeField] private ContractFulfillModalUI fulfillModal;

        private ContractsTab _activeTab = ContractsTab.Available;
        private bool _subscribed;
        private Contract _selectedContract;

        private int CurrentDay => timeManager != null ? timeManager.CurrentDay : 0;

        void Awake()
        {
            if (contractManager == null) contractManager = FindFirstObjectByType<ContractManager>();
            if (playerTrader == null) playerTrader = FindFirstObjectByType<PlayerTrader>();
            if (timeManager == null) timeManager = FindFirstObjectByType<TimeManager>();

            if (availableTabButton != null)
                availableTabButton.onClick.AddListener(() => SetTab(ContractsTab.Available));

            if (myContractsTabButton != null)
                myContractsTabButton.onClick.AddListener(() => SetTab(ContractsTab.Mine));

            if (fulfillModal == null)
                fulfillModal = FindFirstObjectByType<ContractFulfillModalUI>(FindObjectsInactive.Include);

            if (fulfillModal != null)
                fulfillModal.Fulfilled += HandleContractFulfilledByPlayer;

            if (listPanel != null) listPanel.Initialize(ShowDetails);
            if (detailsUI != null) detailsUI.Initialize(AcceptContract, OpenFulfillModal);
        }

        protected override void OnShown()
        {
            if (!_subscribed)
            {
                if (contractManager != null)
                {
                    contractManager.OnContractCreated += HandleContractsChanged;
                    contractManager.OnContractClaimed += HandleContractClaimed;
                    contractManager.OnContractFulfilled += HandleContractsChanged;
                    contractManager.OnContractExpired += HandleContractsChanged;
                }

                if (timeManager != null)
                    timeManager.OnDayChanged += HandleDayChanged;

                _subscribed = true;
            }

            SetTab(_activeTab);
        }

        void OnDestroy()
        {
            if (fulfillModal != null)
                fulfillModal.Fulfilled -= HandleContractFulfilledByPlayer;

            if (!_subscribed) return;

            if (contractManager != null)
            {
                contractManager.OnContractCreated -= HandleContractsChanged;
                contractManager.OnContractClaimed -= HandleContractClaimed;
                contractManager.OnContractFulfilled -= HandleContractsChanged;
                contractManager.OnContractExpired -= HandleContractsChanged;
            }

            if (timeManager != null)
                timeManager.OnDayChanged -= HandleDayChanged;
        }

        private void HandleContractClaimed(Contract contract, string traderId) => RefreshActiveTab();
        private void HandleContractsChanged(Contract contract) => RefreshActiveTab();
        private void HandleDayChanged(int day) => RefreshActiveTab();

        public void SetTab(ContractsTab tab)
        {
            _activeTab = tab;

            if (availableTabSelectedHighlight != null)
                availableTabSelectedHighlight.SetActive(tab == ContractsTab.Available);

            if (myContractsTabSelectedHighlight != null)
                myContractsTabSelectedHighlight.SetActive(tab == ContractsTab.Mine);

            _selectedContract = null;

            if (listPanel != null) listPanel.ClearSelection();
            if (detailsUI != null)
            {
                detailsUI.SetMode(tab);
                detailsUI.ShowEmptyState();
            }

            RefreshActiveTab();
        }

        private void RefreshActiveTab()
        {
            if (!gameObject.activeInHierarchy) return;
            if (listPanel != null) listPanel.SetContracts(GetContractsForActiveTab(), CurrentDay);


            if (_selectedContract != null) ShowDetails(_selectedContract);
        }

        private List<Contract> GetContractsForActiveTab()
        {
            if (contractManager == null) return new List<Contract>();

            return _activeTab switch
            {
                ContractsTab.Available => contractManager.AllContracts
                    .Where(c => c.Status == ContractStatus.Active && (c.Type == ContractType.Open || c.CanBeClaimed))
                    .ToList(),

                ContractsTab.Mine => (playerTrader != null
                        ? playerTrader.Inventory.CommittedContractIds
                        : Enumerable.Empty<string>())
                    .Select(id => contractManager.GetById(id))
                    .Where(c => c != null)
                    .ToList(),

                _ => new List<Contract>()
            };
        }

        public void ShowDetails(Contract contract)
        {
            _selectedContract = contract;

            if (detailsUI == null) return;

            int eligible = EligibleAntiqueCount(contract);
            int required = contract != null ? contract.Requirement.Quantity : 0;
            bool canFulfill = CanPlayerFulfill(contract);

            string fulfillLabel = canFulfill
                ? "Fulfill Contract"
                : $"Fulfill Contract ({eligible}/{required} ready)";

            detailsUI.Show(contract, _activeTab, CanPlayerAccept(contract), canFulfill, fulfillLabel);
        }

        public bool CanPlayerAccept(Contract contract)
        {
            if (contract == null || contract.Status != ContractStatus.Active) return false;
            if (playerTrader != null && playerTrader.Inventory.IsCommittedToContract(contract.ContractId)) return false;

            return contract.Type == ContractType.Open || contract.CanBeClaimed;
        }

        public int EligibleAntiqueCount(Contract contract)
        {
            if (contract == null || playerTrader == null) return 0;
            return ContractFulfillModalUI.EligibleAntiques(playerTrader.Inventory, contract).Count;
        }

        public bool CanPlayerFulfill(Contract contract)
        {
            if (contract == null || playerTrader == null) return false;
            if (contract.Status != ContractStatus.Active) return false;

            if (!playerTrader.Inventory.IsCommittedToContract(contract.ContractId)) return false;
            if (!contract.CanBeFulfilledBy(Antique.PlayerOwnerId)) return false;

            return EligibleAntiqueCount(contract) >= contract.Requirement.Quantity;
        }

        public void OpenFulfillModal(Contract contract)
        {
            if (contract == null) return;

            if (fulfillModal == null)
            {
                Debug.LogWarning("ContractsView: no ContractFulfillModalUI assigned or found in the scene — " +
                                 "run Tools > Antique Trading Simulator > Build Contracts View to generate it.");
                return;
            }

            if (!CanPlayerFulfill(contract)) return;

            fulfillModal.Open(contract);
        }

        private void HandleContractFulfilledByPlayer(Contract contract)
        {
            // The contract leaves the My Contracts list the moment it's fulfilled
            // (the commitment is released), so drop the stale selection with it.
            _selectedContract = null;

            if (listPanel != null) listPanel.ClearSelection();
            if (detailsUI != null) detailsUI.ShowEmptyState();

            RefreshActiveTab();
        }

        public void AcceptContract(Contract contract)
        {
            if (contract == null || playerTrader == null) return;
            if (!CanPlayerAccept(contract)) return;

            if (!playerTrader.AcceptContract(contract.ContractId)) return;

            RefreshActiveTab();
            ShowDetails(contract);
        }
    }
}
