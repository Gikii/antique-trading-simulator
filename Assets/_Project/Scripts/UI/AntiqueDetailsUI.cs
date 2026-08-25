using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AntiqueTradingSimulator.Market;

namespace AntiqueTradingSimulator.UI
{
    public class AntiqueDetailsUI : MonoBehaviour
    {
        [Header("Main")]
        [SerializeField] private Image antiqueImage;

        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text categoryText;

        [Header("Condition")]
        [SerializeField] private TMP_Text conditionText;
        [SerializeField] private TMP_Text qualityText;

        [Header("Price")]
        [SerializeField] private TMP_Text currentPriceText;
        [SerializeField] private TMP_Text basePriceText;

        [Header("Debug / additional")]
        [SerializeField] private TMP_Text listingIdText;

        [Header("Buttons")]
        [SerializeField] private Button closeButton;

        private Antique _currentAntique;

        private void Awake()
        {
            if (closeButton != null)
                closeButton.onClick.AddListener(Hide);
            gameObject.SetActive(false);
        }

        public void Show(Antique antique)
        {
            if (antique == null)
                return;

            _currentAntique = antique;

            nameText.text = antique.Name;
            categoryText.text = antique.Category;

            conditionText.text =
                $"Stan: {antique.State:P0}";

            qualityText.text =
                $"Jakość: {antique.Quality:P0}";

            currentPriceText.text =
                $"{antique.CurrentPrice:F2} zł";

            basePriceText.text =
                $"Cena bazowa: {antique.BasePrice:F2} zł";

            if (listingIdText != null)
                listingIdText.text = $"ID: {antique.Id}";

            gameObject.SetActive(true);
        }

        public void Hide()
        {
            _currentAntique = null;
            gameObject.SetActive(false);
        }
    }
}