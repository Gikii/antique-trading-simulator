using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Market;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.UI
{
    public enum CollectionSortMode
    {
        Newest,
        Oldest,
        ValueHighToLow,
        ValueLowToHigh,
        ProfitHighToLow,
        NameAToZ
    }

    public class InventoryView : UIView
    {
        [Header("Dependencies")]
        [SerializeField] private PlayerTrader playerTrader;
        [SerializeField] private EconomyManager economyManager;
        [SerializeField] private TimeManager timeManager;

        [Header("Listings")]
        [SerializeField] private RectTransform listingsContainer;
        [SerializeField] private GameObject listingRowPrefab;

        [Header("Collection Header")]
        [SerializeField] private TMP_Text collectionTitleText;
        [SerializeField] private TMP_Text collectionValueText;

        [Header("Filters")]
        [SerializeField] private TMP_InputField searchField;
        [SerializeField] private TMP_Dropdown categoryDropdown;
        [SerializeField] private TMP_Dropdown periodDropdown;
        [SerializeField] private TMP_Dropdown conditionDropdown;
        [SerializeField] private TMP_Dropdown sortDropdown;

        [Header("Right Content")]
        [SerializeField] private GameObject collectionSummary;
        [SerializeField] private CollectionSummaryUI collectionSummaryUI;
        [SerializeField] private InventoryDetailsUI inventoryDetailsUI;

        [Header("Pagination - Navigation")]
        [SerializeField] private Button previousButton;
        [SerializeField] private Button nextButton;

        [Header("Pagination - Page Buttons")]
        [SerializeField] private Button firstPageButton;
        [SerializeField] private TMP_Text firstPageText;

        [SerializeField] private TMP_Text leftEllipsis;

        [SerializeField] private Button previousPageNumberButton;
        [SerializeField] private TMP_Text previousPageNumberText;

        [SerializeField] private Button currentPageButton;
        [SerializeField] private TMP_Text currentPageText;

        [SerializeField] private Button nextPageNumberButton;
        [SerializeField] private TMP_Text nextPageNumberText;

        [SerializeField] private TMP_Text rightEllipsis;

        [SerializeField] private Button lastPageButton;
        [SerializeField] private TMP_Text lastPageText;

        private const int ItemsPerPage = 7;

        private static readonly (string Label, CollectionSortMode Mode)[] SortOptions =
        {
            ("Sort: Newest", CollectionSortMode.Newest),
            ("Sort: Oldest", CollectionSortMode.Oldest),
            ("Sort: Value (high-low)", CollectionSortMode.ValueHighToLow),
            ("Sort: Value (low-high)", CollectionSortMode.ValueLowToHigh),
            ("Sort: Profit", CollectionSortMode.ProfitHighToLow),
            ("Sort: Name (A-Z)", CollectionSortMode.NameAToZ),
        };

        // Everything the player owns, unfiltered — what the summary describes.
        private readonly List<Antique> _collection = new();
        // What the list currently shows (after search/filters/sort).
        private readonly List<Antique> _filtered = new();
        private readonly List<InventoryListItemUI> _spawnedRows = new();

        // Option index N of a filter dropdown maps to entry N of these lists;
        // a null entry is the "All ..." option.
        private readonly List<AntiqueType?> _categoryOptions = new();
        private readonly List<Century?> _periodOptions = new();

        private AntiqueType? _categoryFilter;
        private Century? _periodFilter;
        private int _conditionFilter = -1; // index into UIFormat.ConditionBuckets, -1 = all
        private string _searchText = "";
        private CollectionSortMode _sortMode = CollectionSortMode.Newest;

        private Antique _selectedAntique;
        private int _currentPage;
        private bool _subscribed;
        private bool _refreshPending;

        private TraderInventory Inventory => playerTrader != null ? playerTrader.Inventory : null;
        private Market.Market CurrentMarket => economyManager != null ? economyManager.Market : null;

        private void Awake()
        {
            if (playerTrader == null)
                playerTrader = FindFirstObjectByType<PlayerTrader>();

            if (economyManager == null)
                economyManager = FindFirstObjectByType<EconomyManager>();

            if (timeManager == null)
                timeManager = FindFirstObjectByType<TimeManager>();

            if (collectionSummaryUI == null && collectionSummary != null)
                collectionSummaryUI = collectionSummary.GetComponent<CollectionSummaryUI>();

            SetupPaginationButtons();
            SetupFilterControls();

            if (inventoryDetailsUI != null)
                inventoryDetailsUI.Setup(this);

            ShowCollectionSummary();
        }

        protected override void OnShown()
        {
            Subscribe();
            RefreshCollection();
        }

        protected override void OnHidden()
        {
            // Nothing on this screen needs updating while it's hidden;
            // OnShown does a full refresh anyway.
            Unsubscribe();
        }

        private void OnDestroy()
        {
            Unsubscribe();
        }

        // ------------------------------------------------------------------
        // Subscriptions
        // ------------------------------------------------------------------

        private void Subscribe()
        {
            if (_subscribed || Inventory == null)
                return;

            Inventory.OnHoldingChanged += HandleHoldingChanged;
            Inventory.OnHoldingsRevalued += HandleHoldingsRevalued;

            if (timeManager != null)
                timeManager.OnDayChanged += HandleDayChanged;

            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed)
                return;

            if (Inventory != null)
            {
                Inventory.OnHoldingChanged -= HandleHoldingChanged;
                Inventory.OnHoldingsRevalued -= HandleHoldingsRevalued;
            }

            if (timeManager != null)
                timeManager.OnDayChanged -= HandleDayChanged;

            _subscribed = false;
        }

        // Inventory events can arrive in bursts (an event re-prices several antique types
        // one by one), so they only mark the view dirty and it refreshes once per frame.
        private void HandleHoldingChanged(string listingId, Antique antique) => _refreshPending = true;

        private void HandleHoldingsRevalued() => _refreshPending = true;

        // Covers the daily value snapshot even on days with no revaluation.
        private void HandleDayChanged(int day) => _refreshPending = true;

        private void LateUpdate()
        {
            if (!_refreshPending)
                return;

            _refreshPending = false;
            RefreshCollection();
        }

        // ------------------------------------------------------------------
        // Refresh
        // ------------------------------------------------------------------

        public void RefreshCollection()
        {
            _refreshPending = false;
            _collection.Clear();

            if (Inventory != null)
            {
                _collection.AddRange(
                    Inventory.Holdings.Values
                        .Where(antique => antique != null));
            }

            // The selected antique may have just been sold or handed in for a contract.
            if (_selectedAntique != null && !_collection.Contains(_selectedAntique))
                ShowCollectionSummary();

            RebuildFilterOptions();
            ApplyFilters();

            RefreshHeader();
            RefreshListings();
            RefreshPagination();
            RefreshRightPanel();
        }

        private void RefreshRightPanel()
        {
            if (_selectedAntique != null && inventoryDetailsUI != null)
            {
                inventoryDetailsUI.Show(_selectedAntique);
                return;
            }

            if (collectionSummaryUI != null)
                collectionSummaryUI.Refresh(_collection, Inventory, CurrentMarket);
        }

        private void RefreshHeader()
        {
            if (collectionTitleText != null)
            {
                collectionTitleText.text = IsAnyFilterActive
                    ? $"My collection ({_filtered.Count} / {_collection.Count})"
                    : $"My collection ({_collection.Count})";
            }

            if (collectionValueText != null)
            {
                float totalValue = CollectionAnalytics.TotalValue(_collection);
                collectionValueText.text = $"Market value: {UIFormat.Money(totalValue)}";
            }
        }

        private void RefreshListings()
        {
            ClearListings();

            int totalPages = GetTotalPages();

            _currentPage = Mathf.Clamp(
                _currentPage,
                0,
                totalPages - 1);

            List<Antique> pageItems = _filtered
                .Skip(_currentPage * ItemsPerPage)
                .Take(ItemsPerPage)
                .ToList();

            foreach (Antique antique in pageItems)
            {
                GameObject rowObject =
                    Instantiate(listingRowPrefab, listingsContainer);

                InventoryListItemUI rowUI =
                    rowObject.GetComponent<InventoryListItemUI>();

                if (rowUI == null)
                {
                    Debug.LogError(
                        "Inventory listing prefab does not contain InventoryListItemUI.",
                        rowObject);

                    Destroy(rowObject);
                    continue;
                }

                rowUI.Setup(antique, this);
                rowUI.SetSelected(antique == _selectedAntique);

                _spawnedRows.Add(rowUI);
            }
        }

        private void ClearListings()
        {
            foreach (InventoryListItemUI row in _spawnedRows)
            {
                if (row != null)
                    Destroy(row.gameObject);
            }

            _spawnedRows.Clear();
        }

        private void RefreshRowSelection()
        {
            foreach (InventoryListItemUI row in _spawnedRows)
            {
                if (row != null)
                    row.SetSelected(row.Antique == _selectedAntique);
            }
        }

        // ------------------------------------------------------------------
        // Right panel switching
        // ------------------------------------------------------------------

        public void ShowDetails(Antique antique)
        {
            if (antique == null)
                return;

            _selectedAntique = antique;

            if (collectionSummary != null)
                collectionSummary.SetActive(false);

            if (inventoryDetailsUI != null)
                inventoryDetailsUI.Show(antique);
            else
                Debug.LogError("InventoryView: InventoryDetailsUI is not assigned.");

            RefreshRowSelection();
        }

        public void ShowCollectionSummary()
        {
            _selectedAntique = null;

            if (inventoryDetailsUI != null)
                inventoryDetailsUI.gameObject.SetActive(false);

            if (collectionSummary != null)
                collectionSummary.SetActive(true);

            if (collectionSummaryUI != null && isActiveAndEnabled)
                collectionSummaryUI.Refresh(_collection, Inventory, CurrentMarket);

            RefreshRowSelection();
        }

        // ------------------------------------------------------------------
        // Filters, search, sorting
        // ------------------------------------------------------------------

        private bool IsAnyFilterActive =>
            _categoryFilter.HasValue ||
            _periodFilter.HasValue ||
            _conditionFilter >= 0 ||
            !string.IsNullOrWhiteSpace(_searchText);

        private void SetupFilterControls()
        {
            if (searchField != null)
                searchField.onValueChanged.AddListener(OnSearchChanged);

            if (categoryDropdown != null)
                categoryDropdown.onValueChanged.AddListener(OnCategoryChanged);

            if (periodDropdown != null)
                periodDropdown.onValueChanged.AddListener(OnPeriodChanged);

            if (conditionDropdown != null)
            {
                var options = new List<string> { "All conditions" };
                options.AddRange(UIFormat.ConditionBuckets.Select(b => b.Label));
                SetOptions(conditionDropdown, options, 0);
                conditionDropdown.onValueChanged.AddListener(OnConditionChanged);
            }

            if (sortDropdown != null)
            {
                SetOptions(sortDropdown, SortOptions.Select(o => o.Label).ToList(), 0);
                sortDropdown.onValueChanged.AddListener(OnSortChanged);
            }
        }

        /// <summary>
        /// Category and period dropdowns only list values that exist in the collection.
        /// Rebuilt on every refresh; the current choice is kept while it still exists.
        /// </summary>
        private void RebuildFilterOptions()
        {
            var types = _collection.Select(a => a.Type).Distinct().OrderBy(t => t.ToDisplayString()).ToList();
            if (_categoryFilter.HasValue && !types.Contains(_categoryFilter.Value))
                types.Add(_categoryFilter.Value);

            _categoryOptions.Clear();
            _categoryOptions.Add(null);
            _categoryOptions.AddRange(types.Select(t => (AntiqueType?)t));

            if (categoryDropdown != null)
            {
                SetOptions(categoryDropdown,
                    _categoryOptions.Select(t => t.HasValue ? t.Value.ToDisplayString() : "All categories").ToList(),
                    Mathf.Max(0, _categoryOptions.IndexOf(_categoryFilter)));
            }

            var centuries = _collection.Select(a => a.Century).Distinct().OrderBy(c => (int)c).ToList();
            if (_periodFilter.HasValue && !centuries.Contains(_periodFilter.Value))
                centuries.Add(_periodFilter.Value);

            _periodOptions.Clear();
            _periodOptions.Add(null);
            _periodOptions.AddRange(centuries.Select(c => (Century?)c));

            if (periodDropdown != null)
            {
                SetOptions(periodDropdown,
                    _periodOptions.Select(c => c.HasValue ? c.Value.ToDisplayString() : "All periods").ToList(),
                    Mathf.Max(0, _periodOptions.IndexOf(_periodFilter)));
            }
        }

        private static void SetOptions(TMP_Dropdown dropdown, List<string> labels, int selectedIndex)
        {
            // Skip the rebuild if nothing changed — rebuilding closes an open dropdown.
            bool same = dropdown.options.Count == labels.Count;
            for (int i = 0; same && i < labels.Count; i++)
                same = dropdown.options[i].text == labels[i];

            if (!same)
            {
                dropdown.ClearOptions();
                dropdown.AddOptions(labels);
            }

            dropdown.SetValueWithoutNotify(Mathf.Clamp(selectedIndex, 0, labels.Count - 1));
            dropdown.RefreshShownValue();
        }

        private void ApplyFilters()
        {
            IEnumerable<Antique> query = _collection;

            if (_categoryFilter.HasValue)
                query = query.Where(a => a.Type == _categoryFilter.Value);

            if (_periodFilter.HasValue)
                query = query.Where(a => a.Century == _periodFilter.Value);

            if (_conditionFilter >= 0 && _conditionFilter < UIFormat.ConditionBuckets.Length)
            {
                var bucket = UIFormat.ConditionBuckets[_conditionFilter];
                query = query.Where(a => bucket.Contains(a.Condition));
            }

            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                string search = _searchText.Trim();
                query = query.Where(a => Matches(a, search));
            }

            query = _sortMode switch
            {
                CollectionSortMode.Oldest => query.OrderBy(a => a.PurchasedOnDay),
                CollectionSortMode.ValueHighToLow => query.OrderByDescending(a => a.CurrentPrice),
                CollectionSortMode.ValueLowToHigh => query.OrderBy(a => a.CurrentPrice),
                CollectionSortMode.ProfitHighToLow => query.OrderByDescending(a => CollectionAnalytics.Profit(a)),
                CollectionSortMode.NameAToZ => query.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase),
                _ => query.OrderByDescending(a => a.PurchasedOnDay),
            };

            _filtered.Clear();
            _filtered.AddRange(query);
        }

        private static bool Matches(Antique antique, string search)
        {
            return Contains(antique.Name, search)
                || Contains(antique.Category, search)
                || Contains(antique.Country.ToDisplayString(), search)
                || Contains(antique.Century.ToDisplayString(), search);
        }

        private static bool Contains(string text, string search) =>
            !string.IsNullOrEmpty(text) && text.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0;

        private void OnSearchChanged(string text)
        {
            _searchText = text ?? "";
            OnFiltersChanged();
        }

        private void OnCategoryChanged(int index)
        {
            _categoryFilter = index >= 0 && index < _categoryOptions.Count ? _categoryOptions[index] : null;
            OnFiltersChanged();
        }

        private void OnPeriodChanged(int index)
        {
            _periodFilter = index >= 0 && index < _periodOptions.Count ? _periodOptions[index] : null;
            OnFiltersChanged();
        }

        private void OnConditionChanged(int index)
        {
            // Option 0 is "All conditions", so bucket index = option index - 1.
            _conditionFilter = index - 1;
            OnFiltersChanged();
        }

        private void OnSortChanged(int index)
        {
            if (index >= 0 && index < SortOptions.Length)
                _sortMode = SortOptions[index].Mode;

            OnFiltersChanged();
        }

        private void OnFiltersChanged()
        {
            _currentPage = 0;
            ApplyFilters();
            RefreshHeader();
            RefreshListings();
            RefreshPagination();
        }

        // ------------------------------------------------------------------
        // Pagination
        // ------------------------------------------------------------------

        private void SetupPaginationButtons()
        {
            if (previousButton != null)
                previousButton.onClick.AddListener(PreviousPage);

            if (nextButton != null)
                nextButton.onClick.AddListener(NextPage);

            if (firstPageButton != null)
            {
                firstPageButton.onClick.AddListener(
                    () => GoToPage(0));
            }

            if (lastPageButton != null)
            {
                lastPageButton.onClick.AddListener(
                    () => GoToPage(GetTotalPages() - 1));
            }

            if (previousPageNumberButton != null)
            {
                previousPageNumberButton.onClick.AddListener(
                    () => GoToPage(_currentPage - 1));
            }

            if (currentPageButton != null)
                currentPageButton.interactable = false;

            if (nextPageNumberButton != null)
            {
                nextPageNumberButton.onClick.AddListener(
                    () => GoToPage(_currentPage + 1));
            }
        }

        public void PreviousPage()
        {
            if (_currentPage <= 0)
                return;

            _currentPage--;

            RefreshListings();
            RefreshPagination();
        }

        public void NextPage()
        {
            int totalPages = GetTotalPages();

            if (_currentPage >= totalPages - 1)
                return;

            _currentPage++;

            RefreshListings();
            RefreshPagination();
        }

        private void GoToPage(int page)
        {
            int totalPages = GetTotalPages();

            _currentPage = Mathf.Clamp(
                page,
                0,
                totalPages - 1);

            RefreshListings();
            RefreshPagination();
        }

        private int GetTotalPages()
        {
            return Mathf.Max(
                1,
                Mathf.CeilToInt(
                    _filtered.Count / (float)ItemsPerPage));
        }

        private void RefreshPagination()
        {
            int totalPages = GetTotalPages();

            _currentPage = Mathf.Clamp(
                _currentPage,
                0,
                totalPages - 1);

            int currentDisplayPage = _currentPage + 1;

            if (previousButton != null)
                previousButton.interactable = _currentPage > 0;

            if (nextButton != null)
            {
                nextButton.interactable =
                    _currentPage < totalPages - 1;
            }

            if (currentPageButton != null)
                currentPageButton.gameObject.SetActive(true);

            if (currentPageText != null)
                currentPageText.text = currentDisplayPage.ToString();

            bool showPreviousPage =
                currentDisplayPage > 1;

            if (previousPageNumberButton != null)
            {
                previousPageNumberButton.gameObject.SetActive(
                    showPreviousPage);
            }

            if (previousPageNumberText != null)
            {
                previousPageNumberText.text =
                    (currentDisplayPage - 1).ToString();
            }

            bool showNextPage =
                currentDisplayPage < totalPages;

            if (nextPageNumberButton != null)
            {
                nextPageNumberButton.gameObject.SetActive(
                    showNextPage);
            }

            if (nextPageNumberText != null)
            {
                nextPageNumberText.text =
                    (currentDisplayPage + 1).ToString();
            }

            bool showFirstPage =
                currentDisplayPage > 2;

            if (firstPageButton != null)
                firstPageButton.gameObject.SetActive(showFirstPage);

            if (firstPageText != null)
                firstPageText.text = "1";

            bool showLeftEllipsis =
                currentDisplayPage > 3;

            if (leftEllipsis != null)
            {
                leftEllipsis.gameObject.SetActive(
                    showLeftEllipsis);
            }

            bool showLastPage =
                currentDisplayPage < totalPages - 1;

            if (lastPageButton != null)
            {
                lastPageButton.gameObject.SetActive(
                    showLastPage);
            }

            if (lastPageText != null)
                lastPageText.text = totalPages.ToString();

            bool showRightEllipsis =
                currentDisplayPage < totalPages - 2;

            if (rightEllipsis != null)
            {
                rightEllipsis.gameObject.SetActive(
                    showRightEllipsis);
            }
        }
    }
}
