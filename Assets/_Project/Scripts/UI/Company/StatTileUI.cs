using TMPro;
using UnityEngine;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>A metric tile: label, big value and a small change line (e.g. "↑ +12% (7 days)").</summary>
    public class StatTileUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text labelText;
        [SerializeField] private TMP_Text valueText;
        [SerializeField] private TMP_Text changeText;

        private TooltipTrigger _tooltip;

        public void SetLabel(string label)
        {
            if (labelText != null) labelText.text = label;
        }

        public void Set(string value, string change)
        {
            if (valueText != null) valueText.text = value;
            if (changeText != null) changeText.text = change;
        }

        public void SetTooltip(string text)
        {
            if (_tooltip == null) _tooltip = TooltipTrigger.On(this);
            if (_tooltip != null) _tooltip.Text = text;
        }
    }
}
