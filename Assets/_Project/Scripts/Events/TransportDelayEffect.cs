using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.News;
using System;
using UnityEngine;

namespace AntiqueTradingSimulator.Events
{
    /// <summary>
    /// Disrupts transport in one shipping zone while the event lasts — storms on the Channel, frozen
    /// ports, flooded roads. New shipments to that zone take longer (the quotes show it), and shipments
    /// already on their way can be held up once when the event starts. Prices are not touched directly:
    /// what traders do about the delays is up to them.
    /// </summary>
    [Serializable]
    public class TransportDelayEffect : EventEffect
    {
        [Tooltip("Shipping zone the disruption hits.")]
        public ShippingZone Zone = ShippingZone.International;

        [Tooltip("Extra weeks for new shipments in this zone while the event lasts (removed when it ends).")]
        [Min(0)] public int NewShipmentDelayWeeks = 1;

        [Tooltip("Weeks added once, when the event starts, to shipments already travelling in this zone. 0 = spare them.")]
        [Min(0)] public int InTransitDelayWeeks = 1;

        public override void Apply(EventContext context)
        {
            var transport = context.Transport;
            if (transport == null)
            {
                Debug.LogWarning("TransportDelayEffect: no TransportManager in the context — nothing delayed.");
                return;
            }

            if (NewShipmentDelayWeeks > 0)
                transport.AddZoneDelay(Zone, NewShipmentDelayWeeks);

            if (InTransitDelayWeeks > 0)
                transport.DelayShipments(Zone, InTransitDelayWeeks);
        }

        public override void Revert(EventContext context)
        {
            // Shipments already held up keep their new arrival week; only new quotes go back to normal.
            if (NewShipmentDelayWeeks > 0)
                context.Transport?.AddZoneDelay(Zone, -NewShipmentDelayWeeks);
        }

        public override NewsEventData CreateNewsData() =>
            new NewsEventData(TargetScope.Other, default, default, default, affectsPriceUp: false)
            {
                IsTransportDisruption = true,
                Zone = Zone
            };

        public override EventEffect Clone() => new TransportDelayEffect
        {
            Zone = Zone,
            NewShipmentDelayWeeks = NewShipmentDelayWeeks,
            InTransitDelayWeeks = InTransitDelayWeeks
        };
    }
}
