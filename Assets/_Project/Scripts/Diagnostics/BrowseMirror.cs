using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.Market;
using UnityEngine;
using Antique = AntiqueTradingSimulator.Market.Antique;

namespace AntiqueTradingSimulator.Diagnostics
{
    /// <summary>
    /// Replays NPCTrader.TierBrowseMarket from outside, counting how many listings each gate threw out.
    /// </summary>
    public sealed class BrowseMirror
    {
        private readonly TelemetryRecorder _host;
        private readonly TelemetryRun _run;

        /// <summary>NPCTrader.CommittedBargainPenalty.</summary>
        private const float CommittedBargainPenalty = 2f;

        /// <summary>NPCTrader.ReserveSafetyMargin.</summary>
        private const float ReserveSafetyMargin = 1.25f;

        private struct Funnel
        {
            public bool Valid;
            public string TraderId;
            public int Scanned, OwnsIt, NotInterested, PriceTooHigh, ConditionFloor;
            public int OverBudget, CashFloor, BelowBar, NoRestoration;
            public int CandidatesAsIs, CandidatesRestore;
            public string BestAsIsId, BestRestoreId;
            public float BestAsIsReturn, BestAsIsCost, BestRestoreReturn, BestRestoreCost;
            public float Threshold, RestorationBar, Budget, Cash, Headroom;
            public int HoldWeeks;
            public bool Committed;
            public string ChosenPath;
            public string ChosenId;
        }

        private Funnel _pending;

        public BrowseMirror(TelemetryRecorder host)
        {
            _host = host;
            _run = host.Run;
        }

        public void CaptureBrowse(NPCTrader npc, string traderId)
        {
            _pending = Measure(npc, traderId);
        }

        /// <summary>
        /// Writes the captured funnel when the slot ended in a market browse. Browsing is the last
        /// rung, so a slot where nothing fired also reached it and is recorded.
        /// </summary>
        public void EmitBrowse(NPCTrader npc, string traderId, string tier)
        {
            if (!_pending.Valid || _pending.TraderId != traderId) return;
            if (!string.IsNullOrEmpty(tier) && tier != "TierBrowseMarket") { _pending.Valid = false; return; }

            var funnel = _pending;
            _pending.Valid = false;

            bool bought = tier == "TierBrowseMarket";
            bool predicted = funnel.ChosenPath != "None";

            var table = _run.Table(T.FactBrowseFunnelName, T.FactBrowseFunnelHeader);
            _run.Stamp(table)
                .S(traderId)
                .I(funnel.Scanned)
                .I(funnel.OwnsIt)
                .I(funnel.NotInterested)
                .I(funnel.PriceTooHigh)
                .I(funnel.ConditionFloor)
                .I(funnel.OverBudget)
                .I(funnel.CashFloor)
                .I(funnel.BelowBar)
                .I(funnel.NoRestoration)
                .I(funnel.CandidatesAsIs)
                .I(funnel.CandidatesRestore)
                .S(funnel.BestAsIsId)
                .F(funnel.BestAsIsId != null ? funnel.BestAsIsReturn : (float?)null)
                .F(funnel.BestAsIsId != null ? funnel.BestAsIsCost : (float?)null)
                .S(funnel.BestRestoreId)
                .F(funnel.BestRestoreId != null ? funnel.BestRestoreReturn : (float?)null)
                .F(funnel.BestRestoreId != null ? funnel.BestRestoreCost : (float?)null)
                .F(funnel.Threshold)
                .F(funnel.RestorationBar)
                .I(funnel.HoldWeeks)
                .F(funnel.Budget)
                .F(funnel.Cash)
                .F(funnel.Headroom)
                .B(funnel.Committed)
                .S(funnel.ChosenPath)
                .S(funnel.ChosenId)
                .B(predicted == bought)
                .End();
        }

        // ---------------------------------------------------------------- the gauntlet

        private Funnel Measure(NPCTrader npc, string traderId)
        {
            var funnel = new Funnel { TraderId = traderId, ChosenPath = "None" };

            var profile = npc != null ? npc.Profile : null;
            var market = _host.Market;
            if (profile == null || market == null) return funnel;

            var state = npc.CaptureState();
            var inventory = npc.Inventory;

            funnel.Valid = true;
            funnel.Cash = inventory.Cash;
            funnel.Budget = state != null ? state.WeekBudget : 0f;
            funnel.Committed = HasActiveCommitments(npc);
            funnel.HoldWeeks = HoldWeeks(profile);

            float reserves = state != null ? state.ContractReserve + state.PlanReserve : 0f;
            funnel.Headroom = inventory.Cash - reserves * ReserveSafetyMargin;

            float bar = profile.MinBargainScore * (funnel.Committed ? CommittedBargainPenalty : 1f);
            funnel.Threshold = bar / Mathf.Max(1, funnel.HoldWeeks);
            funnel.RestorationBar = TargetWeeklyReturn(profile) * profile.RestorationHurdle;

            float bestAsIs = funnel.Threshold;
            float bestRestore = funnel.Threshold;

            var listings = market.Listings;
            funnel.Scanned = listings.Count;

            for (int i = 0; i < listings.Count; i++)
            {
                var listing = listings[i];
                if (listing == null) continue;

                if (inventory.Owns(listing.Id)) { funnel.OwnsIt++; continue; }
                if (!IsInterestedIn(listing.Definition, profile)) { funnel.NotInterested++; continue; }
                if (!IsAcceptablePrice(listing, profile)) { funnel.PriceTooHigh++; continue; }

                float cost = EstimateTotalCost(listing, inventory);
                if (cost <= 0f || !Core.Money.Covers(funnel.Budget, cost)) { funnel.OverBudget++; continue; }
                if (!Core.Money.Covers(funnel.Headroom, cost)) { funnel.CashFloor++; continue; }

                if (listing.Condition < profile.MinConditionToBuy)
                {
                    funnel.ConditionFloor++;
                }
                else
                {
                    float weekly = BargainScore(listing, cost, profile) / Mathf.Max(1, funnel.HoldWeeks);

                    // Counted, not merely skipped: the bargain bar is the gate most worth tuning,
                    // and leaving it uncounted made the column read zero.
                    if (weekly < funnel.Threshold)
                    {
                        funnel.BelowBar++;
                    }
                    else
                    {
                        funnel.CandidatesAsIs++;
                        if (weekly >= bestAsIs)
                        {
                            bestAsIs = weekly;
                            funnel.BestAsIsId = listing.Id;
                            funnel.BestAsIsReturn = weekly;
                            funnel.BestAsIsCost = cost;
                        }
                    }
                }

                if (!profile.Restores || !profile.BuysToRestore) continue;

                float fee = CheapestUsableFee(npc, listing, profile, out float expectedValue);
                if (float.IsNaN(fee)) { funnel.NoRestoration++; continue; }

                int weeks = Mathf.Max(1, funnel.HoldWeeks);
                float capital = cost + fee;
                float restoreWeekly = capital > 0f ? (expectedValue - capital) / (capital * weeks) : 0f;

                // A workshop that exists but does not clear the hurdle is a rejection too.
                if (restoreWeekly < funnel.RestorationBar)
                {
                    funnel.NoRestoration++;
                    continue;
                }

                funnel.CandidatesRestore++;

                if (restoreWeekly >= bestRestore)
                {
                    bestRestore = restoreWeekly;
                    funnel.BestRestoreId = listing.Id;
                    funnel.BestRestoreReturn = restoreWeekly;
                    funnel.BestRestoreCost = cost + fee;
                }
            }

            bool takeRestore = funnel.BestRestoreId != null &&
                               (funnel.BestAsIsId == null || bestRestore >= bestAsIs * profile.RestorationHurdle);

            if (takeRestore)
            {
                funnel.ChosenPath = "Restore";
                funnel.ChosenId = funnel.BestRestoreId;
            }
            else if (funnel.BestAsIsId != null)
            {
                funnel.ChosenPath = "AsIs";
                funnel.ChosenId = funnel.BestAsIsId;
            }

            return funnel;
        }

        // ---------------------------------------------------------------- mirrored formulas

        private static int HoldWeeks(NpcBehaviorProfile profile) =>
            profile.PatientSellAfterWeeks > 0 ? profile.PatientSellAfterWeeks : Mathf.Max(1, profile.MinHoldingWeeksBeforeSell);

        private static float TargetWeeklyReturn(NpcBehaviorProfile profile) =>
            profile.NeverSells ? 0f : Mathf.Max(0f, profile.ProfitTargetMultiplier - 1f) / HoldWeeks(profile);

        private static bool IsInterestedIn(AntiqueDefinition definition, NpcBehaviorProfile profile)
        {
            if (definition == null) return false;

            bool typeOk = profile.PreferredTypes.Count == 0 || profile.PreferredTypes.Contains(definition.Type);
            bool countryOk = profile.PreferredCountries.Count == 0 || profile.PreferredCountries.Contains(definition.Country);
            bool periodOk = profile.PreferredCenturies.Count == 0 || profile.PreferredCenturies.Contains(definition.Century);
            return typeOk && countryOk && periodOk;
        }

        private float FairValue(Antique listing, NpcBehaviorProfile profile)
        {
            var market = _host.Market;
            var typeState = market != null ? market.GetTypeState(listing.DefinitionId) : null;
            float baseValue = PriceEngine.CalculateReferencePrice(listing.BasePrice, typeState) * listing.Condition;

            return baseValue
                   * (1f + profile.ConditionPremium * Mathf.Max(0f, listing.Condition - 0.5f))
                   * (1f + SeasonalOutlook(listing.DefinitionId, profile));
        }

        private float SeasonalOutlook(string definitionId, NpcBehaviorProfile profile)
        {
            if (profile.SeasonalForesight <= 0f || profile.SeasonalHorizonWeeks <= 0 || _host.Economy == null) return 0f;

            float ratio = _host.Economy.SeasonalPriceOutlook(definitionId, profile.SeasonalHorizonWeeks);
            return profile.SeasonalForesight * (ratio - 1f);
        }

        private bool IsAcceptablePrice(Antique listing, NpcBehaviorProfile profile) =>
            listing.SalePrice <= FairValue(listing, profile) * profile.MaxPriceMultiplierWillingToPay;

        private float BargainScore(Antique listing, float totalCost, NpcBehaviorProfile profile)
        {
            if (totalCost <= 0f) return 0f;
            return (FairValue(listing, profile) - totalCost) / totalCost;
        }

        private float EstimateTotalCost(Antique listing, TraderInventory buyer) =>
            TraderHelper.EstimateTotalCost(listing, _host.Transport, TransportOption.Standard, buyer);

        /// <summary>
        /// Cheapest workshop this trader could use on a listing it does not own, with the
        /// risk-weighted value the antique would come back with. NaN when none applies.
        /// </summary>
        private float CheapestUsableFee(NPCTrader npc, Antique listing, NpcBehaviorProfile profile, out float expectedValue)
        {
            expectedValue = 0f;
            if (_host.Restorations == null || !profile.Restores) return float.NaN;

            int reputation = npc.Reputation;
            float best = float.NaN;

            foreach (var option in _host.Restorations.Options)
            {
                if (option == null || option.RequiredReputation > reputation) continue;
                if (option.TargetCondition <= listing.Condition) continue;
                if (option.DurationWeeks > profile.MaxRestorationWeeks) continue;

                var quote = _host.Restorations.QuoteProspective(npc, listing, option);
                if (quote == null) continue;
                if (!Core.Money.Covers(npc.Inventory.Cash * profile.MaxRestorationShareOfCash, quote.Cost)) continue;
                if (!float.IsNaN(best) && quote.Cost >= best) continue;

                float perfect = quote.StartCondition > 0f ? listing.CurrentPrice / quote.StartCondition : 0f;
                if (perfect <= 0f) continue;

                float averageFailure = Mathf.Max(quote.WorstCondition,
                    quote.StartCondition - (option.FailureLossMin + option.FailureLossMax) * 0.5f);

                best = quote.Cost;
                expectedValue =
                    option.SuccessChance * ValueAt(listing, perfect, quote.TargetCondition, profile) +
                    option.PartialChance * ValueAt(listing, perfect, quote.PartialCondition, profile) +
                    option.FailureChance * ValueAt(listing, perfect, averageFailure, profile);
            }

            return best;
        }

        private float ValueAt(Antique listing, float perfectValue, float condition, NpcBehaviorProfile profile) =>
            Core.Money.RoundToShilling(perfectValue * condition
                * (1f + profile.ConditionPremium * Mathf.Max(0f, condition - 0.5f))
                * (1f + SeasonalOutlook(listing.DefinitionId, profile)));

        private bool HasActiveCommitments(NPCTrader npc)
        {
            if (_host.Board == null) return false;

            foreach (string contractId in npc.Inventory.CommittedContractIds)
            {
                var contract = _host.Board.GetById(contractId);
                if (contract == null || contract.Status != Contracts.ContractStatus.Active) continue;

                int reserved = ContractMath.ReservedMatching(npc.Inventory, contract);
                if (contract.Requirement.Quantity - reserved > 0) return true;
            }

            return false;
        }
    }
}
