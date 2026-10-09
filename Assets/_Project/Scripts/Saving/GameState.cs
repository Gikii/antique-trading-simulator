using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Company;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Events;
using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.News;

namespace AntiqueTradingSimulator.Saving
{
    public class GameState
    {
        public int Version = SaveSerializer.SaveFormatVersion;

        public string SavedAtUtc;

        public int SavedOnDay;

        public TimeManagerState Time;

        public NPCManagerState Npcs;

        public EconomyState Economy;

        public LogisticsState Logistics;

        public ContractsState Contracts;

        public CompanyState Company;

        public EventsState Events;

        public NewsState News;
    }
}
