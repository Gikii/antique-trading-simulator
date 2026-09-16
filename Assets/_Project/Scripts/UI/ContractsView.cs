using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Core;

namespace AntiqueTradingSimulator.UI
{
    // Only these two tabs exist for now. Closed Contracts and History are
    // deliberately left out — add them here, add a button, and add a case to
    // GetContractsForActiveTab() when they're ready; everything else (filters,
    // sorting, the details panel) already works generically off a Contract list.
    public enum ContractsTab
    {
        Available,
        Mine
    }

    /// <summary>
    /// Top-level Contracts view. Owns tab switching between "Available
    /// Contracts" and "My Contracts" and feeds the shared list/detail
    /// sub-components whichever data the active tab needs. The list panel and
    /// details panel are separate components (ContractListPanelUI,
    /// ContractDetailsUI) combined here, the same way MarketView combines
    /// MarketListingUI rows with AntiqueDetailsUI.
    /// </summary>
    public class ContractsView : UIView
    {
        [Header("Dependencies")]
        [SerializeField] private ContractManager contractManager;
        [SerializeField] private PlayerTrader playerTrader;
        [SerializeField] private TimeManager timeManager;

        [Header("Tabs")]
        [SerializeField] private Button availableTabButton;
        [SerializeField] private Button myContractsTabButton;
        [SerializeField] private GameObject availableTabSelectedHighlight; // optional
        [SerializeField] private GameObject myContractsTabSelectedHighlight; // optional

        [Header("Panels")]
        [SerializeField] private ContractListPanelUI listPanel;
        [SerializeField] private ContractDetailsUI detailsUI;

        private ContractsTab _activeTab = ContractsTab.Available;
        private bool _subscribed;

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

            if (listPanel != null) listPanel.Initialize(ShowDetails);
            if (detailsUI != null) detailsUI.Initialize(AcceptContract);
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

        // Called by the two tab buttons
        public void SetTab(ContractsTab tab)
        {
            _activeTab = tab;

            if (availableTabSelectedHighlight != null)
                availableTabSelectedHighlight.SetActive(tab == ContractsTab.Available);

            if (myContractsTabSelectedHighlight != null)
                myContractsTabSelectedHighlight.SetActive(tab == ContractsTab.Mine);

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
        }

        private List<Contract> GetContractsForActiveTab()
        {
            if (contractManager == null) return new List<Contract>();

            return _activeTab switch
            {
                ContractsTab.Available => contractManager.AllContracts
                    .Where(c => c.Status == ContractStatus.Active)
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
            if (detailsUI != null)
                detailsUI.Show(contract, _activeTab, CanPlayerAccept(contract));
        }

        public bool CanPlayerAccept(Contract contract)
        {
            if (contract == null || contract.Status != ContractStatus.Active) return false;
            if (playerTrader != null && playerTrader.Inventory.IsCommittedToContract(contract.ContractId)) return false;

            // Open contracts have no claim step — anyone can go after one at any
            // time. Exclusive contracts can only be accepted while unclaimed.
            return contract.Type == ContractType.Open || contract.CanBeClaimed;
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
