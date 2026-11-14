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

        /// <summary>Opacity of a row the player has already accepted — still readable and clickable, but set back.</summary>
        private const float AcceptedRowAlpha = 0.5f;

        /// <param name="lockReason">
        /// Why the player can't take this contract because of reputation (shown as a tooltip and
        /// a "Locked" badge), or null/empty when it isn't locked.
        /// </param>
        /// <param name="accepted">The player has already accepted it: the row is dimmed and marked "Accepted".</param>
        public void Setup(Contract contract, int currentWeek, Action<Contract> onClick, string lockReason = null, bool accepted = false)
        {
            _contract = contract;
            _onClick = onClick;
            ContractId = contract?.ContractId;

            if (contract == null)
                return;

            icon.ShowContract(contract.Requirement);

            if (nameText != null)
                nameText.text = string.Empty;

            if (requirementText != null)
                requirementText.text = ContractDisplay.RequirementSummary(contract.Requirement);

            if (rewardText != null)
                rewardText.text = $"{contract.TotalReward:F0} £";

            if (deadlineText != null)
            {
                int weeksLeft = Mathf.Max(0, contract.DeadlineWeek - currentWeek);
                deadlineText.text = UIFormat.Weeks(weeksLeft); // was "{n}d" from before the switch to weeks
            }

            bool locked = !accepted && !string.IsNullOrEmpty(lockReason);

            if (typeBadgeText != null)
                typeBadgeText.text = accepted
                    ? $"{ContractDisplay.TypeLabel(contract)} · {UIFormat.Colorize("Accepted", UIFormat.PositiveColor)}"
                    : locked
                        ? UIFormat.Colorize($"{ContractDisplay.TypeLabel(contract)} · Locked", UIFormat.NegativeColor)
                        : ContractDisplay.TypeLabel(contract);

            // Dimmed, not hidden: an accepted open contract stays on the board until someone delivers it.
            var group = GetComponent<CanvasGroup>();
            if (group == null && accepted) group = gameObject.AddComponent<CanvasGroup>();
            if (group != null) group.alpha = accepted ? AcceptedRowAlpha : 1f;

            // Row-wide tooltip, only while locked or accepted (empty text = no tooltip).
            string tooltipText = accepted
                ? "You've accepted this contract — deliver it from My Contracts."
                : locked ? lockReason : "";

            var tooltipTarget = selectButton != null ? (Component)selectButton : this;
            var tooltip = !string.IsNullOrEmpty(tooltipText) ? TooltipTrigger.On(tooltipTarget) : tooltipTarget.GetComponent<TooltipTrigger>();
            if (tooltip != null) tooltip.Text = tooltipText;

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
