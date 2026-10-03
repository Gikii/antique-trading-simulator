using AntiqueTradingSimulator.Logistics;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// One selectable transport service inside BuyModalUI (Economy / Standard / Express).
    /// Set <see cref="Option"/> per instance in the Inspector; the modal fills in the
    /// texts from a TransportQuote and handles selection.
    /// </summary>
    public class TransportOptionUI : MonoBehaviour
    {
        [SerializeField] private TransportOption option = TransportOption.Standard;

        [SerializeField] private Button button;
        [SerializeField] private TMP_Text nameText;     // "Express (secured)"
        [SerializeField] private TMP_Text costText;     // "120 €"
        [SerializeField] private TMP_Text durationText; // "2 days"
        [SerializeField] private TMP_Text riskText;     // "No damage risk" / "3% damage risk"

        [Header("Selection")]
        [Tooltip("Shown only while this option is selected (e.g. an outline or check mark). Optional.")]
        [SerializeField] private GameObject selectedIndicator;
        [Tooltip("Tinted with the colors below depending on selection. Optional — defaults to the Image on this object.")]
        [SerializeField] private Image background;
        [SerializeField] private Color selectedColor = new Color32(0xC9, 0x9A, 0x3C, 0xFF);
        [SerializeField] private Color normalColor = new Color32(0x2B, 0x2B, 0x2B, 0xFF);

        public TransportOption Option => option;

        /// <summary>Raised when the player clicks this option.</summary>
        public event Action<TransportOption> Clicked;

        private void Awake()
        {
            if (button == null) button = GetComponent<Button>();
            if (background == null) background = GetComponent<Image>();

            if (button != null)
                button.onClick.AddListener(() => Clicked?.Invoke(option));
        }

        public void Show(TransportQuote quote, float damageChance)
        {
            if (quote == null)
            {
                gameObject.SetActive(false);
                return;
            }

            gameObject.SetActive(true);
            SetText(nameText, option.ToDisplayString());
            SetText(costText, UIFormat.Money(quote.Cost));
            SetText(durationText, UIFormat.Days(quote.DurationDays));
            SetText(riskText, damageChance > 0f
                ? $"{UIFormat.Percent(damageChance)} damage risk"
                : "No damage risk");
        }

        public void SetSelected(bool selected)
        {
            if (selectedIndicator != null)
                selectedIndicator.SetActive(selected);

            if (background != null)
                background.color = selected ? selectedColor : normalColor;
        }

        private static void SetText(TMP_Text target, string value)
        {
            if (target != null)
                target.text = value;
        }
    }
}
