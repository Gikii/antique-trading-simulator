using AntiqueTradingSimulator.Agents;
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


        [Header("Debug / Additional")]
        [SerializeField] private TMP_Text listingIdText;

        [Header("Buttons")]
        [SerializeField] private Button closeButton;
        [SerializeField] private Button buyButton;

        [Header("Dependencies")]
        [SerializeField] private PlayerTrader playerTrader;

        [Header("History (unique items only)")]
        [SerializeField] private TMP_Text historyText;
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

            if (closeButton != null)
                closeButton.onClick.AddListener(Hide);

            if (buyButton != null)
                buyButton.onClick.AddListener(BuyCurrentAntique);

            gameObject.SetActive(false);
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

            if (currentPriceText != null)
                currentPriceText.text = $"{antique.CurrentPrice:F2} €";

            if (basePriceText != null)
                basePriceText.text = $"{antique.BasePrice:F2} €";

            if (listingIdText != null)
                listingIdText.text = $"ID: {antique.Id}";

            if (historyText != null)
                historyText.text = PlaceholderHistoryText;
            
            if (currentValueText != null)
                currentValueText.text = $"{antique.CurrentPrice:F2} €";
            
            if (changeText != null)
                changeText.text = "—"; 
            


            if (buyButton != null)
                buyButton.interactable = true;

            gameObject.SetActive(true);
        }

        public void Hide()
        {
            _currentAntique = null;
            gameObject.SetActive(false);
        }

        private void BuyCurrentAntique()
        {
            if (_currentAntique == null)
                return;

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