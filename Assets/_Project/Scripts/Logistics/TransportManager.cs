using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Market;
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
    /// and only on its ArrivalDay does this manager deliver it — possibly damaged,
    /// depending on the transport option. Used by the player, NPCs and event rewards alike.
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
            public readonly int Day;
            public readonly float ConditionLost;
            public bool Damaged => ConditionLost > 0f;

            public DeliveryRecord(Shipment shipment, int day, float conditionLost)
            {
                Shipment = shipment;
                Day = day;
                ConditionLost = conditionLost;
            }
        }

        private const int MaxDeliveryLog = 50;
        private readonly List<DeliveryRecord> _deliveryLog = new();

        /// <summary>Most recent deliveries, oldest first (capped at MaxDeliveryLog).</summary>
        public IReadOnlyList<DeliveryRecord> DeliveryLog => _deliveryLog;

        // Extra days added per zone, e.g. by a future "port strike" event.
        // Affects new quotes only; use DelayShipments to also hold up items already on the way.
        private readonly Dictionary<ShippingZone, int> _zoneDelayDays = new();

        public TransportSettings Settings => settings;

        public event Action<Shipment> OnShipmentDispatched;

        /// <summary>(shipment, antique, condition lost — 0 if it arrived intact)</summary>
        public event Action<Shipment, Antique, float> OnShipmentDelivered;

        /// <summary>Fired when in-flight shipments get pushed back (DelayShipments).</summary>
        public event Action<ShippingZone, int> OnShipmentsDelayed;

        public event Action OnShipmentsRestored;

        private int CurrentDay => timeManager != null ? timeManager.CurrentDay : 0;

        void Awake()
        {
            if (economyManager == null) economyManager = FindFirstObjectByType<EconomyManager>();
            if (timeManager == null) timeManager = FindFirstObjectByType<Core.TimeManager>();

            if (settings == null)
            {
                Debug.LogWarning("TransportManager: no TransportSettings assigned — using default values.");
                settings = ScriptableObject.CreateInstance<TransportSettings>();
            }
        }

        void OnEnable()
        {
            if (timeManager != null)
                timeManager.OnDayChanged += HandleDayChanged;
        }

        void OnDisable()
        {
            if (timeManager != null)
                timeManager.OnDayChanged -= HandleDayChanged;
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

            int baseDays = settings.GetBaseDurationDays(zone, option);
            if (modifiers != null)
                baseDays = Mathf.Max(1, baseDays - modifiers.TransportDaysReduction);
            int duration = baseDays + GetZoneDelay(zone);

            float cost = free ? 0f : settings.GetCost(antique.SalePrice, zone, option);
            if (modifiers != null)
                cost *= 1f - modifiers.TransportCostReduction;

            return new TransportQuote(option, zone, cost, CurrentDay, Mathf.Max(1, duration));
        }

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

            antique.MarkInTransit(quote.ArrivalDay);

            var shipment = new Shipment(antique.Id, antique.Name, recipient, recipientName, quote);
            _shipments.Add(shipment);

            Debug.Log($"TransportManager: dispatched {shipment}. Cost {quote.Cost:F2}.");
            OnShipmentDispatched?.Invoke(shipment);
            return shipment;
        }

        // ---------------------------------------------------------------- delivery

        private void HandleDayChanged(int newDay)
        {
            var due = _shipments.Where(s => s.ArrivalDay <= newDay).ToList();
            foreach (var shipment in due)
            {
                _shipments.Remove(shipment);
                Deliver(shipment);
            }
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
            if (UnityEngine.Random.value < settings.GetDamageChance(shipment.Option))
            {
                float before = antique.Condition;
                antique.Condition = Mathf.Max(Antique.MinCondition, antique.Condition - settings.RollConditionLoss());
                conditionLost = before - antique.Condition;

                if (conditionLost > 0f)
                    antique.AppendHistory($"Damaged in transit (day {shipment.ArrivalDay}, {shipment.Option.ToDisplayString()} transport).");
            }

            // Condition affects price — re-value before anyone is notified.
            if (economyManager != null && economyManager.Market != null)
                economyManager.Market.RecalculatePrice(antique);

            shipment.Recipient.CompleteDelivery(antique.Id);

            if (conditionLost > 0f)
                Debug.Log($"TransportManager: {shipment.AntiqueName} arrived DAMAGED at {shipment.RecipientName} (condition -{conditionLost:F2}).");
            else
                Debug.Log($"TransportManager: {shipment.AntiqueName} arrived at {shipment.RecipientName}.");

            _deliveryLog.Add(new DeliveryRecord(shipment, CurrentDay, conditionLost));
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
        public IEnumerable<DeliveryRecord> GetDeliveriesFor(TraderInventory recipient, int sinceDay) =>
            _deliveryLog.Where(d => d.Shipment.Recipient == recipient && d.Day >= sinceDay).Reverse();

        // ---------------------------------------------------------------- delays (hooks for future events)

        public int GetZoneDelay(ShippingZone zone) =>
            _zoneDelayDays.TryGetValue(zone, out int days) ? days : 0;

        /// <summary>Adds (or, with a negative value, removes) extra days for new shipments in a zone.</summary>
        public void AddZoneDelay(ShippingZone zone, int days)
        {
            _zoneDelayDays[zone] = Mathf.Max(0, GetZoneDelay(zone) + days);
        }

        /// <summary>Pushes back the arrival of every shipment already travelling in the given zone.</summary>
        public void DelayShipments(ShippingZone zone, int days)
        {
            if (days <= 0) return;

            foreach (var shipment in _shipments.Where(s => s.Zone == zone))
            {
                shipment.ArrivalDay += days;
                var antique = shipment.Recipient?.GetHolding(shipment.AntiqueId);
                if (antique == null) continue;

                antique.MarkInTransit(shipment.ArrivalDay);
                shipment.Recipient.NotifyHoldingUpdated(antique.Id);
            }

            OnShipmentsDelayed?.Invoke(zone, days);
        }

        // ---------------------------------------------------------------- save / load

        public LogisticsState CaptureState()
        {
            var state = new LogisticsState { ZoneDelayDays = new Dictionary<ShippingZone, int>(_zoneDelayDays) };

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
                    Day = record.Day,
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
            _zoneDelayDays.Clear();

            if (state == null)
            {
                OnShipmentsRestored?.Invoke();
                return;
            }

            if (state.ZoneDelayDays != null)
                foreach (var pair in state.ZoneDelayDays)
                    _zoneDelayDays[pair.Key] = pair.Value;

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
                    _deliveryLog.Add(new DeliveryRecord(shipment, recordState.Day, recordState.ConditionLost));
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
