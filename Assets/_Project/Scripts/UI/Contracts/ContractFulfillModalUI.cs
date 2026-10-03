using System;
using System.Collections.Generic;
using System.Linq;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Market;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    public class ContractFulfillModalUI : MonoBehaviour
    {
        [Header("Game systems (auto-found if empty)")]
        [SerializeField] private PlayerTrader playerTrader;
        [SerializeField] private ContractManager contractManager;
        [SerializeField] private TimeManager timeManager;

        [Header("Options")]
        [Tooltip("Pause the game clock while the modal is open, so the deadline can't pass mid-decision.")]
        [SerializeField] private bool pauseTimeWhileOpen = true;

        [Header("Header")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text subtitleText;
        [SerializeField] private Button closeButton;

        [Header("Requirement summary")]
        [SerializeField] private TMP_Text requirementText;
        [SerializeField] private TMP_Text payoutText;
        [SerializeField] private TMP_Text selectionCountText;

        [Header("Antique list")]
        [SerializeField] private RectTransform listContainer;
        [SerializeField] private GameObject listItemPrefab;
        [SerializeField] private GameObject emptyStateLabel;

        [Header("Footer")]
        [SerializeField] private TMP_Text noteText;
        [SerializeField] private Button cancelButton;
        [SerializeField] private Button confirmButton;
        [SerializeField] private TMP_Text confirmButtonLabel;

        [Header("Colors")]
        [SerializeField] private Color warningColor = new Color32(0xE6, 0xB2, 0x40, 0xFF);

        public event Action<Contract> Fulfilled;

        private Contract _contract;
        private readonly List<ContractFulfillListItemUI> _rows = new();
        private readonly List<string> _selectedListingIds = new();
        private bool _initialized;
        private bool _pausedByModal;

        private TraderInventory Inventory => playerTrader != null ? playerTrader.Inventory : null;
        private int RequiredQuantity => _contract != null ? _contract.Requirement.Quantity : 0;

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        public void Open(Contract contract)
        {
            if (contract == null)
                return;

            EnsureInitialized();

            _contract = contract;
            _selectedListingIds.Clear();

            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            if (pauseTimeWhileOpen && timeManager != null && timeManager.IsRunning)
            {
                timeManager.Pause();
                _pausedByModal = true;
            }

            FillHeader(contract);
            RebuildList();
            RefreshFooter();
        }

        public void Close()
        {
            _contract = null;
            ClearRows();
            _selectedListingIds.Clear();
            gameObject.SetActive(false);
        }

        /// <param name="includeInTransit">Also return matching antiques still on their way (listed last).
        /// They can't be handed in yet — the modal shows them disabled; counts should leave them out.</param>
        public static List<Antique> EligibleAntiques(TraderInventory inventory, Contract contract, bool includeInTransit = false)
        {
            if (inventory == null || contract == null)
                return new List<Antique>();

            return inventory.Holdings.Values
                .Where(a => a != null)
                .Where(a => contract.Requirement.IsSatisfiedBy(a))
                .Where(a => !a.IsListedForSale)
                .Where(a => includeInTransit || !a.IsInTransit)
                .Where(a => !a.IsReservedForContract || a.ReservedForContractId == contract.ContractId)
                .OrderBy(a => a.IsInTransit)
                .ThenByDescending(a => a.ReservedForContractId == contract.ContractId)
                .ThenBy(a => a.CurrentPrice)
                .ToList();
        }

        // ------------------------------------------------------------------
        // Unity
        // ------------------------------------------------------------------

        private void OnDisable()
        {
            // Resume even if something else hides the modal.
            if (_pausedByModal && timeManager != null)
                timeManager.Resume();

            _pausedByModal = false;
        }

        // ------------------------------------------------------------------
        // Setup
        // ------------------------------------------------------------------

        private void EnsureInitialized()
        {
            if (_initialized)
                return;

            _initialized = true;

            if (playerTrader == null) playerTrader = FindFirstObjectByType<PlayerTrader>();
            if (contractManager == null) contractManager = FindFirstObjectByType<ContractManager>();
            if (timeManager == null) timeManager = FindFirstObjectByType<TimeManager>();

            AddListener(closeButton, Close);
            AddListener(cancelButton, Close);
            AddListener(confirmButton, Confirm);

            SetText(titleText, "Fulfill contract");
            SetText(noteText, BuildNote());
        }

        private static void AddListener(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
                button.onClick.AddListener(action);
        }

        private static string BuildNote()
        {
            var lines = new[]
            {
                "Pick exactly as many antiques as the contract asks for — they are handed over the moment you confirm.",
                "Antiques currently listed on the market can't be handed in; cancel the listing first.",
                "Antiques still in transit are shown greyed out — they can be handed in once they arrive.",
                "The payout is the same whichever matching antiques you choose, so handing in your cheapest ones is usually best.",
            };

            return string.Join("\n", lines.Select(l => "• " + l));
        }

        // ------------------------------------------------------------------
        // Content
        // ------------------------------------------------------------------

        private void FillHeader(Contract contract)
        {
            var req = contract.Requirement;

            string attribute = req.Scope switch
            {
                ContractAttributeScope.AntiqueType => req.AntiqueType.ToDisplayString(),
                ContractAttributeScope.Country => req.Country.ToDisplayString(),
                ContractAttributeScope.Century => req.Century.ToDisplayString(),
                _ => "Any"
            };

            SetText(subtitleText, $"{req.Quantity}x {attribute}");
            SetText(requirementText, $"Required: {req.Quantity}x {attribute}");
            SetText(payoutText, $"Payout: {UIFormat.Colorize(UIFormat.Money(contract.TotalReward), UIFormat.PositiveColor)}");
        }

        private void RebuildList()
        {
            ClearRows();
            _selectedListingIds.Clear();

            List<Antique> eligible = EligibleAntiques(Inventory, _contract, includeInTransit: true);

            if (emptyStateLabel != null)
                emptyStateLabel.SetActive(eligible.Count == 0);

            if (listContainer == null || listItemPrefab == null)
            {
                if (eligible.Count > 0)
                    Debug.LogWarning("ContractFulfillModalUI: listContainer or listItemPrefab is not assigned.");
                return;
            }

            foreach (Antique antique in eligible)
            {
                GameObject rowObject = Instantiate(listItemPrefab, listContainer);
                var row = rowObject.GetComponent<ContractFulfillListItemUI>();

                if (row == null)
                {
                    Debug.LogError("ContractFulfillModalUI: list item prefab has no ContractFulfillListItemUI.", rowObject);
                    Destroy(rowObject);
                    continue;
                }

                bool preselect = antique.ReservedForContractId == _contract.ContractId
                                 && !antique.IsInTransit
                                 && _selectedListingIds.Count < RequiredQuantity;

                if (preselect && !_selectedListingIds.Contains(antique.Id))
                    _selectedListingIds.Add(antique.Id);

                row.Setup(antique, preselect, HandleRowToggled);
                if (antique.IsInTransit)
                    row.SetInTransit();
                _rows.Add(row);
            }
        }

        private void ClearRows()
        {
            foreach (ContractFulfillListItemUI row in _rows)
            {
                if (row != null)
                    Destroy(row.gameObject);
            }

            _rows.Clear();
        }

        private void HandleRowToggled(Antique antique, bool isOn)
        {
            if (antique == null) return;

            if (isOn)
            {
                if (_selectedListingIds.Count >= RequiredQuantity)
                {
                    ContractFulfillListItemUI row = _rows.FirstOrDefault(r => r != null && r.Antique == antique);
                    row?.SetSelected(false);
                    RefreshFooter();
                    return;
                }

                if (!_selectedListingIds.Contains(antique.Id))
                    _selectedListingIds.Add(antique.Id);
            }
            else
            {
                _selectedListingIds.Remove(antique.Id);
            }

            RefreshFooter();
        }

        private void RefreshFooter()
        {
            int selected = _selectedListingIds.Count;
            int required = RequiredQuantity;
            bool exact = selected == required && required > 0;

            SetText(selectionCountText, exact
                ? UIFormat.Colorize($"Selected {selected} / {required}", UIFormat.PositiveColor)
                : UIFormat.Colorize($"Selected {selected} / {required}", warningColor));

            SetText(confirmButtonLabel, required > 0
                ? $"Hand in {selected}/{required}"
                : "Hand in");

            if (confirmButton != null)
                confirmButton.interactable = exact && _contract != null && _contract.Status == ContractStatus.Active;
        }

        // ------------------------------------------------------------------
        // Actions
        // ------------------------------------------------------------------

        private void Confirm()
        {
            if (_contract == null || playerTrader == null)
                return;

            if (_selectedListingIds.Count != RequiredQuantity)
                return;

            Contract contract = _contract;

            if (!playerTrader.FulfillContract(contract.ContractId, _selectedListingIds))
            {
                Debug.LogWarning($"ContractFulfillModalUI: failed to fulfill contract {contract.ContractId}.");
 
                RebuildList();
                RefreshFooter();
                return;
            }

            Debug.Log($"ContractFulfillModalUI: fulfilled contract {contract.ContractId} for {contract.TotalReward:F0} €.");
            Close();
            Fulfilled?.Invoke(contract);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static void SetText(TMP_Text target, string value)
        {
            if (target != null)
                target.text = value;
        }
    }
}
