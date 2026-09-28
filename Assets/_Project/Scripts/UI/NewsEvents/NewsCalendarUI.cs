using System;
using System.Collections.Generic;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Events;
using AntiqueTradingSimulator.News;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Month calendar in the News & Events side panel.
    /// Attach to CalendarPanel. Empty references are found automatically by child names.
    /// - highlights the current game day (TimeManager),
    /// - shows colored markers on days with news / events,
    /// - lists the news and events of the selected day below the calendar.
    /// </summary>
    public class NewsCalendarUI : MonoBehaviour
    {
        [Header("Game systems (auto-found if empty)")]
        [SerializeField] private TimeManager timeManager;
        [SerializeField] private NewsManager newsManager;
        [SerializeField] private EventManager eventManager;
        [SerializeField] private Agents.PlayerTrader player;

        [Header("Calendar references (auto-filled by name if empty)")]
        [SerializeField] private TMP_Text monthText;
        [SerializeField] private Button previousMonthButton;
        [SerializeField] private Button nextMonthButton;
        [SerializeField] private Transform calendarGrid;

        [Header("Selected day references (auto-filled by name if empty)")]
        [SerializeField] private TMP_Text selectedDateText;
        [SerializeField] private TMP_Text itemCountText;
        [SerializeField] private Transform selectedDayList;
        [SerializeField] private TMP_Text viewAllEventsText;

        [Header("Cell colors")]
        [SerializeField] private Color cellNormal = new Color32(0x47, 0x47, 0x47, 0xFF);
        [SerializeField] private Color cellToday = new Color32(0x6B, 0x5E, 0x40, 0xFF);
        [SerializeField] private Color cellSelected = new Color32(0x5C, 0x5C, 0x5C, 0xFF);

        [Header("Day number colors")]
        [SerializeField] private Color textCurrentMonth = new Color32(0xEB, 0xEB, 0xEB, 0xFF);
        [SerializeField] private Color textOtherMonth = new Color32(0x94, 0x94, 0x94, 0xFF);

        [Header("Category colors (same as legend)")]
        [SerializeField] private Color officialColor = new Color32(0x59, 0xB8, 0x73, 0xFF);
        [SerializeField] private Color rumourColor = new Color32(0xE6, 0xB2, 0x40, 0xFF);
        [SerializeField] private Color leakedColor = new Color32(0x99, 0x6B, 0xD9, 0xFF);
        [SerializeField] private Color eventColor = new Color32(0xD1, 0x4C, 0x4C, 0xFF);

        [Header("Options")]
        [Tooltip("Hide the 6th row when the month fits in 5 weeks.")]
        [SerializeField] private bool hideUnusedLastRow = false;

        [Tooltip("How many entries fit under the calendar without scrolling.")]
        [SerializeField] private int maxVisibleItems = 3;

        [SerializeField] private float markerSize = 6f;

        /// <summary>Raised when a date gets selected (click or new day).</summary>
        public event Action<DateTime> DateSelected;

        public DateTime Today { get; private set; }
        public DateTime SelectedDate { get; private set; }

        // ------------------------------------------------------------------
        // Internal types
        // ------------------------------------------------------------------

        private enum EntryKind { Official, Rumour, Leaked, Event } // order = legend order

        private readonly struct DayEntry
        {
            public readonly EntryKind Kind;
            public readonly string Title;

            public DayEntry(EntryKind kind, string title)
            {
                Kind = kind;
                Title = title;
            }
        }

        private class Cell
        {
            public GameObject Root;
            public Image Background;
            public TMP_Text Number;
            public Transform Markers;
            public readonly List<Image> MarkerImages = new List<Image>();
            public DateTime Date;
        }

        private Cell[] cells;
        private DateTime displayedMonth; // always the 1st day of the shown month
        private bool initialized;
        private bool dirty;

        private GameObject itemTemplate;
        private GameObject emptyLabel;
        private readonly List<GameObject> spawnedItems = new List<GameObject>();
        private readonly List<DayEntry> entryBuffer = new List<DayEntry>();

        // ------------------------------------------------------------------
        // Unity
        // ------------------------------------------------------------------

        private void Reset() => AutoWire();

        private void OnEnable()
        {
            EnsureInitialized();
            Subscribe();

            if (timeManager == null) return;

            // The panel is hidden while news/event details are open and days may pass meanwhile.
            // Jump to the new "today" only on first show or if the player was looking at today;
            // otherwise keep the player's selected day (e.g. after closing the details panel).
            bool firstShow = SelectedDate == default;
            bool wasViewingToday = SelectedDate == Today;

            Today = timeManager.CurrentDate;

            if (firstShow || wasViewingToday) SelectDate(Today);
            else RefreshAll();
        }

        private void OnDisable() => Unsubscribe();

        private void LateUpdate()
        {
            // Several systems react to the same day change; refresh once, after all of them.
            if (!dirty) return;
            dirty = false;
            RefreshAll();
        }

        // ------------------------------------------------------------------
        // Subscriptions
        // ------------------------------------------------------------------

        private void Subscribe()
        {
            if (timeManager != null) timeManager.OnDayChanged += HandleDayChanged;
            if (newsManager != null) newsManager.OnNewsPublished += HandleNewsPublished;
            if (eventManager != null)
            {
                eventManager.OnEventTriggered += HandleEventChanged;
                eventManager.OnEventEnded += HandleEventChanged;
            }
        }

        private void Unsubscribe()
        {
            if (timeManager != null) timeManager.OnDayChanged -= HandleDayChanged;
            if (newsManager != null) newsManager.OnNewsPublished -= HandleNewsPublished;
            if (eventManager != null)
            {
                eventManager.OnEventTriggered -= HandleEventChanged;
                eventManager.OnEventEnded -= HandleEventChanged;
            }
        }

        private void HandleDayChanged(int day)
        {
            bool wasViewingToday = SelectedDate == Today;
            Today = timeManager.DayToDate(day);

            // Follow the new day only if the player was looking at "today"; don't steal their selection.
            if (wasViewingToday)
            {
                SelectedDate = Today;
                displayedMonth = new DateTime(Today.Year, Today.Month, 1);
                UpdateSelectedDateTexts();
                DateSelected?.Invoke(SelectedDate);
            }

            dirty = true;
        }

        private void HandleNewsPublished(NewsItem news) => dirty = true;

        private void HandleEventChanged(ActiveEvent activeEvent) => dirty = true;

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        /// <summary>Selects a date. Switches the displayed month if needed.</summary>
        public void SelectDate(DateTime date)
        {
            EnsureInitialized();

            SelectedDate = date.Date;
            displayedMonth = new DateTime(date.Year, date.Month, 1);

            UpdateSelectedDateTexts();
            RefreshAll();

            DateSelected?.Invoke(SelectedDate);
        }

        public void ShowPreviousMonth() => ShowMonth(displayedMonth.AddMonths(-1));

        public void ShowNextMonth() => ShowMonth(displayedMonth.AddMonths(1));

        /// <summary>Changes the displayed month without changing the selection.</summary>
        public void ShowMonth(DateTime month)
        {
            EnsureInitialized();
            displayedMonth = new DateTime(month.Year, month.Month, 1);
            RenderGrid();
        }

        // ------------------------------------------------------------------
        // Setup
        // ------------------------------------------------------------------

        private void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;

            AutoWire();
            if (timeManager == null)
                Debug.LogError("[NewsCalendarUI] TimeManager not found in the scene.", this);

            BuildCells();
            BuildSelectedDayList();

            if (previousMonthButton != null) previousMonthButton.onClick.AddListener(ShowPreviousMonth);
            if (nextMonthButton != null) nextMonthButton.onClick.AddListener(ShowNextMonth);
        }

        private void AutoWire()
        {
            if (timeManager == null) timeManager = FindAnyObjectByType<TimeManager>();
            if (newsManager == null) newsManager = FindAnyObjectByType<NewsManager>();
            if (eventManager == null) eventManager = FindAnyObjectByType<EventManager>();
            if (player == null) player = FindAnyObjectByType<Agents.PlayerTrader>();

            if (monthText == null) monthText = FindChild<TMP_Text>("CalendarHeader/MonthText");
            if (previousMonthButton == null) previousMonthButton = FindChild<Button>("CalendarHeader/PreviousMonthButton");
            if (nextMonthButton == null) nextMonthButton = FindChild<Button>("CalendarHeader/NextMonthButton");
            if (calendarGrid == null) calendarGrid = transform.Find("CalendarGrid");

            if (selectedDateText == null) selectedDateText = FindChild<TMP_Text>("SelectedDayHeader/SelectedDateText");
            if (itemCountText == null) itemCountText = FindChild<TMP_Text>("SelectedDayHeader/ItemCountText");
            if (selectedDayList == null) selectedDayList = transform.Find("SelectedDayList");
            if (viewAllEventsText == null) viewAllEventsText = FindChild<TMP_Text>("ViewAllEventsButton/Text");
        }

        private T FindChild<T>(string path) where T : Component
        {
            var t = transform.Find(path);
            return t != null ? t.GetComponent<T>() : null;
        }

        private void BuildCells()
        {
            if (calendarGrid == null)
            {
                Debug.LogError("[NewsCalendarUI] CalendarGrid not found.", this);
                cells = Array.Empty<Cell>();
                return;
            }

            cells = new Cell[calendarGrid.childCount];
            for (int i = 0; i < cells.Length; i++)
            {
                var child = calendarGrid.GetChild(i);
                var numberTransform = child.Find("DayNumber");

                var cell = new Cell
                {
                    Root = child.gameObject,
                    Background = child.GetComponent<Image>(),
                    Number = numberTransform != null ? numberTransform.GetComponent<TMP_Text>() : null,
                    Markers = child.Find("MarkersRow")
                };
                cells[i] = cell;

                // Remove placeholder markers from the mockup; real ones are created from data.
                if (cell.Markers != null)
                    DestroyChildren(cell.Markers, keepFirst: false);

                var button = child.GetComponent<Button>();
                if (button != null)
                    button.onClick.AddListener(() => SelectDate(cell.Date));
            }

            if (cells.Length != 42)
                Debug.LogWarning($"[NewsCalendarUI] Expected 42 day cells (6 weeks x 7), found {cells.Length}.", this);
        }

        /// <summary>
        /// The first item in SelectedDayList becomes a hidden template; the other placeholder items are removed.
        /// A copy of its TitleText is used as the "no events" label.
        /// </summary>
        private void BuildSelectedDayList()
        {
            if (selectedDayList == null || selectedDayList.childCount == 0)
            {
                Debug.LogWarning("[NewsCalendarUI] SelectedDayList or its template item not found.", this);
                return;
            }

            itemTemplate = selectedDayList.GetChild(0).gameObject;
            DestroyChildren(selectedDayList, keepFirst: true);

            var templateTitle = itemTemplate.transform.Find("MainInfo/TitleText");
            if (templateTitle != null)
            {
                emptyLabel = Instantiate(templateTitle.gameObject, selectedDayList);
                emptyLabel.name = "EmptyLabel";

                var text = emptyLabel.GetComponent<TMP_Text>();
                text.text = "No events on this day";
                text.alignment = TextAlignmentOptions.Center;
                text.fontStyle = FontStyles.Normal;
                text.color = textOtherMonth;

                var layout = emptyLabel.GetComponent<LayoutElement>();
                if (layout == null) layout = emptyLabel.AddComponent<LayoutElement>();
                layout.preferredHeight = 46f;

                emptyLabel.SetActive(false);
            }

            itemTemplate.SetActive(false);
        }

        private static void DestroyChildren(Transform parent, bool keepFirst)
        {
            int last = keepFirst ? 1 : 0;
            for (int i = parent.childCount - 1; i >= last; i--)
            {
                var child = parent.GetChild(i).gameObject;
                child.SetActive(false); // hide immediately, Destroy happens at end of frame
                Destroy(child);
            }
        }

        // ------------------------------------------------------------------
        // Data
        // ------------------------------------------------------------------

        /// <summary>
        /// Collects everything the player knows about a game day:
        /// events running that day (active or already ended) and news published that day.
        /// Scheduled (future) events are intentionally NOT shown – the player learns about them only through news.
        /// </summary>
        private void GetEntries(int day, List<DayEntry> result)
        {
            result.Clear();

            if (eventManager != null)
            {
                AddEvents(eventManager.ActiveEvents, day, result);
                AddEvents(eventManager.EndedEvents, day, result);
            }

            if (newsManager != null)
            {
                InfoAccessLevel accessLevel = NewsPresentation.GetAccessLevel(player);

                foreach (NewsItem news in newsManager.PublishedNews)
                {
                    // Only news the player has access to, and only on the day it was published.
                    if (!NewsPresentation.CanSee(news, accessLevel) || news.DayPublished != day) continue;
                    result.Add(new DayEntry(KindOf(news.Type), NewsPresentation.GetTitle(news)));
                }
            }
        }

        private static void AddEvents(IReadOnlyList<ActiveEvent> events, int day, List<DayEntry> result)
        {
            if (events == null) return;

            foreach (ActiveEvent activeEvent in events)
            {
                // EndDay is exclusive (the event expires at the start of EndDay).
                if (activeEvent == null || day < activeEvent.StartDay || day >= activeEvent.EndDay) continue;

                string title = activeEvent.Definition != null ? activeEvent.Definition.DisplayName : "Unknown event";
                result.Add(new DayEntry(EntryKind.Event, title));
            }
        }

        private static EntryKind KindOf(NewsType type) => type switch
        {
            NewsType.Rumor => EntryKind.Rumour,
            NewsType.Leak => EntryKind.Leaked,
            _ => EntryKind.Official
        };

        private Color ColorOf(EntryKind kind) => kind switch
        {
            EntryKind.Official => officialColor,
            EntryKind.Rumour => rumourColor,
            EntryKind.Leaked => leakedColor,
            _ => eventColor
        };

        private static string LabelOf(EntryKind kind) => kind switch
        {
            EntryKind.Official => NewsPresentation.TypeLabel(NewsType.Official),
            EntryKind.Rumour => NewsPresentation.TypeLabel(NewsType.Rumor),
            EntryKind.Leaked => NewsPresentation.TypeLabel(NewsType.Leak),
            _ => "Event"
        };

        // ------------------------------------------------------------------
        // Rendering
        // ------------------------------------------------------------------

        private void RefreshAll()
        {
            RenderGrid();
            RenderSelectedDayList();
        }

        private void RenderGrid()
        {
            if (monthText != null)
                monthText.text = TimeManager.FormatMonthYear(displayedMonth);

            // Monday-first week: Monday = 0 ... Sunday = 6
            int offset = ((int)displayedMonth.DayOfWeek + 6) % 7;
            DateTime gridStart = displayedMonth.AddDays(-offset);

            int daysInMonth = DateTime.DaysInMonth(displayedMonth.Year, displayedMonth.Month);
            int rowsNeeded = Mathf.CeilToInt((offset + daysInMonth) / 7f);

            for (int i = 0; i < cells.Length; i++)
            {
                var cell = cells[i];
                cell.Date = gridStart.AddDays(i);

                bool visible = !hideUnusedLastRow || i < rowsNeeded * 7;
                cell.Root.SetActive(visible);

                bool inMonth = cell.Date.Month == displayedMonth.Month && cell.Date.Year == displayedMonth.Year;

                if (cell.Number != null)
                {
                    cell.Number.text = cell.Date.Day.ToString();
                    cell.Number.color = inMonth ? textCurrentMonth : textOtherMonth;
                }

                if (cell.Background != null)
                {
                    if (cell.Date == Today) cell.Background.color = cellToday;
                    else if (cell.Date == SelectedDate) cell.Background.color = cellSelected;
                    else cell.Background.color = cellNormal;
                }

                if (timeManager != null)
                {
                    GetEntries(timeManager.DateToDay(cell.Date), entryBuffer);
                    RenderMarkers(cell, entryBuffer);
                }
            }

            if (hideUnusedLastRow)
                UpdateGridHeight(rowsNeeded);

            UpdateNavigationButtons();
        }

        /// <summary>One dot per category present on that day (Official, Rumour, Leaked, Event).</summary>
        private void RenderMarkers(Cell cell, List<DayEntry> entries)
        {
            if (cell.Markers == null) return;

            int used = 0;
            for (int k = 0; k <= (int)EntryKind.Event; k++)
            {
                var kind = (EntryKind)k;
                if (!entries.Exists(e => e.Kind == kind)) continue;

                var marker = GetMarker(cell, used++);
                marker.color = ColorOf(kind);
                marker.gameObject.SetActive(true);
            }

            for (int i = used; i < cell.MarkerImages.Count; i++)
                cell.MarkerImages[i].gameObject.SetActive(false);
        }

        private Image GetMarker(Cell cell, int index)
        {
            while (cell.MarkerImages.Count <= index)
            {
                var go = new GameObject("Marker", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                go.transform.SetParent(cell.Markers, false);

                var image = go.GetComponent<Image>();
                image.raycastTarget = false;

                var layout = go.GetComponent<LayoutElement>();
                layout.preferredWidth = markerSize;
                layout.preferredHeight = markerSize;
                layout.flexibleWidth = 0f;
                layout.flexibleHeight = 0f;

                cell.MarkerImages.Add(image);
            }
            return cell.MarkerImages[index];
        }

        private void RenderSelectedDayList()
        {
            if (itemTemplate == null || timeManager == null) return;

            foreach (var item in spawnedItems)
            {
                item.SetActive(false);
                Destroy(item);
            }
            spawnedItems.Clear();

            GetEntries(timeManager.DateToDay(SelectedDate), entryBuffer);

            int shown = Mathf.Min(entryBuffer.Count, maxVisibleItems);
            for (int i = 0; i < shown; i++)
            {
                var item = Instantiate(itemTemplate, selectedDayList);
                item.name = $"DayEntry_{i + 1}";
                item.SetActive(true);
                FillItem(item.transform, entryBuffer[i]);
                spawnedItems.Add(item);
            }

            if (emptyLabel != null)
            {
                emptyLabel.SetActive(entryBuffer.Count == 0);
                emptyLabel.transform.SetAsLastSibling();
            }

            if (itemCountText != null)
                itemCountText.text = entryBuffer.Count == 1 ? "1 item" : $"{entryBuffer.Count} items";
        }

        private void FillItem(Transform item, DayEntry entry)
        {
            var title = item.Find("MainInfo/TitleText");
            if (title != null) title.GetComponent<TMP_Text>().text = entry.Title;

            var type = item.Find("MainInfo/TypeText");
            if (type != null)
            {
                var text = type.GetComponent<TMP_Text>();
                text.text = LabelOf(entry.Kind);
                text.color = ColorOf(entry.Kind);
            }
        }

        private void UpdateGridHeight(int rows)
        {
            var grid = calendarGrid.GetComponent<GridLayoutGroup>();
            var layout = calendarGrid.GetComponent<LayoutElement>();
            if (grid == null || layout == null) return;

            layout.preferredHeight = rows * grid.cellSize.y + (rows - 1) * grid.spacing.y
                                     + grid.padding.top + grid.padding.bottom;
        }

        /// <summary>Only months within the campaign can be browsed.</summary>
        private void UpdateNavigationButtons()
        {
            if (timeManager == null) return;

            DateTime first = timeManager.StartDate;
            DateTime last = timeManager.DayToDate(timeManager.CampaignLength);
            var minMonth = new DateTime(first.Year, first.Month, 1);
            var maxMonth = new DateTime(last.Year, last.Month, 1);

            if (previousMonthButton != null) previousMonthButton.interactable = displayedMonth > minMonth;
            if (nextMonthButton != null) nextMonthButton.interactable = displayedMonth < maxMonth;
        }

        private void UpdateSelectedDateTexts()
        {
            if (selectedDateText != null)
                selectedDateText.text = $"Events on {TimeManager.FormatLong(SelectedDate)}";

            if (viewAllEventsText != null)
                viewAllEventsText.text = $"View all events on {TimeManager.FormatDayMonth(SelectedDate)}  >";
        }
    }
}