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

        [SerializeField] private Button closeButton;

        [Header("Header")]
        [SerializeField] private TMP_Text nameText;
        [Tooltip("Under the name: contract type and time left. Optional.")]
        [SerializeField] private TMP_Text infoText;
        [SerializeField] private Image contractImage;

        [Header("Facts (two columns, like the Auctions lot details)")]
        [Tooltip("When both are assigned they replace the per-line texts below.")]
        [SerializeField] private TMP_Text factLabelsText;
        [SerializeField] private TMP_Text factValuesText;

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
        [SerializeField] private GameObject feedPanel;
        [SerializeField] private GameObject detailsContent;

        private const string DefaultFulfillLabel = "Fulfill Contract";

        public event Action Closed;

        private Contract _contract;
        private Action<Contract> _onAccept;
        private Action<Contract> _onFulfill;
        private TooltipTrigger _acceptTooltip;

        private void Awake()
        {
            if (timeManager == null)
                timeManager = FindFirstObjectByType<TimeManager>();

            if (acceptButton != null)
            {
                acceptButton.onClick.AddListener(HandleAcceptClicked);
                _acceptTooltip = TooltipTrigger.On(acceptButton);
            }

            if (fulfillButton != null)
                fulfillButton.onClick.AddListener(HandleFulfillClicked);

            if (closeButton != null)
                closeButton.onClick.AddListener(Close);


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


        /// <param name="acceptBlockReason">Shown as the Accept button's tooltip while it is disabled.</param>
        public void Show(Contract contract, ContractsTab tab, bool canAccept, bool canFulfill = false, string fulfillLabel = null,
            string acceptBlockReason = null)
        {
            if (contract == null)
            {
                ShowEmptyState();
                return;
            }

            _contract = contract;

            if (detailsContent != null) detailsContent.SetActive(true);
            if (feedPanel != null) feedPanel.SetActive(false);

            var req = contract.Requirement;
            contractImage.ShowContract(req);

            int currentWeek = timeManager != null ? timeManager.CurrentWeek : contract.CreatedWeek;
            int weeksLeft = Mathf.Max(0, contract.DeadlineWeek - currentWeek);

            if (nameText != null)
                nameText.text = ContractDisplay.RequirementName(req); // the condition is listed in the facts below

            if (infoText != null)
                infoText.text = $"{ContractDisplay.TypeLabel(contract)} contract\nDue in {UIFormat.Weeks(weeksLeft)}";

            ShowFacts(contract, req);

            if (antiqueTypeText != null)
                antiqueTypeText.text = $"Antique Type: {(req.Scope == ContractAttributeScope.AntiqueType ? req.AntiqueType.ToDisplayString() : "Any")}";

            if (centuryText != null)
                centuryText.text = $"Century: {(req.Scope == ContractAttributeScope.Century ? req.Century.ToDisplayString() : "Any")}";

            if (countryText != null)
                countryText.text = $"Country: {(req.Scope == ContractAttributeScope.Country ? req.Country.ToDisplayString() : "Any")}";

            if (quantityText != null)
                quantityText.text = $"Quantity: {req.Quantity}\n{ContractDisplay.ConditionRequirement(req)}";

            if (payoutText != null)
                payoutText.text = $"Payout: {contract.TotalReward:F0} £";

            if (deadlineText != null)
                deadlineText.text = $"Deadline: {UIFormat.Weeks(weeksLeft)}";

            if (penaltyText != null)
                penaltyText.text = $"Penalty: {(contract.Penalty > 0f ? $"{contract.Penalty:F0} £" : "None")}";

            if (contractTypeText != null)
                contractTypeText.text = $"Type: {ContractDisplay.TypeLabel(contract)}";

            SetMode(tab);

            if (acceptButton != null)
                acceptButton.interactable = canAccept;

            if (_acceptTooltip != null)
                _acceptTooltip.Text = canAccept ? "" : acceptBlockReason ?? "";

            if (fulfillButtonLabel != null)
                fulfillButtonLabel.text = string.IsNullOrEmpty(fulfillLabel) ? DefaultFulfillLabel : fulfillLabel;

            if (fulfillButton != null)
                fulfillButton.interactable = canFulfill;
        }

        /// <summary>Labels (muted) and values side by side, one fact per line – the Auctions lot details layout.</summary>
        private void ShowFacts(Contract contract, ContractRequirement req)
        {
            if (factLabelsText == null || factValuesText == null)
                return;

            string Attribute(ContractAttributeScope scope, string value) => req.Scope == scope ? value : "Any";

            factLabelsText.text = string.Join("\n",
                "Antique type", "Century", "Country", "Quantity", "Condition", "", "Payout", "Penalty");

            factValuesText.text = string.Join("\n",
                Attribute(ContractAttributeScope.AntiqueType, req.AntiqueType.ToDisplayString()),
                Attribute(ContractAttributeScope.Century, req.Century.ToDisplayString()),
                Attribute(ContractAttributeScope.Country, req.Country.ToDisplayString()),
                req.Quantity.ToString(),
                req.HasMinCondition
                    ? $"at least {UIFormat.Percent(req.MinCondition)} ({UIFormat.ConditionLabel(req.MinCondition)})"
                    : "any",
                "",
                UIFormat.Money(contract.TotalReward),
                contract.Penalty > 0f ? UIFormat.Money(contract.Penalty) : "None");
        }

        public void ShowEmptyState()
        {
            _contract = null;
            if (detailsContent != null) detailsContent.SetActive(false);
            if (feedPanel != null) feedPanel.SetActive(true);
        }

        public void Close()
        {
            ShowEmptyState();
            Closed?.Invoke();
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
