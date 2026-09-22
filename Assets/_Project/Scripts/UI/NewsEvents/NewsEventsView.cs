using UnityEngine;
using UnityEngine.UI;
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

        [Header("Main Content")]
        [SerializeField] private GameObject newsContent;
        [SerializeField] private GameObject activeEventsContent;

        [Header("Side Panel")]
        [SerializeField] private GameObject calendarPanel;
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

        [Header("Calendar")]
        [SerializeField] private Button previousMonthButton;
        [SerializeField] private Button nextMonthButton;
        [SerializeField] private Button[] calendarDayButtons;

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

        private Button selectedCalendarDay;


        // =====================================================
        // UNITY LIFECYCLE
        // =====================================================

        private void Awake()
        {
            BindButtons();
        }

        private void Start()
        {
            SubscribeToEventManager();
        }

        private void OnDestroy()
        {
            UnsubscribeFromEventManager();
        }

        protected override void OnShown()
        {
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

            Bind(previousMonthButton, PreviousMonth);
            Bind(nextMonthButton, NextMonth);

            if (calendarDayButtons != null)
            {
                foreach (Button button in calendarDayButtons)
                {
                    if (button == null)
                        continue;

                    Button capturedButton = button;

                    button.onClick.RemoveAllListeners();

                    button.onClick.AddListener(
                        () => SelectCalendarDay(capturedButton)
                    );
                }
            }
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

            foreach (NewsItem news in newsManager.PublishedNews)
            {
                if (!MatchesCurrentFilter(news))
                    continue;

                NewsListItemUI item = Instantiate(
                    newsListItemPrefab,
                    newsListContainer
                );

                item.Setup(
                    news,
                    HandleNewsClicked
                );
            }
        }

        private void ClearNewsList()
        {
            if (newsListContainer == null)
                return;

            for (int i = newsListContainer.childCount - 1; i >= 0; i--)
            {
                Destroy(
                    newsListContainer.GetChild(i).gameObject
                );
            }
        }

        private void HandleNewsClicked(NewsItem news)
        {
            Debug.Log(
                $"News clicked: {news.Type}, " +
                $"published day {news.DayPublished}"
            );

            OpenNewsDetails();
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
        // CALENDAR
        // =====================================================

        private void PreviousMonth()
        {
            Debug.Log("Previous calendar month");
        }

        private void NextMonth()
        {
            Debug.Log("Next calendar month");
        }

        private void SelectCalendarDay(Button button)
        {
            if (button == null)
                return;

            if (selectedCalendarDay != null)
            {
                SetButtonSelected(
                    selectedCalendarDay,
                    false
                );
            }

            selectedCalendarDay = button;

            SetButtonSelected(
                selectedCalendarDay,
                true
            );

            Debug.Log(
                "Selected calendar day: " +
                button.name
            );
        }


        // =====================================================
        // CREATE EVENT
        // =====================================================

        public void OpenCreateEvent()
        {
            Debug.Log("Create Event clicked");
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