using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.Agents;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// UI representation of a single market listing. Displays its name/category/price
    /// and wires the Buy button to the player's TraderAgent. Also handles a brief
    /// highlight animation when it's first created, to draw attention to new offers.
    /// </summary>
    public class MarketListingUI : MonoBehaviour
    {
        [SerializeField] private Image background;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text categoryText;
        [SerializeField] private TMP_Text priceText;
        [SerializeField] private Button buyButton;

        [Header("New listing highlight")]
        [SerializeField] private Color newListingColor = new Color(1f, 0.92f, 0.55f);
        [SerializeField] private float highlightDuration = 5f;

        private string _listingId;
        private PlayerTrader _playerTrader;
        private MarketUI _marketUI;
        private Color _normalColor;
        private Coroutine _highlightRoutine;

        void Awake()
        {
            if (background != null)
                _normalColor = background.color;
        }

        public void Setup(Antique listing, PlayerTrader playerTrader, MarketUI marketUI)
        {
            _listingId = listing.Id;
            _playerTrader = playerTrader;
            _marketUI = marketUI;

            nameText.text = listing.Name;
            categoryText.text = listing.Category;
            priceText.text = $"{listing.CurrentPrice:F2} zł";

            buyButton.onClick.RemoveAllListeners();
            buyButton.onClick.AddListener(OnBuyClicked);
        }

        /// <summary>
        /// Refreshes just the displayed price, without re-running Setup or restarting
        /// any highlight animation — used for listings that already existed before this refresh.
        /// </summary>
        public void UpdatePrice(Antique listing)
        {
            priceText.text = $"{listing.CurrentPrice:F2} zł";
        }

        /// <summary>
        /// Briefly changes the row's background color to draw attention to it,
        /// then fades back to normal after highlightDuration seconds.
        /// </summary>
        public void PlayNewListingHighlight()
        {
            if (background == null) return;

            if (_highlightRoutine != null)
                StopCoroutine(_highlightRoutine);

            _highlightRoutine = StartCoroutine(HighlightRoutine());
        }

        private IEnumerator HighlightRoutine()
        {
            background.color = newListingColor;
            yield return new WaitForSeconds(highlightDuration);
            background.color = _normalColor;
            _highlightRoutine = null;
        }

        private void OnBuyClicked()
        {
            bool success = _playerTrader.BuyListing(_listingId);

            if (success)
                _marketUI.RefreshListings();
        }
    }
}