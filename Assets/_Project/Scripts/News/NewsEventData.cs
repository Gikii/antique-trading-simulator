using UnityEngine;
using AntiqueTradingSimulator.Events;
using AntiqueTradingSimulator.Market;

namespace AntiqueTradingSimulator.News {
    public class NewsEventData
    {
        public EventEffect.TargetScope targetScope;

        public AntiqueEnums.AntiqueType AntiqueType;
        public AntiqueEnums.Country Country;
        public AntiqueEnums.Century Century;

        public bool affectsPriceUp;

        /// <summary>
        /// Set for effects on transport rather than on prices (TransportDelayEffect — storms, frozen ports):
        /// the news then speaks of delays in <see cref="Zone"/>, and traders find nothing to buy or sell on it.
        /// </summary>
        public bool IsTransportDisruption;
        public Logistics.ShippingZone Zone;

        public NewsEventData(EventEffect.TargetScope targetScope, AntiqueEnums.AntiqueType antiqueType, AntiqueEnums.Country country, AntiqueEnums.Century century, bool affectsPriceUp)
        {
            this.targetScope = targetScope;
            AntiqueType = antiqueType;
            Country = country;
            Century = century;
            this.affectsPriceUp = affectsPriceUp;
        }
    }
}
