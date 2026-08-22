using System;
using System.Collections.Generic;

namespace AntiqueTradingSimulator.Market
{
    /// <summary>
    /// Tracks the dynamic, type-level Supply and Demand for one AntiqueDefinition.
    /// This is what used to live on the old per-type "Antique" runtime object. Now that
    /// each physical antique is its own AntiqueListing, Supply/Demand no longer represent
    /// "how many are left to buy" — instead they represent overall market conditions for
    /// that type of antique, which drive two things:
    ///   1) The odds that a new listing of this type appears (see Market.GenerateListing).
    ///   2) The supply/demand price multiplier applied to every listing of this type.
    /// </summary>
    /// 
    [Serializable]
    public class PricePoint
    {
        public int Day;
        public float Price;

        public PricePoint(int day, float price)
        {
            Day = day;
            Price = price;
        }
    }

    [Serializable]
    public class AntiqueMarketState
    {
        public string DefinitionId;
        public float Supply;
        public float Demand;

        // Rolling price history, capped at 90 entries so it never grows unbounded.
        public List<PricePoint> PriceHistory = new List<PricePoint>();
        private const int MaxHistoryDays = 90;

        public AntiqueMarketState(string definitionId, float initialSupply, float initialDemand)
        {
            DefinitionId = definitionId;
            Supply = initialSupply;
            Demand = initialDemand;
        }

        public void RecordPrice(int day, float price)
        {
            PriceHistory.Add(new PricePoint(day, price));
            if (PriceHistory.Count > MaxHistoryDays)
                PriceHistory.RemoveAt(0);
        }
    }
}
