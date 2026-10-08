using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Market;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.UI
{
    public enum MarketSortMode
    {
        NameAsc,
        PriceAsc,
        PriceDesc
    }

    public class MarketView : UIView
    {
        [SerializeField] private EconomyManager economyManager;
        [SerializeField] private PlayerTrader playerTrader;
        [SerializeField] private TimeManager timeManager;

        [SerializeField] private RectTransform listingsContainer;
        [SerializeField] private GameObject listingRowPrefab;

        [Header("Filters (multi-select — none selected = no filter on that field)")]
        [SerializeField] private MultiSelectDropdown typeFilterDropdown;
        [SerializeField] private MultiSelectDropdown centuryFilterDropdown;
        [SerializeField] private MultiSelectDropdown countryFilterDropdown;
        [SerializeField] private MultiSelectDropdown qualityFilterDropdown;
        [SerializeField] private Button clearFiltersButton;

        [Header("Sorting")]
        [SerializeField] private TMP_Dropdown sortDropdown;

        [Header("Pagination")]
        [SerializeField] private TMP_Text pageText;
        [SerializeField] private Button prevPageButton;
        [SerializeField] private Button nextPageButton;
        private const int ItemsPerPage = 8;

        [SerializeField] private AntiqueDetailsUI antiqueDetailsUI;

        // Century's enum values equal the century number (XII=12 ... XX=20),
        // not 0..N, so the Century dropdown's option index -> enum needs an
        // explicit lookup table (dropdown option order matches this array).
        private static readonly Century[] CenturyOptions =
        {
            Century.XII, Century.XIII, Century.XIV, Century.XV, Century.XVI,
            Century.XVII, Century.XVIII, Century.XIX, Century.XX
        };

        // Quality buckets, expressed as an inclusive [min, max] Condition range
        // (dropdown option order matches this array).
        private static readonly (string Label, float Min, float Max)[] QualityOptions =
        {
            ("Excellent (90-100%)", 0.9f, 1f),
            ("Good (70-89%)", 0.7f, 0.8999f),
            ("Fair (50-69%)", 0.5f, 0.6999f),
            ("Poor (30-49%)", Antique.MinCondition, 0.4999f),
        };

        // Each set holds the currently-selected option indices for that
        // dropdown; an empty set means "don't filter on this field".
        private readonly HashSet<AntiqueType> _typeFilters = new HashSet<AntiqueType>();
        private readonly HashSet<Century> _centuryFilters = new HashSet<Century>();
        private readonly HashSet<Country> _countryFilters = new HashSet<Country>();
        private readonly HashSet<int> _qualityFilterIndices = new HashSet<int>();

        private MarketSortMode _sortMode = MarketSortMode.NameAsc;
        private int _currentPage;

        private readonly Dictionary<string, MarketListingUI> _rowsByListingId = new Dictionary<string, MarketListingUI>();
        private bool _subscribed;

        void Awake()
        {
            if (economyManager == null) economyManager = FindFirstObjectByType<EconomyManager>();
            if (playerTrader == null) playerTrader = FindFirstObjectByType<PlayerTrader>();
            if (timeManager == null) timeManager = FindFirstObjectByType<TimeManager>();

            if (typeFilterDropdown != null) typeFilterDropdown.onSelectionChanged.AddListener(OnTypeFilterChanged);
            if (centuryFilterDropdown != null) centuryFilterDropdown.onSelectionChanged.AddListener(OnCenturyFilterChanged);
            if (countryFilterDropdown != null) countryFilterDropdown.onSelectionChanged.AddListener(OnCountryFilterChanged);
            if (qualityFilterDropdown != null) qualityFilterDropdown.onSelectionChanged.AddListener(OnQualityFilterChanged);

            if (clearFiltersButton != null)
                clearFiltersButton.onClick.AddListener(ClearFilters);
        }

        protected override void OnShown()
        {
            if (!_subscribed && timeManager != null)
            {
                timeManager.OnDayChanged += HandleDayChanged;
                _subscribed = true;
            }

            // Safety net: force the layout groups built into this view
            // (ListPanel, ListArea, FiltersRow, ...) to actually arrange
            // themselves as soon as this view becomes visible, in case
            // nothing else triggered that pass yet.
            LayoutRebuilder.ForceRebuildLayoutImmediate((RectTransform)transform);

            RefreshListings();
        }

        void OnDestroy()
        {
            if (_subscribed && timeManager != null)
                timeManager.OnDayChanged -= HandleDayChanged;
        }

        private void HandleDayChanged(int newDay)
        {
            if (gameObject.activeSelf)
                RefreshListings();
        }


        // Called by the Antique Type filter dropdown's onSelectionChanged.
        // Dropdown option index N corresponds directly to AntiqueType N, since
        // AntiqueType's own values already run 0..18 in declaration order.
        private void OnTypeFilterChanged(List<int> selectedIndices)
        {
            _typeFilters.Clear();
            foreach (var i in selectedIndices)
                _typeFilters.Add((AntiqueType)i);

            _currentPage = 0;
            RefreshListings();
        }

        // Called by the Century filter dropdown's onSelectionChanged.
        private void OnCenturyFilterChanged(List<int> selectedIndices)
        {
            _centuryFilters.Clear();
            foreach (var i in selectedIndices)
                if (i >= 0 && i < CenturyOptions.Length)
                    _centuryFilters.Add(CenturyOptions[i]);

            _currentPage = 0;
            RefreshListings();
        }

        // Called by the Country filter dropdown's onSelectionChanged. Option
        // index N corresponds directly to Country N, since Country's own
        // values already run 0..14 in declaration order.
        private void OnCountryFilterChanged(List<int> selectedIndices)
        {
            _countryFilters.Clear();
            foreach (var i in selectedIndices)
                _countryFilters.Add((Country)i);

            _currentPage = 0;
            RefreshListings();
        }

        // Called by the Quality filter dropdown's onSelectionChanged.
        private void OnQualityFilterChanged(List<int> selectedIndices)
        {
            _qualityFilterIndices.Clear();
            foreach (var i in selectedIndices)
                _qualityFilterIndices.Add(i);

            _currentPage = 0;
            RefreshListings();
        }

        // Called by the "Clear Filters" button.
        public void ClearFilters()
        {
            _typeFilters.Clear();
            _centuryFilters.Clear();
            _countryFilters.Clear();
            _qualityFilterIndices.Clear();

            if (typeFilterDropdown != null) typeFilterDropdown.ClearSelection();
            if (centuryFilterDropdown != null) centuryFilterDropdown.ClearSelection();
            if (countryFilterDropdown != null) countryFilterDropdown.ClearSelection();
            if (qualityFilterDropdown != null) qualityFilterDropdown.ClearSelection();

            _currentPage = 0;
            RefreshListings();
        }

        // Called by the sort TMP_Dropdown's OnValueChanged(int)
        public void SetSortMode(int dropdownIndex)
        {
            _sortMode = (MarketSortMode)dropdownIndex;
            RefreshListings();
        }

        public void NextPage()
        {
            _currentPage++;
            RefreshListings();
        }

        public void PreviousPage()
        {
            if (_currentPage > 0) _currentPage--;
            RefreshListings();
        }

        // Stub for now — implemented in the detail panel phase.
        public void ShowDetails(Antique listing)
        {
            if (antiqueDetailsUI != null)
                antiqueDetailsUI.Show(listing);
        }

        private List<Antique> GetFilteredSortedListings()
        {
            IEnumerable<Antique> result = economyManager.Market.Listings;

            // Within a field, selections are OR'd (Country = France OR Germany);
            // an empty set means that field isn't filtered at all.
            if (_typeFilters.Count > 0)
                result = result.Where(l => _typeFilters.Contains(l.Type));

            if (_centuryFilters.Count > 0)
                result = result.Where(l => _centuryFilters.Contains(l.Century));

            if (_countryFilters.Count > 0)
                result = result.Where(l => _countryFilters.Contains(l.Country));

            if (_qualityFilterIndices.Count > 0)
                result = result.Where(l => _qualityFilterIndices.Any(i =>
                    l.Condition >= QualityOptions[i].Min && l.Condition <= QualityOptions[i].Max));

            result = _sortMode switch
            {
                MarketSortMode.PriceAsc => result.OrderBy(l => l.SalePrice),
                MarketSortMode.PriceDesc => result.OrderByDescending(l => l.SalePrice),
                _ => result.OrderBy(l => l.Name)
            };

            return result.ToList();
        }

        public void RefreshListings()
        {
            if (economyManager == null || economyManager.Market == null)
                return;

            var filteredSorted = GetFilteredSortedListings();
            int totalPages = Mathf.Max(1, Mathf.CeilToInt(filteredSorted.Count / (float)ItemsPerPage));
            _currentPage = Mathf.Clamp(_currentPage, 0, totalPages - 1);

            var pageItems = filteredSorted
                .Skip(_currentPage * ItemsPerPage)
                .Take(ItemsPerPage)
                .ToList();

            // Full rebuild each refresh — only 8 cards at a time, so this stays cheap
            // and avoids leftover cards from a previous filter/page.
            foreach (var row in _rowsByListingId.Values)
                Destroy(row.gameObject);
            _rowsByListingId.Clear();

            foreach (var listing in pageItems)
            {
                var rowObj = Instantiate(listingRowPrefab, listingsContainer);
                var rowUI = rowObj.GetComponent<MarketListingUI>();
                rowUI.Setup(listing, this, timeManager != null ? timeManager.CurrentDay : 0);
                _rowsByListingId[listing.Id] = rowUI;
            }

            if (pageText != null)
                pageText.text = $"Page {_currentPage + 1}/{totalPages}";

            if (prevPageButton != null) prevPageButton.interactable = _currentPage > 0;
            if (nextPageButton != null) nextPageButton.interactable = _currentPage < totalPages - 1;
        }
    }
}
