using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AntiqueTradingSimulator.Contracts;

namespace AntiqueTradingSimulator.UI
{
    public class ContractListItemUI : MonoBehaviour
    {
        [Header("Image")]
        [SerializeField] private Image icon;

        [Header("Text")]
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text requirementText;
        [SerializeField] private TMP_Text rewardText;
        [SerializeField] private TMP_Text deadlineText;
        [SerializeField] private TMP_Text typeBadgeText;

        [Header("Interaction")]
        [SerializeField] private Button selectButton;
        [SerializeField] private GameObject selectedHighlight;

        public string ContractId { get; private set; }

        private Contract _contract;
        private Action<Contract> _onClick;

        public void Setup(Contract contract, int currentDay, Action<Contract> onClick)
        {
            _contract = contract;
            _onClick = onClick;
            ContractId = contract?.ContractId;

            if (contract == null)
                return;

            if (nameText != null)
                nameText.text = string.Empty;

            if (requirementText != null)
                requirementText.text = ContractDisplay.RequirementSummary(contract.Requirement);

            if (rewardText != null)
                rewardText.text = $"{contract.TotalReward:F0} €";

            if (deadlineText != null)
            {
                int daysLeft = Mathf.Max(0, contract.DeadlineDay - currentDay);
                deadlineText.text = $"{daysLeft}d";
            }

            if (typeBadgeText != null)
                typeBadgeText.text = contract.Type.ToDisplayString();

            if (selectButton != null)
            {
                selectButton.onClick.RemoveAllListeners();
                selectButton.onClick.AddListener(() => _onClick?.Invoke(_contract));
            }

            SetSelected(false);
        }

        public void SetSelected(bool selected)
        {
            if (selectedHighlight != null)
                selectedHighlight.SetActive(selected);
        }
    }
}
