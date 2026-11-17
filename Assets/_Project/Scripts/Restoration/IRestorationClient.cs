using AntiqueTradingSimulator.Economy;

namespace AntiqueTradingSimulator.Restoration
{
    /// <summary>
    /// A trader RestorationManager can quote and work for. Implemented by PlayerTrader and NPCTrader
    /// so both go through the same fees, times, outcome rolls and reputation gates.
    /// </summary>
    public interface IRestorationClient
    {
        TraderInventory Inventory { get; }

        /// <summary>Reputation checked against RestorationOption.RequiredReputation.</summary>
        int Reputation { get; }
    }
}
