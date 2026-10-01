using System;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AntiqueTradingSimulator.Market;

namespace AntiqueTradingSimulator.UI
{

    public class ContractFulfillListItemUI : MonoBehaviour
    {
        [Header("Selection")]
        [SerializeField] private Toggle toggle;

        [Header("Antique")]
        [SerializeField] private Image antiqueImage;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text conditionText;

        [Header("Info")]
        [SerializeField] private TMP_Text categoryText;
        [SerializeField] private TMP_Text periodText;
        [SerializeField] private TMP_Text priceText;

        [Header("Selection tint")]
        [Tooltip("Graphic tinted while this row is ticked. Defaults to the Image on this object.")]
        [SerializeField] private Image background;
        [SerializeField] private Color selectedColor = new Color32(0xC9, 0x9A, 0x3C, 0xFF);

        private Antique _antique;
        private Action<Antique, bool> _onToggled;
        private Color _normalColor;
        private bool _colorCached;

        private bool _suppressCallback;

        public Antique Antique => _antique;
        public bool IsSelected => toggle != null && toggle.isOn;

        public void Setup(Antique antique, bool selected, Action<Antique, bool> onToggled)
        {
            _antique = antique;
            _onToggled = onToggled;

            if (_antique == null)
                return;

            if (nameText != null)
                nameText.text = _antique.Name;

            if (conditionText != null)
            {
                string condition = UIFormat.ConditionLabel(_antique.Condition);
                if (_antique.IsReservedForContract)
                    condition += UIFormat.Colorize("  • reserved for this contract", UIFormat.MutedColor);
                conditionText.text = condition;
            }

            if (categoryText != null)
                categoryText.text = _antique.Category;

            if (periodText != null)
                periodText.text = _antique.Century.ToDisplayString();

            if (priceText != null)
                priceText.text = UIFormat.Money(_antique.CurrentPrice);

            if (toggle != null)
            {
                toggle.onValueChanged.RemoveAllListeners();
                SetSelected(selected);
                toggle.onValueChanged.AddListener(HandleToggled);
            }
        }

        public void SetSelected(bool selected)
        {
            if (toggle == null) return;

            _suppressCallback = true;
            toggle.isOn = selected;
            _suppressCallback = false;

            ApplyTint(selected);
        }

        public void SetInteractable(bool interactable)
        {
            if (toggle != null)
                toggle.interactable = interactable;
        }

        private void HandleToggled(bool isOn)
        {
            ApplyTint(isOn);

            if (_suppressCallback || _antique == null) return;
            _onToggled?.Invoke(_antique, isOn);
        }

        private void ApplyTint(bool selected)
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
    }
}
