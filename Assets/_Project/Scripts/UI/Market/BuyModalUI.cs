using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.Market;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// "Buy?" confirmation shown when the player presses Buy on a market listing:
    /// the item and its price, a choice of transport service (cost / time / damage risk),
    /// when it will arrive, the total cost, cash left afterwards and warehouse space.
    /// Buy stays disabled — with the reason shown — when the purchase can't go through.
    /// Confirming calls PlayerTrader.BuyListing with the selected TransportOption.
    /// </summary>
    public class BuyModalUI : MonoBehaviour, IModalPanel
    {
        [Header("Game systems (auto-found if empty)")]
        [SerializeField] private PlayerTrader playerTrader;
        [SerializeField] private EconomyManager economyManager;
        [SerializeField] private TimeManager timeManager;
        [SerializeField] private TransportManager transportManager;

        [Header("Options")]
        [Tooltip("Pause the game clock while the modal is open, so price and quotes can't change before confirming.")]
        [SerializeField] private bool pauseTimeWhileOpen = true;
        [SerializeField] private TransportOption defaultOption = TransportOption.Standard;

        [Header("Item")]
        [SerializeField] private TMP_Text titleText;    // "Buy Chinese Vase?"
        [SerializeField] private TMP_Text itemInfoText; // "Good condition · XVIII century · China"
        [SerializeField] private TMP_Text priceText;    // "1 250 €"
        [SerializeField] private TMP_Text shipsFromText; // "Ships from: International"

        [Header("Transport options (one per TransportOption)")]
        [SerializeField] private List<TransportOptionUI> transportOptions = new();
        [Tooltip("Shown instead of the options when there's no TransportManager in the scene. Optional.")]
        [SerializeField] private GameObject noTransportNotice;

        [Header("Summary")]
        [SerializeField] private TMP_Text shippingCostText; // "120 €"
        [SerializeField] private TMP_Text totalCostText;    // "1 370 €"
        [SerializeField] private TMP_Text arrivalText;      // "7 October (in 5 days)"
        [SerializeField] private TMP_Text cashAfterText;    // "2 630 €"
        [SerializeField] private TMP_Text warehouseText;    // "6 / 8 → 7 / 8"
        [SerializeField] private TMP_Text warningText;      // reason why Buy is disabled

        [Header("Buttons")]
        [SerializeField] private Button closeButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private Button confirmButton;

        /// <summary>Raised after a successful purchase (the bought antique).</summary>
        public event Action<Antique> Bought;

        private Antique _listing;
        private TransportOption _selectedOption;
        private bool _initialized;
        private bool _pausedByModal;
        private bool _subscribed;

        private TraderInventory Inventory => playerTrader != null ? playerTrader.Inventory : null;

        // ------------------------------------------------------------------
        // Open / close
        // ------------------------------------------------------------------

        public void Open(Antique listing)
        {
            if (listing == null)
                return;

            EnsureInitialized();

            _listing = listing;
            _selectedOption = defaultOption;

            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            if (pauseTimeWhileOpen && timeManager != null && timeManager.IsRunning)
            {
                timeManager.Pause();
                _pausedByModal = true;
            }

            Refresh();
        }

        public void Close()
        {
            _listing = null;
            gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            ModalTracker.SetOpen(this);
            Subscribe();
        }

        private void OnDisable()
        {
            ModalTracker.SetClosed(this);
            Unsubscribe();

            // Resume even if something else hides the modal.
            if (_pausedByModal && timeManager != null)
                timeManager.Resume();

            _pausedByModal = false;
        }

        private void Subscribe()
        {
            if (_subscribed || Inventory == null) return;

            Inventory.OnCashChanged += HandleCashChanged;
            if (timeManager != null) timeManager.OnDayChanged += HandleDayChanged;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;

            if (Inventory != null) Inventory.OnCashChanged -= HandleCashChanged;
            if (timeManager != null) timeManager.OnDayChanged -= HandleDayChanged;
            _subscribed = false;
        }

        // Only relevant when the clock keeps running while the modal is open.
        private void HandleCashChanged(float cash) => Refresh();
        private void HandleDayChanged(int day) => Refresh();

        private void EnsureInitialized()
        {
            if (_initialized)
                return;

            _initialized = true;

            if (playerTrader == null) playerTrader = FindFirstObjectByType<PlayerTrader>();
            if (economyManager == null) economyManager = FindFirstObjectByType<EconomyManager>();
            if (timeManager == null) timeManager = FindFirstObjectByType<TimeManager>();
            if (transportManager == null) transportManager = FindFirstObjectByType<TransportManager>();

            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (cancelButton != null) cancelButton.onClick.AddListener(Close);
            if (confirmButton != null) confirmButton.onClick.AddListener(Confirm);

            foreach (var optionUI in transportOptions)
                if (optionUI != null)
                    optionUI.Clicked += SelectOption;

            // OnEnable may have run before the references were resolved.
            if (isActiveAndEnabled)
                Subscribe();
        }

        private void SelectOption(TransportOption option)
        {
            _selectedOption = option;
            Refresh();
        }

        // ------------------------------------------------------------------
        // Content
        // ------------------------------------------------------------------

        private void Refresh()
        {
            if (_listing == null)
                return;

            var listing = _listing;
            float price = listing.SalePrice;

            SetText(titleText, $"Buy <b>{listing.Name}</b>?");
            SetText(itemInfoText,
                $"{UIFormat.ConditionLabel(listing.Condition)} condition · {listing.Century.ToDisplayString()} · {listing.Country.ToDisplayString()}");
            SetText(priceText, UIFormat.Money(price));
            SetText(shipsFromText, $"Ships from: {listing.ShippingZone.ToDisplayString()}");

            TransportQuote selectedQuote = RefreshTransportOptions(listing);

            float shipping = selectedQuote != null ? selectedQuote.Cost : 0f;
            float total = price + shipping;
            float cash = Inventory != null ? Inventory.Cash : 0f;

            SetText(shippingCostText, selectedQuote != null ? UIFormat.Money(shipping) : "—");
            SetText(totalCostText, $"<b>{UIFormat.Money(total)}</b>");
            SetText(arrivalText, ArrivalLabel(selectedQuote));
            SetText(cashAfterText, UIFormat.ColorBySign(UIFormat.Money(cash - total), cash - total < 0f ? -1f : 0f));
            SetText(warehouseText, WarehouseLabel());

            string blockReason = BlockReason(listing, total, cash);
            SetText(warningText, blockReason != null ? UIFormat.Colorize(blockReason, UIFormat.NegativeColor) : "");

            if (confirmButton != null)
                confirmButton.interactable = blockReason == null;
        }

        /// <summary>Fills every option button and returns the quote of the selected one (null without transport).</summary>
        private TransportQuote RefreshTransportOptions(Antique listing)
        {
            bool hasTransport = transportManager != null;

            if (noTransportNotice != null)
                noTransportNotice.SetActive(!hasTransport);

            TransportQuote selected = null;

            foreach (var optionUI in transportOptions)
            {
                if (optionUI == null) continue;

                if (!hasTransport)
                {
                    optionUI.gameObject.SetActive(false);
                    continue;
                }

                TransportQuote quote = transportManager.Quote(listing, optionUI.Option);
                optionUI.Show(quote, transportManager.Settings.GetDamageChance(optionUI.Option));

                bool isSelected = optionUI.Option == _selectedOption;
                optionUI.SetSelected(isSelected);
                if (isSelected) selected = quote;
            }

            // The selected option may have no button in this layout — quote it anyway.
            if (hasTransport && selected == null)
                selected = transportManager.Quote(listing, _selectedOption);

            return selected;
        }

        private string ArrivalLabel(TransportQuote quote)
        {
            if (quote == null)
                return "Immediately";

            if (timeManager == null)
                return $"Day {quote.ArrivalDay} (in {UIFormat.Days(quote.DurationDays)})";

            string date = TimeManager.FormatDayMonth(timeManager.DayToDate(quote.ArrivalDay));
            return $"{date} (in {UIFormat.Days(quote.DurationDays)})";
        }

        private string WarehouseLabel()
        {
            var inventory = Inventory;
            if (inventory == null || !inventory.HasWarehouseLimit)
                return "—";

            int used = inventory.UsedSlots;
            int capacity = inventory.Capacity;
            string after = $"{used + 1} / {capacity}";
            if (used + 1 > capacity)
                after = UIFormat.Colorize(after, UIFormat.NegativeColor);

            return $"{used} / {capacity} → {after}";
        }

        /// <summary>Why the purchase can't go through right now, or null if it can.</summary>
        private string BlockReason(Antique listing, float total, float cash)
        {
            if (playerTrader == null)
                return "Player is missing.";

            var market = economyManager != null ? economyManager.Market : null;
            if (market != null && market.GetById(listing.Id) == null)
                return "This antique is no longer available.";

            if (listing.OwnerId == Antique.PlayerOwnerId)
                return "This is your own listing.";

            if (Inventory != null && !Inventory.HasFreeSlot)
                return $"Warehouse full ({Inventory.UsedSlots} / {Inventory.Capacity}). Sell something to make room.";

            if (total > cash)
                return $"Not enough cash — {UIFormat.Money(total - cash)} short.";

            return null;
        }

        // ------------------------------------------------------------------
        // Confirm
        // ------------------------------------------------------------------

        private void Confirm()
        {
            if (_listing == null)
                return;

            if (playerTrader == null)
            {
                Debug.LogWarning("BuyModalUI: PlayerTrader reference is missing.");
                return;
            }

            Antique listing = _listing;
            if (!playerTrader.BuyListing(listing.Id, _selectedOption))
            {
                Debug.LogWarning($"BuyModalUI: failed to buy {listing.Id} with {_selectedOption} transport.");
                Refresh(); // shows the concrete reason if there is one
                if (confirmButton == null || confirmButton.interactable)
                    SetText(warningText, UIFormat.Colorize("The purchase didn't go through.", UIFormat.NegativeColor));
                return;
            }

            Close();
            Bought?.Invoke(listing);
        }

        private static void SetText(TMP_Text target, string value)
        {
            if (target != null)
                target.text = value;
        }
    }
}
