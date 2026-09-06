using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Market;

namespace AntiqueTradingSimulator.UI
{
    public class AntiqueDetailsUI : MonoBehaviour
    {
        [SerializeField] private MarketView marketView;

        [Header("Main")]
        [SerializeField] private Image antiqueImage;

        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text categoryText;

        [Header("Condition")]
        [SerializeField] private TMP_Text conditionText;
        [SerializeField] private TMP_Text qualityText;

        [Header("Price")]
        [SerializeField] private TMP_Text currentPriceText;
        [SerializeField] private TMP_Text basePriceText;

        [Header("Debug / Additional")]
        [SerializeField] private TMP_Text listingIdText;

        [Header("Buttons")]
        [SerializeField] private Button closeButton;
        [SerializeField] private Button buyButton;

        [Header("Dependencies")]
        [SerializeField] private PlayerTrader playerTrader;

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
                categoryText.text = antique.Category;

            if (conditionText != null)
                conditionText.text = $"State: {antique.State:P0}";

            if (qualityText != null)
                qualityText.text = $"Quality: {antique.Quality:P0}";

            if (currentPriceText != null)
                currentPriceText.text = $"{antique.CurrentPrice:F2} $";

            if (basePriceText != null)
                basePriceText.text = $"Base price: {antique.BasePrice:F2} $";

            if (listingIdText != null)
                listingIdText.text = $"ID: {antique.Id}";

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