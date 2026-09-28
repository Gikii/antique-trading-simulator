using AntiqueTradingSimulator.Economy;

namespace AntiqueTradingSimulator.Events
{
    public class EventContext
    {
        public Market.Market Market;
        public int CurrentDay;
        public TraderInventory PlayerInventory;

        public EventContext(Market.Market market, int currentDay, TraderInventory playerInventory)
        {
            Market = market;
            CurrentDay = currentDay;
            PlayerInventory = playerInventory;
        }
    }
}
