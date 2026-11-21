using System;
using System.Collections.Generic;
using System.Linq;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.Market;
using UnityEngine;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.Autoplay
{
    /// <summary>What a strategy gets each in-game day.</summary>
    public sealed class AutoplayContext
    {
        public Market.Market Market;
        public PlayerTrader Player;
        public AutoplayConfig Config;
        public int Week;
        public int Tick;

        /// <summary>(action, antique or null, money amount, note) — written to autoplay_actions.csv.</summary>
        public Action<string, Antique, float, string> Log;

        public Economy.TraderInventory Inventory => Player.Inventory;

        /// <summary>Cash the bot may spend: everything above the reserve kept for upkeep.</summary>
        public float Spendable => Mathf.Max(0f, Inventory.Cash - Config.Reserve);

        /// <summary>Listing price plus standard shipping to the player.</summary>
        public float CostOf(Antique listing)
        {
            var quote = Player.QuoteTransport(listing.Id, TransportOption.Standard);
            return listing.SalePrice + (quote != null ? quote.Cost : 0f);
        }

        /// <summary>A free warehouse slot, buying the next capacity level if needed and affordable.</summary>
        public bool EnsureFreeSlot(float keepForPurchase)
        {
            if (Inventory.HasFreeSlot) return true;
            if (Spendable < keepForPurchase) return false;

            float before = Inventory.Cash;
            if (!Player.UpgradeWarehouseCapacity()) return false;

            Log("warehouse_upgrade", null, before - Inventory.Cash, $"capacity {Inventory.Capacity}");
            return Inventory.HasFreeSlot;
        }

        /// <summary>Listings the player could buy, in a fixed order so runs repeat.</summary>
        public IEnumerable<Antique> BuyableListings(IEnumerable<Antique> listings) =>
            listings.Where(l => l != null && l.OwnerId != Antique.PlayerOwnerId && !Inventory.Owns(l.Id))
                    .OrderBy(l => l.SalePrice)
                    .ThenBy(l => l.DefinitionId, StringComparer.Ordinal)
                    .ThenBy(l => l.Id, StringComparer.Ordinal);

        /// <summary>The player's holdings, in a fixed order.</summary>
        public List<Antique> Holdings() =>
            Inventory.Holdings.Values.OrderBy(h => h.Id, StringComparer.Ordinal).ToList();

        public bool Buy(Antique listing, string note)
        {
            float cost = CostOf(listing);
            if (!Player.BuyListing(listing.Id)) return false;
            Log("buy", listing, cost, note);
            return true;
        }

        public bool SellNow(Antique holding, string note)
        {
            float value = holding.CurrentPrice;
            if (!Player.SellListing(holding.Id)) return false;
            Log("sell_instant", holding, value, note);
            return true;
        }
    }

    public interface IAutoplayStrategy
    {
        string Name { get; }

        /// <summary>Called once at the start of every in-game day (every TicksPerDay ticks).</summary>
        void OnDay(AutoplayContext context);
    }

    public static class AutoplayStrategies
    {
        public static IAutoplayStrategy Create(AutoplayConfig config, out string error)
        {
            error = null;
            switch (config.Strategy)
            {
                case "idle":
                    return new IdleStrategy();
                case "buyup":
                    if (!Enum.TryParse(config.Category, true, out AntiqueType type))
                    {
                        error = $"unknown category '{config.Category}' (one of: {string.Join(", ", Enum.GetNames(typeof(AntiqueType)))})";
                        return null;
                    }
                    return new BuyUpStrategy(type);
                case "trader":
                    return new TraderStrategy();
                default:
                    error = $"unknown strategy '{config.Strategy}' (idle, buyup, trader)";
                    return null;
            }
        }
    }

    /// <summary>The player does nothing: the market as the NPCs make it. The control run.</summary>
    public sealed class IdleStrategy : IAutoplayStrategy
    {
        public string Name => "idle";
        public void OnDay(AutoplayContext context) { }
    }

    /// <summary>
    /// Buys up one category, then dumps it — the experiment for demand memory and trend following.
    /// From FromWeek (when the optional Grant is paid) to ToWeek it buys every listing of the category
    /// it can afford, cheapest first, buying warehouse space as needed. From SellWeek (unless -1) it
    /// sells everything of that category at once (instant sales), as items arrive.
    /// </summary>
    public sealed class BuyUpStrategy : IAutoplayStrategy
    {
        private readonly AntiqueType _type;
        private bool _granted;

        public BuyUpStrategy(AntiqueType type) => _type = type;

        public string Name => $"buyup({_type})";

        public void OnDay(AutoplayContext c)
        {
            var config = c.Config;

            if (!_granted && c.Week >= config.FromWeek)
            {
                _granted = true;
                if (config.Grant > 0f)
                {
                    c.Inventory.AddCash(config.Grant, Economy.LedgerCategory.Other, "Autoplay experiment grant");
                    c.Log("grant", null, config.Grant, "");
                }
            }

            bool selling = config.SellWeek >= 0 && c.Week >= config.SellWeek;
            if (selling)
            {
                foreach (var holding in c.Holdings())
                {
                    if (holding.Definition == null || holding.Definition.Type != _type) continue;
                    if (!holding.IsAvailable || holding.IsListedForSale || holding.IsReservedForContract) continue;
                    c.SellNow(holding, "dump");
                }
                return;
            }

            if (c.Week < config.FromWeek || c.Week > config.ToWeek) return;

            foreach (var listing in c.BuyableListings(c.Market.GetByType(_type)).ToList())
            {
                if (c.Market.GetById(listing.Id) == null) continue;

                float cost = c.CostOf(listing);
                if (cost > c.Spendable) break; // sorted by price: nothing further is affordable either
                if (!c.EnsureFreeSlot(cost)) break;
                if (cost > c.Spendable) break; // the upgrade may have used the money

                c.Buy(listing, "buy-up");
            }
        }
    }

    /// <summary>
    /// A plain, sensible player for balance runs: buys pieces offered below their worth
    /// (PriceFactor ≤ 0.85), lists them at their worth plus a little, and sells at once
    /// anything that hasn't sold in 8 weeks. No contracts, auctions or restoration.
    /// </summary>
    public sealed class TraderStrategy : IAutoplayStrategy
    {
        private const float MaxBuyPriceFactor = 0.85f;
        private const float AskMarkup = 1.05f;
        private const int MaxBuysPerDay = 2;
        private const int GiveUpAfterWeeks = 8;

        public string Name => "trader";

        public void OnDay(AutoplayContext c)
        {
            foreach (var holding in c.Holdings())
            {
                if (!holding.IsAvailable || holding.IsReservedForContract) continue;

                if (holding.IsListedForSale)
                {
                    if (holding.MarketListedOnWeek >= 0 && c.Week - holding.MarketListedOnWeek >= GiveUpAfterWeeks &&
                        c.Player.CancelMarketListing(holding.Id))
                    {
                        c.Log("cancel_listing", holding, 0f, "unsold");
                        c.SellNow(holding, "unsold listing");
                    }
                    continue;
                }

                float worth = holding.PriceFactor > 0f ? holding.CurrentPrice / holding.PriceFactor : holding.CurrentPrice;
                float ask = worth * AskMarkup;
                if (c.Player.ListOnMarket(holding.Id, ask))
                    c.Log("list", holding, ask, "");
            }

            int bought = 0;
            var candidates = c.BuyableListings(c.Market.Listings)
                .Where(l => !l.IsListedForSale && l.PriceFactor > 0f && l.PriceFactor <= MaxBuyPriceFactor)
                .OrderBy(l => l.PriceFactor)
                .ToList();

            foreach (var listing in candidates)
            {
                if (bought >= MaxBuysPerDay) break;
                if (c.Market.GetById(listing.Id) == null) continue;

                float cost = c.CostOf(listing);
                if (cost > c.Spendable) continue;
                if (!c.EnsureFreeSlot(cost + 20f)) break;
                if (cost > c.Spendable) continue;

                if (c.Buy(listing, $"price factor {listing.PriceFactor:0.00}")) bought++;
            }
        }
    }
}
