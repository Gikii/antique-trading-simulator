using System.Collections.Generic;
using UnityEngine;
using TMPro;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Core;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Populates the market listing view and keeps the player's cash display up to date.
    /// Keeps a row per listing Id: new listings get a fresh row (with a highlight),
    /// listings that disappeared (bought, etc.) get their row removed, and everything
    /// else is left untouched so it doesn't flicker or lose its highlight state.
    /// </summary>
    public class MarketUI : MonoBehaviour
    {
        [SerializeField] private EconomyManager economyManager;
        [SerializeField] private PlayerTrader playerTrader;
        [SerializeField] private TimeManager timeManager;

        [SerializeField] private RectTransform listingsContainer;
        [SerializeField] private GameObject listingRowPrefab;
        [SerializeField] private TMP_Text cashText;

        private readonly Dictionary<string, MarketListingUI> _rowsByListingId = new Dictionary<string, MarketListingUI>();

        void Awake()
        {
            if (economyManager == null) economyManager = FindFirstObjectByType<EconomyManager>();
            if (playerTrader == null) playerTrader = FindFirstObjectByType<PlayerTrader>();
            if (timeManager == null) timeManager = FindFirstObjectByType<TimeManager>();
        }

        void Start()
        {
            playerTrader.Inventory.OnCashChanged += UpdateCashDisplay;
            UpdateCashDisplay(playerTrader.Inventory.Cash);

            if (timeManager != null)
                timeManager.OnDayChanged += HandleDayChanged;

            RefreshListings();
        }

        void OnDestroy()
        {
            if (playerTrader != null)
                playerTrader.Inventory.OnCashChanged -= UpdateCashDisplay;

            if (timeManager != null)
                timeManager.OnDayChanged -= HandleDayChanged;
        }

        private void HandleDayChanged(int newDay)
        {
            RefreshListings();
        }

        public void RefreshListings()
        {
            if (economyManager == null || economyManager.Market == null)
                return;

            var currentListings = economyManager.Market.Listings;
            var currentIds = new HashSet<string>();

            foreach (var listing in currentListings)
            {
                currentIds.Add(listing.Id);

                if (_rowsByListingId.TryGetValue(listing.Id, out var existingRow))
                {
                    // Already on screen — just keep its price current, no re-highlight.
                    existingRow.UpdatePrice(listing);
                }
                else
                {
                    // Genuinely new listing — create its row and play the highlight.
                    var rowObj = Instantiate(listingRowPrefab, listingsContainer);
                    var rowUI = rowObj.GetComponent<MarketListingUI>();
                    rowUI.Setup(listing, playerTrader, this);
                    rowUI.PlayNewListingHighlight();
                    _rowsByListingId[listing.Id] = rowUI;
                }
            }

            // Remove rows whose listing is no longer on the market (bought, etc.)
            var idsToRemove = new List<string>();
            foreach (var kvp in _rowsByListingId)
            {
                if (!currentIds.Contains(kvp.Key))
                    idsToRemove.Add(kvp.Key);
            }

            foreach (var id in idsToRemove)
            {
                Destroy(_rowsByListingId[id].gameObject);
                _rowsByListingId.Remove(id);
            }
        }

        private void UpdateCashDisplay(float cash)
        {
            cashText.text = $"Cash: {cash:F2} zł";
        }
    }
}