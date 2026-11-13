using System;
using System.Collections.Generic;
using UnityEngine;

namespace AntiqueTradingSimulator.Market
{

    [Serializable]
    public class PricePoint
    {
        public int Week;
        public float Price;

        public PricePoint(int week, float price)
        {
            Week = week;
            Price = price;
        }
    }

    [Serializable]
    public class AntiqueMarketState
    {
        public string DefinitionId;

        public float Supply;
        public float Demand;

        public float BaselineSupply;
        public float BaselineDemand;

        public float TempSupplyMod;
        public float TempDemandMod;

        /// <summary>
        /// Seasonal part of the demand (SeasonSettings), set every week by EconomyManager:
        /// BaselineDemand × (seasonal multiplier − 1). Kept apart from TempDemandMod so events and the
        /// dependency network never overwrite it, and the UI can show it on its own.
        /// </summary>
        public float SeasonalDemandMod;

        /// <summary>The demand prices, collectors and the UI work with: own + temporary (events, dependencies) + seasonal.</summary>
        public float EffectiveDemand => Demand + TempDemandMod + SeasonalDemandMod;

        public List<PricePoint> PriceHistory = new List<PricePoint>();
        private const int MaxHistoryWeeks = 90;

        // Hammer price of the last lot of this type sold at auction (0 / -1 = never sold at auction).
        public float LastAuctionPrice;
        public int LastAuctionWeek = -1;
        public bool HasAuctionPrice => LastAuctionWeek >= 0;

        private const float SnapThreshold = 0.01f;

        public AntiqueMarketState(string definitionId, float initialSupply, float initialDemand)
        {
            DefinitionId = definitionId;
            Supply = initialSupply;
            Demand = initialDemand;
            BaselineSupply = initialSupply;
            BaselineDemand = initialDemand;
        }

        public void ApplyMeanReversion(float supplyRate, float demandRate)
        {
            Supply = RevertToward(Supply, BaselineSupply, supplyRate);
            Demand = RevertToward(Demand, BaselineDemand, demandRate);
        }

        private static float RevertToward(float value, float baseline, float rate)
        {
            float next = Mathf.Lerp(value, baseline, Mathf.Clamp01(rate));
            return Mathf.Abs(next - baseline) < SnapThreshold ? baseline : next;
        }


        public void RecordPrice(int week, float price)
        {
            PriceHistory.Add(new PricePoint(week, price));
            if (PriceHistory.Count > MaxHistoryWeeks)
                PriceHistory.RemoveAt(0);
        }
    }
}
