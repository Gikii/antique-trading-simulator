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

        public List<string> FeedMessages = new List<string>();
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

        public List<PricePoint> PriceHistory = new List<PricePoint>();

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
                PriceHistory = new List<PricePoint>(typeState.PriceHistory)
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
                PriceHistory = new List<PricePoint>(this.PriceHistory ?? new List<PricePoint>())
            };

            return typeState;
        }
    }
}
