using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Company;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Dependency;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Events;
using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.News;
using AntiqueTradingSimulator.Restoration;

namespace AntiqueTradingSimulator.Saving
{
    /// <summary>
    /// The whole game as written to a save file. The header fields (version, save time, day,
    /// company figures) come from <see cref="SaveHeader"/>, so a slot list can read just those.
    /// </summary>
    public class GameState : SaveHeader
    {
        public GameState()
        {
            Version = SaveSerializer.SaveFormatVersion;
        }

        public TimeManagerState Time;

        public NPCManagerState Npcs;

        public EconomyState Economy;

        public DependencyState Dependencies;

        public LogisticsState Logistics;

        public RestorationState Restoration;

        public ContractsState Contracts;

        public CompanyState Company;

        public EventsState Events;

        public NewsState News;
    }
}
