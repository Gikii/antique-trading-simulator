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

        [Header("Selection")]
        [Tooltip("Graphic tinted when this row is the selected one. Defaults to the Image on this object.")]
        [SerializeField] private Image background;
        [SerializeField] private Color selectedColor = new Color32(0xC9, 0x9A, 0x3C, 0xFF);

        private Antique _antique;
        private InventoryView _inventoryView;
        private Color _normalColor;
        private bool _colorCached;

        public Antique Antique => _antique;

        public void Setup(Antique antique, InventoryView inventoryView)
        {
            _antique = antique;
            _inventoryView = inventoryView;

            if (_antique == null)
                return;

            if (nameText != null)
                nameText.text = _antique.Name;

            if (conditionText != null)
            {
                string condition = UIFormat.ConditionLabel(_antique.Condition);
                if (_antique.IsListedForSale)
                    condition += UIFormat.Colorize($"  • listed for {UIFormat.Money(_antique.AskingPrice)}", UIFormat.MutedColor);
                else if (_antique.IsReservedForContract)
                    condition += UIFormat.Colorize("  • reserved for contract", UIFormat.MutedColor);
                conditionText.text = condition;
            }

            if (categoryText != null)
                categoryText.text = _antique.Category;

            if (periodText != null)
                periodText.text = _antique.Century.ToDisplayString();

            if (priceText != null)
                priceText.text = UIFormat.Money(_antique.CurrentPrice);

            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(HandleClick);
            }
        }

        public void SetSelected(bool selected)
        {
            if (background == null)
                background = GetComponent<Image>();

            if (background == null)
                return;

            if (!_colorCached)
            {
                _normalColor = background.color;
                _colorCached = true;
            }

            background.color = selected ? selectedColor : _normalColor;
        }

        private void HandleClick()
        {
            if (_inventoryView != null && _antique != null)
                _inventoryView.ShowDetails(_antique);
        }
    }
}
