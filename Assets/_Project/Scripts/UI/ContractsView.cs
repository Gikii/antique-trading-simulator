using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Core;

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
            if (detailsUI != null)
                detailsUI.Show(contract, _activeTab, CanPlayerAccept(contract));
        }

        public bool CanPlayerAccept(Contract contract)
        {
            if (contract == null || contract.Status != ContractStatus.Active) return false;
            if (playerTrader != null && playerTrader.Inventory.IsCommittedToContract(contract.ContractId)) return false;

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
