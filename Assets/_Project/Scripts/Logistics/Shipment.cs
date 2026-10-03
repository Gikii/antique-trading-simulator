using AntiqueTradingSimulator.Economy;
using System;

namespace AntiqueTradingSimulator.Logistics
{
    /// <summary>
    /// One antique on its way to its new owner. Refers to the antique by its ListingId
    /// (same pattern as the rest of the runtime data) — the antique itself stays in the
    /// recipient's TraderInventory with Status = InTransit for the whole journey.
    /// </summary>
    [Serializable]
    public class Shipment
    {
        public string ShipmentId;
        public string AntiqueId;
        public string AntiqueName;
        public string RecipientName;

        public TransportOption Option;
        public ShippingZone Zone;
        public float Cost;

        public int DispatchDay;
        public int ArrivalDay;

        // Runtime-only link to the owner's inventory. Will need to be re-resolved
        // from an owner Id once save/load exists.
        [NonSerialized] public TraderInventory Recipient;

        public int DaysRemaining(int currentDay) => Math.Max(0, ArrivalDay - currentDay);

        public Shipment(string antiqueId, string antiqueName, TraderInventory recipient, string recipientName, TransportQuote quote)
        {
            ShipmentId = Guid.NewGuid().ToString("N");
            AntiqueId = antiqueId;
            AntiqueName = antiqueName;
            Recipient = recipient;
            RecipientName = recipientName;
            Option = quote.Option;
            Zone = quote.Zone;
            Cost = quote.Cost;
            DispatchDay = quote.DispatchDay;
            ArrivalDay = quote.ArrivalDay;
        }

        public override string ToString() =>
            $"{AntiqueName} → {RecipientName} ({Option.ToDisplayString()}, {Zone.ToDisplayString()}), arrives day {ArrivalDay}";
    }
}
