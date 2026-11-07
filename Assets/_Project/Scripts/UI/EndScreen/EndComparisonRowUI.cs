using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>A row comparing one figure: the player, the best rival and the rivals' average.</summary>
    public class EndComparisonRowUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text labelText;
        [SerializeField] private TMP_Text playerText;
        [SerializeField] private TMP_Text bestRivalText;
        [SerializeField] private TMP_Text averageText;

        [SerializeField] private Color aheadColor = new Color32(0x7F, 0xB0, 0x69, 0xFF);
        [SerializeField] private Color behindColor = new Color32(0xE0, 0x50, 0x40, 0xFF);
        [SerializeField] private Color neutralColor = new Color32(0xEA, 0xDF, 0xC8, 0xFF);

        /// <param name="playerAhead">True/false colours the player's value against the best rival; null leaves it plain.</param>
        public void Set(string label, string player, string bestRival, string average, bool? playerAhead)
        {
            if (labelText != null) labelText.text = label;
            if (bestRivalText != null) bestRivalText.text = bestRival;
            if (averageText != null) averageText.text = average;

            if (playerText != null)
            {
                playerText.text = player;
                playerText.color = playerAhead == null ? neutralColor : playerAhead.Value ? aheadColor : behindColor;
            }
        }
    }
}
