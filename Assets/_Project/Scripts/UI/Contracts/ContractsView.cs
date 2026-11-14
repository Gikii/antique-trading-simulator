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

        private int CurrentWeek => timeManager != null ? timeManager.CurrentWeek : 0;

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

            if (listPanel != null) listPanel.Initialize(ShowDetails, ReputationLockReason, IsAcceptedOnAvailableTab);
            if (detailsUI != null)
            {
                detailsUI.Initialize(RequestAccept, OpenFulfillModal);
                detailsUI.Closed += HandleDetailsClosed;
            }
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
                    timeManager.OnWeekChanged += HandleWeekChanged;

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
                timeManager.OnWeekChanged -= HandleWeekChanged;
        }

        private void HandleContractClaimed(Contract contract, string traderId) => RefreshActiveTab();
        private void HandleContractsChanged(Contract contract) => RefreshActiveTab();
        private void HandleDetailsClosed()
        {
            _selectedContract = null;
            if (listPanel != null) listPanel.ClearSelection();
        }

        private void HandleWeekChanged(int week) => RefreshActiveTab();

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
            if (listPanel != null) listPanel.SetContracts(GetContractsForActiveTab(), CurrentWeek);

            // The selected contract was delivered (by anyone) or expired — it's no longer in either list.
            if (_selectedContract != null && _selectedContract.Status != ContractStatus.Active)
            {
                _selectedContract = null;
                if (listPanel != null) listPanel.ClearSelection();
                if (detailsUI != null) detailsUI.ShowEmptyState();
                return;
            }

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
                    // PlayerTrader drops closed contracts itself, but its handler may run after this refresh.
                    .Where(c => c != null && c.Status == ContractStatus.Active)
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

            string acceptBlockReason = "";
            bool canAccept = playerTrader != null && playerTrader.CanAcceptContract(contract, out acceptBlockReason);

            detailsUI.Show(contract, _activeTab, canAccept, canFulfill, fulfillLabel, acceptBlockReason);
        }

        /// <summary>Same checks as PlayerTrader.AcceptContract (state, reputation, Staff contract limit).</summary>
        public bool CanPlayerAccept(Contract contract) =>
            playerTrader != null && playerTrader.CanAcceptContract(contract, out _);

        /// <summary>For the list rows: why the player's reputation is too low for a contract, or null.</summary>
        private string ReputationLockReason(Contract contract)
        {
            if (contract == null || playerTrader == null || playerTrader.MeetsReputationFor(contract)) return null;

            string tier = playerTrader.Company != null ? playerTrader.Company.TierNameFor(contract.RequiredReputation) : "";
            return $"Requires {contract.RequiredReputation} reputation" + (string.IsNullOrEmpty(tier) ? "" : $" ({tier})");
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

        /// <summary>
        /// Rows of the Available tab for contracts the player has already accepted (open contracts stay on
        /// the board — anyone may still deliver them) are dimmed and marked "Accepted". Not on My Contracts,
        /// where every row is accepted.
        /// </summary>
        private bool IsAcceptedOnAvailableTab(Contract contract) =>
            _activeTab == ContractsTab.Available && contract != null && playerTrader != null &&
            playerTrader.Inventory.IsCommittedToContract(contract.ContractId);

        private ContractAcceptDialog _acceptDialog;

        /// <summary>Copied lazily under the same parent as the fulfil modal (the gameplay modals).</summary>
        private ContractAcceptDialog AcceptDialog
        {
            get
            {
                if (_acceptDialog == null)
                {
                    Transform parent = fulfillModal != null ? fulfillModal.transform.parent : transform.root;
                    _acceptDialog = ContractAcceptDialog.Create(parent);
                }

                return _acceptDialog;
            }
        }

        /// <summary>
        /// The Accept button: asks first, listing what the contract demands and what delivering or failing
        /// it does. Accepts straight away if the scene has no dialog to show.
        /// </summary>
        public void RequestAccept(Contract contract)
        {
            if (contract == null || playerTrader == null) return;
            if (!CanPlayerAccept(contract)) return;

            var dialog = AcceptDialog;
            if (dialog == null)
            {
                AcceptContract(contract);
                return;
            }

            var company = playerTrader.Company;
            string message = ContractDisplay.AcceptConfirmation(
                contract,
                company != null ? company.ReputationSettings : null,
                CurrentWeek,
                EligibleAntiqueCount(contract),
                playerTrader.ActiveContractCount,
                playerTrader.MaxActiveContracts);

            dialog.Open("Accept this contract?", message, "Accept", () => AcceptContract(contract));
        }

        public void AcceptContract(Contract contract)
        {
            if (contract == null || playerTrader == null) return;
            // Checked again: the board may have changed since the dialog opened (it pauses the clock, but
            // an NPC acting on the same tick could still have claimed it).
            if (!CanPlayerAccept(contract)) return;

            if (!playerTrader.AcceptContract(contract.ContractId)) return;

            RefreshActiveTab();
            ShowDetails(contract);
        }
    }
}
