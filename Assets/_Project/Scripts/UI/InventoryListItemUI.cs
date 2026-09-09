using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AntiqueTradingSimulator.Market;

namespace AntiqueTradingSimulator.UI
{
    public class InventoryListItemUI : MonoBehaviour
    {
        [Header("Antique")]
        [SerializeField] private Image antiqueImage;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text conditionText;

        [Header("Info")]
        [SerializeField] private TMP_Text categoryText;
        [SerializeField] private TMP_Text periodText;
        [SerializeField] private TMP_Text priceText;

        [Header("Interaction")]
        [SerializeField] private Button button;

        private Antique _antique;
        private InventoryView _inventoryView;

        public void Setup(Antique antique, InventoryView inventoryView)
        {
            _antique = antique;
            _inventoryView = inventoryView;

            if (_antique == null)
                return;

            if (nameText != null)
                nameText.text = _antique.Name;

            if (conditionText != null)
                conditionText.text = GetConditionText(_antique.Condition);

            if (categoryText != null)
                categoryText.text = _antique.Category;

            if (periodText != null)
                periodText.text = _antique.Century.ToDisplayString();

            if (priceText != null)
                priceText.text = $"{_antique.CurrentPrice:F0} €";

            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(HandleClick);
            }
        }

        private void HandleClick()
        {
            if (_inventoryView != null && _antique != null)
                _inventoryView.ShowDetails(_antique);
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
    }
}