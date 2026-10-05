using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>One tier on a TierBarUI: a bar segment with the tier's name and value range below.</summary>
    public class TierSegmentUI : MonoBehaviour
    {
        [SerializeField] private Image background;
        [SerializeField] private ProgressBarUI bar;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text rangeText;

        [SerializeField] private Color normalBackground = new Color(0f, 0f, 0f, 0f);
        [SerializeField] private Color currentBackground = new Color(1f, 1f, 1f, 0.12f);

        public void Set(string tierName, string range, float fill01, Color color, bool isCurrent)
        {
            if (background != null) background.color = isCurrent ? currentBackground : normalBackground;
            if (bar != null)
            {
                bar.SetValue(fill01);
                bar.SetColor(color);
            }

            if (nameText != null)
            {
                nameText.text = tierName;
                nameText.color = isCurrent ? Color.white : UIFormat.MutedColor;
            }

            if (rangeText != null)
                rangeText.text = isCurrent ? UIFormat.Colorize(range, color) : range;
        }
    }
}
