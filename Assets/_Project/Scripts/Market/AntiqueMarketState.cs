using System;
using System.Collections.Generic;
using UnityEngine;

namespace AntiqueTradingSimulator.Market
{

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

        public float BaselineSupply;
        public float BaselineDemand;

        public float TempSupplyMod;
        public float TempDemandMod;

        public List<PricePoint> PriceHistory = new List<PricePoint>();
        private const int MaxHistoryDays = 90;

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


        public void RecordPrice(int day, float price)
        {
            PriceHistory.Add(new PricePoint(day, price));
            if (PriceHistory.Count > MaxHistoryDays)
                PriceHistory.RemoveAt(0);
        }
    }
}
