using System;
using System.Collections.Generic;
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
        private TooltipTrigger _valueTooltip;
        private Company.CompanyManager _company;

        /// <summary>The player's reputation rules — what fulfilling or failing a contract does to reputation.</summary>
        private Company.ReputationSettings ReputationRules
        {
            get
            {
                if (_company == null) _company = FindFirstObjectByType<Company.CompanyManager>();
                return _company != null ? _company.ReputationSettings : null;
            }
        }

        private void Awake()
        {
            if (timeManager == null)
                timeManager = FindFirstObjectByType<TimeManager>();

            // Explains the value floor (and its tolerance) on hover over the requirements.
            var valueTextTarget = factValuesText != null ? factValuesText : quantityText;
            if (valueTextTarget != null)
            {
                valueTextTarget.raycastTarget = true;
                _valueTooltip = TooltipTrigger.On(valueTextTarget);
            }

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
                antiqueTypeText.text = $"Antique Type: {ContractDisplay.AttributeValue(req, ContractAttributeScope.AntiqueType)}";

            if (centuryText != null)
                centuryText.text = $"Century: {ContractDisplay.AttributeValue(req, ContractAttributeScope.Century)}";

            if (countryText != null)
                countryText.text = $"Country: {ContractDisplay.AttributeValue(req, ContractAttributeScope.Country)}";

            if (quantityText != null)
                quantityText.text = $"Quantity: {req.Quantity}\n{ContractDisplay.ConditionRequirement(req)}\n{ContractDisplay.ValueRequirement(req)}";

            if (payoutText != null)
            {
                string premium = ContractDisplay.PremiumText(contract);
                payoutText.text = string.IsNullOrEmpty(premium)
                    ? $"Payout: {UIFormat.Money(contract.TotalReward)}"
                    : $"Payout: {UIFormat.Money(contract.TotalReward)}\n{UIFormat.Colorize(premium, UIFormat.PositiveColor)}";
            }

            if (_valueTooltip != null)
                _valueTooltip.Text = ContractDisplay.ValueRequirementHint(req);

            if (deadlineText != null)
                deadlineText.text = $"Deadline: {UIFormat.Weeks(weeksLeft)}";

            if (penaltyText != null)
            {
                string stakes = ContractDisplay.ReputationStakes(contract, ReputationRules);
                penaltyText.text = $"Penalty: {(contract.Penalty > 0f ? UIFormat.Money(contract.Penalty) : "None")}" +
                                   (string.IsNullOrEmpty(stakes) ? "" : $"\nReputation: {stakes}");
            }

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

            var labels = new List<string> { "Antique type", "Century", "Country", "Quantity", "Condition", "Value", "", "Payout" };
            var values = new List<string>
            {
                ContractDisplay.AttributeValue(req, ContractAttributeScope.AntiqueType),
                ContractDisplay.AttributeValue(req, ContractAttributeScope.Century),
                ContractDisplay.AttributeValue(req, ContractAttributeScope.Country),
                req.Quantity.ToString(),
                req.HasMinCondition
                    ? $"at least {UIFormat.Percent(req.MinCondition)} ({UIFormat.ConditionLabel(req.MinCondition)})"
                    : "any",
                ContractDisplay.ValueRequirementShort(req),
                "",
                UIFormat.Money(contract.TotalReward)
            };

            // What the contract earns over selling the same antiques on the market.
            string premium = ContractDisplay.PremiumText(contract);
            if (!string.IsNullOrEmpty(premium))
            {
                labels.Add("");
                values.Add(UIFormat.Colorize(premium, UIFormat.PositiveColor));
            }

            labels.Add("Penalty");
            values.Add(contract.Penalty > 0f ? UIFormat.Money(contract.Penalty) : "None");

            string stakes = ContractDisplay.ReputationStakes(contract, ReputationRules);
            if (!string.IsNullOrEmpty(stakes))
            {
                labels.Add("Reputation");
                values.Add(stakes);
            }

            factLabelsText.text = string.Join("\n", labels);
            factValuesText.text = string.Join("\n", values);

            FitFactsHeight();
        }

        private LayoutElement _factsLayout;
        private float _factsAuthoredHeight;

        /// <summary>
        /// Grows the facts block (the LayoutElement on the texts' parent) to fit every line. Its height is
        /// fixed in the scene, and the values text cuts off what doesn't fit with an ellipsis — so rows added
        /// later (value, premium, reputation) would silently disappear. Never shrinks below the authored height;
        /// the flexible space under it absorbs the difference.
        /// </summary>
        private void FitFactsHeight()
        {
            if (_factsLayout == null)
            {
                var parent = factValuesText.transform.parent;
                _factsLayout = parent != null ? parent.GetComponent<LayoutElement>() : null;
                if (_factsLayout == null) return;

                _factsAuthoredHeight = Mathf.Max(_factsLayout.minHeight, _factsLayout.preferredHeight);
            }

            float needed = Mathf.Ceil(Mathf.Max(factLabelsText.preferredHeight, factValuesText.preferredHeight));
            float height = Mathf.Max(_factsAuthoredHeight, needed);

            _factsLayout.minHeight = height;
            _factsLayout.preferredHeight = height;
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
