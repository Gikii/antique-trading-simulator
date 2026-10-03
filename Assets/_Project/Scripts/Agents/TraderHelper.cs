using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Logistics;
using System.Reflection;
using UnityEngine;

namespace AntiqueTradingSimulator.Agents
{
    /// <summary>
    /// Shared buy/sell methods for trader classes
    /// </summary>
    public class TraderHelper
    {
        /// <summary>
        /// Buys a listing and ships it to the buyer. Player and NPCs both go through here,
        /// so everyone pays for transport and waits for delivery the same way. Without a
        /// TransportManager the purchase falls back to instant delivery.
        /// </summary>
        public static bool BuyListing(TraderInventory inventory, Market.Market market, string listingId, string traderName,
            int currentDay = -1, TransportManager transport = null, TransportOption option = TransportOption.Standard)
        {
            if (!HasMarket(market, traderName)) return false;

            var listing = market.GetById(listingId);
            string antiqueName = listing != null ? listing.Name : null;
            float price = listing != null ? listing.SalePrice : 0f;

            TransportQuote quote = transport != null && listing != null ? transport.Quote(listing, option) : null;

            bool success = inventory.Buy(market, listingId, currentDay, quote);
            LogResult(traderName, "buy", listingId, success, inventory.Cash);
            if (success) {
                market.Feed.AddPurchase(traderName, antiqueName, price);
                if (quote != null)
                    transport.Dispatch(listing, inventory, quote, traderName);
            }
            return success;
        }

        /// <summary>Price + shipping for a listing with the given option — what BuyListing would charge.</summary>
        public static float EstimateTotalCost(Market.Antique listing, TransportManager transport, TransportOption option = TransportOption.Standard)
        {
            if (listing == null) return 0f;
            var quote = transport != null ? transport.Quote(listing, option) : null;
            return listing.SalePrice + (quote != null ? quote.Cost : 0f);
        }

        public static bool SellListing(TraderInventory inventory, Market.Market market, string listingId, string traderName, int currentDay)
        {
            if (!HasMarket(market, traderName)) return false;

            bool success = inventory.Sell(market, listingId, currentDay);
            LogResult(traderName, "sell", listingId, success, inventory.Cash);
            return success;
        }

        private static bool HasMarket(Market.Market market, string traderName)
        {
            if (market != null) return true;

            Debug.LogWarning($"{traderName}: no Market available yet.");
            return false;
        }

        private static void LogResult(string traderName, string action, string listingId, bool success, float cash)
        {
            if (success)
                Debug.Log($"{traderName} {action} succeeded — listing {listingId}. Cash: {cash:F2}");
            else
                Debug.Log($"{traderName} {action} failed — listing {listingId}.");
        }
    }
}


