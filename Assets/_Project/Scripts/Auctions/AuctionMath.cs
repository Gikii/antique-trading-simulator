using UnityEngine;

namespace AntiqueTradingSimulator.Auctions
{
    /// <summary>Rounding rules for auction prices, so estimates and bids look like an auctioneer's.</summary>
    public static class AuctionMath
    {
        /// <summary>Nearest value of the 1-2-5 series (…, 10, 20, 50, 100, …).</summary>
        public static float NiceStep(float value)
        {
            if (value <= 0f) return 1f;

            float magnitude = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(value)));
            float fraction = value / magnitude;

            float nice = fraction < 1.5f ? 1f
                : fraction < 3.5f ? 2f
                : fraction < 7.5f ? 5f
                : 10f;

            return nice * magnitude;
        }

        /// <summary>How much the next bid adds to <paramref name="currentBid"/>.</summary>
        public static float Increment(float currentBid, AuctionSettings settings) =>
            Mathf.Max(settings.MinIncrement, NiceStep(currentBid * settings.IncrementRate));

        /// <summary>The bid that would top <paramref name="currentBid"/>.</summary>
        public static float NextBid(float currentBid, AuctionSettings settings) =>
            currentBid + Increment(currentBid, settings);

        /// <summary>Rounds a price to a multiple of the increment used at that price (e.g. 1234 → 1250).</summary>
        public static float RoundPrice(float price, AuctionSettings settings)
        {
            if (price <= 0f) return 0f;

            float step = Increment(price, settings);
            return Mathf.Max(step, Mathf.Round(price / step) * step);
        }
    }
}
