using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.Core;

namespace AntiqueTradingSimulator.UI
{
    public class ContractDetailsUI : MonoBehaviour
    {
        [SerializeField] private TimeManager timeManager;

        [Header("Header")]
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private Image contractImage;

        [Header("Requirements (left list)")]
        [SerializeField] private TMP_Text antiqueTypeText;
        [SerializeField] private TMP_Text centuryText;
        [SerializeField] private TMP_Text countryText;
        [SerializeField] private TMP_Text quantityText;

        [Header("Contract terms (right list)")]
        [SerializeField] private TMP_Text payoutText;
        [SerializeField] private TMP_Text deadlineText;
        [SerializeField] private TMP_Text penaltyText;
        [SerializeField] private TMP_Text contractTypeText;

        [Header("Available tab only")]
        [SerializeField] private GameObject acceptButtonContainer;
        [SerializeField] private Button acceptButton;

        [Header("My Contracts tab only")]

        [SerializeField] private GameObject fulfillButtonContainer;
        [SerializeField] private Button fulfillButton;
        [SerializeField] private TMP_Text fulfillButtonLabel;
        [SerializeField] private GameObject myContractsContent;

        [Header("Empty state")]
        [SerializeField] private GameObject noSelectionState;
        [SerializeField] private GameObject detailsContent;

        private const string DefaultFulfillLabel = "Fulfill Contract";

        private Contract _contract;
        private Action<Contract> _onAccept;
        private Action<Contract> _onFulfill;

        private void Awake()
        {
            if (timeManager == null)
                timeManager = FindFirstObjectByType<TimeManager>();

            if (acceptButton != null)
                acceptButton.onClick.AddListener(HandleAcceptClicked);

            if (fulfillButton != null)
                fulfillButton.onClick.AddListener(HandleFulfillClicked);

            ShowEmptyState();
        }

        public void Initialize(Action<Contract> onAccept, Action<Contract> onFulfill = null)
        {
            _onAccept = onAccept;
            _onFulfill = onFulfill;
        }

        public void SetMode(ContractsTab tab)
        {
            if (acceptButtonContainer != null)
                acceptButtonContainer.SetActive(tab == ContractsTab.Available);

            if (fulfillButtonContainer != null)
                fulfillButtonContainer.SetActive(tab == ContractsTab.Mine);

            if (myContractsContent != null)
                myContractsContent.SetActive(tab == ContractsTab.Mine);
        }


        public void Show(Contract contract, ContractsTab tab, bool canAccept, bool canFulfill = false, string fulfillLabel = null)
        {
            if (contract == null)
            {
                ShowEmptyState();
                return;
            }

            _contract = contract;

            if (detailsContent != null) detailsContent.SetActive(true);
            if (noSelectionState != null) noSelectionState.SetActive(false);

            if (nameText != null)
                nameText.text = string.Empty;

            var req = contract.Requirement;

            if (antiqueTypeText != null)
                antiqueTypeText.text = $"Antique Type: {(req.Scope == ContractAttributeScope.AntiqueType ? req.AntiqueType.ToDisplayString() : "Any")}";

            if (centuryText != null)
                centuryText.text = $"Century: {(req.Scope == ContractAttributeScope.Century ? req.Century.ToDisplayString() : "Any")}";

            if (countryText != null)
                countryText.text = $"Country: {(req.Scope == ContractAttributeScope.Country ? req.Country.ToDisplayString() : "Any")}";

            if (quantityText != null)
                quantityText.text = $"Quantity: {req.Quantity}";

            if (payoutText != null)
                payoutText.text = $"Payout: {contract.TotalReward:F0} €";

            if (deadlineText != null)
            {
                int currentDay = timeManager != null ? timeManager.CurrentDay : contract.CreatedDay;
                int daysLeft = Mathf.Max(0, contract.DeadlineDay - currentDay);
                deadlineText.text = $"Deadline: {daysLeft} day(s)";
            }

            if (penaltyText != null)
                penaltyText.text = $"Penalty: {(contract.Penalty > 0f ? $"{contract.Penalty:F0} €" : "None")}";

            if (contractTypeText != null)
                contractTypeText.text = $"Type: {contract.Type.ToDisplayString()}";

            SetMode(tab);

            if (acceptButton != null)
                acceptButton.interactable = canAccept;

            if (fulfillButtonLabel != null)
                fulfillButtonLabel.text = string.IsNullOrEmpty(fulfillLabel) ? DefaultFulfillLabel : fulfillLabel;

            if (fulfillButton != null)
                fulfillButton.interactable = canFulfill;
        }

        public void ShowEmptyState()
        {
            _contract = null;
            if (detailsContent != null) detailsContent.SetActive(false);
            if (noSelectionState != null) noSelectionState.SetActive(true);
        }

        private void HandleAcceptClicked()
        {
            if (_contract == null) return;
            _onAccept?.Invoke(_contract);
        }

        private void HandleFulfillClicked()
        {
            if (_contract == null) return;
            _onFulfill?.Invoke(_contract);
        }
    }
}
