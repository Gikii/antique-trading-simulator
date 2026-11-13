using AntiqueTradingSimulator.Logistics;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.Market
{
    public class AntiqueState
    {
        public string ListingId;
        public string DefinitionId;

        public int MarketListedOnWeek = -1;
        public int EditionNumber;
        public string OwnerId = "";

        public float CurrentPrice;
        public float Condition;
        public float PriceFactor;

        public string History = "";

        public float PurchasePrice;
        public int PurchasedOnWeek = -1;
        public float TransportCost;
        public float RestorationCost;

        public float AskingPrice;
        public string ReservedForContractId;

        public ShippingZone ShippingZone = ShippingZone.Domestic;
        public AntiqueStatus Status = AntiqueStatus.Available;
        public int ArrivalWeek = -1;
        public int RestorationEndWeek = -1;

        public static AntiqueState Capture(Antique antique)
        {
            if (antique == null) return null;

            return new AntiqueState
            {
                ListingId = antique.ListingId,
                DefinitionId = antique.DefinitionId,
                MarketListedOnWeek = antique.MarketListedOnWeek,
                EditionNumber = antique.EditionNumber,
                OwnerId = antique.OwnerId,
                CurrentPrice = antique.CurrentPrice,
                Condition = antique.Condition,
                PriceFactor = antique.PriceFactor,
                History = antique.History,
                PurchasePrice = antique.PurchasePrice,
                PurchasedOnWeek = antique.PurchasedOnWeek,
                TransportCost = antique.TransportCost,
                RestorationCost = antique.RestorationCost,
                AskingPrice = antique.AskingPrice,
                ReservedForContractId = antique.ReservedForContractId,
                ShippingZone = antique.ShippingZone,
                Status = antique.Status,
                ArrivalWeek = antique.ArrivalWeek,
                RestorationEndWeek = antique.RestorationEndWeek
            };
        }

        public Antique Restore()
        {
            var antique = new Antique(DefinitionId, Condition, PriceFactor, History, EditionNumber, OwnerId)
            {
                ListingId = this.ListingId,
                MarketListedOnWeek = this.MarketListedOnWeek,

                Condition = this.Condition,
                PriceFactor = this.PriceFactor,
                CurrentPrice = this.CurrentPrice,

                PurchasePrice = this.PurchasePrice,
                PurchasedOnWeek = this.PurchasedOnWeek,
                TransportCost = this.TransportCost,
                RestorationCost = this.RestorationCost,
                AskingPrice = this.AskingPrice,
                ReservedForContractId = this.ReservedForContractId,
                ShippingZone = this.ShippingZone,
                Status = this.Status,
                ArrivalWeek = this.ArrivalWeek,
                RestorationEndWeek = this.RestorationEndWeek
            };

            return antique;
        }
    }
}
