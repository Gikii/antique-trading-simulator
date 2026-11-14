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

        /// <summary>Raised after a news item has been published (e.g. so the UI can refresh).</summary>
        public event System.Action<NewsItem> OnNewsPublished;

        public event System.Action OnNewsRestored;

        private readonly List<PendingNews> _pendingNews = new();

        private struct PendingNews
        {
            public int PublishWeek;
            public EventDefinition Definition;
            public int EventTriggerWeek;
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
                timeManager.OnWeekChanged += HandleWeekChanged;
        }

        void OnDisable()
        {
            if (timeManager != null)
                timeManager.OnWeekChanged -= HandleWeekChanged;
        }

        void OnDestroy()
        {
            if (eventManager != null)
            {
                eventManager.OnEventScheduled -= HandleEventScheduled;
            }
        }

        private void HandleEventScheduled(EventDefinition definition, int triggerWeek)
        {
            int thisWeek = timeManager != null ? timeManager.CurrentWeek : triggerWeek;
            int leadWeeks = triggerWeek - thisWeek;
            if (leadWeeks < 1) return;

            var candidateTypes = new List<NewsType>();
            if (definition.CreateRumour) candidateTypes.Add(NewsType.Rumor);
            if (definition.CreateLeak) candidateTypes.Add(NewsType.Leak);
            if (definition.CreateOfficialNews) candidateTypes.Add(NewsType.Official);

            int typesToPublish = Mathf.Min(candidateTypes.Count, leadWeeks);
            int startIndex = candidateTypes.Count - typesToPublish;



            int[] publishWeeks = new int[typesToPublish];
            int upperBoundExclusive = triggerWeek;

            for (int i = typesToPublish - 1; i >= 0; i--)
            {
                NewsType type = candidateTypes[startIndex + i];
                int minWeek = thisWeek + i;
                int maxWeek = upperBoundExclusive - 1;

                int week;
                if (type == NewsType.Official)
                {
                    week = Mathf.Clamp(triggerWeek - definition.OfficialAnnouncementWeeksBefore, minWeek, maxWeek);
                }
                else
                {
                    week = UnityEngine.Random.Range(minWeek, maxWeek + 1);
                }

                publishWeeks[i] = week;
                upperBoundExclusive = week;
            }

            for (int i = 0; i < typesToPublish; i++)
            {
                NewsType type = candidateTypes[startIndex + i];
                int publishWeek = publishWeeks[i];

                if (publishWeek <= thisWeek)
                {
                    PublishNews(definition, type, thisWeek, triggerWeek);
                }
                else
                {
                    _pendingNews.Add(new PendingNews { PublishWeek = publishWeek, Definition = definition, Type = type, EventTriggerWeek = triggerWeek });
                    Debug.Log($"NewsManager: {type} queued for '{definition.DisplayName}' [{definition.name}]. publishing week: {publishWeek} event trigger week: {triggerWeek}.");
                }
            }

        }


        private void HandleWeekChanged(int newWeek)
        {
            for (int i = _pendingNews.Count - 1; i >= 0; i--)
            {
                if (_pendingNews[i].PublishWeek > newWeek) continue;

                PublishNews(_pendingNews[i].Definition, _pendingNews[i].Type, newWeek, _pendingNews[i].EventTriggerWeek);
                _pendingNews.RemoveAt(i);
            }
        }

        private void PublishNews(EventDefinition definition, NewsType type, int week, int eventTriggerWeek)
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

            Publish(new NewsItem(newsData, definition.DisplayName, definition.Description, type, credibility, week, accessLevel, eventTriggerWeek));
        }


        private static List<NewsEventData> BuildNewsData(EventDefinition definition, NewsType type)
        {
            var newsData = new List<NewsEventData>();
            if (definition == null || definition.Effects == null) return newsData;

            var effects = definition.Effects.Where(e => e != null).ToList();
            if (effects.Count == 0)
            {
                Debug.LogWarning($"NewsManager: '{definition.name}' has no valid effects.");
                return newsData;
            }

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

        // ---------------------------------------------------------------- save / load

        public NewsState CaptureState()
        {
            var state = new NewsState();

            foreach (var item in _publishedNews)
            {
                if (item == null) continue;
                state.PublishedNews.Add(NewsItemState.Capture(item));
            }

            foreach (var pending in _pendingNews)
            {
                if (pending.Definition == null)
                {
                    Debug.LogWarning("NewsManager: a queued news item has no EventDefinition — left out of the save.");
                    continue;
                }

                state.PendingNews.Add(new PendingNewsState
                {
                    PublishWeek = pending.PublishWeek,
                    EventDefinitionId = pending.Definition.Id,
                    EventTriggerWeek = pending.EventTriggerWeek,
                    Type = pending.Type
                });
            }

            return state;
        }

        public void RestoreState(NewsState state)
        {
            _publishedNews.Clear();
            _pendingNews.Clear();

            if (state == null)
            {
                OnNewsRestored?.Invoke();
                return;
            }

            if (state.PublishedNews != null)
            {
                foreach (var itemState in state.PublishedNews)
                {
                    var item = itemState?.Restore();
                    if (item != null) _publishedNews.Add(item);
                }
            }

            if (state.PendingNews != null)
            {
                foreach (var pendingState in state.PendingNews)
                {
                    if (pendingState == null) continue;

                    var definition = EventDatabase.GetById(pendingState.EventDefinitionId);
                    if (definition == null)
                    {
                        Debug.LogWarning($"NewsManager: queued news referenced EventDefinition '{pendingState.EventDefinitionId}', which no longer exists — dropped.");
                        continue;
                    }

                    _pendingNews.Add(new PendingNews
                    {
                        PublishWeek = pendingState.PublishWeek,
                        Definition = definition,
                        EventTriggerWeek = pendingState.EventTriggerWeek,
                        Type = pendingState.Type
                    });
                }
            }

            OnNewsRestored?.Invoke();
        }

    }
}