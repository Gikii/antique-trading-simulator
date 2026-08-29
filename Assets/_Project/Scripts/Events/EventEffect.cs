using UnityEngine;

namespace AntiqueTradingSimulator.Events
{

    public abstract class EventEffect
    {
        public abstract void Apply(Market.Market market, int currentDay);
        public abstract void Revert(Market.Market market, int currentDay);
        public abstract EventEffect Clone();
    }
}
