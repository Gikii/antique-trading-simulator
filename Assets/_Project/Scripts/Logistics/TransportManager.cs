using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.Seasons;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AntiqueTradingSimulator.Logistics
{
    /// <summary>
    /// Moves purchased antiques from the seller to the buyer's warehouse. A purchase
    /// never puts an item straight into storage: the buyer gets a TransportQuote
    /// (cost + arrival day), the antique enters their inventory with Status = InTransit,
    /// and only on its ArrivalWeek does this manager deliver it — possibly damaged,
    /// depending on the transport option. Used by the player, NPCs and event rewards alike.
    ///
    /// Seasons (SeasonSettings on the TimeManager) add weather: carrier prices change with the
    /// season of dispatch, and the season of the week a shipment is due sets its damage risk and
    /// its chance to be held up a week (rolled in that week) or to arrive a week early (rolled
    /// the week before). A shipment gets at most one such weather shift.
    /// </summary>
    public class TransportManager : MonoBehaviour
    {
        [SerializeField] private TransportSettings settings;
        [SerializeField] private EconomyManager economyManager;
        [SerializeField] private Core.TimeManager timeManager;

        private readonly List<Shipment> _shipments = new();
        public IReadOnlyList<Shipment> Shipments => _shipments;

        /// <summary>A finished shipment — kept briefly so the UI can show "delivered today".</summary>
        public readonly struct DeliveryRecord
        {
            public readonly Shipment Shipment;
            public readonly int Week;
            public readonly float ConditionLost;
            public bool Damaged => ConditionLost > 0f;

            public DeliveryRecord(Shipment shipment, int week, float conditionLost)
            {
                Shipment = shipment;
                Week = week;
                ConditionLost = conditionLost;
            }
        }

        private const int MaxDeliveryLog = 50;
        private readonly List<DeliveryRecord> _deliveryLog = new();

        /// <summary>Most recent deliveries, oldest first (capped at MaxDeliveryLog).</summary>
        public IReadOnlyList<DeliveryRecord> DeliveryLog => _deliveryLog;

        // Extra days added per zone, e.g. by a future "port strike" event.
        // Affects new quotes only; use DelayShipments to also hold up items already on the way.
        private readonly Dictionary<ShippingZone, int> _zoneDelayWeeks = new();

        public TransportSettings Settings => settings;

        /// <summary>Seasonal transport conditions for one shipment, as shown to the buyer.</summary>
        public readonly struct SeasonalOutlook
        {
            public readonly Season Season;
            public readonly float DelayChance;
            public readonly float EarlyChance;
            public readonly float DamageChance;
            public readonly string Description;

            /// <summary>The season's weather if it is already known (the season is under way), otherwise Normal.</summary>
            public readonly WeatherSeverity Severity;

            public SeasonalOutlook(Season season, float delayChance, float earlyChance, float damageChance, string description,
                WeatherSeverity severity = WeatherSeverity.Normal)
            {
                Season = season;
                DelayChance = delayChance;
                EarlyChance = earlyChance;
                DamageChance = damageChance;
                Description = description;
                Severity = severity;
            }
        }

        [Tooltip("Publishes the seasonal weather forecasts. Found automatically if empty.")]
        [SerializeField] private News.NewsManager newsManager;

        /// <summary>How harsh the seasons turn out, and the forecasts about it (see SeasonWeather).</summary>
        public SeasonWeather Weather { get; private set; }

        public event Action<Shipment> OnShipmentDispatched;

        /// <summary>(shipment, antique, condition lost — 0 if it arrived intact)</summary>
        public event Action<Shipment, Antique, float> OnShipmentDelivered;

        /// <summary>Fired when in-flight shipments get pushed back (DelayShipments).</summary>
        public event Action<ShippingZone, int> OnShipmentsDelayed;

        /// <summary>(shipment, weeks: +1 held up / -1 early, season responsible) — a weather shift of one shipment.</summary>
        public event Action<Shipment, int, Season> OnShipmentShiftedBySeason;

        public event Action OnShipmentsRestored;

        private int CurrentWeek => timeManager != null ? timeManager.CurrentWeek : 0;

        // Without a TimeManager there is no calendar, so no seasonal effects.
        private SeasonSettings SeasonRules => timeManager != null ? timeManager.SeasonSettings : null;

        void Awake()
        {
            if (economyManager == null) economyManager = FindFirstObjectByType<EconomyManager>();
            if (timeManager == null) timeManager = FindFirstObjectByType<Core.TimeManager>();

            if (settings == null)
            {
                Debug.LogWarning("TransportManager: no TransportSettings assigned — using default values.");
                settings = ScriptableObject.CreateInstance<TransportSettings>();
            }

            if (newsManager == null) newsManager = FindFirstObjectByType<News.NewsManager>();
            Weather = new SeasonWeather(timeManager, () => SeasonRules, item =>
            {
                if (newsManager != null) newsManager.PublishManual(item);
            });
        }

        void OnEnable()
        {
            if (timeManager != null)
                timeManager.OnWeekChanged += HandleWeekChanged;
        }

        void OnDisable()
        {
            if (timeManager != null)
                timeManager.OnWeekChanged -= HandleWeekChanged;
        }

        // ---------------------------------------------------------------- quoting

        /// <summary>
        /// What shipping this antique with the given option would cost and when it
        /// would arrive if dispatched today. Pure — does not change any state.
        /// </summary>
        /// <param name="free">True for deliveries the recipient doesn't pay for (e.g. event rewards).</param>
        /// <param name="buyer">
        /// Who receives the shipment. Their TraderInventory.Modifiers (e.g. the player's Logistics
        /// upgrade) lower the cost and shorten the base delivery time — never below 1 day.
        /// Event-driven zone delays are added on top and are not shortened. Null = base terms.
        /// </param>
        public TransportQuote Quote(Antique antique, TransportOption option, bool free = false, TraderInventory buyer = null)
        {
            if (antique == null) return null;

            var modifiers = buyer?.Modifiers;
            ShippingZone zone = antique.ShippingZone;

            int baseWeeks = settings.GetBaseDurationWeeks(zone, option);
            if (modifiers != null)
                baseWeeks = Mathf.Max(1, baseWeeks - modifiers.TransportWeeksReduction);
            int duration = baseWeeks + GetZoneDelay(zone);

            float cost = free ? 0f : settings.GetCost(antique.SalePrice, zone, option);
            if (SeasonRules != null)
                cost *= SeasonRules.GetTransportCostMultiplier(timeManager.CurrentSeason);
            if (modifiers != null)
                cost *= 1f - modifiers.TransportCostReduction;

            return new TransportQuote(option, zone, cost, CurrentWeek, Mathf.Max(1, duration));
        }

        /// <summary>
        /// Damage chance of a service for a shipment delivered in the given week, season and its weather
        /// included — as far as the weather is known (a coming season's weather is a secret until it starts).
        /// </summary>
        public float GetDamageChance(TransportOption option, int deliveryWeek) =>
            DamageChance(option, deliveryWeek, KnownWeather(deliveryWeek));

        private float DamageChance(TransportOption option, int deliveryWeek, WeatherSeverity weather)
        {
            float chance = settings.GetDamageChance(option);
            if (SeasonRules != null)
            {
                chance *= SeasonRules.GetDamageChanceMultiplier(timeManager.SeasonOf(deliveryWeek));
                chance *= SeasonRules.GetSeverityEffect(weather).DamageMultiplier;
            }
            return Mathf.Clamp01(chance);
        }

        private WeatherSeverity KnownWeather(int week) => Weather != null ? Weather.KnownFor(week) : WeatherSeverity.Normal;
        private WeatherSeverity ActualWeather(int week) => Weather != null ? Weather.ActualFor(week) : WeatherSeverity.Normal;

        /// <summary>
        /// Weather outlook for a shipment due in the given week: the season it arrives in, its chance
        /// to be held up or to arrive early, and its damage risk. All zero without seasons.
        /// </summary>
        public SeasonalOutlook GetSeasonalOutlook(ShippingZone zone, TransportOption option, int arrivalWeek)
        {
            float damage = GetDamageChance(option, arrivalWeek);
            if (SeasonRules == null)
                return new SeasonalOutlook(Season.Spring, 0f, 0f, damage, "");

            Season season = timeManager.SeasonOf(arrivalWeek);
            WeatherSeverity weather = KnownWeather(arrivalWeek);
            var effect = SeasonRules.GetSeverityEffect(weather);
            return new SeasonalOutlook(
                season,
                Mathf.Clamp01(SeasonRules.GetDelayChance(season, zone, option) * effect.DelayMultiplier),
                Mathf.Clamp01(SeasonRules.GetEarlyChance(season, zone) * effect.EarlyMultiplier),
                damage,
                SeasonRules.Get(season).Description,
                weather);
        }

        public SeasonalOutlook GetSeasonalOutlook(TransportQuote quote) =>
            GetSeasonalOutlook(quote.Zone, quote.Option, quote.ArrivalWeek);

        /// <summary>Quotes every transport option for one antique — handy for a selection modal.</summary>
        public List<TransportQuote> QuoteAllOptions(Antique antique, TraderInventory buyer = null)
        {
            var quotes = new List<TransportQuote>();
            foreach (TransportOption option in Enum.GetValues(typeof(TransportOption)))
                quotes.Add(Quote(antique, option, buyer: buyer));
            return quotes;
        }

        // ---------------------------------------------------------------- dispatching

        /// <summary>
        /// Starts the journey of an antique that is already in the recipient's inventory
        /// (TraderInventory.Buy / GrantHolding with the same quote). Payment is handled
        /// by the inventory — this only tracks the shipment until delivery.
        /// </summary>
        public Shipment Dispatch(Antique antique, TraderInventory recipient, TransportQuote quote, string recipientName)
        {
            if (antique == null || recipient == null || quote == null)
            {
                Debug.LogWarning("TransportManager: Dispatch called with missing antique, recipient or quote.");
                return null;
            }

            antique.MarkInTransit(quote.ArrivalWeek);

            var shipment = new Shipment(antique.Id, antique.Name, recipient, recipientName, quote);
            _shipments.Add(shipment);

            Debug.Log($"TransportManager: dispatched {shipment}. Cost {quote.Cost:F2}.");
            OnShipmentDispatched?.Invoke(shipment);
            return shipment;
        }

        // ---------------------------------------------------------------- delivery

        private void HandleWeekChanged(int newWeek)
        {
            // The season's weather (and its forecasts) first — this week's transport rolls depend on it.
            Weather?.AdvanceWeek(newWeek);

            RollEarlyArrivals(newWeek);

            var due = _shipments.Where(s => s.ArrivalWeek <= newWeek).ToList();
            foreach (var shipment in due)
            {
                if (TryHoldUpByWeather(shipment, newWeek))
                    continue;

                _shipments.Remove(shipment);
                Deliver(shipment);
            }
        }

        /// <summary>
        /// In the week before a shipment is due, good weather may bring it in now — but never in the
        /// week it was dispatched, so every journey still takes at least a week.
        /// </summary>
        private void RollEarlyArrivals(int newWeek)
        {
            if (SeasonRules == null) return;

            foreach (var shipment in _shipments)
            {
                if (shipment.EarlyArrivalRolled || shipment.SeasonalShiftWeeks != 0) continue;
                if (shipment.ArrivalWeek - 1 != newWeek || newWeek <= shipment.DispatchWeek) continue;

                shipment.EarlyArrivalRolled = true;

                Season season = timeManager.SeasonOf(shipment.ArrivalWeek);
                float earlyChance = SeasonRules.GetEarlyChance(season, shipment.Zone) *
                                    SeasonRules.GetSeverityEffect(ActualWeather(shipment.ArrivalWeek)).EarlyMultiplier;
                if (UnityEngine.Random.value >= Mathf.Clamp01(earlyChance)) continue;

                shipment.ArrivalWeek = newWeek;
                shipment.SeasonalShiftWeeks = -1;

                var antique = shipment.Recipient?.GetHolding(shipment.AntiqueId);
                antique?.AppendHistory($"Arrived a week early thanks to good {season.ToLowerString()} weather (week {newWeek}).");

                Debug.Log($"TransportManager: {shipment.AntiqueName} arrives a week early ({season}).");
                OnShipmentShiftedBySeason?.Invoke(shipment, -1, season);
            }
        }

        /// <summary>In the week a shipment is due, bad weather may hold it up one more week. True if it was held up.</summary>
        private bool TryHoldUpByWeather(Shipment shipment, int newWeek)
        {
            if (SeasonRules == null || shipment.SeasonalShiftWeeks != 0) return false;

            Season season = timeManager.SeasonOf(newWeek);
            float delayChance = SeasonRules.GetDelayChance(season, shipment.Zone, shipment.Option) *
                                SeasonRules.GetSeverityEffect(ActualWeather(newWeek)).DelayMultiplier;
            if (UnityEngine.Random.value >= Mathf.Clamp01(delayChance))
                return false;

            shipment.ArrivalWeek = newWeek + 1;
            shipment.SeasonalShiftWeeks = 1;

            var antique = shipment.Recipient?.GetHolding(shipment.AntiqueId);
            if (antique != null)
            {
                antique.MarkInTransit(shipment.ArrivalWeek);
                antique.AppendHistory($"Held up a week by {season.ToLowerString()} weather (week {newWeek}).");
                shipment.Recipient.NotifyHoldingUpdated(antique.Id);
            }

            Debug.Log($"TransportManager: {shipment.AntiqueName} held up a week by {season} weather.");
            OnShipmentShiftedBySeason?.Invoke(shipment, 1, season);
            return true;
        }

        private void Deliver(Shipment shipment)
        {
            var antique = shipment.Recipient?.GetHolding(shipment.AntiqueId);
            if (antique == null)
            {
                Debug.LogWarning($"TransportManager: {shipment.AntiqueName} ({shipment.AntiqueId}) is no longer owned by {shipment.RecipientName} — shipment dropped.");
                return;
            }

            float conditionLost = 0f;
            if (UnityEngine.Random.value < DamageChance(shipment.Option, CurrentWeek, ActualWeather(CurrentWeek)))
            {
                float before = antique.Condition;
                antique.Condition = Antique.QuantizeConditionDown(antique.Condition - settings.RollConditionLoss());
                conditionLost = before - antique.Condition;

                if (conditionLost > 0f)
                    antique.AppendHistory($"Damaged in transit (week {shipment.ArrivalWeek}, {shipment.Option.ToDisplayString()} transport).");
            }

            // Condition affects price — re-value before anyone is notified.
            if (economyManager != null && economyManager.Market != null)
                economyManager.Market.RecalculatePrice(antique);

            shipment.Recipient.CompleteDelivery(antique.Id);

            if (conditionLost > 0f)
                Debug.Log($"TransportManager: {shipment.AntiqueName} arrived DAMAGED at {shipment.RecipientName} (condition -{conditionLost:F2}).");
            else
                Debug.Log($"TransportManager: {shipment.AntiqueName} arrived at {shipment.RecipientName}.");

            _deliveryLog.Add(new DeliveryRecord(shipment, CurrentWeek, conditionLost));
            if (_deliveryLog.Count > MaxDeliveryLog)
                _deliveryLog.RemoveAt(0);

            OnShipmentDelivered?.Invoke(shipment, antique, conditionLost);
        }

        // ---------------------------------------------------------------- queries

        public IEnumerable<Shipment> GetShipmentsFor(TraderInventory recipient) =>
            _shipments.Where(s => s.Recipient == recipient);

        public Shipment GetShipmentForAntique(string antiqueId) =>
            _shipments.FirstOrDefault(s => s.AntiqueId == antiqueId);

        /// <summary>Deliveries to this recipient on or after the given day, newest first.</summary>
        public IEnumerable<DeliveryRecord> GetDeliveriesFor(TraderInventory recipient, int sinceWeek) =>
            _deliveryLog.Where(d => d.Shipment.Recipient == recipient && d.Week >= sinceWeek).Reverse();

        // ---------------------------------------------------------------- delays (hooks for future events)

        public int GetZoneDelay(ShippingZone zone) =>
            _zoneDelayWeeks.TryGetValue(zone, out int weeks) ? weeks : 0;

        /// <summary>Adds (or, with a negative value, removes) extra days for new shipments in a zone.</summary>
        public void AddZoneDelay(ShippingZone zone, int weeks)
        {
            _zoneDelayWeeks[zone] = Mathf.Max(0, GetZoneDelay(zone) + weeks);
        }

        /// <summary>Pushes back the arrival of every shipment already travelling in the given zone.</summary>
        public void DelayShipments(ShippingZone zone, int weeks)
        {
            if (weeks <= 0) return;

            foreach (var shipment in _shipments.Where(s => s.Zone == zone))
            {
                shipment.ArrivalWeek += weeks;
                var antique = shipment.Recipient?.GetHolding(shipment.AntiqueId);
                if (antique == null) continue;

                antique.MarkInTransit(shipment.ArrivalWeek);
                shipment.Recipient.NotifyHoldingUpdated(antique.Id);
            }

            OnShipmentsDelayed?.Invoke(zone, weeks);
        }

        // ---------------------------------------------------------------- save / load

        public LogisticsState CaptureState()
        {
            var state = new LogisticsState
            {
                ZoneDelayWeeks = new Dictionary<ShippingZone, int>(_zoneDelayWeeks),
                Weather = Weather?.State
            };

            var ownerIdsByInventory = BuildOwnerIdLookup();

            foreach (var shipment in _shipments)
            {
                if (shipment == null) continue;
                state.Shipments.Add(ShipmentState.Capture(shipment, ResolveOwnerId(shipment, ownerIdsByInventory)));
            }

            foreach (var record in _deliveryLog)
            {
                if (record.Shipment == null) continue;

                state.DeliveryLog.Add(new DeliveryRecordState
                {
                    Shipment = ShipmentState.Capture(record.Shipment, ResolveOwnerId(record.Shipment, ownerIdsByInventory)),
                    Week = record.Week,
                    ConditionLost = record.ConditionLost
                });
            }

            return state;
        }

        /// <summary>
        /// Call after EconomyManager.RestoreState.
        /// </summary>
        public void RestoreState(LogisticsState state)
        {
            _shipments.Clear();
            _deliveryLog.Clear();
            _zoneDelayWeeks.Clear();
            Weather?.Restore(state?.Weather);

            if (state == null)
            {
                OnShipmentsRestored?.Invoke();
                return;
            }

            if (state.ZoneDelayWeeks != null)
                foreach (var pair in state.ZoneDelayWeeks)
                    _zoneDelayWeeks[pair.Key] = pair.Value;

            if (state.Shipments != null)
            {
                foreach (var shipmentState in state.Shipments)
                {
                    if (shipmentState == null) continue;

                    var recipient = ResolveRecipient(shipmentState);
                    if (recipient == null)
                    {
                        Debug.LogWarning($"TransportManager: '{shipmentState.AntiqueName}' was in transit to '{shipmentState.RecipientName}' ({shipmentState.RecipientOwnerId}), but that trader was not restored — shipment dropped. The antique stays marked InTransit.");
                        continue;
                    }

                    _shipments.Add(shipmentState.Restore(recipient));
                }
            }

            if (state.DeliveryLog != null)
            {
                foreach (var recordState in state.DeliveryLog)
                {
                    if (recordState?.Shipment == null) continue;

                    // A delivered shipment with no resolvable recipient is only a log line, so it
                    // is kept (with a null Recipient) rather than dropped.
                    var shipment = recordState.Shipment.Restore(ResolveRecipient(recordState.Shipment));
                    _deliveryLog.Add(new DeliveryRecord(shipment, recordState.Week, recordState.ConditionLost));
                }

                if (_deliveryLog.Count > MaxDeliveryLog)
                    _deliveryLog.RemoveRange(0, _deliveryLog.Count - MaxDeliveryLog);
            }

            OnShipmentsRestored?.Invoke();
        }

        private Dictionary<TraderInventory, string> BuildOwnerIdLookup()
        {
            var lookup = new Dictionary<TraderInventory, string>();
            if (economyManager == null) return lookup;

            foreach (var pair in economyManager.InventoriesByOwnerId)
                if (pair.Value != null)
                    lookup[pair.Value] = pair.Key;

            return lookup;
        }

        private static string ResolveOwnerId(Shipment shipment, Dictionary<TraderInventory, string> ownerIdsByInventory)
        {
            if (shipment.Recipient != null && ownerIdsByInventory.TryGetValue(shipment.Recipient, out string ownerId))
                return ownerId;

            Debug.LogWarning($"TransportManager: no owner Id for the recipient of '{shipment.AntiqueName}' — it registered without one, so this shipment will not survive the load.");
            return null;
        }

        private TraderInventory ResolveRecipient(ShipmentState shipmentState)
        {
            if (economyManager == null) return null;

            if (!string.IsNullOrEmpty(shipmentState.RecipientOwnerId) &&
                economyManager.InventoriesByOwnerId.TryGetValue(shipmentState.RecipientOwnerId, out var byOwnerId))
                return byOwnerId;

            foreach (var inventory in economyManager.Inventories)
                if (inventory != null && inventory.Owns(shipmentState.AntiqueId))
                    return inventory;

            return null;
        }

    }
}
