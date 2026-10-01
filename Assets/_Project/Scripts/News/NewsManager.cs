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

        /// <summary> Raised after a news item has been published (e.g. so the UI can refresh).</summary>
        public event System.Action<NewsItem> OnNewsPublished;

        /// <summary> Raised when the event the news item is about starts. <summary>
        public event System.Action<NewsItem> OnNewsResolved;

        private readonly List<PendingNews> _pendingNews = new();

        private readonly List<NewsItem> _resolvedBuffer = new();

        private struct PendingNews
        {
            public int PublishDay;
            public EventDefinition Definition;
            public int EventTriggerDay;
            public NewsType Type;
            public string EventInstanceId;
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


        private void HandleEventTriggered(ActiveEvent activeEvent)
        {
            if (activeEvent == null) return;

            if (string.IsNullOrEmpty(activeEvent.InstanceId)) return;

            for (int i = _pendingNews.Count - 1; i >= 0; i--)
            {
                if (_pendingNews[i].EventInstanceId == activeEvent.InstanceId)
                    _pendingNews.RemoveAt(i);
            }

            _resolvedBuffer.Clear();

            foreach (NewsItem news in _publishedNews)
            {
                if (news.Resolved || !IsAbout(news, activeEvent)) continue;

                news.MarkResolved();
                _resolvedBuffer.Add(news);
            }

            foreach (NewsItem news in _resolvedBuffer)
                OnNewsResolved?.Invoke(news);

            _resolvedBuffer.Clear();
        }

        private static bool IsAbout(NewsItem news, ActiveEvent activeEvent)
        {
            return !string.IsNullOrEmpty(news.EventInstanceId)
                && news.EventInstanceId == activeEvent.InstanceId;
        }

        private void HandleEventScheduled(ScheduledEvent scheduled)
        {
            if (scheduled == null) return;

            EventDefinition definition = scheduled.Definition;
            if (definition == null) return;

            int triggerDay = scheduled.TriggerDay;
            string instanceId = scheduled.InstanceId;

            int today = timeManager != null ? timeManager.CurrentDay : triggerDay;
            int leadDays = triggerDay - today;
            if (leadDays < 1) return;

            var candidateTypes = new List<NewsType>();
            if (definition.CreateRumour) candidateTypes.Add(NewsType.Rumor);
            if (definition.CreateLeak) candidateTypes.Add(NewsType.Leak);
            if (definition.CreateOfficialNews) candidateTypes.Add(NewsType.Official);

            int typesToPublish = Mathf.Min(candidateTypes.Count, leadDays);
            int startIndex = candidateTypes.Count - typesToPublish;



            int[] publishDays = new int[typesToPublish];
            int upperBoundExclusive = triggerDay;

            for (int i = typesToPublish - 1; i >= 0; i--)
            {
                NewsType type = candidateTypes[startIndex + i];
                int minDay = today + i;
                int maxDay = upperBoundExclusive - 1;

                int day;
                if (type == NewsType.Official)
                {
                    day = Mathf.Clamp(triggerDay - definition.OfficialNewsDaysBefore, minDay, maxDay);
                }
                else
                {
                    day = UnityEngine.Random.Range(minDay, maxDay + 1);
                }

                publishDays[i] = day;
                upperBoundExclusive = day;
            }

            for (int i = 0; i < typesToPublish; i++)
            {
                NewsType type = candidateTypes[startIndex + i];
                int publishDay = publishDays[i];

                if (publishDay <= today)
                {
                    PublishNews(definition, type, today, triggerDay, instanceId);
                }
                else
                {
                    _pendingNews.Add(new PendingNews { PublishDay = publishDay, Definition = definition, Type = type, EventTriggerDay = triggerDay, EventInstanceId = instanceId });
                    Debug.Log($"NewsManager: {type} queued for '{definition.DisplayName}' [{definition.name}]. publishing day: {publishDay} event trigger day: {triggerDay}.");
                }
            }

        }


        private void HandleDayChanged(int newDay)
        {
            for (int i = _pendingNews.Count - 1; i >= 0; i--)
            {
                if (_pendingNews[i].PublishDay > newDay) continue;

                PublishNews(_pendingNews[i].Definition, _pendingNews[i].Type, newDay, _pendingNews[i].EventTriggerDay, _pendingNews[i].EventInstanceId);
                _pendingNews.RemoveAt(i);
            }
        }

        private void PublishNews(EventDefinition definition, NewsType type, int day, int eventTriggerDay, string eventInstanceId)
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
                NewsType.Official => 1f,
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

            Publish(new NewsItem(newsData, type, credibility, day, accessLevel, eventTriggerDay, definition.Id, eventInstanceId));
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

            OnNewsPublished?.Invoke(item);
        }
    }
}