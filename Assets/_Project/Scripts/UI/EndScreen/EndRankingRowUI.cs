using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>A row of the final ranking: place, name, score and the figures the score came from.</summary>
    public class EndRankingRowUI : MonoBehaviour
    {
        [SerializeField] private Image background;
        [SerializeField] private TMP_Text placeText;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text scoreText;
        [SerializeField] private TMP_Text netWorthText;
        [SerializeField] private TMP_Text collectionText;
        [SerializeField] private TMP_Text reputationText;
        [SerializeField] private TMP_Text contractsText;

        [SerializeField] private Color playerHighlight = new Color(0.79f, 0.6f, 0.24f, 0.18f);

        public void Set(int place, string name, float score, float netWorth, float collection, int reputation,
                        int contracts, bool isPlayer)
        {
            SetText(placeText, place.ToString());
            SetText(nameText, isPlayer ? $"<b>{name}</b>" : name);
            SetText(scoreText, Mathf.RoundToInt(score).ToString());
            SetText(netWorthText, UIFormat.Money(netWorth));
            SetText(collectionText, UIFormat.Money(collection));
            SetText(reputationText, reputation.ToString());
            SetText(contractsText, contracts.ToString());

            if (background != null)
            {
                background.enabled = isPlayer;
                background.color = playerHighlight;
            }
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null) text.text = value;
        }
    }
}
