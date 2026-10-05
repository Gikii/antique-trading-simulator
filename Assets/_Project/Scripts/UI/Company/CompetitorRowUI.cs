using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>A row of the competition ranking: rank, trader name and the sorted value.</summary>
    public class CompetitorRowUI : MonoBehaviour
    {
        [SerializeField] private Image background;
        [SerializeField] private TMP_Text rankText;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text valueText;

        [SerializeField] private Color normalColor = new Color(1f, 1f, 1f, 0.0941f);
        [SerializeField] private Color playerColor = new Color32(0xC9, 0x9A, 0x3C, 0x90);

        public void Set(int rank, string traderName, string value, bool isPlayer)
        {
            if (rankText != null) rankText.text = rank.ToString();
            if (nameText != null) nameText.text = traderName;
            if (valueText != null) valueText.text = value;
            if (background != null) background.color = isPlayer ? playerColor : normalColor;
        }
    }
}
