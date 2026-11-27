using System.Collections.Generic;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.Rumours;
using UnityEngine;
using Antique = AntiqueTradingSimulator.Market.Antique;

namespace AntiqueTradingSimulator.Diagnostics
{
    /// <summary>
    /// The market tables: every transaction, the per-type state, and reported pressure.
    ///
    /// Type state is snapshotted whole rather than rebuilt from pressure events. Six places write
    /// supply or demand and only Market's own paths report: contract demand reports increases
    /// only, and event effects, the dependency network, rumours, mean reversion and the seasonal
    /// term report nothing. Since PriceEngine is pure, a row carrying the full decomposition
    /// reproduces the price exactly, which the pressure stream cannot.
    /// </summary>
    public sealed class MarketRecorder
    {
        private readonly TelemetryRecorder _host;
        private readonly TelemetryRun _run;

        private struct TypeSnapshot
        {
            public float Supply;
            public float Demand;
            public float TempSupply;
            public float TempDemand;
            public float Seasonal;
            public float ContractDemand;
            public float LongTermDemand;
            public float Reference;
        }

        private struct PendingRemoval
        {
            public bool Active;
            public string AntiqueId;
            public string DefinitionId;
            public string OwnerId;
            public bool WasCancelled;
            public float MarketValue;
            public float Condition;
            public ShippingZone Zone;
            public int ListedOnWeek;
        }

        private readonly Dictionary<string, TypeSnapshot> _types = new Dictionary<string, TypeSnapshot>();
        private readonly List<string> _scratch = new List<string>();

        private MarketFeedEntry _lastFeedEntry;

        // Market.SellToMarket re-adds the antique as an anonymous listing and only then writes the
        // feed entry, so the id of the listing just added identifies what was sold. The feed
        // itself stores a name, not an id.
        private string _lastAddedId;
        private string _lastAddedDefinitionId;
        private PendingRemoval _pending;
        private long _lastTransactionSeq;
        private string _lastTransactionKind = "";
        private int _lastTransactionTick = -1;

        public MarketRecorder(TelemetryRecorder host)
        {
            _host = host;
            _run = host.Run;
        }

        public void Reseed()
        {
            var market = _host.Market;
            if (market == null) return;

            foreach (var listing in market.Listings)
                _host.Dimensions.EnsureAntique(listing, "InitialStock");

            foreach (var pair in market.TypeStates)
                _types[pair.Key] = Snapshot(pair.Value);

            _lastFeedEntry = market.Feed.Entries.Count > 0 ? market.Feed.Entries[0] : null;
        }

        private TypeSnapshot Snapshot(AntiqueMarketState state) => new TypeSnapshot
        {
            Supply = state.Supply,
            Demand = state.Demand,
            TempSupply = state.TempSupplyMod,
            TempDemand = state.TempDemandMod,
            Seasonal = state.SeasonalDemandMod,
            ContractDemand = state.ContractDemandMod,
            LongTermDemand = state.LongTermDemandMod,
            Reference = ReferencePrice(state)
        };

        private float ReferencePrice(AntiqueMarketState state)
        {
            var definition = AntiqueDatabase.GetById(state.DefinitionId);
            return PriceEngine.CalculateReferencePrice(definition != null ? definition.BasePrice : 0f, state);
        }

        /// <summary>CalculateReferencePrice of a unit base price is the supply/demand multiplier itself.</summary>
        private static float Multiplier(AntiqueMarketState state) => PriceEngine.CalculateReferencePrice(1f, state);

        // ---------------------------------------------------------------- market events

        public void ListingAdded(Antique antique)
        {
            _run.Safe(T.FactMarketTransactionName, "OnListingAdded", () =>
            {
                _host.Dimensions.EnsureAntique(antique, "MarketGenerated");
                _lastAddedId = antique.Id;
                _lastAddedDefinitionId = antique.DefinitionId;
                WriteTransaction(antique.IsListedForSale ? "OwnerListed" : "ListingAdded", antique,
                    "", antique.IsListedForSale ? antique.OwnerId : "", antique.SalePrice);
            });
        }

        public void ListingRemoved(Antique antique)
        {
            _run.Safe(T.FactMarketTransactionName, "OnListingRemoved", () =>
            {
                FlushPendingRemoval();

                // A cancelled listing clears both of these and raises no feed entry.
                bool cancelled = antique.MarketListedOnWeek < 0 && !antique.IsListedForSale
                                 && !string.IsNullOrEmpty(antique.OwnerId);

                _pending = new PendingRemoval
                {
                    Active = true,
                    AntiqueId = antique.Id,
                    DefinitionId = antique.DefinitionId,
                    OwnerId = antique.OwnerId,
                    WasCancelled = cancelled,
                    MarketValue = antique.CurrentPrice,
                    Condition = antique.Condition,
                    Zone = antique.ShippingZone,
                    ListedOnWeek = antique.MarketListedOnWeek
                };
            });
        }

        public void OwnerListingSold(Antique antique, TraderInventory seller, float price, float proceeds)
        {
            _run.Safe(T.FactMarketTransactionName, "OnOwnerListingSold", () =>
            {
                _host.Traders.NotePath(antique.Id, "ListingSold", "", seller != null ? seller.OwnerId : "", proceeds, price);
            });
        }

        /// <summary>
        /// The feed is the only place a plain purchase names its buyer and seller. Entries are
        /// inserted at the front, so a new one is a reference change at index 0; a Listed entry
        /// whose count merely went up is the same object and is ignored.
        /// </summary>
        public void FeedChanged()
        {
            _run.Safe(T.FactMarketTransactionName, "OnFeedChanged", () =>
            {
                var market = _host.Market;
                if (market == null) return;

                var entries = market.Feed.Entries;
                if (entries.Count == 0)
                {
                    _lastFeedEntry = null;
                    return;
                }

                var top = entries[0];
                if (ReferenceEquals(top, _lastFeedEntry)) return;
                _lastFeedEntry = top;

                if (top.Kind == MarketFeedKind.Listed) return;

                // An instant sale to the market removes no listing, so there is nothing pending;
                // the antique is the one re-added a moment earlier in the same call.
                bool fromAdd = !_pending.Active && top.Kind == MarketFeedKind.SoldToMarket;

                string antiqueId = _pending.Active ? _pending.AntiqueId : fromAdd ? _lastAddedId : "";
                string definitionId = _pending.Active ? _pending.DefinitionId
                    : fromAdd ? _lastAddedDefinitionId : "";
                float marketValue = _pending.Active ? _pending.MarketValue : 0f;
                float condition = _pending.Active ? _pending.Condition : 0f;
                var zone = _pending.Active ? _pending.Zone : ShippingZone.Domestic;
                int listedOn = _pending.Active ? _pending.ListedOnWeek : -1;
                _pending.Active = false;

                string kind = top.Kind.ToString();
                WriteTransactionRaw(kind, antiqueId, definitionId, top.BuyerId, top.SellerId, top.Price,
                    float.NaN, float.NaN, marketValue, condition, zone, listedOn);

                if (!string.IsNullOrEmpty(antiqueId))
                    _host.Traders.NotePath(antiqueId, kind, top.BuyerId, top.SellerId, top.Price, float.NaN);
            });
        }

        /// <summary>Writes a removal the feed never explained: an expired or cancelled listing.</summary>
        private void FlushPendingRemoval()
        {
            if (!_pending.Active) return;
            _pending.Active = false;

            string kind = _pending.WasCancelled ? "ListingCancelled" : "ListingExpired";
            WriteTransactionRaw(kind, _pending.AntiqueId, _pending.DefinitionId, "", _pending.OwnerId,
                float.NaN, float.NaN, float.NaN, _pending.MarketValue, _pending.Condition,
                _pending.Zone, _pending.ListedOnWeek);
        }

        private void WriteTransaction(string kind, Antique antique, string buyerId, string sellerId, float price)
        {
            WriteTransactionRaw(kind, antique.Id, antique.DefinitionId, buyerId, sellerId, price,
                antique.IsListedForSale ? antique.AskingPrice : float.NaN, float.NaN,
                antique.CurrentPrice, antique.Condition, antique.ShippingZone, antique.MarketListedOnWeek);
        }

        private void WriteTransactionRaw(string kind, string antiqueId, string definitionId, string buyerId,
            string sellerId, float price, float askingPrice, float proceeds, float marketValue, float condition,
            ShippingZone zone, int listedOnWeek)
        {
            int before = CountListings(definitionId);
            var table = _run.Table(T.FactMarketTransactionName, T.FactMarketTransactionHeader);

            _lastTransactionSeq = _run.CurrentSeq + 1;
            _lastTransactionKind = kind;
            _lastTransactionTick = _run.Tick;

            _run.Stamp(table)
                .S(kind)
                .S(antiqueId)
                .S(definitionId)
                .S(buyerId)
                .S(sellerId)
                .F(float.IsNaN(price) ? (float?)null : price)
                .F(float.IsNaN(askingPrice) ? (float?)null : askingPrice)
                .F(float.IsNaN(proceeds) ? (float?)null : proceeds)
                .F(float.IsNaN(price) || float.IsNaN(proceeds) ? (float?)null : price - proceeds)
                .F(marketValue)
                .F(condition)
                .E(zone)
                .I(listedOnWeek >= 0 ? listedOnWeek : (int?)null)
                .I(listedOnWeek >= 0 ? _run.Week - listedOnWeek : (int?)null)
                .I(before)
                .I(CountListings(definitionId))
                .E(TakeDisposalReason(kind, antiqueId))
                .F(float.IsNaN(price) ? (float?)null : price)
                .F(SpreadPaid(kind, price))
                .End();

            _host.WeeklyLog.CountTransaction(kind, buyerId, sellerId, price, proceeds);
        }

        /// <summary>
        /// Why each seller was selling, as the agent noted it before the sale, keyed on the
        /// antique and consumed when it changes hands.
        /// </summary>
        private readonly Dictionary<string, Agents.DisposalReason> _disposalReasons =
            new Dictionary<string, Agents.DisposalReason>(System.StringComparer.Ordinal);

        /// <summary>Called by the agent before it lists or dumps a holding.</summary>
        public void NoteDisposal(string antiqueId, Agents.DisposalReason reason)
        {
            if (string.IsNullOrEmpty(antiqueId) || reason == Agents.DisposalReason.None) return;
            _disposalReasons[antiqueId] = reason;
        }

        /// <summary>The reason, consumed once the antique has actually changed hands.</summary>
        private System.Enum TakeDisposalReason(string kind, string antiqueId)
        {
            if (string.IsNullOrEmpty(antiqueId)) return null;
            if (!_disposalReasons.TryGetValue(antiqueId, out var reason)) return null;

            bool gone = kind == "SoldToMarket" || kind == "ListingSold" || kind == "CollectorPurchase" ||
                        kind == "Purchase";
            if (gone) _disposalReasons.Remove(antiqueId);

            return reason;
        }

        /// <summary>
        /// What the market kept on an instant sale, through Market's own definition so the column
        /// matches the charge.
        /// </summary>
        private static float? SpreadPaid(string kind, float gross)
        {
            if (kind != "SoldToMarket" || float.IsNaN(gross)) return null;
            return Core.Money.RoundToShilling(gross - Market.Market.InstantSaleProceeds(gross));
        }

        private int CountListings(string definitionId)
        {
            var market = _host.Market;
            if (market == null || string.IsNullOrEmpty(definitionId)) return 0;

            int count = 0;
            var listings = market.Listings;
            for (int i = 0; i < listings.Count; i++)
                if (listings[i] != null && listings[i].DefinitionId == definitionId) count++;
            return count;
        }

        // ---------------------------------------------------------------- fact_pressure

        public void PressureApplied(string definitionId, float demandDelta, float supplyDelta)
        {
            _run.Safe(T.FactPressureName, "OnPressureApplied", () =>
            {
                var market = _host.Market;
                var state = market != null ? market.GetTypeState(definitionId) : null;

                bool sameTick = _lastTransactionTick == _run.Tick;
                int? attributed = sameTick ? (int?)_lastTransactionSeq : null;
                var table = _run.Table(T.FactPressureName, T.FactPressureHeader);

                _run.Stamp(table)
                    .S(definitionId)
                    .F(demandDelta)
                    .F(supplyDelta)
                    .I(attributed)
                    .S(sameTick ? _lastTransactionKind : "Unattributed")
                    .S("Event effects, dependencies, rumours, mean reversion and seasonal demand never report here")
                    .End();
            });
        }

        // ---------------------------------------------------------------- fact_type_state

        public void TickPass(int ticksElapsed)
        {
            FlushPendingRemoval();
            Emit(ticksElapsed, "Change", onlyChanged: true);
        }

        public void WeeklyGrid(int ticksElapsed) => Emit(ticksElapsed, "WeeklyGrid", onlyChanged: false);

        private void Emit(int ticksElapsed, string trigger, bool onlyChanged)
        {
            var market = _host.Market;
            if (market == null) return;

            var table = _run.Table(T.FactTypeStateName, T.FactTypeStateHeader);

            foreach (var pair in market.TypeStates)
            {
                var state = pair.Value;
                if (state == null) continue;

                var now = Snapshot(state);
                bool known = _types.TryGetValue(pair.Key, out var before);

                if (onlyChanged && known && !Changed(before, now)) continue;

                _types[pair.Key] = now;
                WriteTypeState(table, state, now, known ? (TypeSnapshot?)before : null, trigger, ticksElapsed);
            }
        }

        private static bool Changed(TypeSnapshot a, TypeSnapshot b) =>
            !Mathf.Approximately(a.Supply, b.Supply) ||
            !Mathf.Approximately(a.Demand, b.Demand) ||
            !Mathf.Approximately(a.TempSupply, b.TempSupply) ||
            !Mathf.Approximately(a.TempDemand, b.TempDemand) ||
            !Mathf.Approximately(a.Seasonal, b.Seasonal) ||
            !Mathf.Approximately(a.ContractDemand, b.ContractDemand) ||
            !Mathf.Approximately(a.LongTermDemand, b.LongTermDemand) ||
            !Mathf.Approximately(a.Reference, b.Reference);

        private void WriteTypeState(CsvTable table, AntiqueMarketState state, TypeSnapshot now,
            TypeSnapshot? before, string trigger, int ticksElapsed)
        {
            int listings = 0, held = 0;
            float min = float.NaN, max = float.NaN, sum = 0f;

            var market = _host.Market;
            var all = market.Listings;
            for (int i = 0; i < all.Count; i++)
            {
                var listing = all[i];
                if (listing == null || listing.DefinitionId != state.DefinitionId) continue;

                listings++;
                float price = listing.SalePrice;
                sum += price;
                if (float.IsNaN(min) || price < min) min = price;
                if (float.IsNaN(max) || price > max) max = price;
            }

            foreach (var inventory in _host.Economy.Inventories)
            {
                if (inventory == null) continue;
                foreach (var pair in inventory.Holdings)
                    if (pair.Value != null && pair.Value.DefinitionId == state.DefinitionId) held++;
            }

            float multiplier = Multiplier(state);
            float dependency = _host.Dependencies != null ? _host.Dependencies.CurrentInfluenceOn(state.DefinitionId) : 0f;
            float residual = state.TempDemandMod - dependency;

            _run.Stamp(table)
                .I(ticksElapsed)
                .S(trigger)
                .S(state.DefinitionId)
                .F(state.Supply)
                .F(state.Demand)
                .F(state.TempSupplyMod)
                .F(state.TempDemandMod)
                .F(state.SeasonalDemandMod)
                .F(state.ContractDemandMod)
                .F(state.EffectiveDemand)
                .F(state.BaselineSupply)
                .F(state.BaselineDemand)
                .F(multiplier)
                .F(now.Reference)
                .F(state.HasAuctionPrice ? state.LastAuctionPrice : (float?)null)
                .I(state.LastAuctionWeek)
                .I(listings)
                .F(float.IsNaN(min) ? (float?)null : min)
                .F(listings > 0 ? sum / listings : (float?)null)
                .F(float.IsNaN(max) ? (float?)null : max)
                .I(held)
                .F(before.HasValue ? now.Supply - before.Value.Supply : (float?)null)
                .F(before.HasValue
                    ? (now.Demand + now.TempDemand + now.Seasonal + now.ContractDemand) -
                      (before.Value.Demand + before.Value.TempDemand + before.Value.Seasonal + before.Value.ContractDemand)
                    : (float?)null)
                .F(before.HasValue ? now.Reference - before.Value.Reference : (float?)null)
                .F(dependency)
                .F(HasActiveRumourOn(state.DefinitionId) ? residual : (float?)null)
                .S(ClampState(multiplier))
                .F(state.LongTermDemandMod)
                .F(state.DemandAnchor)
                .F(state.PriceHistory.Count > 0 ? state.PriceHistory[state.PriceHistory.Count - 1].TrendValue : (float?)null)
                .F(market.PriceTrend(state.DefinitionId, TrendWindowWeeks))
                .End();
        }

        /// <summary>Window of the price_trend_4w column (the default NpcBehaviorProfile.TrendWindowWeeks).</summary>
        private const int TrendWindowWeeks = 4;

        private static string ClampState(float multiplier)
        {
            if (multiplier >= 4.999f) return "High";
            if (multiplier <= 0.1001f) return "Low";
            return "None";
        }

        /// <summary>
        /// Whether a planted rumour still covers this type. RumourMill applies its pump and
        /// hangover straight to TempDemandMod without an event, so the share it contributes is
        /// reported as the non-dependency residual and only when a rumour is in fact running.
        /// </summary>
        private bool HasActiveRumourOn(string definitionId)
        {
            var mill = RumourMill.Current;
            if (mill == null) return false;

            var definition = AntiqueDatabase.GetById(definitionId);
            if (definition == null) return false;

            foreach (var record in mill.Records)
            {
                if (record == null || record.Debunked || record.ScopeKeys == null) continue;

                foreach (string key in record.ScopeKeys)
                {
                    if (string.IsNullOrEmpty(key)) continue;
                    if (key.EndsWith(":" + definition.Type) ||
                        key.EndsWith(":" + definition.Country) ||
                        key.EndsWith(":" + definition.Century)) return true;
                }
            }

            return false;
        }
    }
}
