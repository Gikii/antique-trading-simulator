using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AntiqueTradingSimulator.Logistics;
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
        [SerializeField] private TMP_Text centuryText;
        [SerializeField] private TMP_Text conditionText;
        [SerializeField] private TMP_Text sellerText;
        [SerializeField] private TMP_Text priceText;
        [Tooltip("Where the item ships from, e.g. \"International\". Optional.")]
        [SerializeField] private TMP_Text shippingText;
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
            centuryText.text = $"Century: {listing.Century.ToDisplayString()}";
            conditionText.text = $"Condition: {listing.Condition:P0}";
            sellerText.text = SellerLabel(listing);
            priceText.text = $"{listing.SalePrice:F2} €";

            if (shippingText != null)
                shippingText.text = $"Ships from: {listing.ShippingZone.ToDisplayString()}";

            if (newBadge != null)
                newBadge.SetActive(listing.MarketListedOnDay == currentDay);

            showDetailsButton.onClick.RemoveAllListeners();
            showDetailsButton.onClick.AddListener(() => _marketView.ShowDetails(_listing));
        }

        public void UpdatePrice(Antique listing)
        {
            _listing = listing;
            priceText.text = $"{listing.SalePrice:F2} €";
        }

        private static string SellerLabel(Antique listing)
        {
            if (string.IsNullOrEmpty(listing.OwnerId))
                return "Private seller";

            return listing.OwnerId == Antique.PlayerOwnerId ? "You (your listing)" : listing.OwnerId;
        }
    }
}