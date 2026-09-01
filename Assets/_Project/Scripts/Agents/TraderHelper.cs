using AntiqueTradingSimulator.Economy;
using UnityEngine;

namespace AntiqueTradingSimulator.Agents
{
    /// <summary>
    /// Shared buy/sell methods for trader classes
    /// </summary>
    public class TraderHelper
    {
        public static bool BuyListing(TraderInventory inventory, Market.Market market, string listingId, string traderName)
        {
            if (!HasMarket(market, traderName)) return false;

            bool success = inventory.Buy(market, listingId);
            LogResult(traderName, "buy", listingId, success, inventory.Cash);
            return success;
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


