using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.Market;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    public class AntiqueDetailsUI : MonoBehaviour
    {
        [SerializeField] private MarketView marketView;

        [Header("Main")]
        [SerializeField] private Image antiqueImage;

        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text categoryText;
        [SerializeField] private TMP_Text centuryText;
        [SerializeField] private TMP_Text countryText;
        [SerializeField] private TMP_Text descriptionText;

        [Header("Condition")]
        [SerializeField] private TMP_Text conditionText;
        [SerializeField] private TMP_Text rarityText;

        [Header("Market Value")]
        [SerializeField] private TMP_Text basePriceText;
        [SerializeField] private TMP_Text currentValueText;
        [SerializeField] private TMP_Text changeText;

        [Header("Price")]
        [SerializeField] private TMP_Text currentPriceText;

        [Header("Shipping")]
        [Tooltip("e.g. \"Ships from International · 5 days, ~120 € (Standard)\". Optional.")]
        [SerializeField] private TMP_Text shippingText;


        [Header("Debug / Additional")]
        [SerializeField] private TMP_Text listingIdText;

        [Header("Buttons")]
        [SerializeField] private Button closeButton;
        [SerializeField] private Button buyButton;

        [Header("Dependencies")]
        [SerializeField] private PlayerTrader playerTrader;
        [SerializeField] private TransportManager transportManager;
        [Tooltip("Confirmation modal opened by Buy. If missing, Buy purchases directly with Standard transport.")]
        [SerializeField] private BuyModalUI buyModal;

        [Header("History (unique items only)")]
        [SerializeField] private TMP_Text historyText;

        // Visibility is driven through this CanvasGroup instead of
        // gameObject.SetActive(). This panel sits in a HorizontalLayoutGroup
        // row next to the listings panel; LayoutGroups skip inactive
        // children entirely, so SetActive(false) here would make the
        // listings panel stretch to fill the whole row whenever the details
        // panel is hidden. Fading it out via CanvasGroup instead keeps the
        // GameObject active, so its column stays reserved.
        [SerializeField] private CanvasGroup canvasGroup;
        [SerializeField] private GameObject transactionHistoryPanel;
        // Temporary placeholder until unique items carry real, meaningful History
        // text — swap this back to antique.History once that content exists.
        private const string PlaceholderHistoryText =
            "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do " +
            "eiusmod tempor incididunt ut labore et dolore magna aliqua. Ut enim " +
            "aa aliqua. a aliqua. r adipiscing er adipiscing er adipiscing";

        private Antique _currentAntique;

        private void Awake()
        {
            if (playerTrader == null)
                playerTrader = FindFirstObjectByType<PlayerTrader>();

            if (transportManager == null)
                transportManager = FindFirstObjectByType<TransportManager>();

            if (buyModal == null)
                buyModal = FindFirstObjectByType<BuyModalUI>(FindObjectsInactive.Include);

            if (buyModal != null)
                buyModal.Bought += HandleBought;

            if (canvasGroup == null)
                canvasGroup = GetComponent<CanvasGroup>();

            if (closeButton != null)
                closeButton.onClick.AddListener(Hide);

            if (buyButton != null)
                buyButton.onClick.AddListener(BuyCurrentAntique);

            SetVisible(false);
        }

        private void SetVisible(bool visible)
        {
            if (transactionHistoryPanel != null)
                transactionHistoryPanel.SetActive(!visible);

            if (canvasGroup != null)
            {
                canvasGroup.alpha = visible ? 1f : 0f;
                canvasGroup.interactable = visible;
                canvasGroup.blocksRaycasts = visible;
            }
            else
            {
                // Fallback for a panel that doesn't have a CanvasGroup — behaves
                // like before, but won't reserve layout space while hidden.
                gameObject.SetActive(visible);
            }
        }

        public void Show(Antique antique)
        {
            if (antique == null)
                return;

            _currentAntique = antique;

            if (nameText != null)
                nameText.text = antique.Name;

            if (categoryText != null)
                categoryText.text = $"Category: {antique.Category}";

            if (conditionText != null)
                conditionText.text = $"Condition: {antique.Condition:P0}";

            if (rarityText != null)
            {
                rarityText.text = antique.IsLimitedEdition ? antique.RarityLabel : "";
                rarityText.gameObject.SetActive(antique.IsLimitedEdition);
            }

            if (centuryText != null)
                centuryText.text = $"Century: {antique.Century.ToDisplayString()}";
            
            if (countryText != null)
                countryText.text = $"Country: {antique.Country.ToDisplayString()}";
            
            if (descriptionText != null)
                descriptionText.text = antique.Description;

            // Owner listings sell at the owner's asking price, not the market value.
            if (currentPriceText != null)
                currentPriceText.text = $"{antique.SalePrice:F2} €";

            if (basePriceText != null)
                basePriceText.text = $"{antique.BasePrice:F2} €";

            if (shippingText != null)
                shippingText.text = ShippingLabel(antique);

            if (listingIdText != null)
                listingIdText.text = $"ID: {antique.Id}";

            if (historyText != null)
                historyText.text = PlaceholderHistoryText;
            
            if (currentValueText != null)
                currentValueText.text = $"{antique.CurrentPrice:F2} €";
            
            if (changeText != null)
                changeText.text = "—"; 
            


            // The player's own listings show up on the market too, but can't be bought back —
            // they're cancelled from the collection view instead.
            if (buyButton != null)
                buyButton.interactable = antique.OwnerId != Antique.PlayerOwnerId;

            SetVisible(true);
        }

        public void Hide()
        {
            _currentAntique = null;
            SetVisible(false);
        }

        private void OnDestroy()
        {
            if (buyModal != null)
                buyModal.Bought -= HandleBought;
        }

        private string ShippingLabel(Antique antique)
        {
            string zone = $"Ships from {antique.ShippingZone.ToDisplayString()}";
            if (transportManager == null)
                return zone;

            TransportQuote quote = transportManager.Quote(antique, TransportOption.Standard);
            return quote != null
                ? $"{zone} · {UIFormat.Days(quote.DurationDays)}, ~{UIFormat.Money(quote.Cost)} (Standard)"
                : zone;
        }

        private void HandleBought(Antique antique)
        {
            if (marketView != null)
                marketView.RefreshListings();

            Debug.Log($"AntiqueDetailsUI: Bought antique {antique.Id}.");
            Hide();
        }

        private void BuyCurrentAntique()
        {
            if (_currentAntique == null)
                return;

            // Normal path: confirm (and pick transport) in the modal; HandleBought finishes up.
            if (buyModal != null)
            {
                buyModal.Open(_currentAntique);
                return;
            }

            if (playerTrader == null)
            {
                Debug.LogWarning("AntiqueDetailsUI: PlayerTrader reference is missing.");
                return;
            }

            bool success = playerTrader.BuyListing(_currentAntique.Id);

            if (!success)
            {
                Debug.LogWarning(
                    $"AntiqueDetailsUI: Failed to buy antique {_currentAntique.Id}.");

                return;
            }

            if (marketView != null)
                marketView.RefreshListings();

            Debug.Log(
                $"AntiqueDetailsUI: Bought antique {_currentAntique.Id}.");

            Hide();
        }
    }
}