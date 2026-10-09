using UnityEngine;

namespace AntiqueTradingSimulator.Economy
{
    /// <summary>
    /// Per-trader adjustments to trading costs, read by the Market (listing fee) and
    /// TransportManager (shipping cost and time). Neutral by default, so NPCs trade on the
    /// base terms. For the player, CompanyUpgrades sets these from the upgrade levels.
    ///
    /// Not saved: the values are derived from saved data (upgrade levels) and set again
    /// after loading.
    /// </summary>
    public class TraderModifiers
    {
        private float _listingFeeRate = Market.Market.ListingFeeRate;
        private float _transportCostReduction;
        private int _transportDaysReduction;

        /// <summary>Share of an owner-set asking price kept by the market when the listing sells.</summary>
        public float ListingFeeRate
        {
            get => _listingFeeRate;
            set => _listingFeeRate = Mathf.Clamp01(value);
        }

        /// <summary>Fraction taken off shipping cost (0.1 = -10%).</summary>
        public float TransportCostReduction
        {
            get => _transportCostReduction;
            set => _transportCostReduction = Mathf.Clamp01(value);
        }

        /// <summary>Whole days taken off delivery time. TransportManager never goes below 1 day.</summary>
        public int TransportDaysReduction
        {
            get => _transportDaysReduction;
            set => _transportDaysReduction = Mathf.Max(0, value);
        }

        public float ListingFee(float askingPrice) => Mathf.Max(0f, askingPrice) * ListingFeeRate;

        public float ListingProceeds(float askingPrice) => Mathf.Max(0f, askingPrice) - ListingFee(askingPrice);

        public void Reset()
        {
            ListingFeeRate = Market.Market.ListingFeeRate;
            TransportCostReduction = 0f;
            TransportDaysReduction = 0;
        }
    }
}
