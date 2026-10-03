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

        public EventContext(Market.Market market, int currentDay, TraderInventory playerInventory, TransportManager transport = null)
        {
            Market = market;
            CurrentDay = currentDay;
            PlayerInventory = playerInventory;
            Transport = transport;
        }
    }
}
