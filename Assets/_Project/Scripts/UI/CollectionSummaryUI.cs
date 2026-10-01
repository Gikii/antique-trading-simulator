using System.Collections.Generic;
using System.Linq;
using System.Text;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.UI.Charts;
using TMPro;
using UnityEngine;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Right-hand panel of the collection screen shown when no antique is selected:
    /// headline numbers, value-over-time chart, category breakdown, where the items are
    /// and a few highlights. Purely a view — InventoryView decides when to refresh it.
    /// Every reference is optional, so the layout can be trimmed in the scene freely.
    /// </summary>
    public class CollectionSummaryUI : MonoBehaviour
    {
        // How many categories get their own donut slice/legend row; the rest are "Other".
        private const int MaxLegendCategories = 5;
        private const int ChangeWindowDays = 7;
        private const int ChartWindowDays = 30;

        [Header("Header")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text subtitleText;

        [Header("Headline stats (label + value pairs)")]
        [SerializeField] private TMP_Text itemCountLabel;
        [SerializeField] private TMP_Text itemCountValue;
        [SerializeField] private TMP_Text marketValueLabel;
        [SerializeField] private TMP_Text marketValueValue;
        [SerializeField] private TMP_Text purchaseCostLabel;
        [SerializeField] private TMP_Text purchaseCostValue;
        [SerializeField] private TMP_Text profitLabel;
        [SerializeField] private TMP_Text profitValue;

        [Header("Value over time")]
        [SerializeField] private TMP_Text valueChartTitle;
        [SerializeField] private UILineChart valueChart;

        [Header("Category breakdown")]
        [SerializeField] private TMP_Text categoryTitle;
        [SerializeField] private UIDonutChart categoryChart;
        [SerializeField] private TMP_Text categoryCenterText;
        [SerializeField] private TMP_Text categoryLegendText;

        [Header("Collection status")]
        [SerializeField] private TMP_Text statusTitle;
        [SerializeField] private TMP_Text inWarehouseText;
        [SerializeField] private TMP_Text inTransportText;
        [SerializeField] private TMP_Text inRenovationText;
        [SerializeField] private TMP_Text onAuctionText;

        [Header("Highlights")]
        [SerializeField] private TMP_Text mostValuableText;
        [SerializeField] private TMP_Text limitedEditionsText;
        [SerializeField] private TMP_Text averageValueText;

        [Header("Footer")]
        [SerializeField] private TMP_Text valueChangeText;
        [SerializeField] private TMP_Text hintText;

        public void Refresh(IReadOnlyCollection<Antique> collection, TraderInventory inventory, Market.Market market)
        {
            collection ??= new List<Antique>();

            float totalValue = CollectionAnalytics.TotalValue(collection);
            float totalCost = CollectionAnalytics.TotalCost(collection);
            float profit = totalValue - totalCost;
            float? roi = CollectionAnalytics.ReturnOnInvestment(totalValue, totalCost);

            SetText(titleText, "Collection summary");
            SetText(subtitleText, collection.Count == 1 ? "1 antique" : $"{collection.Count} antiques");

            SetText(itemCountLabel, "Antiques");
            SetText(itemCountValue, collection.Count.ToString());

            SetText(marketValueLabel, "Market value");
            SetText(marketValueValue, UIFormat.Money(totalValue));

            SetText(purchaseCostLabel, "Purchase cost");
            SetText(purchaseCostValue, UIFormat.Money(totalCost));

            SetText(profitLabel, "Profit / loss");
            string profitLine = UIFormat.SignedMoney(profit);
            if (roi.HasValue)
                profitLine += $"  <size=65%>{UIFormat.SignedPercent(roi.Value)}</size>";
            SetText(profitValue, UIFormat.ColorBySign(profitLine, profit));

            RefreshValueChart(inventory);
            RefreshCategories(collection);
            RefreshStatus(collection, totalValue);
            RefreshHighlights(collection, totalValue);
            RefreshFooter(collection, market);
        }

        private void RefreshValueChart(TraderInventory inventory)
        {
            SetText(valueChartTitle, $"Collection value (last {ChartWindowDays} days)");

            if (valueChart == null)
                return;

            var values = new List<float>();
            if (inventory != null)
            {
                var history = inventory.ValueHistory;
                int from = Mathf.Max(0, history.Count - ChartWindowDays);
                for (int i = from; i < history.Count; i++)
                    values.Add(history[i].Price);

                // Always end on today's value, even before the first daily snapshot.
                values.Add(inventory.TotalHoldingsValue);
            }

            // A single point is just "today" — not a trend worth drawing.
            valueChart.SetValues(values.Count > 1 ? values : null);
        }

        private void RefreshCategories(IReadOnlyCollection<Antique> collection)
        {
            SetText(categoryTitle, "By category");
            SetText(categoryCenterText, $"{collection.Count}\n<size=60%>antiques</size>");

            var breakdown = CollectionAnalytics.CategoryBreakdown(collection);
            var segments = new List<UIDonutChart.Segment>();
            var legend = new StringBuilder();

            int shown = Mathf.Min(MaxLegendCategories, breakdown.Count);
            // If only one category would be folded into "Other", show it by name instead.
            if (breakdown.Count == MaxLegendCategories + 1)
                shown = breakdown.Count;

            for (int i = 0; i < shown; i++)
            {
                var share = breakdown[i];
                Color color = UIFormat.CategoryColorByRank(i);
                segments.Add(new UIDonutChart.Segment(share.Value, color));
                AppendLegendRow(legend, color, share.Type.ToDisplayString(), share.ValueShare, share.Count);
            }

            if (breakdown.Count > shown)
            {
                var rest = breakdown.Skip(shown).ToList();
                float value = rest.Sum(c => c.Value);
                float valueShare = rest.Sum(c => c.ValueShare);
                int count = rest.Sum(c => c.Count);

                segments.Add(new UIDonutChart.Segment(value, UIFormat.OtherCategoryColor));
                AppendLegendRow(legend, UIFormat.OtherCategoryColor, "Other", valueShare, count);
            }

            if (categoryChart != null)
                categoryChart.SetSegments(segments);

            SetText(categoryLegendText, breakdown.Count == 0 ? "No antiques yet" : legend.ToString().TrimEnd());
        }

        private static void AppendLegendRow(StringBuilder legend, Color color, string name, float valueShare, int count)
        {
            legend.Append(UIFormat.Colorize("■", color))
                  .Append(' ')
                  .Append(name)
                  .Append("  ")
                  .Append(UIFormat.Colorize($"{UIFormat.Percent(valueShare)} ({count})", UIFormat.MutedColor))
                  .Append('\n');
        }

        private void RefreshStatus(IReadOnlyCollection<Antique> collection, float totalValue)
        {
            SetText(statusTitle, "Collection status");

            // Everything the player owns is in the warehouse for now. Transport, renovation
            // and auctions don't exist yet — their tiles show a placeholder until they do.
            // Listed antiques are still physically in the warehouse until a buyer takes them.
            int listed = collection.Count(a => a.IsListedForSale);
            string warehouseDetail = UIFormat.Money(totalValue) + (listed > 0 ? $" · {listed} listed" : "");
            SetText(inWarehouseText, StatusTile("In warehouse", collection.Count.ToString(), warehouseDetail));
            SetText(inTransportText, StatusTile("In transport", "—", "Coming soon"));
            SetText(inRenovationText, StatusTile("In renovation", "—", "Coming soon"));
            SetText(onAuctionText, StatusTile("On auction", "—", "Coming soon"));
        }

        private static string StatusTile(string label, string count, string detail) =>
            $"{label}\n<size=140%><b>{count}</b></size>\n{UIFormat.Colorize(detail, UIFormat.MutedColor)}";

        private void RefreshHighlights(IReadOnlyCollection<Antique> collection, float totalValue)
        {
            Antique best = CollectionAnalytics.MostValuable(collection);
            SetText(mostValuableText, best == null
                ? "Most valuable\n—"
                : $"Most valuable\n<b>{best.Name}</b>\n{UIFormat.Money(best.CurrentPrice)}");

            int limited = CollectionAnalytics.LimitedEditionCount(collection);
            string limitedShare = collection.Count > 0
                ? UIFormat.Colorize($"{UIFormat.Percent((float)limited / collection.Count)} of the collection", UIFormat.MutedColor)
                : "";
            SetText(limitedEditionsText, $"Limited editions\n<b>{limited} / {collection.Count}</b>\n{limitedShare}");

            float average = collection.Count > 0 ? totalValue / collection.Count : 0f;
            SetText(averageValueText, $"Average value\n<b>{UIFormat.Money(average)}</b>");
        }

        private void RefreshFooter(IReadOnlyCollection<Antique> collection, Market.Market market)
        {
            // Market-driven change only: buying or selling items doesn't count as "change in value".
            float change = CollectionAnalytics.MarketValueChange(collection, market, ChangeWindowDays);
            string changeText = UIFormat.ColorBySign(
                $"{UIFormat.SignedMoney(change)} {UIFormat.TrendArrow(change)}", change);

            SetText(valueChangeText, $"Market value change ({ChangeWindowDays} days): {changeText}");
            SetText(hintText, "Click an antique on the list to see its details and actions.");
        }

        private static void SetText(TMP_Text target, string value)
        {
            if (target != null)
                target.text = value;
        }
    }
}
