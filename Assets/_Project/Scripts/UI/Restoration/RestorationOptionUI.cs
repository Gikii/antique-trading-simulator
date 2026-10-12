using System;
using AntiqueTradingSimulator.Restoration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// One workshop card inside RestorationModalUI: name, the condition it can reach, fee, time
    /// and risk. RestorationModalUI clones one per RestorationOption from an inactive template,
    /// so adding a workshop to RestorationSettings needs no scene change.
    /// A workshop that can't take the job stays clickable (so its numbers can be compared) but
    /// is greyed out, with the reason in its tooltip.
    /// </summary>
    public class RestorationOptionUI : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private TMP_Text nameText;     // "Certified Conservator"
        [SerializeField] private TMP_Text resultText;   // "→ Good 85%"
        [SerializeField] private TMP_Text costText;     // "2 025 €"
        [SerializeField] private TMP_Text durationText; // "6 days"
        [SerializeField] private TMP_Text riskText;     // "3% risk of damage" / "Requires 500 reputation"

        [Header("Selection")]
        [Tooltip("Defaults to the Image on this object.")]
        [SerializeField] private Image background;
        [SerializeField] private Color selectedColor = new Color32(0xC9, 0x9A, 0x3C, 0xFF);
        [SerializeField] private Color normalColor = new Color32(0x45, 0x45, 0x45, 0xFF);
        [SerializeField] private Color unavailableColor = new Color32(0x30, 0x30, 0x30, 0xFF);

        private TooltipTrigger _tooltip;
        private bool _available = true;

        public string OptionId { get; private set; }

        /// <summary>Raised with OptionId when the card is clicked.</summary>
        public event Action<string> Clicked;

        private void Awake()
        {
            if (button == null) button = GetComponent<Button>();
            if (background == null) background = GetComponent<Image>();

            if (button != null)
                button.onClick.AddListener(() => Clicked?.Invoke(OptionId));

            _tooltip = TooltipTrigger.On(this);
        }

        public void Show(RestorationQuote quote)
        {
            if (quote == null)
            {
                gameObject.SetActive(false);
                return;
            }

            gameObject.SetActive(true);
            if (_tooltip == null) _tooltip = TooltipTrigger.On(this);

            OptionId = quote.Option.Id;
            _available = quote.CanStart;

            bool improves = quote.TargetCondition > quote.StartCondition;
            Color muted = UIFormat.MutedColor;

            SetText(nameText, quote.Option.DisplayName);
            SetText(resultText, improves
                ? $"→ {UIFormat.ConditionLabel(quote.TargetCondition)} {UIFormat.Percent(quote.TargetCondition)}"
                : UIFormat.Colorize($"Max {UIFormat.Percent(quote.TargetCondition)}", muted));
            SetText(costText, improves ? UIFormat.Money(quote.Cost) : "—");
            SetText(durationText, UIFormat.Days(quote.DurationDays));
            SetText(riskText, RiskLabel(quote));

            if (_tooltip != null)
            {
                string description = string.IsNullOrWhiteSpace(quote.Option.Description) ? "" : quote.Option.Description.Trim();
                _tooltip.Text = quote.CanStart
                    ? description
                    : description + (description.Length > 0 ? "\n" : "") + UIFormat.Colorize(quote.BlockReason, UIFormat.NegativeColor);
            }
        }

        private static string RiskLabel(RestorationQuote quote)
        {
            if (quote.Option.RequiredReputation > 0 && !quote.CanStart && quote.BlockReason.StartsWith("Requires"))
                return UIFormat.Colorize($"Requires {UIFormat.Number(quote.Option.RequiredReputation)} rep.", UIFormat.NegativeColor);

            return quote.FailureChance > 0f
                ? $"{UIFormat.Percent(quote.FailureChance)} risk of damage"
                : "No risk of damage";
        }

        public void SetSelected(bool selected)
        {
            if (background != null)
                background.color = selected ? selectedColor : _available ? normalColor : unavailableColor;
        }

        private static void SetText(TMP_Text target, string value)
        {
            if (target != null)
                target.text = value;
        }
    }
}
