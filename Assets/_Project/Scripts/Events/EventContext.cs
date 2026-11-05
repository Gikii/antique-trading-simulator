using AntiqueTradingSimulator.Dependency;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Logistics;

namespace AntiqueTradingSimulator.Events
{
    public class EventContext
    {
        public Market.Market Market;
        public int CurrentDay;
        public TraderInventory PlayerInventory;
        public TransportManager Transport; // may be null — effects fall back to instant delivery

        public DependencyManager Dependencies;

        public EventContext(Market.Market market, int currentDay, TraderInventory playerInventory, TransportManager transport = null, DependencyManager dependencies = null)
        {
            Market = market;
            CurrentDay = currentDay;
            PlayerInventory = playerInventory;
            Transport = transport;
            Dependencies = dependencies;
        }
    }
}
