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
    /// "who finds out, and how truthfully" is decided. Access is probabilistic rather
    /// than a gate: see InformationSettings for the numbers and TryDeliver for the rule.
    /// </summary>
    public class NewsManager : MonoBehaviour
    {
        [SerializeField] private List<MonoBehaviour> inspectorReceivers = new(); // np. PlayerTrader
        private readonly List<IInformationReceiver> _codeReceivers = new();      // np. NPCTrader from NPCManager

        [SerializeField] private EventManager eventManager;
        [SerializeField] private Core.TimeManager timeManager;

        [Tooltip("Chance and delay of each kind of news reaching a trader, per tier. " +
                 "Left empty: the asset in Data/Resources, or built-in defaults.")]
        [SerializeField] private InformationSettings informationSettings;

        private readonly List<NewsItem> _publishedNews = new();
        public IReadOnlyList<NewsItem> PublishedNews => _publishedNews;

        /// <summary>The chance and delay tables in use.</summary>
        public InformationSettings Information =>
            informationSettings != null ? informationSettings : InformationSettings.Active;

        /// <summary>Consecutive misses per receiver and news type, keyed by ReceiverId.</summary>
        private readonly Dictionary<string, Dictionary<NewsType, int>> _missStreaks = new();

        private readonly List<IInformationReceiver> _receiverBuffer = new();

        private PlayerTrader _player;

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

            // So UI with no NewsManager reference shows the same numbers.
            InformationSettings.SetActive(informationSettings);

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

            Publish(new NewsItem(newsData, definition.DisplayName, definition.Description, type, credibility, week, eventTriggerWeek));
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

            foreach (var receiver in ResolveReceivers())
                TryDeliver(receiver, item);

            OnNewsPublished?.Invoke(item);
        }

        private List<IInformationReceiver> ResolveReceivers()
        {
            _receiverBuffer.Clear();

            foreach (var behaviour in inspectorReceivers)
                if (behaviour is IInformationReceiver inspectorReceiver && !_receiverBuffer.Contains(inspectorReceiver))
                    _receiverBuffer.Add(inspectorReceiver);

            foreach (var receiver in _codeReceivers)
                if (!_receiverBuffer.Contains(receiver))
                    _receiverBuffer.Add(receiver);

            if (_player == null) _player = FindFirstObjectByType<PlayerTrader>();

            if (_player != null && !_receiverBuffer.Contains(_player))
                _receiverBuffer.Add(_player);

            return _receiverBuffer;
        }

        /// <summary>
        /// Decides whether one trader hears about one news item, and when: news that would arrive
        /// too late to act on is not sent at all, and otherwise a chance roll and a delay roll
        /// settle it. The too-late check comes before the chance roll so unreachable items don't
        /// consume dry-spell protection, which keeps the authored chance equal to the share the
        /// trader actually receives.
        /// </summary>
        private void TryDeliver(IInformationReceiver receiver, NewsItem item)
        {
            if (receiver == null || item == null) return;

            InfoAccessLevel level = receiver.AccessLevel;
            int publishWeek = item.WeekPublished;
            var settings = Information;
            Vector2Int delayRange = settings.DelayFor(item.Type, level);

            // News with no event behind it, such as a weather forecast, has no deadline.
            int weeksToSpare = item.EventTriggerWeek > publishWeek
                ? item.EventTriggerWeek - publishWeek - 1
                : int.MaxValue;

            if (delayRange.x > weeksToSpare)
            {
                Debug.Log($"NewsManager: {item.Type} '{item.OfficialName}' never reaches {receiver.ReceiverId} — " +
                          $"their sources need at least {delayRange.x} week(s) and the event starts in " +
                          $"{item.EventTriggerWeek - publishWeek}.");
                return;
            }

            if (!item.GuaranteedDelivery)
            {
                int missStreak = MissStreak(receiver.ReceiverId, item.Type);
                bool owed = missStreak >= settings.ForcedDeliveryAfterMisses(item.Type, level);

                if (!owed && UnityEngine.Random.value > settings.ChanceFor(item.Type, level))
                {
                    SetMissStreak(receiver.ReceiverId, item.Type, missStreak + 1);
                    return;
                }

                SetMissStreak(receiver.ReceiverId, item.Type, 0);
            }

            int delay = UnityEngine.Random.Range(delayRange.x, Mathf.Min(delayRange.y, weeksToSpare) + 1);
            receiver.ReceiveNews(item, publishWeek + delay);
        }

        private int MissStreak(string receiverId, NewsType type)
        {
            if (string.IsNullOrEmpty(receiverId)) return 0;

            return _missStreaks.TryGetValue(receiverId, out var byType) && byType.TryGetValue(type, out int streak)
                ? streak
                : 0;
        }

        private void SetMissStreak(string receiverId, NewsType type, int streak)
        {
            if (string.IsNullOrEmpty(receiverId)) return;

            if (!_missStreaks.TryGetValue(receiverId, out var byType))
            {
                if (streak <= 0) return;
                byType = new Dictionary<NewsType, int>();
                _missStreaks[receiverId] = byType;
            }

            if (streak <= 0) byType.Remove(type);
            else byType[type] = streak;
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

            foreach (var pair in _missStreaks)
            {
                foreach (var byType in pair.Value)
                {
                    if (byType.Value <= 0) continue;

                    state.Channels.Add(new InformationChannelState
                    {
                        ReceiverId = pair.Key,
                        Type = byType.Key,
                        MissStreak = byType.Value
                    });
                }
            }

            return state;
        }

        public void RestoreState(NewsState state)
        {
            _publishedNews.Clear();
            _pendingNews.Clear();
            _missStreaks.Clear();

            if (state == null)
            {
                OnNewsRestored?.Invoke();
                return;
            }

            if (state.Channels != null)
            {
                foreach (var channel in state.Channels)
                {
                    if (channel == null || string.IsNullOrEmpty(channel.ReceiverId)) continue;
                    SetMissStreak(channel.ReceiverId, channel.Type, channel.MissStreak);
                }
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