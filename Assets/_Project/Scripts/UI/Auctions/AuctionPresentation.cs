using AntiqueTradingSimulator.Auctions;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Market;
using System.Collections.Generic;
using System.Linq;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>Shared wording for the auction screens (view, banner, live modal, InfoBar).</summary>
    public static class AuctionPresentation
    {
        /// <summary>"14:00"</summary>
        public static string Hour(int tick) => $"{tick:00}:00";

        /// <summary>"today at 14:00" / "tomorrow at 14:00" / "in 3 days · 9 October, 14:00"</summary>
        public static string When(Auction auction, int today)
        {
            if (auction == null) return "";

            int daysLeft = auction.Day - today;
            string hour = Hour(auction.StartTick);

            if (daysLeft <= 0) return $"today at {hour}";
            if (daysLeft == 1) return $"tomorrow at {hour}";
            return $"in {UIFormat.Days(daysLeft)} · {UIFormat.GameDate(auction.Day)}, {hour}";
        }

        /// <summary>"Auction #3"</summary>
        public static string Title(Auction auction) => auction != null ? $"Auction #{auction.Number}" : "Auction";

        /// <summary>"850–1 300 £"</summary>
        public static string Estimate(AuctionLot lot) =>
            lot == null ? "—" : $"{UIFormat.Number(lot.EstimateLow)}–{UIFormat.Money(lot.EstimateHigh)}";

        /// <summary>"850–1 300 £" summed over all lots.</summary>
        public static string TotalEstimate(Auction auction)
        {
            if (auction == null || auction.Lots.Count == 0) return "—";
            float low = auction.Lots.Sum(l => l.EstimateLow);
            float high = auction.Lots.Sum(l => l.EstimateHigh);
            return $"{UIFormat.Number(low)}–{UIFormat.Money(high)}";
        }

        /// <summary>"Porcelain · Germany · XVIII century · Very good"</summary>
        public static string InfoLine(Antique antique)
        {
            if (antique == null) return "";
            return $"{antique.Category} · {antique.Country.ToDisplayString()} · {antique.Century.ToDisplayString()} · " +
                   $"{UIFormat.ConditionLabel(antique.Condition)} condition";
        }

        /// <summary>"Lot 2 · Meissen vase"</summary>
        public static string LotTitle(AuctionLot lot) => lot == null ? "" : $"Lot {lot.LotNumber} · {lot.Name}";

        /// <summary>The lot's history text (provenance), or "" when it has none.</summary>
        public static string Provenance(AuctionLot lot) =>
            lot != null && lot.HasProvenance && lot.Antique != null && !string.IsNullOrWhiteSpace(lot.Antique.History)
                ? lot.Antique.History.Trim()
                : "";

        public static bool IsPlayer(string ownerId) => ownerId == Antique.PlayerOwnerId;

        /// <summary>"You" for the player, otherwise the stored name.</summary>
        public static string Winner(AuctionLot lot) =>
            lot == null ? "" : IsPlayer(lot.WinnerId) ? "You" : lot.WinnerName;

        /// <summary>"within estimate" / "+12% over estimate" (above the high estimate) / "-8% under estimate" (below the low one), coloured.</summary>
        public static string VersusEstimate(AuctionLot lot)
        {
            if (lot == null || !lot.IsSold) return "";

            if (lot.HammerPrice > lot.EstimateHigh && lot.EstimateHigh > 0f)
                return UIFormat.Colorize($"{UIFormat.SignedPercent(lot.HammerPrice / lot.EstimateHigh - 1f)} over estimate", UIFormat.PositiveColor);

            if (lot.HammerPrice < lot.EstimateLow && lot.EstimateLow > 0f)
                return UIFormat.Colorize($"{UIFormat.SignedPercent(lot.HammerPrice / lot.EstimateLow - 1f)} under estimate", UIFormat.NegativeColor);

            return UIFormat.Colorize("within estimate", UIFormat.MutedColor);
        }

        public const int TrendWindowDays = 30;

        /// <summary>
        /// What a bidder needs to judge a lot: the estimate, the market value (with its trend) to compare
        /// it with, the type's last auction price and what winning within the estimate would cost.
        /// The live presentation adds the opening bid, which matters right before bidding opens.
        /// </summary>
        public static List<(string label, string value)> CatalogFacts(AuctionLot lot, AuctionManager manager,
            Market.Market market, bool includeOpeningBid)
        {
            var facts = new List<(string label, string value)>();
            if (lot == null) return facts;

            facts.Add(("Estimate", Estimate(lot)));
            if (includeOpeningBid) facts.Add(("Opening bid", UIFormat.Money(lot.StartPrice)));
            facts.Add(("Market value", MarketValueWithTrend(lot.Antique, market)));
            facts.Add(("Last auction price (type)", LastAuctionPrice(market, lot.Antique)));

            if (manager != null)
            {
                float low = manager.PlayerCostFor(lot, lot.EstimateLow);
                float high = manager.PlayerCostFor(lot, lot.EstimateHigh);
                facts.Add(("Winning within estimate costs", $"{UIFormat.Number(low)}–{UIFormat.Money(high)}"));
            }

            return facts;
        }

        /// <summary>"1 020 £  ↑ +4%" — current market value with its change over the last 30 days.</summary>
        public static string MarketValueWithTrend(Antique antique, Market.Market market)
        {
            if (antique == null) return "—";

            string value = UIFormat.Money(antique.CurrentPrice);
            if (market == null) return value;

            float change = CollectionAnalytics.MarketValueChange(antique, market, TrendWindowDays);
            float past = antique.CurrentPrice - change;
            if (past <= 0f) return value;

            string trend = UIFormat.ColorBySign($"{UIFormat.TrendArrow(change)} {UIFormat.SignedPercent(change / past)}", change);
            return $"{value}  <size=85%>{trend}</size>";
        }

        /// <summary>"Last auction price" value for the lot's type: "960 £ · 3 October" or "none yet".</summary>
        public static string LastAuctionPrice(Market.Market market, Antique antique)
        {
            var typeState = market != null && antique != null ? market.GetTypeState(antique.DefinitionId) : null;
            if (typeState == null || !typeState.HasAuctionPrice)
                return UIFormat.Colorize("none yet", UIFormat.MutedColor);

            return $"{UIFormat.Money(typeState.LastAuctionPrice)} · {UIFormat.GameDate(typeState.LastAuctionDay)}";
        }
    }
}
