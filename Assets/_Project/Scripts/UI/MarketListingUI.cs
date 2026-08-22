using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AntiqueTradingSimulator.Market;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// A single antique card in the market grid. Shows an image placeholder, name,
    /// a short description and price. Buying happens in the detail panel now —
    /// this card only opens it via "Show details".
    /// </summary>
    public class MarketListingUI : MonoBehaviour
    {
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private TMP_Text priceText;
        [SerializeField] private Button showDetailsButton;

        [Header("New listing highlight")]
        [SerializeField] private Image background;
        [SerializeField] private Color newListingColor = new Color(1f, 0.92f, 0.55f);
        [SerializeField] private float highlightDuration = 5f;

        private Antique _listing;
        private MarketView _marketView;
        private Color _normalColor;
        private Coroutine _highlightRoutine;

        void Awake()
        {
            if (background != null)
                _normalColor = background.color;
        }

        public void Setup(Antique listing, MarketView marketView)
        {
            _listing = listing;
            _marketView = marketView;

            nameText.text = listing.Name;
            descriptionText.text = $"{listing.Category} — condition {listing.State:P0}";
            priceText.text = $"{listing.CurrentPrice:F2} zł";

            showDetailsButton.onClick.RemoveAllListeners();
            showDetailsButton.onClick.AddListener(() => _marketView.ShowDetails(_listing));
        }

        public void UpdatePrice(Antique listing)
        {
            _listing = listing;
            priceText.text = $"{listing.CurrentPrice:F2} zł";
        }

        public void PlayNewListingHighlight()
        {
            if (background == null) return;
            if (_highlightRoutine != null) StopCoroutine(_highlightRoutine);
            _highlightRoutine = StartCoroutine(HighlightRoutine());
        }

        private IEnumerator HighlightRoutine()
        {
            background.color = newListingColor;
            yield return new WaitForSeconds(highlightDuration);
            background.color = _normalColor;
            _highlightRoutine = null;
        }
    }
}