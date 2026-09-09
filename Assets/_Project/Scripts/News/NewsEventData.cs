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
