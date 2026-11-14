using System;
using System.Collections.Generic;
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
        [Tooltip("Shown only in the week view. Auto-found at News Content/FiltersRow/EventsFilter.")]
        [SerializeField] private Button eventsFilterButton;

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

        [Header("Week View (auto-found under News Content/WeekBar if empty)")]
        [SerializeField] private GameObject weekBar;
        [SerializeField] private TMP_Text weekTitleText;
        [SerializeField] private Button previousWeekButton;
        [SerializeField] private Button nextWeekButton;
        [SerializeField] private Button closeWeekButton;
        [Tooltip("Auto-found on the Calendar Panel.")]
        [SerializeField] private NewsCalendarUI calendar;

        private enum ContentMode
        {
            News,
            ActiveEvents,

            /// <summary>Everything the player knows about one week, opened from the calendar.</summary>
            Week
        }

        private enum NewsFilter
        {
            All,
            Official,
            Rumours,
            Leaked,

            /// <summary>Week view only.</summary>
            Events
        }

        private ContentMode currentMode = ContentMode.News;
        private NewsFilter currentFilter = NewsFilter.All;

        // Fill the right-side panels with data of the clicked item (added automatically if missing).
        private DetailsPanelUI newsDetailsUI;
        private DetailsPanelUI eventDetailsUI;

        // The news item currently shown in the details panel, so the panel can be closed
        // if that item stops being news.
        private NewsItem shownNews;

        // Week view: the shown week and its entries (same data as the calendar's list).
        private int weekViewWeek;
        private readonly List<WeekEntry> weekEntries = new List<WeekEntry>();

        /// <summary>
        /// Today, or a day so early that nothing counts as started — without a TimeManager the
        /// view cannot tell what is in the past, and showing every item beats hiding every item.
        /// </summary>
        private int CurrentWeekOrNone => timeManager != null ? timeManager.CurrentWeek : int.MinValue;


        // =====================================================
        // UNITY LIFECYCLE
        // =====================================================

        private void Awake()
        {
            AutoWireWeekView();
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
            SubscribeToCalendar();
        }

        private void OnDestroy()
        {
            UnsubscribeFromEventManager();
            UnsubscribeFromNewsManager();
            UnsubscribeFromTimeManager();
            UnsubscribeFromCalendar();
        }

        private void AutoWireWeekView()
        {
            if (calendar == null && calendarPanel != null)
                calendar = calendarPanel.GetComponent<NewsCalendarUI>();

            if (newsContent == null)
                return;

            Transform content = newsContent.transform;

            if (eventsFilterButton == null)
                eventsFilterButton = FindIn<Button>(content, "FiltersRow/EventsFilter");

            if (weekBar == null)
            {
                Transform bar = content.Find("WeekBar");
                if (bar != null)
                    weekBar = bar.gameObject;
            }

            if (weekBar == null)
            {
                Debug.LogWarning(
                    "NewsEventsView: WeekBar not found under News Content. " +
                    "The week view works without it, but has no title or week arrows.",
                    this
                );

                return;
            }

            Transform weekBarTransform = weekBar.transform;

            if (weekTitleText == null) weekTitleText = FindIn<TMP_Text>(weekBarTransform, "WeekTitleText");
            if (previousWeekButton == null) previousWeekButton = FindIn<Button>(weekBarTransform, "PreviousWeekButton");
            if (nextWeekButton == null) nextWeekButton = FindIn<Button>(weekBarTransform, "NextWeekButton");
            if (closeWeekButton == null) closeWeekButton = FindIn<Button>(weekBarTransform, "CloseWeekButton");

            // Label lives in code so the scene can't drift from it.
            SetButtonLabel(closeWeekButton, "×  Back to current");
        }

        private static T FindIn<T>(Transform root, string path) where T : Component
        {
            Transform t = root.Find(path);
            return t != null ? t.GetComponent<T>() : null;
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

            if (currentMode == ContentMode.Week && isActiveAndEnabled)
                RefreshWeekList();
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
                timeManager.OnWeekChanged += HandleWeekChanged;
        }

        private void UnsubscribeFromTimeManager()
        {
            if (timeManager != null)
                timeManager.OnWeekChanged -= HandleWeekChanged;
        }

        /// <summary>
        /// A new week can push announced events into the past, which drops their news out of
        /// the news list. If the open details panel is showing one of those, it closes.
        /// (Only in the News tab – the week view and the calendar keep showing such news.)
        /// </summary>
        private void HandleWeekChanged(int newWeek)
        {
            if (shownNews != null && currentMode == ContentMode.News &&
                shownNews.EventStarted(newWeek) && !shownNews.EventStarted(newWeek - 1))
                CloseNewsDetails();

            if (!isActiveAndEnabled)
                return;

            RefreshCurrentList();
        }

        private void HandleNewsPublished(NewsItem news)
        {
            // Hidden view doesn't need to rebuild – OnShown refreshes it anyway.
            if (!isActiveAndEnabled)
                return;

            RefreshCurrentList();
        }

        /// <summary>Rebuilds the list on screen; counts in tabs/filters change even when the Active Events tab is open.</summary>
        private void RefreshCurrentList()
        {
            switch (currentMode)
            {
                case ContentMode.News:
                    RefreshNewsList();
                    break;

                case ContentMode.Week:
                    RefreshWeekList();
                    break;

                default:
                    UpdateCounts();
                    break;
            }
        }


        // =====================================================
        // CALENDAR SUBSCRIPTION
        // =====================================================

        private void SubscribeToCalendar()
        {
            if (calendar == null)
                return;

            calendar.ViewAllRequested += ShowWeek;
            calendar.DateSelected += HandleCalendarDateSelected;
            calendar.EntryClicked += HandleCalendarEntryClicked;
        }

        private void UnsubscribeFromCalendar()
        {
            if (calendar == null)
                return;

            calendar.ViewAllRequested -= ShowWeek;
            calendar.DateSelected -= HandleCalendarDateSelected;
            calendar.EntryClicked -= HandleCalendarEntryClicked;
        }

        /// <summary>In the week view the list follows the week selected in the calendar.</summary>
        private void HandleCalendarDateSelected(DateTime date)
        {
            if (currentMode != ContentMode.Week || timeManager == null)
                return;

            int week = timeManager.DateToWeek(date);
            if (week == weekViewWeek)
                return;

            weekViewWeek = week;

            if (!isActiveAndEnabled)
                return;

            RefreshWeekList();
            ScrollNewsListToTop();
        }

        private void HandleCalendarEntryClicked(WeekEntry entry)
        {
            if (entry.IsEvent)
                ShowEventDetails(entry.Event);
            else if (entry.News != null)
                HandleNewsClicked(entry.News);
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
            Bind(eventsFilterButton, ShowEvents);

            Bind(previousWeekButton, ShowPreviousWeek);
            Bind(nextWeekButton, ShowNextWeek);
            Bind(closeWeekButton, ShowNewsMode);

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

            SetWeekViewVisible(false);

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

            SetWeekViewVisible(false);

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
            int currentWeek = CurrentWeekOrNone;

            for (int i = publishedNews.Count - 1; i >= 0; i--)
            {
                NewsItem news = publishedNews[i];

                if (news.EventStarted(currentWeek))
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
                $"published week {news.WeekPublished}"
            );

            OpenNewsDetails();

            // Panel is active now, so it can fill its texts.
            if (newsDetailsUI != null && currentMode != ContentMode.ActiveEvents)
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

                case NewsFilter.Events:
                    return false;

                default:
                    return true;
            }
        }

        private bool MatchesCurrentFilter(WeekEntry entry)
        {
            switch (currentFilter)
            {
                case NewsFilter.Official: return entry.Kind == WeekEntryKind.Official;
                case NewsFilter.Rumours: return entry.Kind == WeekEntryKind.Rumour;
                case NewsFilter.Leaked: return entry.Kind == WeekEntryKind.Leaked;
                case NewsFilter.Events: return entry.Kind == WeekEntryKind.Event;
                default: return true;
            }
        }


        // =====================================================
        // NEWS DETAILS
        // =====================================================

        public void OpenNewsDetails()
        {
            if (currentMode == ContentMode.ActiveEvents)
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

            if (currentMode != ContentMode.ActiveEvents)
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
                $"Current week: {timeManager.CurrentWeek}, " +
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
                    timeManager.CurrentWeek,
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
                $"(Week {activeEvent.StartWeek}-{activeEvent.EndWeek})"
            );

            ShowEventDetails(activeEvent);
        }


        // =====================================================
        // EVENT DETAILS
        // =====================================================

        /// <summary>Opens the event details in place of the calendar (any tab: active events, week view, calendar list).</summary>
        public void OpenEventDetails()
        {
            shownNews = null;

            SetActive(calendarPanel, false);
            SetActive(newsDetails, false);
            SetActive(eventDetails, true);
        }

        private void ShowEventDetails(ActiveEvent activeEvent)
        {
            if (activeEvent == null)
                return;

            OpenEventDetails();

            // Panel is active now, so it can fill its texts.
            if (eventDetailsUI != null)
                eventDetailsUI.ShowEvent(activeEvent);
        }

        public void CloseEventDetails()
        {
            SetActive(eventDetails, false);

            // The Active Events tab has no calendar; the other modes show it again.
            if (currentMode != ContentMode.ActiveEvents)
                SetActive(calendarPanel, true);
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

        public void ShowEvents()
        {
            SetFilter(NewsFilter.Events);
        }

        private void SetFilter(NewsFilter filter)
        {
            currentFilter = filter;

            UpdateFilterButtons();

            if (currentMode == ContentMode.Week)
                RefreshWeekList();
            else
                RefreshNewsList();
        }

        private void UpdateFilterButtons()
        {
            SetButtonSelected(allFilterButton, currentFilter == NewsFilter.All);
            SetButtonSelected(officialFilterButton, currentFilter == NewsFilter.Official);
            SetButtonSelected(rumoursFilterButton, currentFilter == NewsFilter.Rumours);
            SetButtonSelected(leakedFilterButton, currentFilter == NewsFilter.Leaked);
            SetButtonSelected(eventsFilterButton, currentFilter == NewsFilter.Events);
        }


        // =====================================================
        // WEEK VIEW
        // =====================================================

        /// <summary>
        /// Shows everything the player knows about the week of <paramref name="date"/> in the news list:
        /// events running that week (active or ended) and all news published that week – also those
        /// whose event has already started, which the News tab no longer lists.
        /// Opened by "View all …" under the calendar; the list then follows the calendar's selected week.
        /// </summary>
        public void ShowWeek(DateTime date)
        {
            if (timeManager == null)
            {
                Debug.LogError("NewsEventsView: TimeManager is not assigned.", this);
                return;
            }

            currentMode = ContentMode.Week;
            weekViewWeek = timeManager.DateToWeek(date);
            shownNews = null;

            SetActive(newsContent, true);
            SetActive(activeEventsContent, false);

            SetActive(calendarPanel, true);
            SetActive(newsDetails, false);
            SetActive(eventDetails, false);

            // Neither tab is the current one – clicking either leaves the week view.
            SetButtonSelected(newsTabButton, false);
            SetButtonSelected(activeEventsTabButton, false);

            SetWeekViewVisible(true);
            UpdateFilterButtons();

            RefreshWeekList();
            ScrollNewsListToTop();
        }

        public void ShowPreviousWeek()
        {
            StepWeek(-1);
        }

        public void ShowNextWeek()
        {
            StepWeek(1);
        }

        /// <summary>Weeks from the campaign's first week up to the current one (later weeks are still empty).</summary>
        private void StepWeek(int delta)
        {
            if (currentMode != ContentMode.Week || timeManager == null)
                return;

            int week = weekViewWeek + delta;
            if (week < FirstWeek || (delta > 0 && week > timeManager.CurrentWeek))
                return;

            weekViewWeek = week;

            // Keep the calendar on the same week. For the current week select today,
            // so the calendar (and with it this list) keeps following the game time.
            if (calendar != null)
            {
                DateTime date = week == timeManager.CurrentWeek
                    ? timeManager.CurrentDate
                    : timeManager.WeekToDate(week);

                calendar.SelectDate(date); // raises DateSelected – ignored, the week is already set
            }

            RefreshWeekList();
            ScrollNewsListToTop();
        }

        private void ScrollNewsListToTop()
        {
            ScrollRect scroll = newsListContainer != null
                ? newsListContainer.GetComponentInParent<ScrollRect>()
                : null;

            if (scroll != null)
                scroll.verticalNormalizedPosition = 1f;
        }

        private int FirstWeek => timeManager != null ? timeManager.DateToWeek(timeManager.StartDate) : 1;

        private void RefreshWeekList()
        {
            if (timeManager == null || newsListContainer == null ||
                newsListItemPrefab == null || activeEventListItemPrefab == null)
            {
                Debug.LogError(
                    "NewsEventsView: the week view needs TimeManager, News List Container " +
                    "and both list item prefabs assigned.",
                    this
                );

                return;
            }

            ClearNewsList();

            int currentWeek = timeManager.CurrentWeek;

            WeekEntries.Collect(
                weekViewWeek,
                eventManager,
                newsManager,
                currentWeek,
                weekEntries
            );

            // Same order as the list under the calendar: events first, then news in publishing order.
            foreach (WeekEntry entry in weekEntries)
            {
                if (!MatchesCurrentFilter(entry))
                    continue;

                if (entry.IsEvent)
                {
                    ActiveEventListItemUI item = Instantiate(
                        activeEventListItemPrefab,
                        newsListContainer
                    );

                    item.Setup(entry.Event, currentWeek, ShowEventDetails);
                }
                else
                {
                    NewsListItemUI item = Instantiate(
                        newsListItemPrefab,
                        newsListContainer
                    );

                    item.Setup(entry.News, HandleNewsClicked, timeManager);
                }
            }

            UpdateWeekBar();
            UpdateCounts();
        }

        /// <summary>
        /// "Week of 3 March 1884 · 5 weeks ago" (counts are on the filter chips), plus a note for
        /// weeks with nothing in them. Also the arrow states.
        /// </summary>
        private void UpdateWeekBar()
        {
            if (timeManager == null)
                return;

            int currentWeek = timeManager.CurrentWeek;

            if (weekTitleText != null)
            {
                string title = $"Week of {Core.TimeManager.FormatLong(timeManager.WeekToDate(weekViewWeek))}" +
                               $"  ·  {NewsPresentation.RelativeWeeks(weekViewWeek - currentWeek).ToLowerInvariant()}";

                if (weekEntries.Count == 0)
                    title += weekViewWeek >= currentWeek ? "  ·  quiet so far" : "  ·  a quiet week";

                weekTitleText.text = title;
            }

            if (previousWeekButton != null)
                previousWeekButton.interactable = weekViewWeek > FirstWeek;

            if (nextWeekButton != null)
                nextWeekButton.interactable = weekViewWeek < currentWeek;
        }

        /// <summary>The week bar and the Events chip exist only in the week view.</summary>
        private void SetWeekViewVisible(bool visible)
        {
            SetActive(weekBar, visible);

            if (eventsFilterButton != null)
                eventsFilterButton.gameObject.SetActive(visible);

            if (!visible && currentFilter == NewsFilter.Events)
            {
                currentFilter = NewsFilter.All;
                UpdateFilterButtons();
            }
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
                int currentWeek = CurrentWeekOrNone;

                foreach (NewsItem news in newsManager.PublishedNews)
                {
                    // Same rule as RefreshNewsList, so the counts match the rows on screen.
                    if (news.EventStarted(currentWeek) || !NewsPresentation.CanSee(news, accessLevel))
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

            if (currentMode == ContentMode.Week)
            {
                UpdateWeekFilterCounts();
                return;
            }

            SetButtonLabel(allFilterButton, $"All  ({all})");
            SetButtonLabel(officialFilterButton, $"Official  ({official})");
            SetButtonLabel(rumoursFilterButton, $"Rumours  ({rumours})");
            SetButtonLabel(leakedFilterButton, $"Leaked  ({leaked})");
        }

        /// <summary>In the week view the filter chips count the shown week's entries (the tabs keep the global counts).</summary>
        private void UpdateWeekFilterCounts()
        {
            int official = 0, rumours = 0, leaked = 0, events = 0;

            foreach (WeekEntry entry in weekEntries)
            {
                switch (entry.Kind)
                {
                    case WeekEntryKind.Official: official++; break;
                    case WeekEntryKind.Rumour: rumours++; break;
                    case WeekEntryKind.Leaked: leaked++; break;
                    case WeekEntryKind.Event: events++; break;
                }
            }

            SetButtonLabel(allFilterButton, $"All  ({weekEntries.Count})");
            SetButtonLabel(officialFilterButton, $"Official  ({official})");
            SetButtonLabel(rumoursFilterButton, $"Rumours  ({rumours})");
            SetButtonLabel(leakedFilterButton, $"Leaked  ({leaked})");
            SetButtonLabel(eventsFilterButton, $"Events  ({events})");
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

            // With the theme, the selected tab / filter shows the active tab frame (instead of only
            // looking disabled); without it, its colour stays as it is.
            var image = button.targetGraphic as Image;
            var current = image != null ? image.color : Color.white;
            UITheme.ApplyTabState(button, selected, current, current);
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