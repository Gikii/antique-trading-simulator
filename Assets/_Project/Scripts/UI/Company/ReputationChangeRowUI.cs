using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>One line of a reputation/credibility history: date, change, marker and reason.</summary>
    public class ReputationChangeRowUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text dateText;
        [SerializeField] private TMP_Text deltaText;
        [Tooltip("Small square standing in for the mockup's icon, coloured by the sign of the change.")]
        [SerializeField] private Image marker;
        [SerializeField] private TMP_Text reasonText;

        public void Set(string date, string delta, float sign, string reason)
        {
            if (dateText != null) dateText.text = date;
            if (deltaText != null) deltaText.text = UIFormat.ColorBySign(delta, sign);
            if (marker != null) marker.color = sign >= 0f ? UIFormat.PositiveColor : UIFormat.NegativeColor;
            if (reasonText != null) reasonText.text = reason;
        }
    }
}
