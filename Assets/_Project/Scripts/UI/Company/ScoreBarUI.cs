using TMPro;
using UnityEngine;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>One row of "Congress readiness": criterion name, 0..100 bar and "68 / 100".</summary>
    public class ScoreBarUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text labelText;
        [SerializeField] private ProgressBarUI bar;
        [SerializeField] private TMP_Text valueText;

        private TooltipTrigger _tooltip;

        public void Set(string label, float score, Color color)
        {
            if (labelText != null) labelText.text = label;
            if (valueText != null) valueText.text = $"{Mathf.RoundToInt(score)} / 100";
            if (bar != null)
            {
                bar.SetValue(score / 100f);
                bar.SetColor(color);
            }
        }

        public void SetTooltip(string text)
        {
            if (_tooltip == null) _tooltip = TooltipTrigger.On(this);
            if (_tooltip != null) _tooltip.Text = text;
        }
    }
}
