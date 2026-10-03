using TMPro;
using UnityEngine;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// One line in an InfoBar tab: a label on the left and a short detail on the right
    /// (e.g. "Chinese Vase" | "7 Oct · 2 days"). Shared by the Transports and Warehouse tabs.
    /// </summary>
    public class InfoBarRowUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text labelText;
        [SerializeField] private TMP_Text detailText;

        public void Setup(string label, string detail, string tooltip = null)
        {
            if (labelText != null) labelText.text = label;
            if (detailText != null) detailText.text = detail;

            // Only bother with a trigger when there's something to explain.
            var existing = GetComponent<TooltipTrigger>();
            if (!string.IsNullOrEmpty(tooltip))
                TooltipTrigger.On(this).Text = tooltip;
            else if (existing != null)
                existing.Text = "";
        }
    }
}
