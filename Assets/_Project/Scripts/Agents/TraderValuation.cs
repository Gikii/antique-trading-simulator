using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.Market;
using UnityEngine;

namespace AntiqueTradingSimulator.Agents
{
    /// <summary>How a trader's asking price answers the rest of the board.</summary>
    public enum PricingStance
    {
        /// <summary>Keeps its own price and waits.</summary>
        HoldLine,
        /// <summary>Goes a shilling under the cheapest rival of the same type.</summary>
        Undercut,
        /// <summary>Settles near the board's middle price.</summary>
        Follow
    }

    /// <summary>One carriage service quoted for one antique, and what buying it that way returns.</summary>
    public readonly struct TransportChoice
    {
        public readonly TransportOption Option;

        /// <summary>Asking price plus this service's carriage.</summary>
        public readonly float TotalCost;

        public readonly int TransitWeeks;

        /// <summary>Return per pound per week over transit plus the hold, damage risk priced in.</summary>
        public readonly float WeeklyReturn;

        public TransportChoice(TransportOption option, float totalCost, int transitWeeks, float weeklyReturn)
        {
            Option = option;
            TotalCost = totalCost;
            TransitWeeks = transitWeeks;
            WeeklyReturn = weeklyReturn;
        }
    }

    /// <summary>
    /// The one valuation every trading decision reads, and the pricing policy behind every asking
    /// price. Shared by the NPC logic, the player's modals and the telemetry mirrors.
    /// </summary>
    public static class TraderValuation
    {
        /// <summary>
        /// The services a discretionary purchase is quoted at, in a fixed order and with no
        /// allocation on the browse path.
        /// </summary>
        private static readonly TransportOption[] Services =
            { TransportOption.Economy, TransportOption.Standard, TransportOption.Express };

        /// <summary>
        /// Picks the carriage for a discretionary purchase off the same quotes the player's modal
        /// shows: the service with the best weekly return over transit plus the hold wins.
        /// </summary>
        public static TransportChoice BestTransport(Antique antique, float worth, int holdWeeks,
            TransportManager transport, TraderInventory buyer, float carryPerWeek,
            TransportOption fallback = TransportOption.Standard)
        {
            var chosen = new TransportChoice(fallback, 0f, 0, float.NegativeInfinity);
            if (antique == null) return chosen;

            if (transport == null)
                return new TransportChoice(fallback, antique.SalePrice, 0, 0f);

            float maxLoss = transport.Settings != null ? transport.Settings.MaxConditionLoss : 0f;

            for (int i = 0; i < Services.Length; i++)
            {
                var option = Services[i];
                var quote = transport.Quote(antique, option, buyer: buyer);
                if (quote == null) continue;

                float cost = antique.SalePrice + quote.Cost;
                if (cost <= 0f) continue;

                int weeks = Mathf.Max(1, quote.DurationWeeks + Mathf.Max(0, holdWeeks));

                // Price is linear in condition, so damage risk is priced as a share of what the
                // piece is worth.
                float risk = Mathf.Clamp01(transport.GetDamageChance(option, quote.ArrivalWeek));
                float expected = worth * (1f - risk * Mathf.Clamp01(maxLoss));

                float weekly = WeeklyReturn(cost, expected, weeks, carryPerWeek);
                if (weekly <= chosen.WeeklyReturn) continue;

                chosen = new TransportChoice(option, cost, quote.DurationWeeks, weekly);
            }

            if (chosen.WeeklyReturn > float.NegativeInfinity) return chosen;

            // Nothing quoted: fall back to the default service's own cost so the caller still has one.
            var plain = transport.Quote(antique, fallback, buyer: buyer);
            return new TransportChoice(fallback,
                antique.SalePrice + (plain != null ? plain.Cost : 0f),
                plain != null ? plain.DurationWeeks : 0,
                0f);
        }

        /// <summary>What one more item adds to a trader's weekly warehouse upkeep. 0 without one.</summary>
        public static float CarryPerWeek(TraderInventory inventory)
        {
            var warehouse = inventory != null ? inventory.Warehouse : null;
            if (warehouse == null) return 0f;

            int used = inventory.UsedSlots;
            return Mathf.Max(0f, warehouse.CalculateWeeklyUpkeep(used + 1) - warehouse.CalculateWeeklyUpkeep(used));
        }

        /// <summary>
        /// What this trader thinks the antique is worth: its value in its condition, with the
        /// condition premium, moved by the part of its outlook the trader is confident in.
        /// </summary>
        public static float Worth(Antique antique, Market.Market market, TraderWorldView view,
            NpcBehaviorProfile profile, int horizonWeeks)
        {
            if (antique == null || profile == null) return 0f;

            var typeState = market != null ? market.GetTypeState(antique.DefinitionId) : null;
            float fundamental = PriceEngine.CalculateReferencePrice(antique.BasePrice, typeState)
                                * antique.Condition
                                * (1f + profile.ConditionPremium * Mathf.Max(0f, antique.Condition - 0.5f));

            if (view == null || horizonWeeks <= 0) return fundamental;

            var outlook = view.OutlookFor(antique.DefinitionId, horizonWeeks, profile);
            return fundamental * (1f + outlook.Conviction * OutlookWeightOf(profile));
        }

        /// <summary>Value in its condition only, for decisions a coming move must not sway.</summary>
        public static float Fundamental(Antique antique, Market.Market market, NpcBehaviorProfile profile) =>
            Worth(antique, market, null, profile, 0);

        /// <summary>
        /// Return per pound per week of a trade, carrying cost included. The one hurdle every
        /// capital decision is measured against.
        /// </summary>
        public static float WeeklyReturn(float totalCost, float worth, int weeks, float carryPerWeek)
        {
            if (totalCost <= 0f || weeks <= 0) return 0f;
            return (worth - totalCost - weeks * carryPerWeek) / (totalCost * weeks);
        }

        /// <summary>How much of its outlook a profile prices in. Falls back to pricing it fully.</summary>
        public static float OutlookWeightOf(NpcBehaviorProfile profile) =>
            profile != null && profile.OutlookWeight > 0f ? profile.OutlookWeight : 1f;

        // ---------------------------------------------------------------- asking prices

        /// <summary>
        /// Asking price for a listing: it opens at a markup and walks down to the trader's
        /// reservation price over its patience, then answers the rest of the board.
        /// </summary>
        public static float AskFor(Antique antique, Position position, int weeksListed, float worth,
            TraderInventory inventory, Market.Market market, NpcBehaviorProfile profile)
        {
            if (antique == null || profile == null || worth <= 0f) return 0f;

            float floor = worth * ReservationMultiplierFor(inventory, profile);
            float opening = Mathf.Max(worth * Mathf.Max(1f, profile.ListingMarkup),
                                      position != null ? position.CostBasis * profile.ProfitTargetMultiplier : 0f);

            // A position bought on a view opens at the price that view says it should reach, which
            // is also what prices a planted rumour's stock at what the believers will pay.
            if (position != null && position.TargetPrice > opening) opening = position.TargetPrice;

            if (opening < floor) opening = floor;

            int patienceWeeks = profile.PatientSellAfterWeeks > 0 ? profile.PatientSellAfterWeeks : 1;
            float patience = Mathf.Clamp01(weeksListed / (float)patienceWeeks);
            float ask = Mathf.Lerp(opening, floor, patience);

            ask = AnswerTheBoard(ask, floor, antique, market, profile);
            return Core.Money.RoundToShilling(Mathf.Max(floor, ask));
        }

        /// <summary>
        /// The lowest a listing may be priced: never so low that the fee leaves less than selling
        /// to the market outright would pay, whatever the profile asks for.
        /// </summary>
        public static float ReservationMultiplierFor(TraderInventory inventory, NpcBehaviorProfile profile)
        {
            float feeRate = inventory != null ? Mathf.Clamp01(inventory.Modifiers.ListingFeeRate) : 0f;
            float spread = Mathf.Clamp01(Market.Market.InstantSaleSpread);

            float breakEven = feeRate < 1f ? (1f - spread) / (1f - feeRate) : 1f;
            float wanted = profile != null ? Mathf.Max(1f, profile.MinListingMultiplier) : 1f;

            return Mathf.Max(wanted, breakEven);
        }

        /// <summary>Moves the ask toward or under the rival asks of the same type, by stance.</summary>
        private static float AnswerTheBoard(float ask, float floor, Antique antique,
            Market.Market market, NpcBehaviorProfile profile)
        {
            if (market == null) return ask;

            switch (profile.Stance)
            {
                case PricingStance.Undercut:
                    float lowest = market.LowestAskFor(antique.DefinitionId, antique.Id);
                    if (lowest <= 0f) return ask;
                    return Mathf.Max(floor, Mathf.Min(ask, lowest - Core.Money.OneShilling));

                case PricingStance.Follow:
                    float median = market.MedianAskFor(antique.DefinitionId, antique.Id);
                    if (median <= 0f) return ask;
                    return Mathf.Max(floor, Mathf.Lerp(ask, median, 0.5f));

                default:
                    return ask;
            }
        }
    }
}
