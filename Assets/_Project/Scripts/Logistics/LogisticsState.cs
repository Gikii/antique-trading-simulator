using System.Collections.Generic;
using AntiqueTradingSimulator.Economy;

namespace AntiqueTradingSimulator.Logistics
{
    public class LogisticsState
    {
        public List<ShipmentState> Shipments = new List<ShipmentState>();

        public List<DeliveryRecordState> DeliveryLog = new List<DeliveryRecordState>();


        public Dictionary<ShippingZone, int> ZoneDelayWeeks = new Dictionary<ShippingZone, int>();

        /// <summary>Weather of the current season and the forecast one (null in saves made before seasons).</summary>
        public Seasons.SeasonWeatherState Weather;
    }

    public class ShipmentState
    {
        public string ShipmentId;
        public string AntiqueId;
        public string AntiqueName;


        public string RecipientOwnerId;

        public string RecipientName;

        public TransportOption Option;
        public ShippingZone Zone;
        public float Cost;

        public int DispatchWeek;
        public int ArrivalWeek;

        public int SeasonalShiftWeeks;
        public bool EarlyArrivalRolled;

        public static ShipmentState Capture(Shipment shipment, string recipientOwnerId)
        {
            if (shipment == null) return null;

            return new ShipmentState
            {
                ShipmentId = shipment.ShipmentId,
                AntiqueId = shipment.AntiqueId,
                AntiqueName = shipment.AntiqueName,
                RecipientOwnerId = recipientOwnerId,
                RecipientName = shipment.RecipientName,
                Option = shipment.Option,
                Zone = shipment.Zone,
                Cost = shipment.Cost,
                DispatchWeek = shipment.DispatchWeek,
                ArrivalWeek = shipment.ArrivalWeek,
                SeasonalShiftWeeks = shipment.SeasonalShiftWeeks,
                EarlyArrivalRolled = shipment.EarlyArrivalRolled
            };
        }

        public Shipment Restore(TraderInventory recipient)
        {

            var quote = new TransportQuote(Option, Zone, Cost, DispatchWeek, ArrivalWeek - DispatchWeek);

            return new Shipment(AntiqueId, AntiqueName, recipient, RecipientName, quote)
            {
                ShipmentId = this.ShipmentId,
                ArrivalWeek = this.ArrivalWeek,
                SeasonalShiftWeeks = this.SeasonalShiftWeeks,
                EarlyArrivalRolled = this.EarlyArrivalRolled
            };
        }
    }

    public class DeliveryRecordState
    {
        public ShipmentState Shipment;
        public int Week;
        public float ConditionLost;
    }

    public class WarehouseState
    {
        public int CapacityLevel;
        public int SecurityLevel;
    }
}
