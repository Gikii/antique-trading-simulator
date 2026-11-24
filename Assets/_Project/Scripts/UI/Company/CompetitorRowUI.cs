using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>A row of the competition ranking: rank, avatar, trader name and the sorted value.</summary>
    public class CompetitorRowUI : MonoBehaviour
    {
        [SerializeField] private Image background;
        [SerializeField] private TMP_Text rankText;
        [Tooltip("Optional. Emblem of the player or of the NPC's profile, from UIIllustrations.")]
        [SerializeField] private Image avatar;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text valueText;

        [SerializeField] private Color normalColor = new Color(1f, 1f, 1f, 0.0941f);
        [SerializeField] private Color playerColor = new Color32(0xC9, 0x9A, 0x3C, 0x90);

        public void Set(int rank, string traderName, string value, bool isPlayer, Sprite emblem = null)
        {
            // Rows are reused when the ranking re-sorts, so an empty emblem must clear the old one.
            UIIllustrations.Show(avatar, emblem);

            if (rankText != null) rankText.text = rank.ToString();
            if (nameText != null) nameText.text = traderName;
            if (valueText != null) valueText.text = value;
            if (background != null) background.color = isPlayer ? playerColor : normalColor;
        }
    }
}
