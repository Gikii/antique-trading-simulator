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
    /// Restoration planning: the workshop bar, the best leg for a piece and the step that starts a job.
    /// One part of NPCTrader, which is a partial class.
    /// </summary>
    public partial class NPCTrader
    {
        // ---------------------------------------------------------------- restoration bar

        /// <summary>A workshop worth sending one antique to, and what that is worth per week.</summary>
        private readonly struct RestorationLeg
        {
            public readonly RestorationOption Option;
            public readonly float Fee;
            public readonly int Weeks;
            /// <summary>Risk-adjusted value the antique is worth on its return, at today's prices.</summary>
            public readonly float ExpectedValue;
            public readonly float WeeklyReturn;

            public RestorationLeg(RestorationOption option, float fee, int weeks, float expectedValue, float weeklyReturn)
            {
                Option = option;
                Fee = fee;
                Weeks = weeks;
                ExpectedValue = expectedValue;
                WeeklyReturn = weeklyReturn;
            }

            public bool Exists => Option != null;
        }

        /// <summary>How long this trader usually holds a piece before selling it.</summary>
        private int HoldWeeks(NpcBehaviorProfile profile) =>
            profile.PatientSellAfterWeeks > 0 ? profile.PatientSellAfterWeeks : Mathf.Max(1, profile.MinHoldingWeeksBeforeSell);

        /// <summary>
        /// The return per week this trader expects of its own capital: its profit target over its
        /// usual hold. A collector that never sells has no such target, so adding value is enough.
        /// </summary>
        private float TargetWeeklyReturn(NpcBehaviorProfile profile) =>
            profile.NeverSells ? 0f : Mathf.Max(0f, profile.ProfitTargetMultiplier - 1f) / HoldWeeks(profile);

        /// <summary>
        /// What the antique is worth at a condition, with the profile's condition premium and season,
        /// rounded as the market would price it — so a gain too small to show is read as no gain.
        /// </summary>
        private float ValueAtCondition(Antique antique, float perfectValue, float condition, NpcBehaviorProfile profile) =>
            Core.Money.RoundToShilling(perfectValue * condition
                * (1f + profile.ConditionPremium * Mathf.Max(0f, condition - 0.5f))
                * (1f + SeasonalOutlook(antique.DefinitionId, profile)));

        /// <summary>
        /// What one workshop is worth for one antique: the value it is expected to add, less the fee,
        /// the downside the profile fears and the upkeep, over the capital and weeks it ties up.
        /// </summary>
        private RestorationLeg EvaluateLeg(Antique antique, RestorationQuote quote, NpcBehaviorProfile profile, int delayWeeks)
        {
            var option = quote.Option;
            float perfectValue = quote.StartCondition > 0f ? antique.CurrentPrice / quote.StartCondition : 0f;
            if (perfectValue <= 0f) return default;

            float averageFailure = Mathf.Max(quote.WorstCondition,
                quote.StartCondition - (option.FailureLossMin + option.FailureLossMax) * 0.5f);

            float valueNow = ValueAtCondition(antique, perfectValue, quote.StartCondition, profile);
            float expected =
                option.SuccessChance * ValueAtCondition(antique, perfectValue, quote.TargetCondition, profile) +
                option.PartialChance * ValueAtCondition(antique, perfectValue, quote.PartialCondition, profile) +
                option.FailureChance * ValueAtCondition(antique, perfectValue, averageFailure, profile);

            expected -= (1f - profile.RiskTolerance) * option.FailureChance
                        * Mathf.Max(0f, valueNow - ValueAtCondition(antique, perfectValue, quote.WorstCondition, profile));

            int weeks = Mathf.Max(1, quote.DurationWeeks + Mathf.Max(0, delayWeeks));
            float capital = quote.Cost + valueNow;
            if (capital <= 0f) return default;

            float weekly = (expected - valueNow - quote.Cost - weeks * MarginalUpkeepPerItem()) / (capital * weeks);
            return new RestorationLeg(option, quote.Cost, quote.DurationWeeks, expected, weekly);
        }

        /// <summary>
        /// Best workshop for an antique, by risk-adjusted return per pound per week. The cheap gates
        /// run first so a quote is only built for workshops that could take the job.
        /// </summary>
        private RestorationLeg BestLegFor(Antique antique, NpcBehaviorProfile profile, bool owned, int delayWeeks, float barWeeklyReturn)
        {
            if (antique == null || _restorationManager == null || !profile.Restores) return default;
            if (OpenRestorationCount() >= profile.MaxConcurrentRestorations) return default;

            int reputation = Record.Reputation.Reputation;
            RestorationLeg best = default;

            foreach (var option in _restorationManager.Options)
            {
                if (option == null || option.RequiredReputation > reputation) continue;
                if (option.TargetCondition <= antique.Condition + ConditionEpsilon) continue;

                var quote = owned
                    ? _restorationManager.Quote(this, antique, option)
                    : _restorationManager.QuoteProspective(this, antique, option);
                if (quote == null) continue;
                if (owned && !quote.CanStart) continue;
                if (quote.DurationWeeks > profile.MaxRestorationWeeks) continue;
                if (!Core.Money.Covers(Inventory.Cash * profile.MaxRestorationShareOfCash, quote.Cost)) continue;
                if (!PassesCashFloor(quote.Cost)) continue;

                var leg = EvaluateLeg(antique, quote, profile, delayWeeks);
                if (!leg.Exists || leg.WeeklyReturn <= 0f || leg.WeeklyReturn < barWeeklyReturn) continue;

                if (!best.Exists || leg.WeeklyReturn > best.WeeklyReturn) best = leg;
            }

            return best;
        }

        /// <summary>Return per pound per week of buying a listing, restoring it and holding it to sell.</summary>
        private float BuyAndRestoreWeeklyReturn(float buyCost, RestorationLeg leg, NpcBehaviorProfile profile)
        {
            int weeks = Mathf.Max(1, leg.Weeks + HoldWeeks(profile));
            float capital = buyCost + leg.Fee;
            if (capital <= 0f) return 0f;

            return (leg.ExpectedValue - capital - weeks * MarginalUpkeepPerItem()) / (capital * weeks);
        }

        /// <summary>Return per pound per week of buying a listing and selling it on as it stands.</summary>
        private float BuyAsIsWeeklyReturn(Antique listing, float cost, NpcBehaviorProfile profile) =>
            cost > 0f ? BargainScore(listing, cost, profile) / HoldWeeks(profile) : 0f;

        /// <summary>Highest condition any workshop this NPC may use can reach.</summary>
        private float BestAchievableCondition()
        {
            if (_restorationManager == null) return 0f;

            float best = 0f;
            int reputation = Record.Reputation.Reputation;
            foreach (var option in _restorationManager.Options)
                if (option != null && option.RequiredReputation <= reputation)
                    best = Mathf.Max(best, option.TargetCondition);

            return best;
        }

        /// <summary>
        /// A listing the NPC would buy in order to restore: below its condition floor is allowed when
        /// a workshop can bring it back over.
        /// </summary>
        private bool CanConsiderForRestoration(Antique listing, NpcBehaviorProfile profile)
        {
            if (listing == null || Inventory.Owns(listing.Id)) return false;
            if (listing.Condition >= profile.MinConditionToBuy) return true;
            return BestAchievableCondition() >= profile.MinConditionToBuy;
        }

        /// <summary>Chance a finished restoration leaves the antique meeting a contract's requirement.</summary>
        private static float RestorationConfidence(ContractRequirement requirement, Antique antique, RestorationQuote quote)
        {
            var option = quote.Option;
            float confidence = 0f;

            if (requirement.IsSatisfiedAt(antique, quote.TargetCondition)) confidence += option.SuccessChance;
            if (requirement.IsSatisfiedAt(antique, quote.PartialCondition)) confidence += option.PartialChance;
            if (requirement.IsSatisfiedAt(antique, quote.WorstCondition)) confidence += option.FailureChance;

            return confidence;
        }

        /// <summary>How sure of the outcome the profile has to be before it restores for a contract.</summary>
        private static float RequiredConfidence(NpcBehaviorProfile profile) =>
            Mathf.Lerp(0.95f, 0.60f, profile.RiskTolerance);

        /// <summary>
        /// Cheapest workshop that would bring an antique up to a contract's requirement, in time from
        /// fromWeek and with enough confidence for the profile. Null if none would.
        /// </summary>
        private RestorationQuote BestContractQuote(Contract contract, Antique antique, int fromWeek, NpcBehaviorProfile profile, bool owned)
        {
            if (_restorationManager == null || !profile.Restores || !profile.RestoresForContracts) return null;
            if (OpenRestorationCount() >= profile.MaxConcurrentRestorations) return null;

            int latestArrival = LatestUsefulArrivalWeek(contract);
            int reputation = Record.Reputation.Reputation;
            float required = RequiredConfidence(profile);
            RestorationQuote best = null;

            foreach (var option in _restorationManager.Options)
            {
                if (option == null || option.RequiredReputation > reputation) continue;
                if (option.TargetCondition <= antique.Condition + ConditionEpsilon) continue;

                var quote = owned
                    ? _restorationManager.Quote(this, antique, option)
                    : _restorationManager.QuoteProspective(this, antique, option);
                if (quote == null) continue;
                if (owned && !quote.CanStart) continue;
                if (quote.DurationWeeks > profile.MaxRestorationWeeks) continue;

                // A failure leaves the slot empty, so the work has to finish with time to find a replacement.
                if (fromWeek + quote.DurationWeeks + profile.ContractPanicWeeks > latestArrival) continue;
                if (!Core.Money.Covers(Inventory.Cash * profile.MaxRestorationShareOfCash, quote.Cost)) continue;
                if (RestorationConfidence(contract.Requirement, antique, quote) < required) continue;

                if (best == null || quote.Cost < best.Cost) best = quote;
            }

            return best;
        }

        // ---------------------------------------------------------------- plan tick actions

        /// <summary>
        /// Carries out the next step of a plan already committed to: starting a restoration costed
        /// when the antique was acquired, or listing and then dumping stock into a planted rumour.
        /// </summary>
        private bool TierExecutePlans(int week, NpcBehaviorProfile profile)
        {
            if (_plans.Count == 0) return false;

            _planScratch.Clear();
            _planScratch.AddRange(_plans.Keys);

            for (int i = 0; i < _planScratch.Count; i++)
            {
                string antiqueId = _planScratch[i];
                var plan = PlanFor(antiqueId);
                var antique = Inventory.GetHolding(antiqueId);
                if (plan == null || antique == null) continue;

                if (plan.HasRestorationLeg && StartPlannedRestoration(antique, plan, profile)) return true;
            }

            return false;
        }

        /// <summary>
        /// Re-quotes a plan's restoration at today's prices and starts it, or clears the leg when it
        /// no longer pays. The fee joins the antique's cost basis so later sales price it in.
        /// </summary>
        private bool StartPlannedRestoration(Antique antique, Position plan, NpcBehaviorProfile profile)
        {
            if (_restorationManager == null)
            {
                plan.ClearRestorationLeg();
                return false;
            }

            if (IsRestoring(antique.Id)) return false;
            if (!antique.IsAvailable || antique.IsListedForSale || antique.IsReservedForContract) return false;

            var option = _restorationManager.Settings.Get(plan.RestorationOptionId);
            var quote = option != null ? _restorationManager.Quote(this, antique, option) : null;

            bool viable = quote != null && quote.CanStart && PassesCashFloor(quote.Cost);

            if (viable)
                viable = plan.Purpose == PlanPurpose.Contract
                    ? RestorationStillServesContract(antique, plan, quote, profile)
                    : ClearsResaleBar(EvaluateLeg(antique, quote, profile, 0), profile);

            if (!viable)
            {
                plan.ClearRestorationLeg();
                _planReserve = CalculatePlanReserve();
                return false;
            }

            if (!_restorationManager.StartRestoration(this, antique, option.Id))
            {
                plan.ClearRestorationLeg();
                _planReserve = CalculatePlanReserve();
                return false;
            }

            plan.CostBasis = Core.Money.RoundToShilling(plan.CostBasis + quote.Cost);
            _planReserve = CalculatePlanReserve();
            Debug.Log($"{TraderName} sent {antique.Name} to {option.DisplayName} for {Core.Money.Format(quote.Cost)} ({plan.Purpose}).");
            return true;
        }

        /// <summary>Whether a leg is worth paying for on its own merits, with no contract behind it.</summary>
        private bool ClearsResaleBar(RestorationLeg leg, NpcBehaviorProfile profile) =>
            leg.Exists && leg.WeeklyReturn > 0f
            && leg.WeeklyReturn >= TargetWeeklyReturn(profile) * profile.RestorationHurdle;

        /// <summary>True while the plan's contract is still live and the work would still land in time.</summary>
        private bool RestorationStillServesContract(Antique antique, Position plan, RestorationQuote quote, NpcBehaviorProfile profile)
        {
            var contract = _contractManager != null ? _contractManager.GetById(plan.ContractId) : null;
            if (contract == null || contract.Status != ContractStatus.Active) return false;
            if (CurrentWeek + quote.DurationWeeks + profile.ContractPanicWeeks > LatestUsefulArrivalWeek(contract)) return false;

            return RestorationConfidence(contract.Requirement, antique, quote) >= RequiredConfidence(profile);
        }

        /// <summary>A finished restoration ends the leg; the plan's remaining purpose runs from the next slot.</summary>
        private void HandleRestorationCompleted(RestorationRecord record)
        {
            if (record == null || !Inventory.Owns(record.AntiqueId)) return;

            var plan = PlanFor(record.AntiqueId);
            if (plan == null) return;

            plan.ClearRestorationLeg();
            _planReserve = CalculatePlanReserve();
        }
    }
}
