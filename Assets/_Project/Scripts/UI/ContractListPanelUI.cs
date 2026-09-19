using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using AntiqueTradingSimulator.Contracts;

namespace AntiqueTradingSimulator.UI
{
    public enum ContractTypeFilter
    {
        All,
        Open,
        Exclusive
    }
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

    public class ContractListPanelUI : MonoBehaviour
    {
        [Header("Filters")]
        [SerializeField] private TMP_Dropdown typeFilterDropdown;
        [SerializeField] private TMP_Dropdown attributeFilterDropdown;

        [Header("Sorting")]
        [SerializeField] private TMP_Dropdown sortDropdown;

        [Header("List")]
        [SerializeField] private RectTransform listContainer;
        [SerializeField] private GameObject listItemPrefab;

        [Header("Pagination")]
        [SerializeField] private TMP_Text pageText;
        [SerializeField] private Button prevPageButton;
        [SerializeField] private Button nextPageButton;
        private const int ItemsPerPage = 6;

        [Header("Empty state")]
        [SerializeField] private GameObject emptyStateLabel; // optional — shown when the filtered list has 0 results

        private ContractTypeFilter _typeFilter = ContractTypeFilter.All;
        private ContractAttributeFilter _attributeFilter = ContractAttributeFilter.All;
        private ContractSortMode _sortMode = ContractSortMode.DeadlineAsc;
        private int _currentPage;

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

            if (prevPageButton != null)
                prevPageButton.onClick.AddListener(PreviousPage);

            if (nextPageButton != null)
                nextPageButton.onClick.AddListener(NextPage);
        }

        public void SetContracts(List<Contract> contracts, int currentDay)
        {
            _sourceContracts = contracts ?? new List<Contract>();
            _currentDay = currentDay;
            Refresh();
        }


        public void SetTypeFilter(int dropdownIndex)
        {
            _typeFilter = (ContractTypeFilter)dropdownIndex;
            _currentPage = 0;
            Refresh();
        }

        public void SetAttributeFilter(int dropdownIndex)
        {
            _attributeFilter = (ContractAttributeFilter)dropdownIndex;
            _currentPage = 0;
            Refresh();
        }

        public void SetSortMode(int dropdownIndex)
        {
            _sortMode = (ContractSortMode)dropdownIndex;
            Refresh();
        }

        public void NextPage()
        {
            _currentPage++;
            Refresh();
        }

        public void PreviousPage()
        {
            if (_currentPage > 0) _currentPage--;
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
            var filteredSorted = GetFilteredSorted();

            int totalPages = Mathf.Max(1, Mathf.CeilToInt(filteredSorted.Count / (float)ItemsPerPage));
            _currentPage = Mathf.Clamp(_currentPage, 0, totalPages - 1);

            var pageItems = filteredSorted
                .Skip(_currentPage * ItemsPerPage)
                .Take(ItemsPerPage)
                .ToList();

            foreach (var row in _spawnedRows)
                if (row != null) Destroy(row.gameObject);
            _spawnedRows.Clear();

            if (emptyStateLabel != null)
                emptyStateLabel.SetActive(filteredSorted.Count == 0);

            if (listContainer != null && listItemPrefab != null)
            {
                float rowHeight = GetRowHeight();

                foreach (var contract in pageItems)
                {
                    var rowObj = Instantiate(listItemPrefab, listContainer);
                    var rowUI = rowObj.GetComponent<ContractListItemUI>();
                    if (rowUI == null)
                    {
                        Debug.LogError("ContractListPanelUI: list item prefab is missing ContractListItemUI.", rowObj);
                        Destroy(rowObj);
                        continue;
                    }

                    if (rowHeight > 0f)
                    {
                        var rowRT = rowObj.GetComponent<RectTransform>();
                        if (rowRT != null) rowRT.sizeDelta = new Vector2(rowRT.sizeDelta.x, rowHeight);
                    }

                    rowUI.Setup(contract, _currentDay, HandleRowClicked);
                    rowUI.SetSelected(contract.ContractId == _selectedContractId);
                    _spawnedRows.Add(rowUI);
                }
            }

            if (pageText != null)
                pageText.text = $"Page {_currentPage + 1}/{totalPages}";

            if (prevPageButton != null) prevPageButton.interactable = _currentPage > 0;
            if (nextPageButton != null) nextPageButton.interactable = _currentPage < totalPages - 1;
        }

        private float GetRowHeight()
        {
            if (listContainer == null) return 0f;

            Canvas.ForceUpdateCanvases();

            float availableHeight = listContainer.rect.height;
            float spacing = 0f;
            float verticalPadding = 0f;

            var layoutGroup = listContainer.GetComponent<VerticalLayoutGroup>();
            if (layoutGroup != null)
            {
                spacing = layoutGroup.spacing;
                verticalPadding = layoutGroup.padding.vertical;
            }

            float usableHeight = availableHeight - verticalPadding - (ItemsPerPage - 1) * spacing;
            return ItemsPerPage > 0 ? usableHeight / ItemsPerPage : 0f;
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
