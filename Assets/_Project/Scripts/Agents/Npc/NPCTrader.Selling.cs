using AntiqueTradingSimulator.Auctions;
using AntiqueTradingSimulator.Company;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Events;
using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.News;
using AntiqueTradingSimulator.Restoration;
using AntiqueTradingSimulator.Rumours;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static AntiqueTradingSimulator.Events.EventEffect;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.Agents
{
    /// <summary>
    /// Selling policy: which channel a disposal deserves, and what makes an instant sale the last resort.
    /// One part of NPCTrader, which is a partial class.
    /// </summary>
    public partial class NPCTrader
    {
        // ---------------------------------------------------------------- disposing of a holding

        /// <summary>
        /// Gets rid of one holding by the route its reason deserves: a listing for anything that
        /// can wait, the market only when the trader is out of money, space or time.
        /// </summary>
        private bool DisposeOf(string antiqueId, DisposalReason reason, int week, NpcBehaviorProfile profile)
        {
            var antique = Inventory.GetHolding(antiqueId);
            if (antique == null) return false;

            var market = _economyManager.Market;
            if (market == null) return false;

            var plan = PlanForOrCreate(antique, week);

            // Noted before the sale, so the market's record carries why the seller was selling.
            Diagnostics.Telemetry.Disposal(antiqueId, reason);

            if (MustSellNow(reason, plan, week, profile))
            {
                if (antique.IsListedForSale) Inventory.CancelListing(market, antiqueId);
                if (!SellListing(antiqueId)) return false;

                _plans.Remove(antiqueId);
                _agenda.DropTarget(antiqueId);
                return true;
            }

            if (!profile.ListsOnMarket)
            {
                if (!SellListing(antiqueId)) return false;

                _plans.Remove(antiqueId);
                _agenda.DropTarget(antiqueId);
                return true;
            }

            // A holding under its stop starts its clock here, and only dumps if nobody bites.
            if (reason == DisposalReason.StopLoss && plan.DistressSince < 0) plan.DistressSince = week;

            float worth = TraderValuation.Worth(antique, market, WorldView, profile, PricingHorizonWeeks);
            int weeksListed = ClearingNow(reason) ? Mathf.Max(1, profile.PatientSellAfterWeeks) : 0;
            float ask = TraderValuation.AskFor(antique, plan, weeksListed, worth, Inventory, market, profile);
            if (ask <= 0f) return false;

            if (antique.IsListedForSale)
                return Inventory.RepriceListing(market, antiqueId, ask);

            if (!Inventory.ListForSale(market, antiqueId, ask, week)) return false;

            plan.OfferedSinceWeek = week;
            Debug.Log($"{TraderName} listed {antique.Name} for {Core.Money.Format(ask)} (worth {Core.Money.Format(worth)}).");
            return true;
        }

        /// <summary>
        /// The only reasons to take the market's price: cash is needed now, the rumour's deadline
        /// is real, or a listing priced to clear has sat past the trader's patience with it.
        /// </summary>
        private bool MustSellNow(DisposalReason reason, Position plan, int week, NpcBehaviorProfile profile)
        {
            if (reason == DisposalReason.CashNeed || reason == DisposalReason.RumourDump) return true;

            if (reason != DisposalReason.StopLoss) return false;

            int wait = Mathf.Max(0, profile.PanicSellAfterWeeks);
            return plan.DistressSince >= 0 && week - plan.DistressSince >= wait;
        }

        /// <summary>Reasons that want the piece gone soon, so it opens at the reservation price.</summary>
        private static bool ClearingNow(DisposalReason reason) =>
            reason == DisposalReason.StopLoss ||
            reason == DisposalReason.BadNews ||
            reason == DisposalReason.ThesisInvalidated ||
            reason == DisposalReason.FadeApproaching ||
            reason == DisposalReason.Capacity;

        /// <summary>The cheapest holding that is free to sell, for raising cash or clearing space.</summary>
        private string CheapestFreeHolding(out string name)
        {
            string cheapest = null;
            name = null;
            float lowest = 0f;

            foreach (var pair in Inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding.IsReservedForContract || holding.IsInTransit || holding.IsInRestoration) continue;

                var plan = PlanFor(holding.Id);
                if (plan != null && plan.Purpose == PlanPurpose.Contract) continue;

                if (cheapest != null && holding.CurrentPrice >= lowest) continue;

                cheapest = holding.Id;
                name = holding.Name;
                lowest = holding.CurrentPrice;
            }

            return cheapest;
        }

        // ---------------------------------------------------------------- rumours

        /// <summary>
        /// Plants a fake rumour to pump stock this NPC already holds, when one pays for itself
        /// (NpcRumourPlanner). The spend comes out of its cash like any other purchase.
        /// </summary>
        private bool TierPlantRumour(int week, NpcBehaviorProfile profile)
        {
            if (!_mayPlantRumour) return false;

            var mill = RumourMill.Current;
            if (mill == null || !mill.CanPlant(this, week))
            {
                _mayPlantRumour = false;
                return false;
            }

            _mayPlantRumour = false;

            if (!NpcRumourPlanner.TryFindPlan(mill, this, week, out var plan)) return false;

            if (plan.Spend > _weekBudget || !PassesCashFloor(plan.Spend)) return false;

            if (!mill.PlantFor(this, plan)) return false;

            _weekBudget = Mathf.Max(0f, _weekBudget - plan.Spend);
            return true;
        }

        private static bool MatchesScope(Antique antique, NewsEventData effect) => effect.targetScope switch
        {
            TargetScope.AntiqueType => antique.Definition != null && antique.Definition.Type == effect.AntiqueType,
            TargetScope.Country => antique.Definition != null && antique.Definition.Country == effect.Country,
            TargetScope.Century => antique.Definition != null && antique.Definition.Century == effect.Century,
            _ => false
        };
    }
}
