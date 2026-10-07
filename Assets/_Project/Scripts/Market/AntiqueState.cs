using AntiqueTradingSimulator.Logistics;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.Market
{
    public class AntiqueState
    {
        public string ListingId;
        public string DefinitionId;

        public int MarketListedOnDay = -1;
        public int EditionNumber;
        public string OwnerId = "";

        public float CurrentPrice;
        public float Condition;
        public float PriceFactor;

        public string History = "";

        public float PurchasePrice;
        public int PurchasedOnDay = -1;
        public float TransportCost;

        public float AskingPrice;
        public string ReservedForContractId;

        public ShippingZone ShippingZone = ShippingZone.Domestic;
        public AntiqueStatus Status = AntiqueStatus.Available;
        public int ArrivalDay = -1;

        public static AntiqueState Capture(Antique antique)
        {
            if (antique == null) return null;

            return new AntiqueState
            {
                ListingId = antique.ListingId,
                DefinitionId = antique.DefinitionId,
                MarketListedOnDay = antique.MarketListedOnDay,
                EditionNumber = antique.EditionNumber,
                OwnerId = antique.OwnerId,
                CurrentPrice = antique.CurrentPrice,
                Condition = antique.Condition,
                PriceFactor = antique.PriceFactor,
                History = antique.History,
                PurchasePrice = antique.PurchasePrice,
                PurchasedOnDay = antique.PurchasedOnDay,
                TransportCost = antique.TransportCost,
                AskingPrice = antique.AskingPrice,
                ReservedForContractId = antique.ReservedForContractId,
                ShippingZone = antique.ShippingZone,
                Status = antique.Status,
                ArrivalDay = antique.ArrivalDay
            };
        }

        public Antique Restore()
        {
            var antique = new Antique(DefinitionId, Condition, PriceFactor, History, EditionNumber, OwnerId)
            {
                ListingId = this.ListingId,
                MarketListedOnDay = this.MarketListedOnDay,

                Condition = this.Condition,
                PriceFactor = this.PriceFactor,
                CurrentPrice = this.CurrentPrice,

                PurchasePrice = this.PurchasePrice,
                PurchasedOnDay = this.PurchasedOnDay,
                TransportCost = this.TransportCost,
                AskingPrice = this.AskingPrice,
                ReservedForContractId = this.ReservedForContractId,
                ShippingZone = this.ShippingZone,
                Status = this.Status,
                ArrivalDay = this.ArrivalDay
            };

            return antique;
        }
    }
}
