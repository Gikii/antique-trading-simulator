using AntiqueTradingSimulator.Events;
using AntiqueTradingSimulator.News;

namespace AntiqueTradingSimulator.Agents
{
    public interface IInformationReceiver
    {
        /// <summary>
        /// Anything that can be targeted by NewsManager — implemented independently by
        /// PlayerTrader and NPCTrader so news distribution doesn't depend on either being
        /// a MonoBehaviour or sharing a common trading base class.
        /// </summary>
        InfoAccessLevel AccessLevel { get; }
        void ReceiveNews(NewsItem news);
    }
}