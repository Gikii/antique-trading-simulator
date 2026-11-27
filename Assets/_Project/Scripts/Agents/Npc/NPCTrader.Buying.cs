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
    /// Buying from the open board, and the valuation every purchase is measured against.
    /// One part of NPCTrader, which is a partial class.
    /// </summary>
    public partial class NPCTrader
    {
        // ---------------------------------------------------------------- buy from market tick actions

        /// <summary>
        /// Puts a workshop to stock whose plan has run out: a collector's piece below the condition
        /// it collects at, or a seller's stock that has sat unsold past its patience.
        /// </summary>
        private bool TierRestoreStock(int week, NpcBehaviorProfile profile)
        {
            if (_restorationManager == null || !profile.Restores) return false;
            if (OpenRestorationCount() >= profile.MaxConcurrentRestorations) return false;

            float bar = TargetWeeklyReturn(profile) * profile.RestorationHurdle;
            Antique chosen = null;
            RestorationLeg chosenLeg = default;

            foreach (var pair in Inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding == null || !holding.IsAvailable) continue;
                if (holding.IsReservedForContract || holding.IsListedForSale) continue;

                var plan = PlanFor(holding.Id);
                if (plan == null || plan.Purpose != PlanPurpose.Resale || plan.HasRestorationLeg) continue;

                bool spent = profile.NeverSells
                    ? holding.Condition < profile.MinConditionToBuy
                    : week - plan.AcquiredWeek >= HoldWeeks(profile);
                if (!spent) continue;

                var leg = BestLegFor(holding, profile, owned: true, delayWeeks: 0, barWeeklyReturn: bar);
                if (!leg.Exists || leg.WeeklyReturn <= 0f) continue;
                if (chosenLeg.Exists && leg.WeeklyReturn <= chosenLeg.WeeklyReturn) continue;

                chosen = holding;
                chosenLeg = leg;
            }

            if (chosen == null) return false;

            AttachRestorationLeg(chosen.Id, chosenLeg.Option.Id, chosenLeg.Fee, week + RestorationLegGraceWeeks);
            Debug.Log($"{TraderName} will restore {chosen.Name} at {chosenLeg.Option.DisplayName} ({Core.Money.Format(chosenLeg.Fee)}).");
            return true;
        }

        /// <summary>
        /// Looks over the board for the best return per pound per week, as-is or after a workshop
        /// stay. Nothing is refused on price alone: the return hurdle is the only bar.
        /// </summary>
        private bool TierBrowseMarket(int week, NpcBehaviorProfile profile)
        {
            float asIsBudget = BudgetFor(Intent.TakeBargain);
            float restoreBudget = BudgetFor(Intent.SendToWorkshop);
            if (asIsBudget <= 0f && restoreBudget <= 0f) return false;

            var market = _economyManager.Market;
            if (market == null) return false;

            var listings = market.Listings;
            int holdWeeks = HoldWeeks(profile);
            float threshold = BargainThreshold(profile) / holdWeeks;
            float restorationBar = TargetWeeklyReturn(profile) * profile.RestorationHurdle;

            Antique bestAsIs = null;
            float bestAsIsWeekly = threshold;
            float bestAsIsCost = 0f;
            TransportOption bestAsIsOption = NpcTransportOption;

            Antique bestRestore = null;
            float bestRestoreWeekly = threshold;
            float bestRestoreCost = 0f;
            TransportOption bestRestoreOption = NpcTransportOption;
            int bestRestoreTransit = 0;
            RestorationLeg bestLeg = default;

            for (int i = 0; i < listings.Count; i++)
            {
                var listing = listings[i];
                if (listing == null || Inventory.Owns(listing.Id)) continue;
                if (!IsInterestedIn(listing.Definition, profile)) continue;

                // The carriage is chosen per listing, off the same quotes the player's modal shows.
                var shipping = ChooseTransportFor(listing, ExpectedValue(listing, profile), holdWeeks);
                float cost = shipping.TotalCost;
                if (cost <= 0f || !PassesCashFloor(cost)) continue;

                if (Core.Money.Covers(asIsBudget, cost) && CanConsider(listing, profile))
                {
                    float weekly = BuyAsIsWeeklyReturn(listing, cost, profile);
                    if (weekly >= bestAsIsWeekly)
                    {
                        bestAsIs = listing;
                        bestAsIsWeekly = weekly;
                        bestAsIsCost = cost;
                        bestAsIsOption = shipping.Option;
                    }
                }

                if (!profile.Restores || !profile.BuysToRestore) continue;
                if (!CanConsiderForRestoration(listing, profile)) continue;

                var leg = BestLegFor(listing, profile, owned: false, delayWeeks: shipping.TransitWeeks,
                    barWeeklyReturn: restorationBar);
                if (!leg.Exists) continue;
                if (!Core.Money.Covers(restoreBudget, cost + leg.Fee) || !PassesCashFloor(cost + leg.Fee)) continue;

                float restoreWeekly = BuyAndRestoreWeeklyReturn(cost, leg, profile);
                if (restoreWeekly < bestRestoreWeekly) continue;

                bestRestore = listing;
                bestRestoreWeekly = restoreWeekly;
                bestRestoreCost = cost;
                bestRestoreOption = shipping.Option;
                bestRestoreTransit = shipping.TransitWeeks;
                bestLeg = leg;
            }

            bool takeRestore = bestRestore != null &&
                               (bestAsIs == null || bestRestoreWeekly >= bestAsIsWeekly * profile.RestorationHurdle);

            if (takeRestore)
            {
                int arrival = week + bestRestoreTransit;
                string restoreName = bestRestore.Name;
                if (!TryBuyForStock(bestRestore, week, bestRestoreCost, Intent.SendToWorkshop, bestRestoreOption)) return false;

                AttachRestorationLeg(bestRestore.Id, bestLeg.Option.Id, bestLeg.Fee, arrival + RestorationLegGraceWeeks);
                Debug.Log($"{TraderName} bought {restoreName} to restore at {bestLeg.Option.DisplayName} " +
                          $"(return {bestRestoreWeekly:P1} a week, cost {Core.Money.Format(bestRestoreCost + bestLeg.Fee)}).");
                return true;
            }

            if (bestAsIs == null) return false;

            string name = bestAsIs.Name;
            if (!TryBuyForStock(bestAsIs, week, bestAsIsCost, Intent.TakeBargain, bestAsIsOption)) return false;

            Debug.Log($"{TraderName} bought {name} on the open market (return {bestAsIsWeekly:P1} a week, cost {Core.Money.Format(bestAsIsCost)}).");
            return true;
        }

        private int TransitWeeksFor(Antique listing, TransportOption option)
        {
            var quote = QuoteFor(listing, option);
            return quote != null ? quote.DurationWeeks : 0;
        }

        // ---------------------------------------------------------------- valuation

        /// <summary>Return over the whole hold, carrying cost included, as a share of what it costs.</summary>
        private float BargainScore(Antique listing, float totalCost, NpcBehaviorProfile profile)
        {
            if (totalCost <= 0f) return 0f;

            float worth = ExpectedValue(listing, profile);
            int holdPeriods = profile.PatientSellAfterWeeks > 0
                ? profile.PatientSellAfterWeeks
                : Mathf.Max(1, profile.MinHoldingWeeksBeforeSell);

            float carry = holdPeriods * MarginalUpkeepPerItem();
            return (worth - totalCost - carry) / totalCost;
        }

        /// <summary>The return a trade must clear. Doubled while a contract is outstanding.</summary>
        private float BargainThreshold(NpcBehaviorProfile profile)
        {
            float bar = profile.MinBargainScore;
            return HasActiveCommitments() ? bar * Policy.CommittedBargainPenalty : bar;
        }

        /// <summary>What one more item adds to this week's warehouse upkeep.</summary>
        private float MarginalUpkeepPerItem() => TraderValuation.CarryPerWeek(Inventory);

        /// <summary>
        /// What the antique is worth in its condition today, with the profile's condition premium.
        /// Used where a coming move must not sway the decision, such as filling a contract.
        /// </summary>
        private float FairValue(Antique listing, NpcBehaviorProfile profile) =>
            TraderValuation.Fundamental(listing, _economyManager?.Market, profile);

        /// <summary>
        /// The seasonal price change this trader expects for a type over its horizon, weighted by
        /// its foresight. 0 for traders who ignore the calendar.
        /// </summary>
        private float SeasonalOutlook(string definitionId, NpcBehaviorProfile profile)
        {
            if (profile.SeasonalForesight <= 0f || profile.SeasonalHorizonWeeks <= 0 || _economyManager == null) return 0f;

            float ratio = _economyManager.SeasonalPriceOutlook(definitionId, profile.SeasonalHorizonWeeks);
            return profile.SeasonalForesight * (ratio - 1f);
        }

        /// <summary>The trend this trader extrapolates for a type: 0.1 = expects +10%.</summary>
        private float TrendOutlook(string definitionId, NpcBehaviorProfile profile) =>
            TraderHelper.TrendOutlook(_economyManager?.Market, definitionId, profile);

        /// <summary>
        /// What the trader thinks the antique is worth, its whole view of the market included:
        /// believed news, the season, the trend and the dependency graph, in one number.
        /// </summary>
        private float ExpectedValue(Antique listing, NpcBehaviorProfile profile) =>
            TraderValuation.Worth(listing, _economyManager?.Market, WorldView, profile, HoldWeeks(profile));

        /// <summary>
        /// Price ceiling for stock bought to fill a contract, against fair value with the outlook
        /// excluded. The binding cap is the contract's reward per unit, tested in BuildOfferPlan.
        /// </summary>
        private bool IsAcceptableContractPrice(Antique listing, NpcBehaviorProfile profile) =>
            listing.SalePrice <= FairValue(listing, profile) * ContractValueCeiling(profile);

        /// <summary>
        /// How far over fair value a contract purchase may go: nothing at RiskTolerance 0, the
        /// policy's risk premium at 1.
        /// </summary>
        private static float ContractValueCeiling(NpcBehaviorProfile profile) =>
            1f + Mathf.Clamp01(profile.RiskTolerance) * ContractRiskPremium;

        /// <summary>A regular purchase may never eat into the cash a taken contract needs.</summary>
        private bool PassesCashFloor(float cost) =>
            Core.Money.Covers(Inventory.Cash - (_contractReserve + _planReserve) * ReserveSafetyMargin, cost);

        /// <summary> Share of its cash this NPC may tie up in contract purchases at once. Scales with RiskTolerance. /// </summary>
        private float ContractCashShareFor(NpcBehaviorProfile profile) =>
            Mathf.Clamp01(Policy.ContractCashShareBase
                + Policy.ContractCashShareRiskSpan * Mathf.Clamp01(profile.RiskTolerance));

        private bool HasActiveCommitments()
        {
            if (_contractManager == null) return false;

            foreach (var contractId in Inventory.CommittedContractIds)
            {
                var contract = _contractManager.GetById(contractId);
                if (contract != null && contract.Status == ContractStatus.Active && MissingFor(contract) > 0)
                    return true;
            }

            return false;
        }

        private bool CanConsider(Antique listing, NpcBehaviorProfile profile)
        {
            if (listing == null) return false;
            if (Inventory.Owns(listing.Id)) return false;              // its own listing
            if (listing.Condition < profile.MinConditionToBuy) return false;
            return true;
        }

        private bool IsInterestedIn(AntiqueDefinition def, NpcBehaviorProfile profile)
        {
            if (def == null) return false;
            bool typeOk = profile.PreferredTypes.Count == 0 || profile.PreferredTypes.Contains(def.Type);
            bool countryOk = profile.PreferredCountries.Count == 0 || profile.PreferredCountries.Contains(def.Country);
            bool periodOk = profile.PreferredCenturies.Count == 0 || profile.PreferredCenturies.Contains(def.Century);
            return typeOk && countryOk && periodOk;
        }
    }
}
