using System.Collections.Generic;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.Market;
using UnityEngine;
using Antique = AntiqueTradingSimulator.Market.Antique;

namespace AntiqueTradingSimulator.Diagnostics
{
    /// <summary>
    /// The dimension tables: the run's configuration, the traders and their profiles, the antique
    /// types, every antique instance, every contract and every workshop. Written once per entity.
    ///
    /// dim_run reads the managers' private serialized fields by reflection. That keeps the whole
    /// configuration in the output without widening any manager's API; it runs once at startup.
    /// </summary>
    public sealed class DimensionRecorder
    {
        private readonly TelemetryRecorder _host;
        private readonly TelemetryRun _run;

        private readonly HashSet<string> _traders = new HashSet<string>();
        private readonly HashSet<string> _antiques = new HashSet<string>();
        private readonly HashSet<string> _contracts = new HashSet<string>();

        public DimensionRecorder(TelemetryRecorder host)
        {
            _host = host;
            _run = host.Run;
        }

        // ---------------------------------------------------------------- dim_run

        public void WriteRun()
        {
            var table = _run.Table(T.DimRunName, T.DimRunHeader);
            var economy = _host.Economy;
            var board = _host.Board;
            var clock = _host.Clock;

            _run.StampRun(table)
                .S(_run.StartedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ"))
                .S(TelemetryRun.SchemaVersion)
                .S(Application.unityVersion)
                .S(Application.version)
                .I(TelemetrySeed.Seed)
                .I(clock.CampaignLength)
                .D(clock.StartDate)
                .I(clock.TicksPerDay)
                .I(clock.TicksPerPeriod)
                .F(clock.SecondsPerWeek)
                .F(TelemetryRun.Field(economy, "defaultInitialSupply", 0f))
                .F(TelemetryRun.Field(economy, "defaultInitialDemand", 0f))
                .F(TelemetryRun.Field(economy, "supplyReversionRate", 0f))
                .F(TelemetryRun.Field(economy, "demandReversionRate", 0f))
                .I(TelemetryRun.Field(economy, "initialListingCount", 0))
                .I(TelemetryRun.Field(economy, "newListingsPerWeek", 0))
                .I(TelemetryRun.Field(economy, "anonymousListingLifetimeWeeks", 0))
                .F(AntiqueTradingSimulator.Market.Market.ListingFeeRate)
                .F(AntiqueTradingSimulator.Market.Market.LocalListingWeight)
                .F(AntiqueTradingSimulator.Market.Market.DomesticListingWeight)
                .F(AntiqueTradingSimulator.Market.Market.InternationalListingWeight)
                .I(TelemetryRun.Field(board, "contractsGeneratedPerWeek", 0))
                .I(TelemetryRun.Field(board, "maxActiveContracts", 0))
                .I(TelemetryRun.Field(board, "initialContractCount", 0))
                .I(TelemetryRun.Field(board, "minQuantity", 0))
                .I(TelemetryRun.Field(board, "maxQuantity", 0))
                .I(TelemetryRun.Field(board, "minDurationWeeks", 0))
                .I(TelemetryRun.Field(board, "maxDurationWeeks", 0))
                .I(board != null ? board.ShortestFeasibleDuration : 0)
                .F(TelemetryRun.Field(board, "pairedRequirementChance", 0f))
                .I(TelemetryRun.Field(board, "minTypesForPair", 0))
                .F(TelemetryRun.Field(board, "supplyShareForContract", 0f))
                .I(TelemetryRun.Field(board, "maxGenerationAttempts", 0))
                .F(TelemetryRun.Field(board, "demandPerRequestedPiece", 0f))
                .F(TelemetryRun.Field(board, "maxContractDemandPerType", 0f))
                .F(TelemetryRun.Field(board, "minRewardMultiplier", 0f))
                .F(TelemetryRun.Field(board, "maxRewardMultiplier", 0f))
                .F(TelemetryRun.Field(board, "maxUrgencyBonusMultiplier", 0f))
                .F(TelemetryRun.Field(board, "exclusiveContractChance", 0f))
                .F(TelemetryRun.Field(board, "exclusivePenaltyFraction", 0f))
                .F(TelemetryRun.Field(board, "standardContractWeight", 0f))
                .F(TelemetryRun.Field(board, "lockedClassPreviewWeight", 0f))
                .S(board != null ? TelemetryRun.Join(board.ReputationClasses, c => c != null ? c.Name : null) : "")
                .I(_host.Npcs != null ? _host.Npcs.NPCs.Count : 0)
                .F(TelemetryRun.Field(_host.Npcs, "defaultStartingCash", 0f))
                .B(economy.CollectorSettings != null && economy.CollectorSettings.Enabled)
                .I(AntiqueDatabase.GetAll() != null ? AntiqueDatabase.GetAll().Count : 0)
                .End();
        }

        // ---------------------------------------------------------------- dim_trader

        /// <summary>Writes any trader not written yet. Safe to call repeatedly.</summary>
        public void WriteTraders()
        {
            if (_host.Player != null) WritePlayer(_host.Player);

            if (_host.Npcs == null) return;
            foreach (var npc in _host.Npcs.NPCs)
                if (npc != null) WriteNpc(npc);
        }

        public void WritePlayer(PlayerTrader player)
        {
            if (player == null || !_traders.Add(Antique.PlayerOwnerId)) return;

            var table = _run.Table(T.DimTraderName, T.DimTraderHeader);
            _run.StampRun(table)
                .S(Antique.PlayerOwnerId)
                .S(player.Company != null ? player.Company.CompanyName : "Player")
                .S("Player")
                .N()                                   // profile_id
                .N()                                   // profile_name
                .F(player.Inventory != null ? player.Inventory.Cash : 0f)
                .E(player.AccessLevel)
                .N(52)                                 // the profile columns: the player has none
                .End();
        }

        public void WriteNpc(NPCTrader npc)
        {
            if (npc == null || !_traders.Add(npc.Id)) return;

            var profile = npc.Profile;
            var table = _run.Table(T.DimTraderName, T.DimTraderHeader);
            var r = _run.StampRun(table)
                .S(npc.Id)
                .S(npc.TraderName)
                .S("Npc")
                .S(npc.ProfileId)
                .S(profile != null ? profile.ProfileName : "")
                .F(profile != null ? profile.StartingCash : 0f)
                .E(npc.AccessLevel);

            if (profile == null)
            {
                r.N(52).End();
                return;
            }

            r.I(profile.ActionsPerPeriod)
                .F(profile.WeeklyBudgetFraction)
                .F(profile.MinBargainScore)
                .F(profile.MaxPriceMultiplierWillingToPay)
                .F(profile.MinConditionToBuy)
                .F(profile.ConditionPremium)
                .F(profile.ProfitTargetMultiplier)
                .F(profile.PatientProfitMultiplier)
                .I(profile.MinHoldingWeeksBeforeSell)
                .I(profile.PatientSellAfterWeeks)
                .I(profile.RelistAfterWeeks)
                .F(profile.ListingMarkup)
                .F(profile.ListingPriceCutPerWeek)
                .F(profile.MinListingMultiplier)
                .F(profile.StopLossMultiplier)
                .F(profile.RiskTolerance)
                .B(profile.NeverSells)
                .B(profile.ListsOnMarket)
                .F(profile.ListingChance)
                .B(profile.TakesContracts)
                .F(profile.ContractSlotShare)
                .I(profile.ContractPanicWeeks)
                .B(profile.Restores)
                .B(profile.BuysToRestore)
                .B(profile.RestoresForContracts)
                .I(profile.MaxConcurrentRestorations)
                .I(profile.MaxRestorationWeeks)
                .F(profile.MaxRestorationShareOfCash)
                .F(profile.RestorationHurdle)
                .F(profile.RumourPlantChance)
                .B(profile.ListsIntoOwnRumour)
                .F(profile.ExitPumpConfidence)
                .F(profile.ExitTargetRatio)
                .I(profile.ExitDumpLeadWeeks)
                .F(profile.SeasonalForesight)
                .I(profile.SeasonalHorizonWeeks)
                .F(profile.AuctionBudgetShare)
                .F(profile.AuctionValueMultiplier)
                .F(profile.AuctionFeverChance)
                .F(profile.AuctionFeverRaise)
                .F(profile.AuctionHesitation)
                .F(profile.AuctionNewsPremium)
                .F(profile.LeakTrust)
                .F(profile.RumorTrust)
                .I(profile.MinReactionDelayWeeks)
                .I(profile.MaxReactionDelayWeeks)
                .S(TelemetryRun.Join(profile.PreferredTypes, t => t.ToString()))
                .S(TelemetryRun.Join(profile.PreferredCountries, c => c.ToString()))
                .S(TelemetryRun.Join(profile.PreferredCenturies, c => c.ToString()))
                .F(profile.TrendSensitivity)
                .I(profile.TrendWindowWeeks)
                .F(profile.MaxTrendBoost)
                .End();
        }

        // ---------------------------------------------------------------- dim_antique_definition

        public void WriteDefinitions()
        {
            var all = AntiqueDatabase.GetAll();
            if (all == null) return;

            var table = _run.Table(T.DimAntiqueDefinitionName, T.DimAntiqueDefinitionHeader);
            var market = _host.Market;

            foreach (var def in all)
            {
                if (def == null) continue;
                var state = market != null ? market.GetTypeState(def.Id) : null;

                _run.StampRun(table)
                    .S(def.Id)
                    .S(def.DisplayName)
                    .E(def.Type)
                    .E(def.Century)
                    .E(def.Country)
                    .F(def.BasePrice)
                    .I(def.EditionSize)
                    .F(state != null ? state.BaselineSupply : 0f)
                    .F(state != null ? state.BaselineDemand : 0f)
                    .End();
            }
        }

        // ---------------------------------------------------------------- dim_antique

        /// <summary>Writes an antique the first time it is seen. Later calls do nothing.</summary>
        public void EnsureAntique(Antique antique, string origin)
        {
            if (antique == null || !_antiques.Add(antique.Id)) return;

            var table = _run.Table(T.DimAntiqueName, T.DimAntiqueHeader);
            _run.StampRun(table)
                .S(antique.Id)
                .S(antique.DefinitionId)
                .I(_run.CurrentSeq)
                .I(_run.Week)
                .I(_run.Tick)
                .S(origin)
                .F(antique.Condition)
                .F(antique.PriceFactor)
                .I(antique.EditionNumber)
                .E(antique.ShippingZone)
                .F(antique.CurrentPrice)
                .I(antique.MarketListedOnWeek)
                .End();
        }

        // ---------------------------------------------------------------- dim_contract

        public void EnsureContract(Contract contract, string createdBy)
        {
            if (contract == null || !_contracts.Add(contract.ContractId)) return;

            var requirement = contract.Requirement;
            var market = _host.Market;
            var matching = requirement != null ? requirement.MatchingDefinitions() : null;

            int qualifying = 0;
            float cheapest = float.NaN;
            if (market != null && requirement != null)
            {
                var listings = market.Listings;
                for (int i = 0; i < listings.Count; i++)
                {
                    if (!requirement.IsSatisfiedBy(listings[i])) continue;
                    qualifying++;
                    if (float.IsNaN(cheapest) || listings[i].SalePrice < cheapest) cheapest = listings[i].SalePrice;
                }
            }

            float expected = ContractMath.ExpectedSupply(_host, requirement, contract.DeadlineWeek);
            float share = TelemetryRun.Field(_host.Board, "supplyShareForContract", 0.5f);

            var table = _run.Table(T.DimContractName, T.DimContractHeader);
            _run.StampRun(table)
                .S(contract.ContractId)
                .I(_run.CurrentSeq)
                .I(contract.CreatedWeek)
                .E(contract.Type)
                .S(contract.ClassName)
                .I(contract.DurationWeeks)
                .I(contract.DeadlineWeek)
                .I(requirement != null ? requirement.Quantity : 0)
                .F(contract.RewardPerUnit)
                .F(contract.TotalReward)
                .F(contract.Premium)
                .F(contract.Penalty)
                .I(contract.RequiredReputation)
                .S(requirement != null ? requirement.EffectiveAttributes.ToString().Replace(", ", "|") : "")
                .E(requirement != null && requirement.Uses(ContractAttributeScope.AntiqueType) ? (System.Enum)requirement.AntiqueType : null)
                .E(requirement != null && requirement.Uses(ContractAttributeScope.Country) ? (System.Enum)requirement.Country : null)
                .E(requirement != null && requirement.Uses(ContractAttributeScope.Century) ? (System.Enum)requirement.Century : null)
                .F(requirement != null && requirement.HasMinCondition ? requirement.MinCondition : (float?)null)
                .F(requirement != null && requirement.HasMinValue ? requirement.MinValue : (float?)null)
                .I(matching != null ? matching.Count : 0)
                .S(TelemetryRun.Join(matching, d => d != null ? d.Id : null))
                .F(expected)
                .F(expected * share)
                .I(qualifying)
                .F(float.IsNaN(cheapest) ? (float?)null : cheapest)
                .S(createdBy)
                .End();
        }

        // ---------------------------------------------------------------- dim_restoration_option

        public void WriteRestorationOptions()
        {
            if (_host.Restorations == null) return;

            var table = _run.Table(T.DimRestorationOptionName, T.DimRestorationOptionHeader);
            foreach (var option in _host.Restorations.Options)
            {
                if (option == null) continue;

                _run.StampRun(table)
                    .S(option.Id)
                    .S(option.DisplayName)
                    .F(option.TargetCondition)
                    .F(option.MaxCondition)
                    .I(option.DurationWeeks)
                    .F(option.CostRate)
                    .F(option.MinimumFee)
                    .F(option.SuccessChance)
                    .F(option.PartialChance)
                    .F(option.FailureChance)
                    .F(option.FailureLossMin)
                    .F(option.FailureLossMax)
                    .I(option.RequiredReputation)
                    .End();
            }
        }
    }
}
