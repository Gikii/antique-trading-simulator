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

        [SerializeField] private EventManager eventManager;
        [SerializeField] private Core.TimeManager timeManager;

        private readonly List<NewsItem> _publishedNews = new();
        public IReadOnlyList<NewsItem> PublishedNews => _publishedNews;

        private readonly List<PendingLeak> _pendingLeaks = new();

        private struct PendingLeak
        {
            public int PublishDay;
            public EventDefinition Definition;
        }



        private void Awake()
        {
            if (eventManager == null) eventManager = FindFirstObjectByType<EventManager>();
            if (timeManager == null) timeManager = FindFirstObjectByType<Core.TimeManager>();

            if (eventManager != null)
            {
                eventManager.OnEventScheduled += HandleEventScheduled;
                eventManager.OnEventTriggered += HandleEventTriggered;
            }
            else
            {
                Debug.LogWarning("NewsManager: no EventManager found — events will not generate news.");
            }

        }

        void OnEnable()
        {
            if (timeManager != null)
                timeManager.OnDayChanged += HandleDayChanged;
        }

        void OnDisable()
        {
            if (timeManager != null)
                timeManager.OnDayChanged -= HandleDayChanged;
        }

        void OnDestroy()
        {
            if (eventManager != null)
            {
                eventManager.OnEventScheduled -= HandleEventScheduled;
                eventManager.OnEventTriggered -= HandleEventTriggered;
            }
        }

        private void HandleEventScheduled(EventDefinition definition, int triggerDay)
        {
            if (!definition.CreateLeak) return;

            int today = timeManager != null ? timeManager.CurrentDay : triggerDay;
            int publishDay = Mathf.Max(triggerDay - Mathf.Max(0, definition.LeakDaysBefore), today);

            if (publishDay <= today)
            {
                PublishLeak(definition, today);
            }
            else
            {
                _pendingLeaks.Add(new PendingLeak { PublishDay = publishDay, Definition = definition });
                Debug.Log($"NewsManager: leak queued for '{definition.DisplayName}' [{definition.name}] — publishing Day {publishDay} (event triggers Day {triggerDay}).");
            }
        }

        private void HandleEventTriggered(ActiveEvent active)
        {
            EventDefinition definition = active.Definition;
            List<NewsEventData> newsData = BuildNewsData(definition);

            if (definition.CreateOfficialNews)
            {
                Publish(new NewsItem(newsData, NewsType.Official, definition.OfficialCredibility,
                    active.StartDay, InfoAccessLevel.LocalPress));
                Debug.Log("Published official news on event " + definition.name);
            }

            if (definition.CreateRumour)
            {
                Publish(new NewsItem(newsData, NewsType.Rumor, definition.RumorCredibility,
                    active.StartDay, InfoAccessLevel.IndustrySources));
                Debug.Log("Published rumor on event " + definition.name);
            }
        }

        private void HandleDayChanged(int newDay)
        {
            for (int i = _pendingLeaks.Count - 1; i >= 0; i--)
            {
                if (_pendingLeaks[i].PublishDay > newDay) continue;

                PublishLeak(_pendingLeaks[i].Definition, newDay);
                _pendingLeaks.RemoveAt(i);
            }
        }

        private void PublishLeak(EventDefinition definition, int day)
        {
            List<NewsEventData> newsData = BuildNewsData(definition);
            Publish(new NewsItem(newsData, NewsType.Leak, definition.LeakCredibility,
                day, InfoAccessLevel.InformantNetwork));
            Debug.Log("Published leak on event " + definition.name);
        }

        private static List<NewsEventData> BuildNewsData(EventDefinition definition)
        {
            var newsData = new List<NewsEventData>();
            foreach (EventEffect eventEffect in definition.Effects)
                newsData.Add(eventEffect.CreateNewsData());
            return newsData;
        }



        public void RegisterReceiver(IInformationReceiver receiver)
        {
            if (!_codeReceivers.Contains(receiver)) _codeReceivers.Add(receiver);
        }

        public void UnregisterReceiver(IInformationReceiver receiver) => _codeReceivers.Remove(receiver);

        public void PublishFromEvent(EventDefinition gameEvent, int currentDay)
        {
            List<NewsEventData> newsData = new List<NewsEventData>();

            foreach (EventEffect eventEffect in gameEvent.Effects)
            {
                newsData.Add(eventEffect.CreateNewsData());
            }

            if (gameEvent.CreateOfficialNews)
                Publish(new NewsItem(newsData, NewsType.Official, 1f,
                    currentDay, InfoAccessLevel.LocalPress));

            if (gameEvent.CreateRumour)
                Publish(new NewsItem(newsData, NewsType.Rumor, 0.1f,
                    currentDay, InfoAccessLevel.IndustrySources));

            if (gameEvent.CreateLeak)
                Publish(new NewsItem(newsData, NewsType.Leak, 0.3f,
                    currentDay, InfoAccessLevel.InformantNetwork));

            Debug.Log("Published news about " + gameEvent.name);
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