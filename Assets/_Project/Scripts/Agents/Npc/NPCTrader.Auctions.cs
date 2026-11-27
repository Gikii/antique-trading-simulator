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
    /// What this trader will bid on a lot and how far it will chase one.
    /// One part of NPCTrader, which is a partial class.
    /// </summary>
    public partial class NPCTrader
    {
        // ---------------------------------------------------------------- auctions

        /// <summary>
        /// Most this NPC can bid on one lot (hammer price): its free cash after the contract reserve,
        /// times the profile's auction budget share, leaving costAllowance for the
        /// buyer's premium and transport. Recomputed for every lot, so winning one lowers the next.
        /// </summary>
        public float AuctionBudget(float costAllowance)
        {
            var profile = Profile;
            if (profile == null) return 0f;

            float free = Mathf.Max(0f, Inventory.Cash - CalculateContractReserve() * ReserveSafetyMargin);
            return free * profile.AuctionBudgetShare / (1f + Mathf.Max(0f, costAllowance));
        }

        /// <summary>
        /// This NPC's paddle for a lot, or null if it doesn't want it (outside its interests, below its
        /// condition standard, no budget). The limit starts from the same fair value used on the market,
        /// scaled by the profile's auction multiplier, the lot's extra appeal (valuePremium,
        /// e.g. provenance) and the news it believes about this kind of antique — capped by its budget.
        /// </summary>
        public AuctionBidder CreateAuctionBidder(Antique antique, float valuePremium, float costAllowance, System.Random rng)
        {
            var profile = Profile;
            if (profile == null || antique == null || antique.Definition == null || _economyManager.Market == null) return null;
            if (!IsInterestedIn(antique.Definition, profile)) return null;
            if (antique.Condition < profile.MinConditionToBuy) return null;

            float cap = AuctionBudget(costAllowance);
            if (cap <= 0f) return null;

            float jitter = rng != null ? 0.9f + 0.2f * (float)rng.NextDouble() : 1f;
            float value = ExpectedValue(antique, profile)
                          * profile.AuctionValueMultiplier
                          * (1f + Mathf.Max(0f, valuePremium))
                          * (1f + AuctionNewsBias(antique, profile))
                          * jitter;

            float limit = Mathf.Min(value, cap);
            if (limit <= 0f) return null;

            return new AuctionBidder
            {
                Id = Id,
                Name = TraderName,
                Kind = BidderKind.Npc,
                Limit = limit,
                Cap = cap,
                FeverChance = profile.AuctionFeverChance,
                FeverRaise = profile.AuctionFeverRaise,
                Hesitation = profile.AuctionHesitation
            };
        }

        /// <summary>Remembers a won lot like a market purchase, so the usual selling rules apply to it.</summary>
        public void RecordAuctionPurchase(Antique antique, int week)
        {
            if (antique == null) return;
            RecordPlan(antique.Id, antique.PurchasePrice, week, PlanPurpose.Resale);
        }

        /// <summary>
        /// +premium per believed piece of good news matching the antique, −premium per bad one. Only news
        /// the NPC already reacts to (passed its trust roll, reaction day reached) counts.
        /// </summary>
        private float AuctionNewsBias(Antique antique, NpcBehaviorProfile profile)
        {
            if (profile.AuctionNewsPremium <= 0f) return 0f;

            if (WorldView == null) return 0f;

            var outlook = WorldView.OutlookFor(antique.DefinitionId, HoldWeeks(profile), profile);
            int sources = Mathf.Max(outlook.Supporting, outlook.Opposing);
            if (sources <= 0) return 0f;

            float bias = Mathf.Sign(outlook.Expected) * profile.AuctionNewsPremium * sources * outlook.Confidence;
            return Mathf.Clamp(bias, -0.5f, 0.5f);
        }
    }
}
