using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Events;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

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

        private readonly List<PendingNews> _pendingNews = new();

        private struct PendingNews
        {
            public int PublishDay;
            public EventDefinition Definition;
            public int EventTriggerDay;
            public NewsType Type;
        }




        private void Awake()
        {
            if (eventManager == null) eventManager = FindFirstObjectByType<EventManager>();
            if (timeManager == null) timeManager = FindFirstObjectByType<Core.TimeManager>();

            if (eventManager != null)
            {
                eventManager.OnEventScheduled += HandleEventScheduled;
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
            }
        }

        private void HandleEventScheduled(EventDefinition definition, int triggerDay)
        {
            int today = timeManager != null ? timeManager.CurrentDay : triggerDay;
            int leadDays = triggerDay - today;
            if (leadDays < 1) return;

            var candidateTypes = new List<NewsType>();
            if (definition.CreateRumour) candidateTypes.Add(NewsType.Rumor);
            if (definition.CreateLeak) candidateTypes.Add(NewsType.Leak);
            if (definition.CreateOfficialNews) candidateTypes.Add(NewsType.Official);

            int typesToPublish = Mathf.Min(candidateTypes.Count, leadDays);
            int startIndex = candidateTypes.Count - typesToPublish;

            for (int i = 0; i < typesToPublish; i++)
            {
                NewsType type = candidateTypes[startIndex + i];
                int publishDay = triggerDay - (typesToPublish - i);

                if (publishDay <= today)
                {
                    PublishNews(definition, type, today, triggerDay);
                }
                else
                {
                    _pendingNews.Add(new PendingNews { PublishDay = publishDay, Definition = definition, Type = type, EventTriggerDay = triggerDay });
                    Debug.Log($"NewsManager: {type} queued for '{definition.DisplayName}' [{definition.name}]. publishing day: {publishDay} event trigger day: {triggerDay}.");
                }
            }
        }


        private void HandleDayChanged(int newDay)
        {
            for (int i = _pendingNews.Count - 1; i >= 0; i--)
            {
                if (_pendingNews[i].PublishDay > newDay) continue;

                PublishNews(_pendingNews[i].Definition, _pendingNews[i].Type, newDay, _pendingNews[i].EventTriggerDay);
                _pendingNews.RemoveAt(i);
            }
        }

        private void PublishNews(EventDefinition definition, NewsType type, int day, int eventTriggerDay)
        {
            string label = type switch
            {
                NewsType.Official => "official news",
                NewsType.Leak => "leak",
                NewsType.Rumor => "rumor",
                _ => type.ToString()
            };
            Debug.Log($"Published {label} on event {definition.name}");

            List<NewsEventData> newsData = BuildNewsData(definition, type);

            float credibility = type switch
            {
                NewsType.Official => definition.OfficialCredibility,
                NewsType.Leak => definition.LeakCredibility,
                NewsType.Rumor => definition.RumorCredibility,
                _ => 1f
            };

            InfoAccessLevel accessLevel = type switch
            {
                NewsType.Official => InfoAccessLevel.LocalPress,
                NewsType.Leak => InfoAccessLevel.InformantNetwork,
                NewsType.Rumor => InfoAccessLevel.IndustrySources,
                _ => InfoAccessLevel.LocalPress
            };

            Publish(new NewsItem(newsData, type, credibility, day, accessLevel, eventTriggerDay));
        }


        private static List<NewsEventData> BuildNewsData(EventDefinition definition, NewsType type)
        {
            var effects = definition.Effects;

            if (type == NewsType.Rumor && effects.Count > 1)
            {
                int revealCount = UnityEngine.Random.Range(1, effects.Count);
                var revealedIndices = Enumerable.Range(0, effects.Count)
                    .OrderBy(_ => UnityEngine.Random.value)
                    .Take(revealCount)
                    .OrderBy(index => index);

                var partial = new List<NewsEventData>();
                foreach (int index in revealedIndices)
                    partial.Add(effects[index].CreateNewsData());
                return partial;
            }
            var newsData = new List<NewsEventData>();
            foreach (EventEffect eventEffect in effects)
                newsData.Add(eventEffect.CreateNewsData());
            return newsData;
        }


        public void RegisterReceiver(IInformationReceiver receiver)
        {
            if (!_codeReceivers.Contains(receiver)) _codeReceivers.Add(receiver);
        }

        public void UnregisterReceiver(IInformationReceiver receiver) => _codeReceivers.Remove(receiver);

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