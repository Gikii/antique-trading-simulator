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

        private static readonly Century[] CenturyOptions =
        {
            Century.XII, Century.XIII, Century.XIV, Century.XV, Century.XVI,
            Century.XVII, Century.XVIII, Century.XIX, Century.XX
        };

        private static readonly (string Label, float Min, float Max)[] QualityOptions =
        {
            ("Excellent (90-100%)", 0.9f, 1f),
            ("Good (70-89%)", 0.7f, 0.8999f),
            ("Fair (50-69%)", 0.5f, 0.6999f),
            ("Poor (30-49%)", Antique.MinCondition, 0.4999f),
        };

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

        private void OnTypeFilterChanged(List<int> selectedIndices)
        {
            _typeFilters.Clear();
            foreach (var i in selectedIndices)
                _typeFilters.Add((AntiqueType)i);

            _currentPage = 0;
            RefreshListings();
        }

        private void OnCenturyFilterChanged(List<int> selectedIndices)
        {
            _centuryFilters.Clear();
            foreach (var i in selectedIndices)
                if (i >= 0 && i < CenturyOptions.Length)
                    _centuryFilters.Add(CenturyOptions[i]);

            _currentPage = 0;
            RefreshListings();
        }

        private void OnCountryFilterChanged(List<int> selectedIndices)
        {
            _countryFilters.Clear();
            foreach (var i in selectedIndices)
                _countryFilters.Add((Country)i);

            _currentPage = 0;
            RefreshListings();
        }

        private void OnQualityFilterChanged(List<int> selectedIndices)
        {
            _qualityFilterIndices.Clear();
            foreach (var i in selectedIndices)
                _qualityFilterIndices.Add(i);

            _currentPage = 0;
            RefreshListings();
        }

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

        public void ShowDetails(Antique listing)
        {
            if (antiqueDetailsUI != null)
                antiqueDetailsUI.Show(listing);
        }

        private List<Antique> GetFilteredSortedListings()
        {
            IEnumerable<Antique> result = economyManager.Market.Listings;

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
                MarketSortMode.PriceAsc => result.OrderBy(l => l.CurrentPrice),
                MarketSortMode.PriceDesc => result.OrderByDescending(l => l.CurrentPrice),
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
