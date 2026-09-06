using System.Collections.Generic;
using UnityEngine;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Events;

namespace AntiqueTradingSimulator.News
{
    /// <summary>
    /// Turns GameEvents (or player-initiated actions like spreading a rumor) into
    /// NewsItems and delivers them to every qualifying IInformationReceiver — both
    /// scene-based receivers (e.g. PlayerTrader, via inspector) and code-registered
    /// receivers (e.g. NPCTrader, via NPCManager). This is the only place where
    /// "who finds out, and how truthfully" is decided.
    /// </summary>
    public class NewsManager : MonoBehaviour
    {
        [SerializeField] private List<MonoBehaviour> inspectorReceivers = new(); // np. PlayerTrader
        private readonly List<IInformationReceiver> _codeReceivers = new();      // np. NPCTrader from NPCManager

        private readonly List<NewsItem> _publishedNews = new();
        public IReadOnlyList<NewsItem> PublishedNews => _publishedNews;

        public void RegisterReceiver(IInformationReceiver receiver)
        {
            if (!_codeReceivers.Contains(receiver)) _codeReceivers.Add(receiver);
        }

        public void UnregisterReceiver(IInformationReceiver receiver) => _codeReceivers.Remove(receiver);

        public void PublishFromEvent(GameEvent gameEvent, int currentDay)
        {
            var def = gameEvent.Definition;

            if (def.GeneratesOfficialNews)
                Publish(new NewsItem(gameEvent, NewsType.Official, def.OfficialCredibility, true,
                    currentDay, InfoAccessLevel.LocalPress, gameEvent.SpecificAntiqueTypeId));

            if (def.CanSpawnAsRumorOnly)
                Publish(new NewsItem(gameEvent, NewsType.Rumor, def.RumorCredibility, true,
                    currentDay, InfoAccessLevel.IndustrySources, gameEvent.SpecificAntiqueTypeId));

            if (def.CanLeakEarly)
                Publish(new NewsItem(gameEvent, NewsType.Leak, def.LeakCredibility, true,
                    currentDay, InfoAccessLevel.InformantNetwork, gameEvent.SpecificAntiqueTypeId));
        }

        public void PublishManual(NewsItem item) => Publish(item);

        private void Publish(NewsItem item)
        {
            _publishedNews.Add(item);

            foreach (var behaviour in inspectorReceivers)
                if (behaviour is IInformationReceiver r && r.AccessLevel >= item.RequiredAccessLevel)
                    r.ReceiveNews(item);

            foreach (var receiver in _codeReceivers)
                if (receiver.AccessLevel >= item.RequiredAccessLevel)
                    receiver.ReceiveNews(item);
        }
    }
}