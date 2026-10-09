using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Market;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// "Sell now?" confirmation shown before an instant sale: which antique, what the player
    /// receives and how that compares to what they paid. Confirming calls PlayerTrader.SellListing.
    /// </summary>
    public class SellNowModalUI : MonoBehaviour, IModalPanel
    {
        [Header("Game systems (auto-found if empty)")]
        [SerializeField] private PlayerTrader playerTrader;
        [SerializeField] private EconomyManager economyManager;
        [SerializeField] private TimeManager timeManager;

        [Header("Options")]
        [Tooltip("Pause the game clock while the modal is open, so the price can't change before confirming.")]
        [SerializeField] private bool pauseTimeWhileOpen = true;

        [Header("Content")]
        [SerializeField] private TMP_Text questionText;
        [SerializeField] private TMP_Text marketValueText;
        [SerializeField] private TMP_Text receiveText;
        [SerializeField] private TMP_Text profitText;

        [Header("Buttons")]
        [SerializeField] private Button closeButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private Button confirmButton;

        private Antique _antique;
        private bool _initialized;
        private bool _pausedByModal;

        public void Open(Antique antique)
        {
            if (antique == null)
                return;

            EnsureInitialized();

            _antique = antique;

            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            if (pauseTimeWhileOpen && timeManager != null && timeManager.IsRunning)
            {
                timeManager.Pause();
                _pausedByModal = true;
            }

            float received = EstimatedPrice(antique);

            SetText(questionText,
                $"Are you sure you want to sell <b>{antique.Name}</b> for <b>{UIFormat.Money(received)}</b>?");
            SetText(marketValueText, UIFormat.Money(antique.CurrentPrice));
            SetText(receiveText, UIFormat.Colorize(UIFormat.Money(received), UIFormat.PositiveColor));

            if (antique.HasPurchaseRecord)
            {
                float profit = received - antique.PurchasePrice;
                string bought = antique.PurchasePrice > 0f
                    ? $"Bought for {UIFormat.Money(antique.PurchasePrice)}"
                    : "Received for free";
                SetText(profitText, $"{bought} · profit / loss: {UIFormat.ColorBySign(UIFormat.SignedMoney(profit), profit)}");
            }
            else
            {
                SetText(profitText, "");
            }

            if (confirmButton != null)
                confirmButton.interactable = !antique.IsReservedForContract && !antique.IsListedForSale;
        }

        public void Close()
        {
            _antique = null;
            gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            ModalTracker.SetOpen(this);

            var inventory = playerTrader != null ? playerTrader.Inventory : null;
            if (inventory != null) inventory.OnStateRestored += Close;
        }

        private void OnDisable()
        {
            ModalTracker.SetClosed(this);

            var inventory = playerTrader != null ? playerTrader.Inventory : null;
            if (inventory != null) inventory.OnStateRestored -= Close;

            // Resume even if something else hides the modal.
            if (_pausedByModal && timeManager != null)
                timeManager.Resume();

            _pausedByModal = false;
        }

        private void EnsureInitialized()
        {
            if (_initialized)
                return;

            _initialized = true;

            if (playerTrader == null) playerTrader = FindFirstObjectByType<PlayerTrader>();
            if (economyManager == null) economyManager = FindFirstObjectByType<EconomyManager>();
            if (timeManager == null) timeManager = FindFirstObjectByType<TimeManager>();

            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (cancelButton != null) cancelButton.onClick.AddListener(Close);
            if (confirmButton != null) confirmButton.onClick.AddListener(Confirm);
        }

        // Instant sale pays the price after the sale's own effect on supply — the same
        // amount TraderInventory.Sell adds to cash.
        private float EstimatedPrice(Antique antique)
        {
            var market = economyManager != null ? economyManager.Market : null;
            return market != null ? market.EstimateInstantSalePrice(antique) : antique.CurrentPrice;
        }

        private void Confirm()
        {
            if (_antique == null)
                return;

            if (playerTrader == null)
            {
                Debug.LogWarning("SellNowModalUI: PlayerTrader reference is missing.");
                return;
            }

            string antiqueId = _antique.Id;
            if (!playerTrader.SellListing(antiqueId))
            {
                Debug.LogWarning($"SellNowModalUI: failed to sell antique {antiqueId}.");
                return;
            }

            // InventoryView notices the antique left the collection and switches to the summary.
            Close();
        }

        private static void SetText(TMP_Text target, string value)
        {
            if (target != null)
                target.text = value;
        }
    }
}