using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using TMPro;
using UnityEngine.UI;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Market;

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

        [Header("Sorting")]
        [SerializeField] private TMP_Dropdown sortDropdown;

        [Header("Pagination")]
        [SerializeField] private TMP_Text pageText;
        [SerializeField] private Button prevPageButton;
        [SerializeField] private Button nextPageButton;
        private const int ItemsPerPage = 8;

        private string _categoryFilter; // null = all
        private MarketSortMode _sortMode = MarketSortMode.NameAsc;
        private int _currentPage;

        private readonly Dictionary<string, MarketListingUI> _rowsByListingId = new Dictionary<string, MarketListingUI>();
        private bool _subscribed;

        void Awake()
        {
            if (economyManager == null) economyManager = FindFirstObjectByType<EconomyManager>();
            if (playerTrader == null) playerTrader = FindFirstObjectByType<PlayerTrader>();
            if (timeManager == null) timeManager = FindFirstObjectByType<TimeManager>();
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

        // Called by the category filter buttons (pass "" for "All")
        public void SetCategoryFilter(string category)
        {
            _categoryFilter = string.IsNullOrEmpty(category) ? null : category;
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
            Debug.Log($"Show details for {listing.Name} (Id: {listing.Id})");
        }

        private List<Antique> GetFilteredSortedListings()
        {
            IEnumerable<Antique> result = economyManager.Market.Listings;

            if (_categoryFilter != null)
                result = result.Where(l => l.Category == _categoryFilter);

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

            // Full rebuild each refresh — only 8 cards at a time, so this stays cheap
            // and avoids leftover cards from a previous filter/page.
            foreach (var row in _rowsByListingId.Values)
                Destroy(row.gameObject);
            _rowsByListingId.Clear();

            foreach (var listing in pageItems)
            {
                var rowObj = Instantiate(listingRowPrefab, listingsContainer);
                var rowUI = rowObj.GetComponent<MarketListingUI>();
                rowUI.Setup(listing, this);
                _rowsByListingId[listing.Id] = rowUI;
            }

            if (pageText != null)
                pageText.text = $"Page {_currentPage + 1}/{totalPages}";

            if (prevPageButton != null) prevPageButton.interactable = _currentPage > 0;
            if (nextPageButton != null) nextPageButton.interactable = _currentPage < totalPages - 1;
        }
    }
}