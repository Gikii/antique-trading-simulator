using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>One line of the chart legend: a colour swatch and a name.</summary>
    public class EndLegendItemUI : MonoBehaviour
    {
        [SerializeField] private Image swatch;
        [SerializeField] private TMP_Text nameText;

        public void Set(Color color, string name, bool emphasise)
        {
            if (swatch != null) swatch.color = color;
            if (nameText != null) nameText.text = emphasise ? $"<b>{name}</b>" : name;
        }
    }
}
