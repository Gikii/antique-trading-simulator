using AntiqueTradingSimulator.Market;
using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// One lot in the Auctions view list: thumbnail, "Lot 2 · Name", a second line and a short
    /// detail on the right (the estimate in the catalog, the hammer price in the results).
    /// </summary>
    public class AuctionLotRowUI : MonoBehaviour
    {
        [SerializeField] private Button button;
        [SerializeField] private Image background;
        [SerializeField] private Image thumbnail;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text subtitleText;
        [SerializeField] private TMP_Text detailText;

        [Header("Colours")]
        [SerializeField] private Color normalColor = new Color32(0x17, 0x13, 0x0C, 0xFF);
        [SerializeField] private Color selectedColor = new Color32(0x4A, 0x35, 0x18, 0xFF);

        private Action _onClick;

        private void Awake()
        {
            if (button != null) button.onClick.AddListener(() => _onClick?.Invoke());
        }

        public void Setup(Antique antique, string title, string subtitle, string detail, Action onClick)
        {
            _onClick = onClick;

            if (thumbnail != null) thumbnail.ShowAntique(antique);
            if (titleText != null) titleText.text = title;
            if (subtitleText != null) subtitleText.text = subtitle;
            if (detailText != null) detailText.text = detail;

            SetSelected(false);
        }

        public void SetSelected(bool selected)
        {
            if (background != null) background.color = selected ? selectedColor : normalColor;
        }
    }
}
