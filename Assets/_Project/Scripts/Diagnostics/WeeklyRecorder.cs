using System;
using System.Collections.Generic;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.Restoration;
using AntiqueTradingSimulator.Rumours;
using UnityEngine;
using Antique = AntiqueTradingSimulator.Market.Antique;

namespace AntiqueTradingSimulator.Diagnostics
{
    /// <summary>
    /// The weekly snapshots: one row per trader, one for the economy as a whole, one per standing
    /// listing, plus the invariant checks. Also owns the browse mirror and the running weekly
    /// counters the ledger and transaction handlers feed.
    /// </summary>
    public sealed class WeeklyRecorder
    {
        private readonly TelemetryRecorder _host;
        private readonly TelemetryRun _run;

        public BrowseMirror Mirror { get; }
        public ActionGateMirror Gates { get; }

        private readonly Dictionary<LedgerCategory, float> _ledgerWeek = new Dictionary<LedgerCategory, float>();
        private readonly List<float> _wealth = new List<float>();
        private readonly List<float> _prices = new List<float>();
        private readonly List<string> _contractIds = new List<string>();

        private float _fromCollectors, _fromMarketSales, _toAnonymous, _transferVolume, _turnover;
        private int _listingsAdded, _listingsSold, _listingsExpired, _salesCount;
        private int _contractsCreated, _contractsFulfilled, _contractsExpired, _contractsNeverPickedUp;
        private float _lastPriceIndex = float.NaN;

        public WeeklyRecorder(TelemetryRecorder host)
        {
            _host = host;
            _run = host.Run;
            Mirror = new BrowseMirror(host);
            Gates = new ActionGateMirror(host);
        }

        // ---------------------------------------------------------------- weekly counters

        public void CountLedger(LedgerEntry entry)
        {
            _ledgerWeek.TryGetValue(entry.Category, out float sum);
            _ledgerWeek[entry.Category] = sum + entry.Amount;
        }

        /// <summary>
        /// Classifies a transaction for the money-supply columns. Whether cash entered or left the
        /// trader economy depends on whether the counterparty was a real trader, which only the
        /// market feed knows.
        /// </summary>
        public void CountTransaction(string kind, string buyerId, string sellerId, float price, float proceeds)
        {
            bool realPrice = !float.IsNaN(price);

            switch (kind)
            {
                case "ListingAdded":
                case "OwnerListed":
                    _listingsAdded++;
                    break;

                case "ListingExpired":
                    _listingsExpired++;
                    break;

                case "Purchase":
                    _listingsSold++;
                    _salesCount++;
                    if (realPrice) _turnover += price;
                    if (realPrice && string.IsNullOrEmpty(sellerId)) _toAnonymous += price;
                    else if (realPrice) _transferVolume += price;
                    break;

                case "CollectorPurchase":
                    _listingsSold++;
                    _salesCount++;
                    if (realPrice) { _turnover += price; _fromCollectors += price; }
                    break;

                case "SoldToMarket":
                    if (realPrice) _fromMarketSales += price;
                    break;

                case "AuctionSold":
                case "ContractDelivery":
                    _salesCount++;
                    if (realPrice) _turnover += price;
                    break;
            }
        }

        public void CountContract(string eventName, bool hadCommitment)
        {
            switch (eventName)
            {
                case "Created": _contractsCreated++; break;
                case "Fulfilled": _contractsFulfilled++; break;
                case "Expired":
                    _contractsExpired++;
                    if (!hadCommitment) _contractsNeverPickedUp++;
                    break;
            }
        }

        private void ResetCounters()
        {
            _ledgerWeek.Clear();
            _fromCollectors = _fromMarketSales = _toAnonymous = _transferVolume = _turnover = 0f;
            _listingsAdded = _listingsSold = _listingsExpired = _salesCount = 0;
            _contractsCreated = _contractsFulfilled = _contractsExpired = _contractsNeverPickedUp = 0;
            _host.Traders.ResetWeeklyCounters();
        }

        private float Ledger(LedgerCategory category) =>
            _ledgerWeek.TryGetValue(category, out float sum) ? sum : 0f;

        // ---------------------------------------------------------------- fact_trader_week

        public void TraderWeek()
        {
            var table = _run.Table(T.FactTraderWeekName, T.FactTraderWeekHeader);
            var season = _host.Clock.SeasonOf(_run.Week);
            int week = _run.Week;

            foreach (string traderId in _host.Traders.TraderIds)
            {
                var inventory = _host.Traders.InventoryOf(traderId);
                if (inventory == null) continue;

                var npc = _host.Traders.NpcOf(traderId);
                var record = _host.Traders.RecordOf(traderId);
                var state = npc != null ? npc.CaptureState() : null;

                float costBasis = 0f;
                int listed = 0, inTransit = 0, reserved = 0;
                float listedValue = 0f;

                foreach (var pair in inventory.Holdings)
                {
                    var holding = pair.Value;
                    if (holding == null) continue;

                    costBasis += holding.PurchasePrice;
                    if (holding.IsListedForSale) { listed++; listedValue += holding.AskingPrice; }
                    if (holding.IsInTransit) inTransit++;
                    if (holding.IsReservedForContract) reserved++;
                }

                var reputation = record != null ? record.Reputation : null;
                var tier = reputation != null ? reputation.CurrentTier(Company.ReputationKind.Reputation) : null;

                _run.StampWeek(table)
                    .E(season)
                    .S(traderId)
                    .F(inventory.Cash)
                    .I(inventory.UsedSlots)
                    .F(inventory.TotalHoldingsValue)
                    .F(costBasis)
                    .F(inventory.TotalHoldingsValue - costBasis)
                    .F(inventory.Wealth)
                    .F(Company.TraderStats.MarketShare(inventory, _host.Economy.Inventories))
                    .I(reputation != null ? reputation.Reputation : 0)
                    .S(tier != null ? tier.Name : "")
                    .I(record != null ? record.ContractsAccepted : 0)
                    .I(record != null ? record.ContractsFulfilled : 0)
                    .I(record != null ? record.ContractsFailed : 0)
                    .I(inventory.CommittedContractIds.Count)
                    .I(listed)
                    .F(listedValue)
                    .I(OpenRestorations(inventory))
                    .I(inTransit)
                    .I(reserved)
                    .I(inventory.HasWarehouseLimit ? inventory.UsedSlots : (int?)null)
                    .I(inventory.HasWarehouseLimit ? inventory.Capacity : (int?)null)
                    .B(inventory.IsOverCapacity)
                    .F(inventory.Ledger.Income(week, week))
                    .F(inventory.Ledger.Expenses(week, week))
                    .F(inventory.Ledger.TradeVolume(week, week))
                    .F(state != null ? state.WeekBudget : (float?)null)
                    .F(state != null ? state.ContractReserve : (float?)null)
                    .F(state != null ? state.PlanReserve : (float?)null)
                    .I(state != null && state.SlotTicks != null ? state.SlotTicks.Count : (int?)null)
                    .F(npc != null ? npc.ActivityScale : (float?)null)
                    .End();
            }
        }

        private int OpenRestorations(TraderInventory inventory)
        {
            if (_host.Restorations == null) return 0;

            int count = 0;
            foreach (var job in _host.Restorations.Jobs)
                if (job != null && ReferenceEquals(job.Owner, inventory)) count++;
            return count;
        }

        // ---------------------------------------------------------------- fact_economy_week

        public void EconomyWeek()
        {
            var market = _host.Market;
            if (market == null) return;

            float cash = 0f, holdings = 0f;
            _wealth.Clear();

            foreach (var inventory in _host.Economy.Inventories)
            {
                if (inventory == null) continue;
                cash += inventory.Cash;
                holdings += inventory.TotalHoldingsValue;
                _wealth.Add(inventory.Wealth);
            }

            float fromContracts = Ledger(LedgerCategory.Contract);
            float fromAuctions = Mathf.Max(0f, Ledger(LedgerCategory.Auction));
            float toTransport = -Ledger(LedgerCategory.Transport);
            float toCommission = -Ledger(LedgerCategory.Commission);
            float toWarehouse = -(Ledger(LedgerCategory.Warehouse) + Ledger(LedgerCategory.Upkeep));
            float toRestoration = -Ledger(LedgerCategory.Restoration);
            float toRumours = -Ledger(LedgerCategory.Rumour);
            float toPenalties = -Ledger(LedgerCategory.Penalty);
            float toUpgrades = -Ledger(LedgerCategory.Upgrade);

            float created = fromContracts + fromAuctions + _fromCollectors + _fromMarketSales;
            float destroyed = _toAnonymous + toTransport + toCommission + toWarehouse
                              + toRestoration + toRumours + toPenalties + toUpgrades;

            int clampedHigh = 0, clampedLow = 0, atMinSupply = 0;
            float weightedPrice = 0f, totalWeight = 0f, supplySum = 0f, demandSum = 0f;
            int types = 0;

            foreach (var pair in market.TypeStates)
            {
                var state = pair.Value;
                if (state == null) continue;

                types++;
                supplySum += state.Supply;
                demandSum += state.EffectiveDemand;

                float multiplier = PriceEngine.CalculateReferencePrice(1f, state);
                if (multiplier >= 4.999f) clampedHigh++;
                else if (multiplier <= 0.1001f) clampedLow++;
                if (state.Supply + state.TempSupplyMod <= 0.1001f) atMinSupply++;

                var definition = AntiqueDatabase.GetById(state.DefinitionId);
                float basePrice = definition != null ? definition.BasePrice : 0f;
                weightedPrice += basePrice * multiplier * basePrice;
                totalWeight += basePrice;
            }

            float index = totalWeight > 0f ? weightedPrice / totalWeight : 0f;

            _prices.Clear();
            float listedValue = 0f;
            foreach (var listing in market.Listings)
            {
                if (listing == null) continue;
                _prices.Add(listing.SalePrice);
                listedValue += listing.SalePrice;
            }
            _prices.Sort();

            int resolved = _listingsSold + _listingsExpired;
            var conditions = _host.Economy.CurrentMarketSeason;

            var table = _run.Table(T.FactEconomyWeekName, T.FactEconomyWeekHeader);
            _run.StampWeek(table)
                .E(_host.Clock.SeasonOf(_run.Week))
                .F(cash)
                .F(holdings)
                .F(cash + holdings)
                .F(Gini(_wealth))
                .F(TopShare(_wealth))
                .F(created)
                .F(destroyed)
                .F(created - destroyed)
                .F(fromContracts)
                .F(_fromCollectors)
                .F(_fromMarketSales)
                .F(fromAuctions)
                .F(_toAnonymous)
                .F(toTransport)
                .F(toCommission)
                .F(toWarehouse)
                .F(toRestoration)
                .F(toRumours)
                .F(toPenalties)
                .F(toUpgrades)
                .F(_transferVolume)
                .F(index)
                .F(float.IsNaN(_lastPriceIndex) ? (float?)null : index - _lastPriceIndex)
                .I(clampedHigh)
                .I(clampedLow)
                .I(atMinSupply)
                .F(types > 0 ? supplySum / types : 0f)
                .F(types > 0 ? demandSum / types : 0f)
                .I(market.Listings.Count)
                .I(_listingsAdded)
                .I(_listingsSold)
                .I(_listingsExpired)
                .F(resolved > 0 ? _listingsExpired / (float)resolved : 0f)
                .F(Median(_prices))
                .F(listedValue)
                .F(_turnover)
                .I(_salesCount)
                .I(_host.Board != null ? _host.Board.ActiveContracts.Count : 0)
                .I(_contractsCreated)
                .I(_contractsFulfilled)
                .I(_contractsExpired)
                .I(_contractsNeverPickedUp)
                .I(ActiveRumours())
                .F(RumourMill.CurrentSkepticism)
                .I(_host.WorldEvents != null ? _host.WorldEvents.ActiveEvents.Count : 0)
                .I(_host.Restorations != null ? _host.Restorations.Jobs.Count : 0)
                .I(_host.Transport != null ? _host.Transport.Shipments.Count : 0)
                .F(conditions.NpcActivity)
                .F(conditions.CollectorChance)
                .F(conditions.NewListings)
                .S(SeasonDemand(conditions))
                .End();

            _lastPriceIndex = index;
            ResetCounters();
        }

        private static string SeasonDemand(Seasons.SeasonSettings.MarketConditions conditions)
        {
            var sb = new System.Text.StringBuilder();
            foreach (AntiqueEnums.AntiqueType type in Enum.GetValues(typeof(AntiqueEnums.AntiqueType)))
            {
                if (sb.Length > 0) sb.Append('|');
                sb.Append(type).Append(':').Append(conditions.DemandMultiplier(type).ToString("0.###",
                    System.Globalization.CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }

        private int ActiveRumours()
        {
            var mill = RumourMill.Current;
            if (mill == null) return 0;

            int count = 0;
            foreach (var record in mill.Records)
                if (record != null && !record.Debunked) count++;
            return count;
        }

        private static float Median(List<float> sorted)
        {
            if (sorted.Count == 0) return 0f;
            int middle = sorted.Count / 2;
            return sorted.Count % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) * 0.5f;
        }

        private static float TopShare(List<float> values)
        {
            float total = 0f, top = 0f;
            for (int i = 0; i < values.Count; i++)
            {
                total += values[i];
                if (values[i] > top) top = values[i];
            }
            return total > 0f ? top / total : 0f;
        }

        /// <summary>Concentration of wealth across traders, 0 even to 1 held by one.</summary>
        private static float Gini(List<float> values)
        {
            if (values.Count < 2) return 0f;

            values.Sort();
            float sum = 0f, weighted = 0f;
            for (int i = 0; i < values.Count; i++)
            {
                sum += values[i];
                weighted += (i + 1) * values[i];
            }

            if (sum <= 0f) return 0f;
            int n = values.Count;
            return (2f * weighted) / (n * sum) - (n + 1f) / n;
        }

        // ---------------------------------------------------------------- fact_listing_week

        public void ListingWeek()
        {
            var market = _host.Market;
            if (market == null) return;

            var table = _run.Table(T.FactListingWeekName, T.FactListingWeekHeader);
            int lifetime = TelemetryRun.Field(_host.Economy, "anonymousListingLifetimeWeeks", 0);
            int week = _run.Week;

            foreach (var listing in market.Listings)
            {
                if (listing == null) continue;

                int qualifying = 0;
                _contractIds.Clear();
                if (_host.Board != null)
                {
                    foreach (var contract in _host.Board.ActiveContracts)
                    {
                        if (contract == null || !contract.Requirement.IsSatisfiedBy(listing)) continue;
                        qualifying++;
                        _contractIds.Add(contract.ContractId);
                    }
                }

                int interested = 0, affordable = 0;
                if (_host.Npcs != null)
                {
                    foreach (var npc in _host.Npcs.NPCs)
                    {
                        var profile = npc != null ? npc.Profile : null;
                        if (profile == null) continue;
                        if (listing.Condition < profile.MinConditionToBuy) continue;
                        if (!Interested(listing, profile)) continue;

                        interested++;
                        if (Core.Money.Covers(npc.Inventory.Cash, listing.SalePrice)) affordable++;
                    }
                }

                int age = listing.MarketListedOnWeek >= 0 ? week - listing.MarketListedOnWeek : 0;
                var typeState = market.GetTypeState(listing.DefinitionId);
                var definition = listing.Definition;

                _run.StampWeek(table)
                    .S(listing.Id)
                    .S(listing.DefinitionId)
                    .S(listing.OwnerId)
                    .B(listing.IsListedForSale)
                    .F(listing.IsListedForSale ? listing.AskingPrice : (float?)null)
                    .F(listing.CurrentPrice)
                    .F(listing.SalePrice)
                    .F(listing.IsListedForSale && listing.CurrentPrice > 0f
                        ? listing.AskingPrice / listing.CurrentPrice
                        : (float?)null)
                    .F(listing.Condition)
                    .F(listing.PriceFactor)
                    .E(listing.ShippingZone)
                    .I(listing.MarketListedOnWeek)
                    .I(age)
                    .I(!listing.IsListedForSale && lifetime > 0 && listing.MarketListedOnWeek >= 0
                        ? Mathf.Max(0, lifetime - age)
                        : (int?)null)
                    .F(listing.IsListedForSale
                        ? _host.Economy.CollectorSaleChance(listing, listing.AskingPrice)
                        : (float?)null)
                    .I(qualifying)
                    .S(TelemetryRun.Join(_contractIds, id => id))
                    .I(interested)
                    .I(affordable)
                    .F(PriceEngine.CalculateReferencePrice(definition != null ? definition.BasePrice : 0f, typeState))
                    .F(PriceRank(market, listing))
                    .End();
            }
        }

        private static bool Interested(Antique listing, Agents.NpcBehaviorProfile profile)
        {
            var definition = listing.Definition;
            if (definition == null) return false;

            return (profile.PreferredTypes.Count == 0 || profile.PreferredTypes.Contains(definition.Type))
                   && (profile.PreferredCountries.Count == 0 || profile.PreferredCountries.Contains(definition.Country))
                   && (profile.PreferredCenturies.Count == 0 || profile.PreferredCenturies.Contains(definition.Century));
        }

        private static float PriceRank(AntiqueTradingSimulator.Market.Market market, Antique listing)
        {
            int total = 0, below = 0;
            var listings = market.Listings;

            for (int i = 0; i < listings.Count; i++)
            {
                if (listings[i] == null || listings[i].DefinitionId != listing.DefinitionId) continue;
                total++;
                if (listings[i].SalePrice < listing.SalePrice) below++;
            }

            return total > 1 ? below / (float)(total - 1) : 0f;
        }

        // ---------------------------------------------------------------- fact_integrity

        public void Integrity()
        {
            var table = _run.Table(T.FactIntegrityName, T.FactIntegrityHeader);

            foreach (string traderId in _host.Traders.TraderIds)
            {
                var inventory = _host.Traders.InventoryOf(traderId);
                if (inventory == null) continue;

                float expected = inventory.Cash + inventory.TotalHoldingsValue;
                Check(table, "WealthMatchesParts", "Trader", traderId, expected, inventory.Wealth, null);

                int danglingReservations = 0;
                int orphanRestorations = 0;
                foreach (var pair in inventory.Holdings)
                {
                    var holding = pair.Value;
                    if (holding == null) continue;

                    if (holding.IsReservedForContract)
                    {
                        var contract = _host.Board != null ? _host.Board.GetById(holding.ReservedForContractId) : null;
                        if (contract == null || contract.Status != Contracts.ContractStatus.Active) danglingReservations++;
                    }

                    if (holding.IsInRestoration && _host.Restorations != null &&
                        _host.Restorations.GetJob(holding.Id) == null) orphanRestorations++;
                }

                Check(table, "ReservationPointsAtLiveContract", "Trader", traderId, 0f, danglingReservations, null);
                Check(table, "RestorationHasJob", "Trader", traderId, 0f, orphanRestorations, null);

                int danglingCommitments = 0;
                foreach (string contractId in inventory.CommittedContractIds)
                {
                    var contract = _host.Board != null ? _host.Board.GetById(contractId) : null;
                    if (contract == null || contract.Status != Contracts.ContractStatus.Active) danglingCommitments++;
                }
                Check(table, "ContractCommitmentConsistent", "Trader", traderId, 0f, danglingCommitments, null);
            }

            var market = _host.Market;
            if (market != null)
            {
                foreach (var pair in market.TypeStates)
                {
                    var state = pair.Value;
                    var definition = AntiqueDatabase.GetById(pair.Key);
                    if (state == null || definition == null) continue;

                    float expected = definition.BasePrice * PriceEngine.CalculateReferencePrice(1f, state);
                    float actual = PriceEngine.CalculateReferencePrice(definition.BasePrice, state);
                    if (Mathf.Approximately(expected, actual)) continue;

                    Check(table, "PriceMatchesDecomposition", "Definition", pair.Key, expected, actual, null);
                }
            }

            Check(table, "OwnershipEventMatchesDiff", "Global", "", 0f, _host.Traders.UnexplainedOwnershipRows,
                "Acquisitions and disposals no market event explained");
        }

        private void Check(CsvTable table, string name, string scopeKind, string scopeId,
            float expected, float actual, string detail)
        {
            bool passed = Mathf.Abs(expected - actual) < 0.01f;

            _run.StampShort(table)
                .S(name)
                .S(scopeKind)
                .S(scopeId)
                .B(passed)
                .F(expected)
                .F(actual)
                .F(actual - expected)
                .S(detail)
                .End();
        }
    }
}
