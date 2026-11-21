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

        /// <summary>
        /// The same reference price without the season's effect on demand — what NPC trend followers
        /// read, so a coming or passing season isn't taken for a trend. 0 in saves from before it
        /// existed: use <see cref="TrendValue"/>, which falls back to Price.
        /// </summary>
        public float TrendPrice;

        /// <summary>TrendPrice, or Price for points recorded before TrendPrice existed.</summary>
        public float TrendValue => TrendPrice > 0f ? TrendPrice : Price;

        // Kept as the only constructor: the save loader (Newtonsoft) builds points through it.
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

        /// <summary>
        /// Demand from the clients of open contracts that this type can satisfy — buyers waiting outside
        /// the market. Set (not added to) by ContractManager whenever the board changes, and rebuilt from
        /// the contracts after loading, so it isn't saved. Kept apart from TempDemandMod for the same
        /// reason as the seasonal part: nothing else may overwrite it, and the UI can show it on its own.
        /// </summary>
        public float ContractDemandMod;

        /// <summary>
        /// Slow, bounded memory of lasting interest in this type. Follows the gap between Demand and
        /// the level it is pulled back to (BaselineDemand + this), decays slowly and is capped at a
        /// fraction of BaselineDemand — see UpdateLongTermDemand. Demand is pulled back toward
        /// BaselineDemand + LongTermDemandMod, so long-running buying or selling leaves a trace that
        /// short spikes don't, while values can't run away (BaselineDemand itself never moves).
        /// Permanent event changes (ChangeDemandEffect.permDemandChange) are added here as well.
        /// </summary>
        public float LongTermDemandMod;

        /// <summary>The level Demand is pulled back to every week: baseline plus the long-term memory.</summary>
        public float DemandAnchor => BaselineDemand + LongTermDemandMod;

        /// <summary>The demand prices, collectors and the UI work with: own + temporary (events, dependencies) + seasonal + open contracts.</summary>
        public float EffectiveDemand => Demand + TempDemandMod + SeasonalDemandMod + ContractDemandMod;

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
            Demand = RevertToward(Demand, DemandAnchor, demandRate);
        }

        /// <summary>
        /// One weekly step of the long-term memory, run after ApplyMeanReversion:
        /// it moves toward the remaining gap between Demand and DemandAnchor by <paramref name="rate"/>,
        /// shrinks by <paramref name="decay"/> and is clamped to ±<paramref name="cap"/> × BaselineDemand.
        /// </summary>
        public void UpdateLongTermDemand(float rate, float decay, float cap)
        {
            LongTermDemandMod += (Demand - DemandAnchor) * Mathf.Clamp01(rate);
            LongTermDemandMod *= 1f - Mathf.Clamp01(decay);
            ClampLongTermDemand(cap);
        }

        /// <summary>Adds a lasting change (e.g. an event's permanent demand change) to the memory, within the cap.</summary>
        public void AddLongTermDemand(float amount, float cap)
        {
            LongTermDemandMod += amount;
            ClampLongTermDemand(cap);
        }

        private void ClampLongTermDemand(float cap)
        {
            float limit = Mathf.Max(0f, cap) * Mathf.Max(0f, BaselineDemand);
            LongTermDemandMod = Mathf.Clamp(LongTermDemandMod, -limit, limit);
        }

        private static float RevertToward(float value, float baseline, float rate)
        {
            float next = Mathf.Lerp(value, baseline, Mathf.Clamp01(rate));
            return Mathf.Abs(next - baseline) < SnapThreshold ? baseline : next;
        }


        public void RecordPrice(int week, float price, float trendPrice)
        {
            PriceHistory.Add(new PricePoint(week, price) { TrendPrice = trendPrice });
            if (PriceHistory.Count > MaxHistoryWeeks)
                PriceHistory.RemoveAt(0);
        }

        /// <summary>
        /// Relative change of the season-free reference price (PricePoint.TrendValue) from
        /// <paramref name="windowWeeks"/> weeks before the last recorded week to the last recorded week:
        /// 0.1 = up 10%. Reads only weekly records, so a trade shows up in the trend a week later.
        /// 0 while the history is too short.
        /// </summary>
        public float PriceTrend(int windowWeeks)
        {
            if (windowWeeks <= 0 || PriceHistory.Count < 2) return 0f;

            var last = PriceHistory[PriceHistory.Count - 1];
            int fromWeek = last.Week - windowWeeks;

            for (int i = PriceHistory.Count - 2; i >= 0; i--)
            {
                var point = PriceHistory[i];
                if (point.Week > fromWeek) continue;

                float then = point.TrendValue;
                return then > 0f ? last.TrendValue / then - 1f : 0f;
            }

            return 0f;
        }
    }
}
