using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;
using AntiqueTradingSimulator.Contracts;

namespace AntiqueTradingSimulator.UI
{
    // Dropdown index order matters here — each enum's declaration order must
    // match the option order set on its TMP_Dropdown in the Inspector.

    public enum ContractTypeFilter
    {
        All,
        Open,
        Exclusive
    }

    // Filters by *which* attribute a contract's requirement is scoped to
    // (AntiqueType / Country / Century) — not by a concrete value of that
    // attribute (e.g. not "only France" or "only Porcelain"). That's a
    // deliberate scope cut for now; see ContractRequirement.Scope.
    public enum ContractAttributeFilter
    {
        All,
        AntiqueType,
        Country,
        Century
    }

    public enum ContractSortMode
    {
        DeadlineAsc,
        DeadlineDesc,
        RewardAsc,
        RewardDesc,
        QuantityAsc,
        QuantityDesc
    }

    /// <summary>
    /// The list half of the Contracts view: status/attribute filters, a sort
    /// dropdown, and the scrolling list of contract rows. Fully self-contained —
    /// ContractsView just calls SetContracts() with whatever pool of contracts
    /// the active tab should show; this panel handles filtering, sorting, and
    /// spawning ContractListItemUI rows, and reports row clicks back through
    /// the callback given to Initialize().
    ///
    /// Filtering by contract status (open/closed as in "still active" vs
    /// "fulfilled/expired") and full history are intentionally left for later —
    /// this only filters the pool it's given, which for now is always the
    /// currently-Active contracts for whichever tab is showing.
    /// </summary>
    public class ContractListPanelUI : MonoBehaviour
    {
        [Header("Filters")]
        [SerializeField] private TMP_Dropdown typeFilterDropdown; // All / Open / Exclusive
        [SerializeField] private TMP_Dropdown attributeFilterDropdown; // All / Antique Type / Country / Century

        [Header("Sorting")]
        [SerializeField] private TMP_Dropdown sortDropdown; // Deadline/Reward/Quantity x Asc/Desc

        [Header("List")]
        [SerializeField] private RectTransform listContainer;
        [SerializeField] private GameObject listItemPrefab;

        [Header("Empty state")]
        [SerializeField] private GameObject emptyStateLabel; // optional — shown when the filtered list has 0 results

        private ContractTypeFilter _typeFilter = ContractTypeFilter.All;
        private ContractAttributeFilter _attributeFilter = ContractAttributeFilter.All;
        private ContractSortMode _sortMode = ContractSortMode.DeadlineAsc;

        private List<Contract> _sourceContracts = new();
        private readonly List<ContractListItemUI> _spawnedRows = new();

        private int _currentDay;
        private string _selectedContractId;
        private Action<Contract> _onSelect;

        public void Initialize(Action<Contract> onSelect)
        {
            _onSelect = onSelect;

            if (typeFilterDropdown != null)
                typeFilterDropdown.onValueChanged.AddListener(SetTypeFilter);

            if (attributeFilterDropdown != null)
                attributeFilterDropdown.onValueChanged.AddListener(SetAttributeFilter);

            if (sortDropdown != null)
                sortDropdown.onValueChanged.AddListener(SetSortMode);
        }

        public void SetContracts(List<Contract> contracts, int currentDay)
        {
            _sourceContracts = contracts ?? new List<Contract>();
            _currentDay = currentDay;
            Refresh();
        }

        // Called by the type filter TMP_Dropdown's OnValueChanged(int)
        public void SetTypeFilter(int dropdownIndex)
        {
            _typeFilter = (ContractTypeFilter)dropdownIndex;
            Refresh();
        }

        // Called by the attribute filter TMP_Dropdown's OnValueChanged(int)
        public void SetAttributeFilter(int dropdownIndex)
        {
            _attributeFilter = (ContractAttributeFilter)dropdownIndex;
            Refresh();
        }

        // Called by the sort TMP_Dropdown's OnValueChanged(int)
        public void SetSortMode(int dropdownIndex)
        {
            _sortMode = (ContractSortMode)dropdownIndex;
            Refresh();
        }

        public void SetSelected(string contractId)
        {
            _selectedContractId = contractId;
            foreach (var row in _spawnedRows)
                row.SetSelected(row.ContractId == _selectedContractId);
        }

        public void ClearSelection() => SetSelected(null);

        private void Refresh()
        {
            var filtered = GetFilteredSorted();

            foreach (var row in _spawnedRows)
                if (row != null) Destroy(row.gameObject);
            _spawnedRows.Clear();

            if (emptyStateLabel != null)
                emptyStateLabel.SetActive(filtered.Count == 0);

            if (listContainer == null || listItemPrefab == null)
                return;

            foreach (var contract in filtered)
            {
                var rowObj = Instantiate(listItemPrefab, listContainer);
                var rowUI = rowObj.GetComponent<ContractListItemUI>();
                if (rowUI == null)
                {
                    Debug.LogError("ContractListPanelUI: list item prefab is missing ContractListItemUI.", rowObj);
                    Destroy(rowObj);
                    continue;
                }

                rowUI.Setup(contract, _currentDay, HandleRowClicked);
                rowUI.SetSelected(contract.ContractId == _selectedContractId);
                _spawnedRows.Add(rowUI);
            }
        }

        private void HandleRowClicked(Contract contract)
        {
            SetSelected(contract.ContractId);
            _onSelect?.Invoke(contract);
        }

        private List<Contract> GetFilteredSorted()
        {
            IEnumerable<Contract> result = _sourceContracts;

            if (_typeFilter == ContractTypeFilter.Open)
                result = result.Where(c => c.Type == ContractType.Open);
            else if (_typeFilter == ContractTypeFilter.Exclusive)
                result = result.Where(c => c.Type == ContractType.Exclusive);

            if (_attributeFilter != ContractAttributeFilter.All)
            {
                var scope = (ContractAttributeScope)((int)_attributeFilter - 1);
                result = result.Where(c => c.Requirement.Scope == scope);
            }

            result = _sortMode switch
            {
                ContractSortMode.DeadlineAsc => result.OrderBy(c => c.DeadlineDay),
                ContractSortMode.DeadlineDesc => result.OrderByDescending(c => c.DeadlineDay),
                ContractSortMode.RewardAsc => result.OrderBy(c => c.TotalReward),
                ContractSortMode.RewardDesc => result.OrderByDescending(c => c.TotalReward),
                ContractSortMode.QuantityAsc => result.OrderBy(c => c.Requirement.Quantity),
                ContractSortMode.QuantityDesc => result.OrderByDescending(c => c.Requirement.Quantity),
                _ => result
            };

            return result.ToList();
        }
    }
}
