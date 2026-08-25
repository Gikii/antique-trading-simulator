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

        [Header("New listing badge")]
        [SerializeField] private GameObject newBadge;

        private Antique _listing;
        private MarketView _marketView;

       public void Setup(Antique listing, MarketView marketView, int currentDay)
        {
            _listing = listing;
            _marketView = marketView;

            nameText.text = listing.Name;
            descriptionText.text = $"{listing.Category} — condition {listing.State:P0}";
            priceText.text = $"{listing.CurrentPrice:F2} $";

            if (newBadge != null)
                newBadge.SetActive(listing.MarketListedOnDay == currentDay);

            showDetailsButton.onClick.RemoveAllListeners();
            showDetailsButton.onClick.AddListener(() => _marketView.ShowDetails(_listing));
        }

        public void UpdatePrice(Antique listing)
        {
            _listing = listing;
            priceText.text = $"{listing.CurrentPrice:F2} zł";
        }
    }
}