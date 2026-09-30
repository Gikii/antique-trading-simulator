using System.Collections.Generic;
using System.Linq;
using System.Text;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Events;
using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.News;
using AntiqueTradingSimulator.UI.Charts;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    public class InventoryDetailsUI : MonoBehaviour
    {
        private const int TrendWindowDays = 30;
        private const int RelatedNewsWindowDays = 7;
        private const int MaxRelatedNews = 2;

        // Demand/supply this far above or below their baseline count as "high"/"low".
        private const float HighLevelRatio = 1.15f;
        private const float LowLevelRatio = 0.85f;

        [Header("Dependencies")]
        [SerializeField] private PlayerTrader playerTrader;
        [SerializeField] private EconomyManager economyManager;
        [SerializeField] private TimeManager timeManager;
        [SerializeField] private NewsManager newsManager;

        [Header("Header")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private Button closeButton;

        [Header("Main Image")]
        [SerializeField] private Image antiqueImage;

        [Header("Antique Info")]
        [SerializeField] private TMP_Text categoryText;
        [SerializeField] private TMP_Text periodText;
        [SerializeField] private TMP_Text originText;
        [SerializeField] private TMP_Text ownerText;
        [SerializeField] private TMP_Text itemIdText;

        [Header("Condition")]
        [SerializeField] private TMP_Text conditionText;
        [SerializeField] private TMP_Text rarityText;
        [SerializeField] private TMP_Text historyText;

        [Header("Value (legacy single fields, optional)")]
        [SerializeField] private TMP_Text currentValueText;
        [SerializeField] private TMP_Text baseValueText;

        [Header("Value Trend Card")]
        [SerializeField] private TMP_Text trendHeaderText;
        [SerializeField] private TMP_Text trendValueText;
        [SerializeField] private TMP_Text trendChangeText;
        [SerializeField] private UILineChart trendChart;
        [SerializeField] private TMP_Text trendRangeText;

        [Header("Investment Card")]
        [SerializeField] private TMP_Text investmentHeaderText;
        [SerializeField] private TMP_Text purchasePriceText;
        [SerializeField] private TMP_Text purchaseDateText;
        [SerializeField] private TMP_Text costsText;
        [SerializeField] private TMP_Text investmentValueText;
        [SerializeField] private TMP_Text profitText;

        [Header("Value Factors Card")]
        [SerializeField] private TMP_Text factorsHeaderText;
        [SerializeField] private TMP_Text demandText;
        [SerializeField] private TMP_Text supplyText;
        [SerializeField] private TMP_Text marketPressureText;
        [SerializeField] private TMP_Text availabilityText;
        [SerializeField] private TMP_Text relatedNewsText;

        [Header("Actions")]
        [SerializeField] private Button listOnMarketButton;
        [SerializeField] private TMP_Text listOnMarketLabel;
        [SerializeField] private ListOnMarketModalUI listOnMarketModal;
        [SerializeField] private Button placeOnAuctionButton;
        [SerializeField] private Button sellNowButton;
        [SerializeField] private TMP_Text sellNowLabel;
        [SerializeField] private SellNowModalUI sellNowModal;

        private Antique _currentAntique;
        private InventoryView _inventoryView;

        private Market.Market CurrentMarket => economyManager != null ? economyManager.Market : null;
        private int CurrentDay => timeManager != null ? timeManager.CurrentDay : 0;

        private void Awake()
        {
            if (playerTrader == null)
                playerTrader = FindFirstObjectByType<PlayerTrader>();

            if (economyManager == null)
                economyManager = FindFirstObjectByType<EconomyManager>();

            if (timeManager == null)
                timeManager = FindFirstObjectByType<TimeManager>();

            if (newsManager == null)
                newsManager = FindFirstObjectByType<NewsManager>();

            if (closeButton != null)
                closeButton.onClick.AddListener(Close);

            if (sellNowButton != null)
                sellNowButton.onClick.AddListener(OpenSellNowModal);

            if (sellNowModal == null)
                sellNowModal = FindFirstObjectByType<SellNowModalUI>(FindObjectsInactive.Include);

            // The modal starts inactive, so it has to be searched for including inactive objects.
            if (listOnMarketModal == null)
                listOnMarketModal = FindFirstObjectByType<ListOnMarketModalUI>(FindObjectsInactive.Include);

            if (listOnMarketButton != null)
                listOnMarketButton.onClick.AddListener(ToggleMarketListing);

            // Auctions don't exist yet — keep the button visible so the layout is final,
            // but not clickable.
            if (placeOnAuctionButton != null)
                placeOnAuctionButton.interactable = false;
        }

        public void Setup(InventoryView inventoryView)
        {
            _inventoryView = inventoryView;
        }

        public void Show(Antique antique)
        {
            if (antique == null)
                return;

            _currentAntique = antique;

            SetText(titleText, antique.Name);
            SetText(categoryText, antique.Category);
            SetText(periodText, antique.Century.ToDisplayString());
            SetText(originText, antique.Country.ToDisplayString());
            SetText(conditionText, $"{UIFormat.ConditionLabel(antique.Condition)} ({UIFormat.Percent(antique.Condition)})");

            SetText(ownerText, $"Owner: {OwnerLabel(antique)}");
            SetText(itemIdText, $"Item ID: {ShortId(antique.Id)}");
            SetText(rarityText, antique.IsLimitedEdition
                ? $"Rarity: limited edition {antique.RarityLabel}"
                : "Rarity: regular item");
            SetText(historyText, string.IsNullOrEmpty(antique.History)
                ? "History: no recorded provenance"
                : $"History: {antique.History}");

            SetText(currentValueText, UIFormat.Money(antique.CurrentPrice));
            SetText(baseValueText, UIFormat.Money(antique.BasePrice));

            RefreshTrendCard(antique);
            RefreshInvestmentCard(antique);
            RefreshFactorsCard(antique);
            RefreshActions(antique);

            gameObject.SetActive(true);
        }

        // ------------------------------------------------------------------
        // Cards
        // ------------------------------------------------------------------

        private void RefreshTrendCard(Antique antique)
        {
            SetText(trendHeaderText, "Value & trend");
            SetText(trendValueText, UIFormat.Money(antique.CurrentPrice));

            var market = CurrentMarket;
            List<float> history = CollectionAnalytics.ItemValueHistory(antique, market, TrendWindowDays);
            history.Add(antique.CurrentPrice);

            float change = CollectionAnalytics.MarketValueChange(antique, market, TrendWindowDays);
            float past = antique.CurrentPrice - change;
            string changeText = past > 0f
                ? $"{UIFormat.SignedPercent(change / past)} {UIFormat.TrendArrow(change)}"
                : "—";
            SetText(trendChangeText, $"{UIFormat.ColorBySign(changeText, change)} <size=75%>({TrendWindowDays} days)</size>");

            if (trendChart != null)
                trendChart.SetValues(history.Count > 1 ? history : null);

            string range = history.Count > 1
                ? $"{UIFormat.Money(history.Min())} – {UIFormat.Money(history.Max())}"
                : "—";
            SetText(trendRangeText, $"Base price: {UIFormat.Money(antique.BasePrice)}\n{TrendWindowDays}-day range: {range}");
        }

        private void RefreshInvestmentCard(Antique antique)
        {
            SetText(investmentHeaderText, "Investment");

            if (!antique.HasPurchaseRecord)
            {
                SetText(purchasePriceText, "Purchase price: unknown");
                SetText(purchaseDateText, "Acquired: unknown");
            }
            else if (antique.PurchasePrice <= 0f)
            {
                SetText(purchasePriceText, "Purchase price: free (event reward)");
                SetText(purchaseDateText, $"Acquired: {FormatDay(antique.PurchasedOnDay)}");
            }
            else
            {
                SetText(purchasePriceText, $"Purchase price: {UIFormat.Money(antique.PurchasePrice)}");
                SetText(purchaseDateText, $"Purchased: {FormatDay(antique.PurchasedOnDay)}");
            }

            // Transport/renovation costs will be added once those systems exist.
            SetText(costsText, UIFormat.Colorize("Transport & renovation: —", UIFormat.MutedColor));
            SetText(investmentValueText, $"Current value: {UIFormat.Money(antique.CurrentPrice)}");

            float profit = CollectionAnalytics.Profit(antique);
            float? roi = CollectionAnalytics.ReturnOnInvestment(antique.CurrentPrice, antique.PurchasePrice);
            string profitLine = UIFormat.SignedMoney(profit) + (roi.HasValue ? $" ({UIFormat.SignedPercent(roi.Value)})" : "");

            if (antique.HasPurchaseRecord && antique.PurchasedOnDay < CurrentDay)
            {
                int daysHeld = CurrentDay - antique.PurchasedOnDay;
                profitLine += UIFormat.Colorize($" · held {daysHeld} {(daysHeld == 1 ? "day" : "days")}", UIFormat.MutedColor);
            }

            SetText(profitText, $"Profit / loss: {UIFormat.ColorBySign(profitLine, profit)}");
        }

        private void RefreshFactorsCard(Antique antique)
        {
            SetText(factorsHeaderText, "Value factors");

            var market = CurrentMarket;
            var typeState = market != null ? market.GetTypeState(antique.DefinitionId) : null;

            if (typeState == null)
            {
                SetText(demandText, "Demand: —");
                SetText(supplyText, "Supply: —");
                SetText(marketPressureText, "Market price level: —");
                SetText(availabilityText, "On the market: —");
            }
            else
            {
                float demand = typeState.Demand + typeState.TempDemandMod;
                float supply = typeState.Supply + typeState.TempSupplyMod;

                // For an owner, high demand is good news and high supply is bad news.
                SetText(demandText, $"Demand: {LevelLabel(demand, typeState.BaselineDemand, higherIsGood: true)}");
                SetText(supplyText, $"Supply: {LevelLabel(supply, typeState.BaselineSupply, higherIsGood: false)}");

                float referencePrice = PriceEngine.CalculateReferencePrice(antique.BasePrice, typeState);
                float priceLevel = antique.BasePrice > 0f ? referencePrice / antique.BasePrice : 1f;
                string levelText = $"×{priceLevel:0.00}";
                SetText(marketPressureText,
                    $"Market price level: {UIFormat.ColorBySign(levelText, priceLevel - 1f)} of base");

                int sameItem = market.GetListingsByDefinition(antique.DefinitionId).Count;
                int sameCategory = market.GetByType(antique.Type).Count;
                SetText(availabilityText, $"On the market: {sameItem} identical, {sameCategory} in category");
            }

            SetText(relatedNewsText, BuildRelatedNews(antique));
        }

        private static string LevelLabel(float value, float baseline, bool higherIsGood)
        {
            float ratio = baseline > 0f ? value / baseline : 1f;

            if (ratio >= HighLevelRatio)
                return UIFormat.ColorBySign("High ↑", higherIsGood ? 1f : -1f);

            if (ratio <= LowLevelRatio)
                return UIFormat.ColorBySign("Low ↓", higherIsGood ? -1f : 1f);

            return "Normal";
        }

        /// <summary>
        /// Recent news the player could actually have received (their access level) that
        /// mentions this antique's type, country or century. Shows what the player knows,
        /// not hidden event state, so it doesn't leak information.
        /// </summary>
        private string BuildRelatedNews(Antique antique)
        {
            if (newsManager == null)
                return "Related news: —";

            InfoAccessLevel access = playerTrader != null ? playerTrader.AccessLevel : InfoAccessLevel.LocalPress;
            int fromDay = CurrentDay - RelatedNewsWindowDays;

            List<NewsItem> related = newsManager.PublishedNews
                .Where(n => n.DayPublished >= fromDay && n.RequiredAccessLevel <= access)
                .Where(n => n.NewsData != null && n.NewsData.Any(d => Concerns(d, antique)))
                .OrderByDescending(n => n.DayPublished)
                .Take(MaxRelatedNews)
                .ToList();

            if (related.Count == 0)
                return "Related news: none recently";

            var sb = new StringBuilder("Related news:");
            foreach (var news in related)
            {
                string tag = UIFormat.Colorize($"[{NewsPresentation.TypeLabel(news.Type)}]", NewsPresentation.TypeColor(news.Type));
                sb.Append('\n').Append(tag).Append(' ').Append(NewsPresentation.GetTitle(news));
            }

            return sb.ToString();
        }

        private static bool Concerns(NewsEventData data, Antique antique)
        {
            return data.targetScope switch
            {
                EventEffect.TargetScope.AntiqueType => data.AntiqueType == antique.Type,
                EventEffect.TargetScope.Country => data.Country == antique.Country,
                EventEffect.TargetScope.Century => data.Century == antique.Century,
                _ => false
            };
        }

        // ------------------------------------------------------------------
        // Actions
        // ------------------------------------------------------------------

        private void RefreshActions(Antique antique)
        {
            bool reserved = antique.IsReservedForContract;
            bool listed = antique.IsListedForSale;

            // List on market <-> Cancel listing
            if (listOnMarketButton != null)
                listOnMarketButton.interactable = listed || (!reserved && listOnMarketModal != null);

            if (listOnMarketLabel != null)
            {
                if (listed)
                {
                    string since = antique.MarketListedOnDay >= 0 ? $" since day {antique.MarketListedOnDay}" : "";
                    listOnMarketLabel.text = $"Cancel listing\n<size=70%>Listed for {UIFormat.Money(antique.AskingPrice)}{since}</size>";
                }
                else
                {
                    listOnMarketLabel.text = reserved
                        ? "List on market\n<size=70%>Reserved for a contract</size>"
                        : "List on market";
                }
            }

            if (sellNowButton == null)
                return;

            sellNowButton.interactable = !reserved && !listed;

            if (sellNowLabel == null)
                return;

            if (reserved)
            {
                sellNowLabel.text = "Sell now\n<size=70%>Reserved for a contract</size>";
                return;
            }

            if (listed)
            {
                sellNowLabel.text = "Sell now\n<size=70%>Cancel the listing first</size>";
                return;
            }

            float offer = CurrentMarket != null
                ? CurrentMarket.EstimateInstantSalePrice(antique)
                : antique.CurrentPrice;

            sellNowLabel.text = $"Sell now\n<size=70%>Instant offer: {UIFormat.Money(offer)}</size>";
        }

        private void ToggleMarketListing()
        {
            if (_currentAntique == null)
                return;

            if (_currentAntique.IsListedForSale)
            {
                if (playerTrader == null || !playerTrader.CancelMarketListing(_currentAntique.Id))
                {
                    Debug.LogWarning($"InventoryDetailsUI: failed to cancel the listing of {_currentAntique.Id}.");
                    return;
                }

                Show(_currentAntique);
                return;
            }

            if (listOnMarketModal != null)
                listOnMarketModal.Open(_currentAntique);
        }

        private void Close()
        {
            _currentAntique = null;

            if (_inventoryView != null)
                _inventoryView.ShowCollectionSummary();
        }

        // The sale happens in the modal after confirmation. When it goes through,
        // InventoryView notices the antique left the collection and shows the summary.
        private void OpenSellNowModal()
        {
            if (_currentAntique != null && sellNowModal != null)
                sellNowModal.Open(_currentAntique);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private string FormatDay(int day)
        {
            if (timeManager == null)
                return $"day {day}";

            return $"{TimeManager.FormatLong(timeManager.DayToDate(day))} (day {day})";
        }

        private static string OwnerLabel(Antique antique)
        {
            if (string.IsNullOrEmpty(antique.OwnerId))
                return "Unknown";

            return antique.OwnerId == Antique.PlayerOwnerId ? "You" : antique.OwnerId;
        }

        // Full GUIDs are noise in the UI; the first 8 characters are enough to tell items apart.
        private static string ShortId(string id) =>
            string.IsNullOrEmpty(id) ? "—" : id.Length > 8 ? id.Substring(0, 8).ToUpperInvariant() : id.ToUpperInvariant();

        private static void SetText(TMP_Text target, string value)
        {
            if (target != null)
                target.text = value;
        }
    }
}
