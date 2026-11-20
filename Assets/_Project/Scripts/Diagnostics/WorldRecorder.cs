using System;
using System.Collections.Generic;
using AntiqueTradingSimulator.Auctions;
using AntiqueTradingSimulator.Dependency;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Events;
using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.News;
using AntiqueTradingSimulator.Restoration;
using AntiqueTradingSimulator.Rumours;
using UnityEngine;
using Antique = AntiqueTradingSimulator.Market.Antique;

namespace AntiqueTradingSimulator.Diagnostics
{
    /// <summary>
    /// Restorations, shipments, news, rumours, auctions, world events and the dependency network.
    /// </summary>
    public sealed class WorldRecorder
    {
        private readonly TelemetryRecorder _host;
        private readonly TelemetryRun _run;

        private struct InfluenceView
        {
            public float Amount;
            public int LastRefreshed;
        }

        private readonly Dictionary<string, InfluenceView> _influences = new Dictionary<string, InfluenceView>();
        private readonly Dictionary<string, float> _conditionOnDispatch = new Dictionary<string, float>();

        // RestorationRecord carries no start week, so the job id of the Started row is kept here
        // and reused on completion. Without it the two rows of one job would not join.
        private readonly Dictionary<string, int> _restorationStart = new Dictionary<string, int>();
        private readonly List<string> _scratch = new List<string>();
        private readonly List<Action> _detach = new List<Action>();

        public WorldRecorder(TelemetryRecorder host)
        {
            _host = host;
            _run = host.Run;
        }

        public void Reseed() => TickPass();

        /// <summary>Releases the per-auction subscriptions made while live sessions were running.</summary>
        public void Detach()
        {
            for (int i = _detach.Count - 1; i >= 0; i--)
            {
                try { _detach[i](); }
                catch { /* the publisher is already gone */ }
            }

            _detach.Clear();
        }

        // ---------------------------------------------------------------- fact_restoration

        public void RestorationStarted(RestorationJob job)
        {
            _run.Safe(T.FactRestorationName, "OnRestorationStarted", () =>
            {
                string traderId = job.Owner != null ? job.Owner.OwnerId : "";
                var antique = job.Owner != null ? job.Owner.GetHolding(job.AntiqueId) : null;
                _restorationStart[job.AntiqueId] = job.StartWeek;

                WriteRestoration("Started", job.AntiqueId, traderId, antique, job.OptionId, job.OptionName,
                    job.StartWeek, job.EndWeek, job.Cost, job.StartCondition, job.TargetCondition,
                    partial: null, worst: null, result: null, outcome: null,
                    valueBefore: null, valueAfter: null);
            });
        }

        public void RestorationCompleted(RestorationRecord record)
        {
            _run.Safe(T.FactRestorationName, "OnRestorationCompleted", () =>
            {
                var inventory = _host.Economy != null && !string.IsNullOrEmpty(record.OwnerId) &&
                                _host.Economy.InventoriesByOwnerId.TryGetValue(record.OwnerId, out var found)
                    ? found
                    : null;
                var antique = inventory != null ? inventory.GetHolding(record.AntiqueId) : null;

                float valueAfter = antique != null ? antique.CurrentPrice : 0f;
                float valueBefore = valueAfter - record.ValueChange;

                _restorationStart.TryGetValue(record.AntiqueId, out int startWeek);
                _restorationStart.Remove(record.AntiqueId);

                WriteRestoration("Completed", record.AntiqueId, record.OwnerId, antique, "",
                    record.OptionName, startWeek, record.Week, 0f, record.StartCondition, 0f,
                    null, null, record.ResultCondition, record.Outcome, valueBefore, valueAfter);
            });
        }

        private void WriteRestoration(string eventName, string antiqueId, string traderId,
            Antique antique, string optionId, string optionName, int jobStartWeek, int endWeek, float cost,
            float startCondition, float targetCondition, float? partial, float? worst, float? result,
            RestorationOutcome? outcome, float? valueBefore, float? valueAfter)
        {
            var option = _host.Restorations != null && !string.IsNullOrEmpty(optionId)
                ? _host.Restorations.Settings.Get(optionId)
                : null;

            if (option != null)
            {
                partial = Antique.QuantizeCondition(startCondition + Mathf.Max(0f, targetCondition - startCondition) * 0.5f);
                worst = Antique.QuantizeConditionDown(startCondition - Mathf.Max(option.FailureLossMin, option.FailureLossMax));
            }

            float? change = valueBefore.HasValue && valueAfter.HasValue ? valueAfter - valueBefore : null;
            var record = _host.Traders.RecordOf(traderId);
            var plan = _host.Traders.PlanFor(traderId, antiqueId);

            var table = _run.Table(T.FactRestorationName, T.FactRestorationHeader);
            _run.Stamp(table)
                .S(antiqueId + ":" + jobStartWeek)
                .S(eventName)
                .S(traderId)
                .S(antiqueId)
                .S(antique != null ? antique.DefinitionId : "")
                .S(optionId)
                .S(optionName)
                .I(jobStartWeek)
                .I(endWeek)
                .I(Mathf.Max(0, endWeek - jobStartWeek))
                .F(cost)
                .F(startCondition)
                .F(targetCondition)
                .F(partial)
                .F(worst)
                .F(result)
                .E(outcome.HasValue ? (System.Enum)outcome.Value : null)
                .F(valueBefore)
                .F(valueAfter)
                .F(change)
                .F(change.HasValue ? change.Value - cost : (float?)null)
                .E(plan.HasValue ? (System.Enum)plan.Value.Purpose : null)
                .S(plan.HasValue ? plan.Value.ContractId : null)
                .F(antique != null ? antique.PurchasePrice : (float?)null)
                .I(record != null ? record.Reputation.Reputation : 0)
                .End();
        }

        // ---------------------------------------------------------------- fact_shipment

        public void ShipmentDispatched(Shipment shipment)
        {
            _run.Safe(T.FactShipmentName, "OnShipmentDispatched", () =>
            {
                var antique = shipment.Recipient != null ? shipment.Recipient.GetHolding(shipment.AntiqueId) : null;
                if (antique != null) _conditionOnDispatch[shipment.AntiqueId] = antique.Condition;

                WriteShipment("Dispatched", shipment, antique, null, null);
            });
        }

        public void ShipmentDelivered(Shipment shipment, Antique antique, float conditionLost)
        {
            _run.Safe(T.FactShipmentName, "OnShipmentDelivered", () =>
            {
                _conditionOnDispatch.TryGetValue(shipment.AntiqueId, out float before);
                _conditionOnDispatch.Remove(shipment.AntiqueId);

                WriteShipment("Delivered", shipment, antique, before > 0f ? before : (float?)null, conditionLost);
            });
        }

        public void ShipmentsDelayed(ShippingZone zone, int weeks)
        {
            _run.Safe(T.FactShipmentName, "OnShipmentsDelayed", () =>
            {
                if (_host.Transport == null) return;

                foreach (var shipment in _host.Transport.Shipments)
                {
                    if (shipment == null || shipment.Zone != zone) continue;

                    var antique = shipment.Recipient != null ? shipment.Recipient.GetHolding(shipment.AntiqueId) : null;
                    WriteShipment("Delayed", shipment, antique, null, null);
                }
            });
        }

        private void WriteShipment(string eventName, Shipment shipment, Antique antique,
            float? conditionBefore, float? conditionLost)
        {
            string contractId = antique != null ? antique.ReservedForContractId : null;
            var contract = !string.IsNullOrEmpty(contractId) && _host.Board != null
                ? _host.Board.GetById(contractId)
                : null;

            bool? satisfies = contract != null && antique != null
                ? contract.Requirement.IsSatisfiedBy(antique)
                : (bool?)null;
            bool? inTime = contract != null
                ? shipment.ArrivalWeek <= ContractMath.LatestUsefulArrivalWeek(contract)
                : (bool?)null;

            var table = _run.Table(T.FactShipmentName, T.FactShipmentHeader);
            _run.Stamp(table)
                .S(shipment.ShipmentId)
                .S(eventName)
                .S(shipment.Recipient != null ? shipment.Recipient.OwnerId : "")
                .S(shipment.AntiqueId)
                .S(antique != null ? antique.DefinitionId : "")
                .E(shipment.Option)
                .E(shipment.Zone)
                .F(shipment.Cost)
                .I(shipment.DispatchWeek)
                .I(shipment.ArrivalWeek)
                .I(shipment.SeasonalShiftWeeks)
                .I(eventName == "Delivered" ? shipment.ArrivalWeek - shipment.DispatchWeek : (int?)null)
                .F(conditionBefore)
                .F(conditionLost)
                .F(antique != null ? antique.Condition : (float?)null)
                .S(contractId)
                .B(satisfies)
                .B(inTime)
                .End();
        }

        // ---------------------------------------------------------------- fact_news

        public void NewsPublished(NewsItem news)
        {
            _run.Safe(T.FactNewsName, "OnNewsPublished", () => WriteNews(news, "Published"));
        }

        private void WriteNews(NewsItem news, string eventName)
        {
            var table = _run.Table(T.FactNewsName, T.FactNewsHeader);
            _run.Stamp(table)
                .S(news.Id)
                .S(eventName)
                .E(news.Type)
                .S(news.OfficialName)
                .F(news.Credibility)
                .I(news.WeekPublished)
                .I(news.EventTriggerWeek)
                .B(news.GuaranteedDelivery)
                .S(news.PlantedBy)
                .S(news.DebunksNewsId)
                .B(news.Debunked)
                .I(news.NewsData != null ? news.NewsData.Count : 0)
                .S(ScopeLabel(news.NewsData))
                .B(news.NewsData != null && news.NewsData.Count > 0 ? news.NewsData[0].affectsPriceUp : (bool?)null)
                .I(news.PlayerDeliveryWeek)
                .F(RumourMill.CurrentSkepticism)
                .End();
        }

        public static string ScopeLabel(IReadOnlyList<NewsEventData> data)
        {
            if (data == null) return "";

            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < data.Count; i++)
            {
                var effect = data[i];
                if (effect == null) continue;

                string value = effect.targetScope switch
                {
                    EventEffect.TargetScope.AntiqueType => effect.AntiqueType.ToString(),
                    EventEffect.TargetScope.Country => effect.Country.ToString(),
                    EventEffect.TargetScope.Century => effect.Century.ToString(),
                    _ => "Other"
                };

                if (sb.Length > 0) sb.Append('|');
                sb.Append(effect.targetScope).Append(':').Append(value)
                    .Append(':').Append(effect.affectsPriceUp ? "up" : "down");
            }
            return sb.ToString();
        }

        // ---------------------------------------------------------------- fact_news_reception

        public void NewsReception(IInformationReceiver receiver, NewsItem news, int deliveryWeek, string outcome,
            float baseTrust, float effectiveTrust, float actionChance, float trustRoll, float expertChance,
            int delayWeeks, int reactionWeek)
        {
            _run.Safe(T.FactNewsReceptionName, "ReceiveNews", () =>
            {
                string traderId = receiver != null ? receiver.ReceiverId : "";
                var npc = _host.Traders.NpcOf(traderId);
                var inventory = _host.Traders.InventoryOf(traderId);

                int inScope = 0;
                float inScopeValue = 0f;
                if (inventory != null && news.NewsData != null)
                {
                    foreach (var pair in inventory.Holdings)
                    {
                        if (pair.Value == null || !MatchesAnyScope(pair.Value, news.NewsData)) continue;
                        inScope++;
                        inScopeValue += pair.Value.CurrentPrice;
                    }
                }

                var profile = npc != null ? npc.Profile : null;

                var table = _run.Table(T.FactNewsReceptionName, T.FactNewsReceptionHeader);
                _run.Stamp(table)
                    .S(news.Id)
                    .S(traderId)
                    .I(deliveryWeek)
                    .S(outcome)
                    .E(news.Type)
                    .E(receiver != null ? (System.Enum)receiver.AccessLevel : null)
                    .F(news.Credibility)
                    .F(baseTrust)
                    .F(RumourMill.CurrentSkepticism)
                    .F(effectiveTrust)
                    .F(profile != null ? profile.RiskTolerance : (float?)null)
                    .F(actionChance)
                    .F(float.IsNaN(trustRoll) ? (float?)null : trustRoll)
                    .F(float.IsNaN(expertChance) ? (float?)null : expertChance)
                    .I(delayWeeks >= 0 ? delayWeeks : (int?)null)
                    .I(reactionWeek >= 0 ? reactionWeek : (int?)null)
                    .I(news.EventTriggerWeek)
                    .N()                                        // acted_on_week, by seq adjacency
                    .S(ScopeLabel(news.NewsData))
                    .I(inScope)
                    .F(inScopeValue)
                    .End();
            });
        }

        private static bool MatchesAnyScope(Antique antique, IReadOnlyList<NewsEventData> data)
        {
            if (antique.Definition == null) return false;

            for (int i = 0; i < data.Count; i++)
            {
                var effect = data[i];
                if (effect == null) continue;

                bool match = effect.targetScope switch
                {
                    EventEffect.TargetScope.AntiqueType => antique.Definition.Type == effect.AntiqueType,
                    EventEffect.TargetScope.Country => antique.Definition.Country == effect.Country,
                    EventEffect.TargetScope.Century => antique.Definition.Century == effect.Century,
                    _ => false
                };
                if (match) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- fact_rumour

        public void RumourPlanted(RumourRecord record) => WriteRumour(record, "Planted");

        public void RumourDebunked(RumourRecord record) => WriteRumour(record, "Debunked");

        private void WriteRumour(RumourRecord record, string eventName)
        {
            _run.Safe(T.FactRumourName, "OnRumour" + eventName, () =>
            {
                var inventory = _host.Traders.InventoryOf(record.PlanterId);

                int inScope = 0;
                float inScopeValue = 0f;
                if (inventory != null && record.ScopeKeys != null)
                {
                    foreach (var pair in inventory.Holdings)
                    {
                        if (pair.Value == null || !MatchesScopeKeys(pair.Value, record.ScopeKeys)) continue;
                        inScope++;
                        inScopeValue += pair.Value.CurrentPrice;
                    }
                }

                var table = _run.Table(T.FactRumourName, T.FactRumourHeader);
                _run.Stamp(table)
                    .S(record.NewsId)
                    .S(eventName)
                    .S(record.PlanterId)
                    .S(record.PlanterName)
                    .S(record.Title)
                    .S(record.Scope)
                    .S(TelemetryRun.Join(record.ScopeKeys, k => k))
                    .B(record.Bullish)
                    .I(record.PlantWeek)
                    .I(record.SpreadWeek)
                    .I(record.ClaimWeek)
                    .F(record.Spend)
                    .F(record.CostScale)
                    .F(record.Credibility)
                    .B(record.Traced)
                    .F(RumourMill.CurrentSkepticism)
                    .F(inventory != null ? inventory.Cash : (float?)null)
                    .I(inScope)
                    .F(inScopeValue)
                    .End();
            });
        }

        private static bool MatchesScopeKeys(Antique antique, List<string> keys)
        {
            var definition = antique.Definition;
            if (definition == null) return false;

            foreach (string key in keys)
            {
                if (string.IsNullOrEmpty(key)) continue;
                if (key.EndsWith(":" + definition.Type) ||
                    key.EndsWith(":" + definition.Country) ||
                    key.EndsWith(":" + definition.Century)) return true;
            }
            return false;
        }

        // ---------------------------------------------------------------- fact_auction

        public void AuctionAnnounced(Auction auction) => WriteAuction(auction, null, "Announced");

        public void AuctionFinished(Auction auction) => WriteAuction(auction, null, "Finished");

        public void LotSettled(Auction auction, AuctionLot lot)
        {
            _run.Safe(T.FactAuctionName, "OnLotSettled", () =>
            {
                if (lot != null) _host.Dimensions.EnsureAntique(lot.Antique, "AuctionLot");

                // An auction lot is not a market listing, so nothing in the market feed can name
                // it. This is the only place the won antique and its winner appear together.
                if (lot != null && lot.IsSold && lot.Antique != null)
                    _host.Traders.NotePath(lot.Antique.Id, "AuctionWin", lot.WinnerId, "",
                        lot.HammerPrice, float.NaN);

                WriteAuction(auction, lot, "LotSettled");
            });
        }

        public void LiveSessionStarted(AuctionSession session)
        {
            _run.Safe(T.FactAuctionBidName, "OnLiveSessionStarted", () =>
            {
                if (session == null) return;

                // Tracked like every other subscription: a live auction can outlast the session
                // that was watching it, and a handler firing afterwards must not reach game code.
                Action<LotBidding> onLotOpened = bidding =>
                {
                    if (bidding == null) return;

                    Action<AuctionBidder, float> onBid = (bidder, amount) =>
                        WriteBid(session, bidding, bidder, "Bid", amount);
                    Action<AuctionBidder> onWithdraw = bidder =>
                        WriteBid(session, bidding, bidder, "Withdraw", float.NaN);

                    bidding.OnBid += onBid;
                    bidding.OnWithdraw += onWithdraw;

                    _detach.Add(() =>
                    {
                        bidding.OnBid -= onBid;
                        bidding.OnWithdraw -= onWithdraw;
                    });
                };

                session.OnLotOpened += onLotOpened;
                _detach.Add(() => session.OnLotOpened -= onLotOpened);
            });
        }

        private void WriteAuction(Auction auction, AuctionLot lot, string eventName)
        {
            _run.Safe(T.FactAuctionName, "Auction" + eventName, () =>
            {
                float premium = _host.AuctionHouse != null && _host.AuctionHouse.Settings != null && lot != null
                    ? lot.HammerPrice * _host.AuctionHouse.Settings.BuyersPremiumRate
                    : 0f;

                var table = _run.Table(T.FactAuctionName, T.FactAuctionHeader);
                _run.Stamp(table)
                    .S(auction != null ? auction.Id : "")
                    .I(auction != null ? auction.Number : 0)
                    .S(eventName)
                    .I(auction != null ? auction.Week : 0)
                    .I(auction != null ? auction.StartTick : 0)
                    .I(auction != null ? auction.AnnouncedOnWeek : 0)
                    .I(lot != null ? lot.LotNumber : (int?)null)
                    .S(lot != null && lot.Antique != null ? lot.Antique.Id : null)
                    .S(lot != null && lot.Antique != null ? lot.Antique.DefinitionId : null)
                    .F(lot != null ? lot.EstimateLow : (float?)null)
                    .F(lot != null ? lot.EstimateHigh : (float?)null)
                    .F(lot != null ? lot.StartPrice : (float?)null)
                    .B(lot != null ? lot.HasProvenance : (bool?)null)
                    .E(lot != null ? (System.Enum)lot.Status : null)
                    .S(lot != null ? lot.WinnerId : null)
                    .F(lot != null && lot.IsSold ? lot.HammerPrice : (float?)null)
                    .F(lot != null ? lot.MarketValueAtSale : (float?)null)
                    .I(lot != null ? lot.BidCount : (int?)null)
                    .F(lot != null && lot.IsSold ? premium : (float?)null)
                    .B(auction != null && auction.PlayerJoined)
                    .End();
            });
        }

        private void WriteBid(AuctionSession session, LotBidding bidding, AuctionBidder bidder, string eventName, float amount)
        {
            _run.Safe(T.FactAuctionBidName, "OnBid", () =>
            {
                var lot = bidding != null ? bidding.Lot : null;
                var antique = lot != null ? lot.Antique : null;
                float marketValue = antique != null ? antique.CurrentPrice : 0f;

                var table = _run.Table(T.FactAuctionBidName, T.FactAuctionBidHeader);
                _run.Stamp(table)
                    .S(session != null && session.Auction != null ? session.Auction.Id : "")
                    .I(lot != null ? lot.LotNumber : 0)
                    .S(antique != null ? antique.Id : "")
                    .S(bidder != null ? bidder.Id : "")
                    .S(bidder != null ? bidder.Name : "")
                    .E(bidder != null ? (System.Enum)bidder.Kind : null)
                    .S(eventName)
                    .F(float.IsNaN(amount) ? (float?)null : amount)
                    .F(bidder != null ? bidder.Limit : 0f)
                    .F(bidder != null ? bidder.Cap : 0f)
                    .F(marketValue)
                    .F(float.IsNaN(amount) ? (float?)null : amount - marketValue)
                    .End();
            });
        }

        // ---------------------------------------------------------------- fact_world_event

        public void EventTriggered(ActiveEvent active) => WriteWorldEvent(active, active.Definition, "Triggered",
            active.StartWeek, active.EndWeek);

        public void EventEnded(ActiveEvent active) => WriteWorldEvent(active, active.Definition, "Ended",
            active.StartWeek, active.EndWeek);

        public void EventScheduled(EventDefinition definition, int triggerWeek) =>
            WriteWorldEvent(null, definition, "Scheduled", triggerWeek, 0);

        public void EventFailed(EventDefinition definition, int triggerWeek) =>
            WriteWorldEvent(null, definition, "Failed", triggerWeek, 0);

        private void WriteWorldEvent(ActiveEvent active, EventDefinition definition, string lifecycle,
            int triggerWeek, int endWeek)
        {
            _run.Safe(T.FactWorldEventName, "WorldEvent" + lifecycle, () =>
            {
                var effects = active != null && active.EffectInstances != null
                    ? (IReadOnlyList<EventEffect>)active.EffectInstances
                    : definition != null ? definition.Effects : null;

                float permDemand = 0f, tempDemand = 0f, permSupply = 0f, tempSupply = 0f;
                var scopes = new System.Text.StringBuilder();
                var affected = new HashSet<string>();

                if (effects != null)
                {
                    for (int i = 0; i < effects.Count; i++)
                    {
                        var effect = effects[i];
                        if (effect == null) continue;

                        if (effect is ChangeDemandEffect demand)
                        {
                            permDemand += demand.permDemandChange;
                            tempDemand += demand.tempDemandChange;
                            Append(scopes, demand.Scope, demand.AntiqueType, demand.Country, demand.Century);
                            CollectAffected(affected, demand.Scope, demand.AntiqueType, demand.Country, demand.Century);
                        }
                        else if (effect is ChangeSupplyEffect supply)
                        {
                            permSupply += supply.permSupplyChange;
                            tempSupply += supply.tempSupplyChange;
                            Append(scopes, supply.Scope, supply.AntiqueType, supply.Country, supply.Century);
                            CollectAffected(affected, supply.Scope, supply.AntiqueType, supply.Country, supply.Century);
                        }
                    }
                }

                var table = _run.Table(T.FactWorldEventName, T.FactWorldEventHeader);
                _run.Stamp(table)
                    .S(active != null ? active.EventDefinitionID + ":" + active.StartWeek : "")
                    .S(definition != null ? definition.Id : "")
                    .S(definition != null ? definition.DisplayName : "")
                    .E(definition != null ? (System.Enum)definition.EventType : null)
                    .S(lifecycle)
                    .I(triggerWeek)
                    .I(endWeek > 0 ? endWeek : (int?)null)
                    .I(endWeek > 0 ? endWeek - triggerWeek : (int?)null)
                    .S(TelemetryRun.Join(effects, e => e != null ? e.GetType().Name : null))
                    .S(scopes.ToString())
                    .F(permDemand)
                    .F(tempDemand)
                    .F(permSupply)
                    .F(tempSupply)
                    .S(TelemetryRun.Join(affected, id => id))
                    .End();
            });
        }

        /// <summary>The antique types an effect's scope actually covers, so the blast radius joins
        /// to fact_type_state instead of having to be re-derived.</summary>
        private static void CollectAffected(HashSet<string> into, EventEffect.TargetScope scope,
            AntiqueEnums.AntiqueType type, AntiqueEnums.Country country, AntiqueEnums.Century century)
        {
            var all = AntiqueDatabase.GetAll();
            if (all == null) return;

            for (int i = 0; i < all.Count; i++)
            {
                var definition = all[i];
                if (definition == null) continue;

                bool match = scope switch
                {
                    EventEffect.TargetScope.AntiqueType => definition.Type == type,
                    EventEffect.TargetScope.Country => definition.Country == country,
                    EventEffect.TargetScope.Century => definition.Century == century,
                    _ => false
                };

                if (match) into.Add(definition.Id);
            }
        }

        private static void Append(System.Text.StringBuilder sb, EventEffect.TargetScope scope,
            AntiqueEnums.AntiqueType type, AntiqueEnums.Country country, AntiqueEnums.Century century)
        {
            string value = scope switch
            {
                EventEffect.TargetScope.AntiqueType => type.ToString(),
                EventEffect.TargetScope.Country => country.ToString(),
                EventEffect.TargetScope.Century => century.ToString(),
                _ => "Other"
            };

            if (sb.Length > 0) sb.Append('|');
            sb.Append(scope).Append(':').Append(value);
        }

        // ---------------------------------------------------------------- fact_dependency_influence

        public void TickPass()
        {
            var engine = _host.Dependencies != null ? _host.Dependencies.Engine : null;
            if (engine == null) return;

            var table = _run.Table(T.FactDependencyInfluenceName, T.FactDependencyInfluenceHeader);
            var seen = new HashSet<string>();

            foreach (var influence in engine.ActiveInfluences)
            {
                if (influence == null) continue;

                string key = influence.Facet + ">" + influence.Origin + ">" + influence.Kind + ">" + influence.CreatedOnWeek;
                seen.Add(key);

                bool known = _influences.TryGetValue(key, out var before);
                if (known && Mathf.Approximately(before.Amount, influence.Amount) &&
                    before.LastRefreshed == influence.LastRefreshedOnWeek) continue;

                string eventName = !known ? "Created"
                    : influence.LastRefreshedOnWeek != before.LastRefreshed ? "Refreshed"
                    : "Decayed";

                _influences[key] = new InfluenceView
                {
                    Amount = influence.Amount,
                    LastRefreshed = influence.LastRefreshedOnWeek
                };

                WriteInfluence(table, influence, eventName, known ? (float?)before.Amount : null);
            }

            _scratch.Clear();
            foreach (var pair in _influences)
                if (!seen.Contains(pair.Key)) _scratch.Add(pair.Key);

            foreach (string key in _scratch) _influences.Remove(key);
        }

        private void WriteInfluence(CsvTable table, DependencyInfluence influence, string eventName, float? previous)
        {
            var engine = _host.Dependencies.Engine;
            int affected = 0;
            float applied = 0f;

            foreach (var pair in engine.CurrentModifiers)
            {
                if (Mathf.Approximately(pair.Value, 0f)) continue;
                affected++;
                applied += pair.Value;
            }

            _run.StampWeek(table)
                .S(eventName)
                .E(influence.Facet.Kind)
                .I(influence.Facet.Value)
                .S(MarketFacets.DisplayName(influence.Facet))
                .E(influence.Origin.Kind)
                .I(influence.Origin.Value)
                .S(MarketFacets.DisplayName(influence.Origin))
                .E(influence.Kind)
                .F(influence.Amount)
                .F(previous)
                .F(influence.DecayPerWeek)
                .I(influence.Depth)
                .I(influence.CreatedOnWeek)
                .I(influence.LastRefreshedOnWeek)
                .I(affected)
                .F(applied)
                .N()                                            // trigger_seq, by seq adjacency
                .End();
        }
    }
}
