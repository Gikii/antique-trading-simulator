using UnityEngine;
using UnityEngine.UI;
using TMPro;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.News;
using AntiqueTradingSimulator.Events;

namespace AntiqueTradingSimulator.UI
{
    public class NewsEventsView : UIView
    {
        [Header("Tabs")]
        [SerializeField] private Button newsTabButton;
        [SerializeField] private Button activeEventsTabButton;

        [Header("Filters")]
        [SerializeField] private Button allFilterButton;
        [SerializeField] private Button officialFilterButton;
        [SerializeField] private Button rumoursFilterButton;
        [SerializeField] private Button leakedFilterButton;

        [Header("Header Actions")]
        [SerializeField] private Button createEventButton;
        [SerializeField] private CreateEventModalUI createEventModal; // auto-found (also when inactive)

        [Header("Player Information Access (auto-found if empty)")]
        [SerializeField] private PlayerTrader player;
        [SerializeField] private TMP_Text accessLevelText;

        [Header("Main Content")]
        [SerializeField] private GameObject newsContent;
        [SerializeField] private GameObject activeEventsContent;

        [Header("Side Panel")]
        [SerializeField] private GameObject calendarPanel; // calendar logic lives in NewsCalendarUI on this object
        [SerializeField] private GameObject newsDetails;
        [SerializeField] private GameObject eventDetails;

        [Header("Dynamic News List")]
        [SerializeField] private NewsManager newsManager;
        [SerializeField] private Transform newsListContainer;
        [SerializeField] private NewsListItemUI newsListItemPrefab;

        [Header("Dynamic Active Events")]
        [SerializeField] private EventManager eventManager;
        [SerializeField] private Transform activeEventsListContainer;
        [SerializeField] private ActiveEventListItemUI activeEventListItemPrefab;
        [SerializeField] private Core.TimeManager timeManager;

        [Header("Close Buttons")]
        [SerializeField] private Button newsDetailsCloseButton;
        [SerializeField] private Button eventDetailsCloseButton;

        private enum ContentMode
        {
            News,
            ActiveEvents
        }

        private enum NewsFilter
        {
            All,
            Official,
            Rumours,
            Leaked
        }

        private ContentMode currentMode = ContentMode.News;
        private NewsFilter currentFilter = NewsFilter.All;

        // Fill the right-side panels with data of the clicked item (added automatically if missing).
        private DetailsPanelUI newsDetailsUI;
        private DetailsPanelUI eventDetailsUI;

        // The news item currently shown in the details panel, so the panel can be closed
        // if that item stops being news.
        private NewsItem shownNews;

        /// <summary>
        /// Today, or a day so early that nothing counts as started — without a TimeManager the
        /// view cannot tell what is in the past, and showing every item beats hiding every item.
        /// </summary>
        private int CurrentDayOrNone => timeManager != null ? timeManager.CurrentDay : int.MinValue;


        // =====================================================
        // UNITY LIFECYCLE
        // =====================================================

        private void Awake()
        {
            BindButtons();

            newsDetailsUI = GetOrAddDetailsPanel(newsDetails);
            eventDetailsUI = GetOrAddDetailsPanel(eventDetails);

            if (player == null)
                player = FindAnyObjectByType<PlayerTrader>();

            if (accessLevelText == null)
            {
                Transform t = transform.Find("Header/InfoAccessPanel/AccessInfo/AccessLevelText");
                if (t != null)
                    accessLevelText = t.GetComponent<TMP_Text>();
            }
        }

        private static DetailsPanelUI GetOrAddDetailsPanel(GameObject panel)
        {
            if (panel == null)
                return null;

            DetailsPanelUI details = panel.GetComponent<DetailsPanelUI>();
            if (details == null)
                details = panel.AddComponent<DetailsPanelUI>();

            return details;
        }

        private void Start()
        {
            SubscribeToEventManager();
            SubscribeToNewsManager();
            SubscribeToTimeManager();
        }

        private void OnDestroy()
        {
            UnsubscribeFromEventManager();
            UnsubscribeFromNewsManager();
            UnsubscribeFromTimeManager();
        }

        protected override void OnShown()
        {
            UpdateAccessLevelText();

            ShowNewsMode();

            RefreshNewsList();
            RefreshActiveEventsList();
        }


        // =====================================================
        // EVENT MANAGER SUBSCRIPTION
        // =====================================================

        private void SubscribeToEventManager()
        {
            if (eventManager == null)
            {
                Debug.LogError(
                    "NewsEventsView: EventManager is not assigned.",
                    this
                );

                return;
            }

            eventManager.OnEventTriggered += HandleEventChanged;
            eventManager.OnEventEnded += HandleEventChanged;
        }

        private void UnsubscribeFromEventManager()
        {
            if (eventManager == null)
                return;

            eventManager.OnEventTriggered -= HandleEventChanged;
            eventManager.OnEventEnded -= HandleEventChanged;
        }

        private void HandleEventChanged(ActiveEvent activeEvent)
        {
            RefreshActiveEventsList();
        }


        // =====================================================
        // NEWS MANAGER SUBSCRIPTION
        // =====================================================

        private void SubscribeToNewsManager()
        {
            if (newsManager != null)
                newsManager.OnNewsPublished += HandleNewsPublished;
        }

        private void UnsubscribeFromNewsManager()
        {
            if (newsManager != null)
                newsManager.OnNewsPublished -= HandleNewsPublished;
        }

        // =====================================================
        // CLOCK SUBSCRIPTION
        // =====================================================

        private void SubscribeToTimeManager()
        {
            if (timeManager != null)
                timeManager.OnDayChanged += HandleDayChanged;
        }

        private void UnsubscribeFromTimeManager()
        {
            if (timeManager != null)
                timeManager.OnDayChanged -= HandleDayChanged;
        }

        /// <summary>
        /// A new day can push announced events into the past, which drops their news out of
        /// the list. If the open details panel is showing one of those, it closes.
        /// </summary>
        private void HandleDayChanged(int newDay)
        {
            if (shownNews != null && shownNews.EventStarted(newDay))
                CloseNewsDetails();

            if (!isActiveAndEnabled)
                return;

            if (currentMode == ContentMode.News)
                RefreshNewsList();
            else
                UpdateCounts();
        }

        private void HandleNewsPublished(NewsItem news)
        {
            // Hidden view doesn't need to rebuild – OnShown refreshes it anyway.
            if (!isActiveAndEnabled)
                return;

            // Counts in tabs/filters change even when the Active Events tab is open.
            if (currentMode == ContentMode.News)
                RefreshNewsList();
            else
                UpdateCounts();
        }


        // =====================================================
        // BUTTON BINDING
        // =====================================================

        private void BindButtons()
        {
            Bind(newsTabButton, ShowNewsMode);
            Bind(activeEventsTabButton, ShowActiveEventsMode);

            Bind(allFilterButton, ShowAll);
            Bind(officialFilterButton, ShowOfficial);
            Bind(rumoursFilterButton, ShowRumours);
            Bind(leakedFilterButton, ShowLeaked);

            Bind(createEventButton, OpenCreateEvent);

            Bind(newsDetailsCloseButton, CloseNewsDetails);
            Bind(eventDetailsCloseButton, CloseEventDetails);
        }

        private static void Bind(
            Button button,
            UnityEngine.Events.UnityAction action
        )
        {
            if (button == null)
                return;

            button.onClick.RemoveListener(action);
            button.onClick.AddListener(action);
        }


        // =====================================================
        // TABS
        // =====================================================

        public void ShowNewsMode()
        {
            currentMode = ContentMode.News;
            shownNews = null;

            SetActive(newsContent, true);
            SetActive(activeEventsContent, false);

            SetActive(calendarPanel, true);
            SetActive(newsDetails, false);
            SetActive(eventDetails, false);

            SetButtonSelected(newsTabButton, true);
            SetButtonSelected(activeEventsTabButton, false);

            RefreshNewsList();
        }

        public void ShowActiveEventsMode()
        {
            currentMode = ContentMode.ActiveEvents;
            shownNews = null;

            SetActive(newsContent, false);
            SetActive(activeEventsContent, true);

            SetActive(calendarPanel, false);
            SetActive(newsDetails, false);

            SetActive(eventDetails, false);

            SetButtonSelected(newsTabButton, false);
            SetButtonSelected(activeEventsTabButton, true);

            RefreshActiveEventsList();
        }


        // =====================================================
        // NEWS LIST
        // =====================================================

        private void RefreshNewsList()
        {
            if (newsManager == null)
            {
                Debug.LogError(
                    "NewsEventsView: NewsManager is not assigned.",
                    this
                );

                return;
            }

            if (newsListContainer == null)
            {
                Debug.LogError(
                    "NewsEventsView: News List Container is not assigned.",
                    this
                );

                return;
            }

            if (newsListItemPrefab == null)
            {
                Debug.LogError(
                    "NewsEventsView: News List Item Prefab is not assigned.",
                    this
                );

                return;
            }

            ClearNewsList();

            // Newest first (PublishedNews is stored in publishing order).
            var publishedNews = newsManager.PublishedNews;
            InfoAccessLevel accessLevel = NewsPresentation.GetAccessLevel(player);
            int currentDay = CurrentDayOrNone;

            for (int i = publishedNews.Count - 1; i >= 0; i--)
            {
                NewsItem news = publishedNews[i];

                if (news.EventStarted(currentDay))
                    continue;

                if (!NewsPresentation.CanSee(news, accessLevel) || !MatchesCurrentFilter(news))
                    continue;

                NewsListItemUI item = Instantiate(
                    newsListItemPrefab,
                    newsListContainer
                );

                item.Setup(
                    news,
                    HandleNewsClicked,
                    timeManager
                );
            }

            UpdateCounts();
        }

        private void ClearNewsList()
        {
            if (newsListContainer == null)
                return;

            for (int i = newsListContainer.childCount - 1; i >= 0; i--)
            {
                GameObject child = newsListContainer.GetChild(i).gameObject;

                // Destroy happens at the end of the frame; hide first so the layout
                // doesn't show old and new rows together for one frame.
                child.SetActive(false);
                Destroy(child);
            }
        }

        private void HandleNewsClicked(NewsItem news)
        {
            Debug.Log(
                $"News clicked: {news.Type}, " +
                $"published day {news.DayPublished}"
            );

            OpenNewsDetails();

            // Panel is active now, so it can fill its texts.
            if (newsDetailsUI != null && currentMode == ContentMode.News)
            {
                newsDetailsUI.ShowNews(news);
                shownNews = news;
            }
        }

        private bool MatchesCurrentFilter(NewsItem news)
        {
            switch (currentFilter)
            {
                case NewsFilter.All:
                    return true;

                case NewsFilter.Official:
                    return news.Type == AntiqueTradingSimulator.News.NewsType.Official;

                case NewsFilter.Rumours:
                    return news.Type == AntiqueTradingSimulator.News.NewsType.Rumor;

                case NewsFilter.Leaked:
                    return news.Type == AntiqueTradingSimulator.News.NewsType.Leak;

                default:
                    return true;
            }
        }


        // =====================================================
        // NEWS DETAILS
        // =====================================================

        public void OpenNewsDetails()
        {
            if (currentMode != ContentMode.News)
                return;

            SetActive(calendarPanel, false);
            SetActive(newsDetails, true);
            SetActive(eventDetails, false);
        }

        public void CloseNewsDetails()
        {
            shownNews = null;

            SetActive(newsDetails, false);
            SetActive(eventDetails, false);

            if (currentMode == ContentMode.News)
                SetActive(calendarPanel, true);
        }


        // =====================================================
        // ACTIVE EVENTS LIST
        // =====================================================

        private void RefreshActiveEventsList()
        {
            if (eventManager == null)
            {
                Debug.LogError(
                    "NewsEventsView: EventManager is not assigned.",
                    this
                );

                return;
            }

            if (timeManager == null)
            {
                Debug.LogError(
                    "NewsEventsView: TimeManager is not assigned.",
                    this
                );

                return;
            }

            if (activeEventsListContainer == null)
            {
                Debug.LogError(
                    "NewsEventsView: Active Events List Container is not assigned.",
                    this
                );

                return;
            }

            if (activeEventListItemPrefab == null)
            {
                Debug.LogError(
                    "NewsEventsView: Active Event List Item Prefab is not assigned.",
                    this
                );

                return;
            }

            ClearActiveEventsList();

            Debug.Log(
                $"NewsEventsView: refreshing Active Events. " +
                $"Current day: {timeManager.CurrentDay}, " +
                $"active events: {eventManager.ActiveEvents.Count}"
            );

            foreach (ActiveEvent activeEvent in eventManager.ActiveEvents)
            {
                if (activeEvent == null)
                    continue;

                ActiveEventListItemUI item = Instantiate(
                    activeEventListItemPrefab,
                    activeEventsListContainer
                );

                item.Setup(
                    activeEvent,
                    timeManager.CurrentDay,
                    HandleActiveEventClicked
                );
            }

            UpdateCounts();
        }

        private void ClearActiveEventsList()
        {
            if (activeEventsListContainer == null)
                return;

            for (int i = activeEventsListContainer.childCount - 1; i >= 0; i--)
            {
                Destroy(
                    activeEventsListContainer.GetChild(i).gameObject
                );
            }
        }

        private void HandleActiveEventClicked(ActiveEvent activeEvent)
        {
            if (activeEvent == null)
                return;

            string eventName =
                activeEvent.Definition != null
                    ? activeEvent.Definition.DisplayName
                    : "Unknown Event";

            Debug.Log(
                $"Active event clicked: {eventName} " +
                $"(Day {activeEvent.StartDay}-{activeEvent.EndDay})"
            );

            OpenEventDetails();

            if (eventDetailsUI != null && currentMode == ContentMode.ActiveEvents)
                eventDetailsUI.ShowEvent(activeEvent);
        }


        // =====================================================
        // EVENT DETAILS
        // =====================================================

        public void OpenEventDetails()
        {
            if (currentMode != ContentMode.ActiveEvents)
                return;

            SetActive(calendarPanel, false);
            SetActive(newsDetails, false);
            SetActive(eventDetails, true);
        }

        public void CloseEventDetails()
        {
            SetActive(eventDetails, false);
        }


        // =====================================================
        // FILTERS
        // =====================================================

        public void ShowAll()
        {
            SetFilter(NewsFilter.All);
        }

        public void ShowOfficial()
        {
            SetFilter(NewsFilter.Official);
        }

        public void ShowRumours()
        {
            SetFilter(NewsFilter.Rumours);
        }

        public void ShowLeaked()
        {
            SetFilter(NewsFilter.Leaked);
        }

        private void SetFilter(NewsFilter filter)
        {
            currentFilter = filter;

            SetButtonSelected(
                allFilterButton,
                filter == NewsFilter.All
            );

            SetButtonSelected(
                officialFilterButton,
                filter == NewsFilter.Official
            );

            SetButtonSelected(
                rumoursFilterButton,
                filter == NewsFilter.Rumours
            );

            SetButtonSelected(
                leakedFilterButton,
                filter == NewsFilter.Leaked
            );

            RefreshNewsList();
        }


        // =====================================================
        // COUNTS & ACCESS LEVEL
        // =====================================================

        /// <summary>Updates "News (N)", "Active Events (N)" and the filter chips from what the player can see.</summary>
        private void UpdateCounts()
        {
            int all = 0, official = 0, rumours = 0, leaked = 0;

            if (newsManager != null)
            {
                InfoAccessLevel accessLevel = NewsPresentation.GetAccessLevel(player);
                int currentDay = CurrentDayOrNone;

                foreach (NewsItem news in newsManager.PublishedNews)
                {
                    // Same rule as RefreshNewsList, so the counts match the rows on screen.
                    if (news.EventStarted(currentDay) || !NewsPresentation.CanSee(news, accessLevel))
                        continue;

                    all++;

                    switch (news.Type)
                    {
                        case NewsType.Official: official++; break;
                        case NewsType.Rumor: rumours++; break;
                        case NewsType.Leak: leaked++; break;
                    }
                }
            }

            int activeEvents = eventManager != null ? eventManager.ActiveEvents.Count : 0;

            SetButtonLabel(newsTabButton, $"News  ({all})");
            SetButtonLabel(activeEventsTabButton, $"Active Events  ({activeEvents})");

            SetButtonLabel(allFilterButton, $"All  ({all})");
            SetButtonLabel(officialFilterButton, $"Official  ({official})");
            SetButtonLabel(rumoursFilterButton, $"Rumours  ({rumours})");
            SetButtonLabel(leakedFilterButton, $"Leaked  ({leaked})");
        }

        private void UpdateAccessLevelText()
        {
            if (accessLevelText == null)
                return;

            accessLevelText.text = NewsPresentation.AccessLevelLabel(
                NewsPresentation.GetAccessLevel(player)
            );
        }

        private static void SetButtonLabel(Button button, string label)
        {
            if (button == null)
                return;

            TMP_Text text = button.GetComponentInChildren<TMP_Text>(true);
            if (text != null)
                text.text = label;
        }


        // =====================================================
        // CREATE EVENT
        // =====================================================

        public void OpenCreateEvent()
        {
            if (createEventModal == null)
                createEventModal = FindAnyObjectByType<CreateEventModalUI>(FindObjectsInactive.Include);

            if (createEventModal == null)
            {
                Debug.LogError(
                    "NewsEventsView: Create Event modal not found. " +
                    "Build it with Tools > UI > News & Events > Build Create Event Modal.",
                    this
                );

                return;
            }

            createEventModal.Open();
        }


        // =====================================================
        // VISUAL BUTTON STATE
        // =====================================================

        private static void SetButtonSelected(
            Button button,
            bool selected
        )
        {
            if (button == null)
                return;

            button.interactable = !selected;
        }


        // =====================================================
        // HELPERS
        // =====================================================

        private static void SetActive(
            GameObject target,
            bool state
        )
        {
            if (target != null)
                target.SetActive(state);
        }
    }
}