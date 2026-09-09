using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Market;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    public class InventoryDetailsUI : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private PlayerTrader playerTrader;

        [Header("Header")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private Button closeButton;

        [Header("Main Image")]
        [SerializeField] private Image antiqueImage;

        [Header("Antique Info")]
        [SerializeField] private TMP_Text categoryText;
        [SerializeField] private TMP_Text periodText;
        [SerializeField] private TMP_Text originText;
        [SerializeField] private TMP_Text ownerText;
        [SerializeField] private TMP_Text itemIdText;

        [Header("Condition")]
        [SerializeField] private TMP_Text conditionText;
        [SerializeField] private TMP_Text rarityText;
        [SerializeField] private TMP_Text historyText;

        [Header("Value")]
        [SerializeField] private TMP_Text currentValueText;
        [SerializeField] private TMP_Text baseValueText;

        [Header("Actions")]
        [SerializeField] private Button listOnMarketButton;
        [SerializeField] private Button sellNowButton;

        private Antique _currentAntique;
        private InventoryView _inventoryView;

        private void Awake()
        {
            if (playerTrader == null)
                playerTrader = FindFirstObjectByType<PlayerTrader>();

            if (closeButton != null)
                closeButton.onClick.AddListener(Close);

            if (sellNowButton != null)
                sellNowButton.onClick.AddListener(SellCurrentAntique);
        }

        public void Setup(InventoryView inventoryView)
        {
            _inventoryView = inventoryView;
        }

        public void Show(Antique antique)
        {
            if (antique == null)
                return;

            _currentAntique = antique;

            if (titleText != null)
                titleText.text = antique.Name;

            if (categoryText != null)
                categoryText.text = antique.Category;

            if (periodText != null)
                periodText.text = antique.Century.ToDisplayString();

            if (originText != null)
                originText.text = antique.Country.ToDisplayString();

            if (ownerText != null)
                ownerText.text = string.IsNullOrEmpty(antique.OwnerId) ? "Unknown" : antique.OwnerId;

            if (itemIdText != null)
                itemIdText.text = antique.Id;

            if (conditionText != null)
                conditionText.text = GetConditionText(antique.Condition);

            if (rarityText != null)
                rarityText.text = antique.IsLimitedEdition ? antique.RarityLabel : "-";

            if (historyText != null)
                historyText.text = string.IsNullOrEmpty(antique.History) ? "-" : antique.History;

            if (currentValueText != null)
                currentValueText.text = $"{antique.CurrentPrice:F0} €";

            if (baseValueText != null)
                baseValueText.text = $"{antique.BasePrice:F0} €";

            gameObject.SetActive(true);
        }

        private void Close()
        {
            _currentAntique = null;

            if (_inventoryView != null)
                _inventoryView.ShowCollectionSummary();
        }

        private string GetConditionText(float state)
        {
            if (state >= 0.9f)
                return "Very Good";

            if (state >= 0.7f)
                return "Good";

            if (state >= 0.5f)
                return "Average";

            if (state >= 0.3f)
                return "Poor";

            return "Very Poor";
        }
        private void SellCurrentAntique()
        {
            if (_currentAntique == null)
                return;

            if (playerTrader == null)
            {
                Debug.LogWarning("InventoryDetailsUI: PlayerTrader reference is missing.");
                return;
            }

            bool success = playerTrader.SellListing(_currentAntique.Id);

            if (!success)
            {
                Debug.LogWarning(
                    $"InventoryDetailsUI: Failed to sell antique {_currentAntique.Id}.");

                return;
            }

            Debug.Log(
                $"InventoryDetailsUI: Sold antique {_currentAntique.Id} for {_currentAntique.CurrentPrice:F2} €.");

            _currentAntique = null;

            if (_inventoryView != null)
                _inventoryView.ShowCollectionSummary();
        }
    }
}