using System.Collections.Generic;

namespace AntiqueTradingSimulator.Market
{

    public class MarketState
    {
        /// <summary>Supply/demand and price history per AntiqueDefinition.</summary>
        public List<MarketTypeState> TypeStates = new List<MarketTypeState>();

        /// <summary>Every listing.</summary>
        public List<string> ListingIds = new List<string>();

        /// <summary>Listings without owners.</summary>
        public List<AntiqueState> UnownedListings = new List<AntiqueState>();

        /// <summary>Market history, newest first. (Saves from before it was structured had FeedMessages – ignored.)</summary>
        public List<MarketFeedEntry> FeedEntries = new List<MarketFeedEntry>();

        public MarketFeedTotals FeedPlayerTotals = new MarketFeedTotals();

        public MarketWeekStats FeedWeekStats = new MarketWeekStats();
    }

    public class MarketTypeState
    {
        public string DefinitionId;

        public float Supply;
        public float Demand;

        public float BaselineSupply;
        public float BaselineDemand;

        public float TempSupplyMod;
        public float TempDemandMod;
        public float SeasonalDemandMod;

        public List<PricePoint> PriceHistory = new List<PricePoint>();

        public float LastAuctionPrice;
        public int LastAuctionWeek = -1;

        public static MarketTypeState Capture(AntiqueMarketState typeState)
        {
            if (typeState == null) return null;

            return new MarketTypeState
            {
                DefinitionId = typeState.DefinitionId,
                Supply = typeState.Supply,
                Demand = typeState.Demand,
                BaselineSupply = typeState.BaselineSupply,
                BaselineDemand = typeState.BaselineDemand,
                TempSupplyMod = typeState.TempSupplyMod,
                TempDemandMod = typeState.TempDemandMod,
                SeasonalDemandMod = typeState.SeasonalDemandMod,
                PriceHistory = new List<PricePoint>(typeState.PriceHistory),
                LastAuctionPrice = typeState.LastAuctionPrice,
                LastAuctionWeek = typeState.LastAuctionWeek
            };
        }

        public AntiqueMarketState Restore()
        {
            var typeState = new AntiqueMarketState(DefinitionId, Supply, Demand)
            {
                BaselineSupply = this.BaselineSupply,
                BaselineDemand = this.BaselineDemand,
                TempSupplyMod = this.TempSupplyMod,
                TempDemandMod = this.TempDemandMod,
                SeasonalDemandMod = this.SeasonalDemandMod,
                PriceHistory = new List<PricePoint>(this.PriceHistory ?? new List<PricePoint>()),
                LastAuctionPrice = this.LastAuctionPrice,
                LastAuctionWeek = this.LastAuctionWeek
            };

            return typeState;
        }
    }
}
