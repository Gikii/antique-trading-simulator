using System;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Logistics;
using UnityEngine;

namespace AntiqueTradingSimulator.Diagnostics
{
    /// <summary>
    /// Contract-side measurements shared by the dimension, progress and eligibility passes.
    /// Mirrors what ContractManager and NPCTrader do.
    /// </summary>
    public static class ContractMath
    {
        /// <summary>NPCTrader.ContractCashShareBase, mirrored.</summary>
        public const float ContractCashShareBase = 0.40f;

        /// <summary>NPCTrader.ContractCashShareRiskSpan, mirrored.</summary>
        public const float ContractCashShareRiskSpan = 0.35f;

        /// <summary>NPCTrader.ContractRiskPremium, mirrored.</summary>
        public const float ContractRiskPremium = 0.35f;

        /// <summary>NPCTrader.ContractCashShareFor, mirrored.</summary>
        public static float ContractCashShareFor(Agents.NpcBehaviorProfile profile)
        {
            if (profile == null) return ContractCashShareBase;
            return Mathf.Clamp01(ContractCashShareBase
                + ContractCashShareRiskSpan * Mathf.Clamp01(profile.RiskTolerance));
        }

        /// <summary>NPCTrader.SeasonalOutlook, mirrored.</summary>
        public static float SeasonalOutlook(TelemetryRecorder host, string definitionId,
            Agents.NpcBehaviorProfile profile)
        {
            if (profile == null || profile.SeasonalForesight <= 0f
                || profile.SeasonalHorizonWeeks <= 0 || host == null || host.Economy == null) return 0f;

            float ratio = host.Economy.SeasonalPriceOutlook(definitionId, profile.SeasonalHorizonWeeks);
            return profile.SeasonalForesight * (ratio - 1f);
        }

        /// <summary>NPCTrader.FairValue, mirrored.</summary>
        public static float FairValue(TelemetryRecorder host, Market.Antique listing,
            Agents.NpcBehaviorProfile profile)
        {
            if (listing == null || profile == null) return 0f;

            var market = host != null ? host.Market : null;
            var typeState = market != null ? market.GetTypeState(listing.DefinitionId) : null;
            float baseValue = Economy.PriceEngine.CalculateReferencePrice(listing.BasePrice, typeState)
                              * listing.Condition;

            return baseValue
                   * (1f + profile.ConditionPremium * Mathf.Max(0f, listing.Condition - 0.5f))
                   * (1f + SeasonalOutlook(host, listing.DefinitionId, profile));
        }

        /// <summary>
        /// NPCTrader.IsAcceptableContractPrice, mirrored: the ordinary ceiling widened by
        /// RiskTolerance. Without this the eligibility pass was profile-blind on price and reported
        /// identical supply for every profile.
        /// </summary>
        public static bool IsAcceptableContractPrice(TelemetryRecorder host, Market.Antique listing,
            Agents.NpcBehaviorProfile profile)
        {
            if (listing == null || profile == null) return false;

            return listing.SalePrice <= FairValue(host, listing, profile)
                * profile.MaxPriceMultiplierWillingToPay
                * (1f + Mathf.Clamp01(profile.RiskTolerance) * ContractRiskPremium);
        }
        /// <summary>What a qualifying listing looks like to a buyer right now.</summary>
        public struct Supply
        {
            public int Qualifying;
            public int Reachable;
            public float CheapestPrice;
            public float CheapestTotal;

            public bool HasCheapest => !float.IsNaN(CheapestTotal);
        }

        public static Func<ShippingZone, int> ExpressWeeks(TelemetryRecorder host)
        {
            var settings = host.Transport != null ? host.Transport.Settings : null;
            if (settings == null) return _ => 1;
            return zone => settings.GetBaseDurationWeeks(zone, TransportOption.Express);
        }

        public static float ExpectedSupply(TelemetryRecorder host, ContractRequirement requirement, int deadlineWeek)
        {
            var market = host.Market;
            if (requirement == null || market == null || host.Economy == null) return 0f;

            return ContractFeasibility.ExpectedSupply(requirement, market, host.Clock.CurrentWeek, deadlineWeek,
                host.Economy.ExpectedNewListingsPerWeek, ExpressWeeks(host));
        }

        /// <summary>
        /// Listings that satisfy the requirement now, how many of them could still arrive before
        /// <paramref name="latestArrival"/> by Express, and the cheapest of those with shipping.
        /// </summary>
        public static Supply Measure(TelemetryRecorder host, ContractRequirement requirement, int latestArrival)
        {
            var supply = new Supply { CheapestPrice = float.NaN, CheapestTotal = float.NaN };

            var market = host.Market;
            if (requirement == null || market == null) return supply;

            int week = host.Clock.CurrentWeek;
            var express = ExpressWeeks(host);
            var settings = host.Transport != null ? host.Transport.Settings : null;
            var listings = market.Listings;

            for (int i = 0; i < listings.Count; i++)
            {
                var listing = listings[i];
                if (listing == null || !requirement.IsSatisfiedBy(listing)) continue;

                supply.Qualifying++;
                if (week + express(listing.ShippingZone) > latestArrival) continue;

                supply.Reachable++;

                float shipping = settings != null
                    ? settings.GetCost(listing.SalePrice, listing.ShippingZone, TransportOption.Express)
                    : 0f;
                float total = listing.SalePrice + shipping;

                if (float.IsNaN(supply.CheapestTotal) || total < supply.CheapestTotal)
                {
                    supply.CheapestTotal = total;
                    supply.CheapestPrice = listing.SalePrice;
                }
            }

            return supply;
        }

        /// <summary>Deadline week less one: nothing arriving later can be handed in.</summary>
        public static int LatestUsefulArrivalWeek(Contract contract) => contract.DeadlineWeek - 1;

        /// <summary>Reserved for this contract and still matching, in transit included.</summary>
        public static int ReservedMatching(Economy.TraderInventory inventory, Contract contract)
        {
            if (inventory == null || contract == null) return 0;

            int count = 0;
            foreach (var pair in inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding == null || holding.ReservedForContractId != contract.ContractId) continue;
                if (!contract.Requirement.IsSatisfiedBy(holding)) continue;
                count++;
            }
            return count;
        }

        /// <summary>Reserved, arrived and still matching: deliverable right now.</summary>
        public static int ReadyCount(Economy.TraderInventory inventory, Contract contract)
        {
            if (inventory == null || contract == null) return 0;

            int count = 0;
            foreach (var pair in inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding == null || holding.ReservedForContractId != contract.ContractId) continue;
                if (!holding.IsAvailable) continue;
                if (!contract.Requirement.IsSatisfiedBy(holding)) continue;
                count++;
            }
            return count;
        }

        public static int ReservedInTransit(Economy.TraderInventory inventory, Contract contract)
        {
            if (inventory == null || contract == null) return 0;

            int count = 0;
            foreach (var pair in inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding == null || holding.ReservedForContractId != contract.ContractId) continue;
                if (holding.IsInTransit) count++;
            }
            return count;
        }

        /// <summary>Normal, Pressing or Critical — the same slack test NPCTrader uses.</summary>
        public static string Urgency(Contract contract, int week, int missing, int worstTransitAhead, int panicWeeks)
        {
            if (missing <= 0) return "Normal";

            int slack = LatestUsefulArrivalWeek(contract) - week - Mathf.Max(1, worstTransitAhead);
            if (slack <= 0) return "Critical";
            return slack <= panicWeeks ? "Pressing" : "Normal";
        }

        /// <summary>How many other traders hold a live commitment to this contract.</summary>
        public static int CommittedCount(TelemetryRecorder host, string contractId, string excludeTraderId)
        {
            if (host.Economy == null) return 0;

            int count = 0;
            foreach (var pair in host.Economy.InventoriesByOwnerId)
            {
                if (pair.Key == excludeTraderId) continue;
                if (pair.Value != null && pair.Value.IsCommittedToContract(contractId)) count++;
            }
            return count;
        }
    }
}
