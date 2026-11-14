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
    /// - lists the news and events of the selected day below the calendar (click = details),
    /// - "View all …" asks the news list to show the whole selected week (see NewsEventsView).
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
        [Tooltip("Opens the selected week in the news list. Gets a Button at runtime if it has none.")]
        [SerializeField] private Button viewAllEventsButton;
        [SerializeField] private TMP_Text viewAllEventsText;

        [Header("Cell colors")]
        [SerializeField] private Color cellNormal = new Color32(0x47, 0x47, 0x47, 0xFF);
        [SerializeField] private Color cellToday = new Color32(0x6B, 0x5E, 0x40, 0xFF);
        [SerializeField] private Color cellSelected = new Color32(0x5C, 0x5C, 0x5C, 0xFF);
        [Tooltip("The other days of the current game week (today itself uses Cell Today).")]
        [SerializeField] private Color cellCurrentWeek = new Color(0.14f, 0.11f, 0.066f, 1f);

        [Header("Day number colors")]
        [SerializeField] private Color textCurrentMonth = new Color32(0xEB, 0xEB, 0xEB, 0xFF);
        [SerializeField] private Color textOtherMonth = new Color32(0x94, 0x94, 0x94, 0xFF);

        [Header("Today and current week (independent of the cell background, which the sprite darkens)")]
        [Tooltip("Number of today's cell (bold).")]
        [SerializeField] private Color textToday = new Color32(0xC9, 0x9A, 0x3C, 0xFF);
        [Tooltip("Numbers of the other days of the current game week.")]
        [SerializeField] private Color textCurrentWeek = new Color32(0xFF, 0xF1, 0xD0, 0xFF);
        [Tooltip("Frame drawn around today's cell.")]
        [SerializeField] private Color todayOutline = new Color32(0xC9, 0x9A, 0x3C, 0xFF);
        [SerializeField] private Vector2 todayOutlineDistance = new Vector2(2f, -2f);

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

        /// <summary>Raised by the "View all …" button with the selected date.</summary>
        public event Action<DateTime> ViewAllRequested;

        /// <summary>Raised when an entry in the list under the calendar is clicked.</summary>
        public event Action<WeekEntry> EntryClicked;

        public DateTime Today { get; private set; }
        public DateTime SelectedDate { get; private set; }

        // ------------------------------------------------------------------
        // Internal types
        // ------------------------------------------------------------------

        private class Cell
        {
            public GameObject Root;
            public Image Background;
            public TMP_Text Number;
            public Outline TodayFrame;
            public FontStyles NumberStyle;   // as authored in the scene; today/selected add to it
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
        private readonly List<WeekEntry> entryBuffer = new List<WeekEntry>();
        private Color viewAllTextColor;

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
            if (timeManager != null)
            {
                timeManager.OnWeekChanged += HandleWeekChanged;
                timeManager.OnTick += HandleTick;
            }
            if (newsManager != null) newsManager.OnNewsPublished += HandleNewsPublished;
            if (eventManager != null)
            {
                eventManager.OnEventTriggered += HandleEventChanged;
                eventManager.OnEventEnded += HandleEventChanged;
            }
        }

        private void Unsubscribe()
        {
            if (timeManager != null)
            {
                timeManager.OnWeekChanged -= HandleWeekChanged;
                timeManager.OnTick -= HandleTick;
            }
            if (newsManager != null) newsManager.OnNewsPublished -= HandleNewsPublished;
            if (eventManager != null)
            {
                eventManager.OnEventTriggered -= HandleEventChanged;
                eventManager.OnEventEnded -= HandleEventChanged;
            }
        }

        private void HandleWeekChanged(int week) => FollowToday();

        // The week only changes on OnWeekChanged, but the calendar day moves on with the ticks.
        private void HandleTick(int week, int tick)
        {
            if (timeManager.CurrentDate != Today) FollowToday();
        }

        private void FollowToday()
        {
            bool wasViewingToday = SelectedDate == Today;
            Today = timeManager.CurrentDate;

            // Follow the new day only if the player was looking at "today"; don't steal their selection.
            if (wasViewingToday)
            {
                SelectedDate = Today;
                displayedMonth = new DateTime(Today.Year, Today.Month, 1);
                DateSelected?.Invoke(SelectedDate);
            }

            // "Events this week" vs "in the week of …" depends on the current week, so refresh it either way.
            UpdateSelectedDateTexts();
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
            SetupViewAllButton();

            if (previousMonthButton != null) previousMonthButton.onClick.AddListener(ShowPreviousMonth);
            if (nextMonthButton != null) nextMonthButton.onClick.AddListener(ShowNextMonth);
        }

        /// <summary>The mockup's ViewAllEventsButton is only an Image + Text; give it a Button if it has none.</summary>
        private void SetupViewAllButton()
        {
            if (viewAllEventsButton == null)
            {
                var buttonTransform = transform.Find("ViewAllEventsButton");
                if (buttonTransform != null)
                {
                    viewAllEventsButton = buttonTransform.GetComponent<Button>();
                    if (viewAllEventsButton == null)
                    {
                        viewAllEventsButton = buttonTransform.gameObject.AddComponent<Button>();
                        viewAllEventsButton.targetGraphic = buttonTransform.GetComponent<Image>();
                    }
                }
            }

            if (viewAllEventsText != null) viewAllTextColor = viewAllEventsText.color;

            if (viewAllEventsButton != null)
            {
                MakeClickable(viewAllEventsButton);
                viewAllEventsButton.onClick.AddListener(() => ViewAllRequested?.Invoke(SelectedDate));
            }
        }

        /// <summary>
        /// The calendar mockup was built as display-only: its Images have Raycast Target off,
        /// so a Button on them never receives clicks. Turn it on for the button's graphic.
        /// </summary>
        private static void MakeClickable(Button button)
        {
            if (button.targetGraphic == null)
                button.targetGraphic = button.GetComponent<Graphic>();

            if (button.targetGraphic != null)
                button.targetGraphic.raycastTarget = true;
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

                if (cell.Number != null) cell.NumberStyle = cell.Number.fontStyle;

                // Today's frame: an Outline on the cell's own Image, switched on for today only.
                if (cell.Background != null)
                {
                    cell.TodayFrame = child.GetComponent<Outline>();
                    if (cell.TodayFrame == null) cell.TodayFrame = child.gameObject.AddComponent<Outline>();
                    cell.TodayFrame.enabled = false;
                }

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
                text.text = "No events this week";
                text.alignment = TextAlignmentOptions.Center;
                text.fontStyle = FontStyles.Normal;
                text.color = textOtherMonth;

                var layout = emptyLabel.GetComponent<LayoutElement>();
                if (layout == null) layout = emptyLabel.AddComponent<LayoutElement>();
                layout.preferredHeight = 46f;

                emptyLabel.SetActive(false);
            }

            // Entries open their details; the mockup item has no Button, so the template gets one
            // (copies inherit it). Added after the empty label copy, which must not be clickable.
            var itemButton = itemTemplate.GetComponent<Button>();
            if (itemButton == null)
            {
                itemButton = itemTemplate.AddComponent<Button>();
                itemButton.targetGraphic = itemTemplate.GetComponent<Image>();
            }
            MakeClickable(itemButton);

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

        /// <summary>Everything the player knows about a game week (see <see cref="WeekEntries.Collect"/>).</summary>
        private void GetEntries(int week, List<WeekEntry> result)
        {
            WeekEntries.Collect(week, eventManager, newsManager, timeManager != null ? timeManager.CurrentWeek : week, result);
        }

        private Color ColorOf(WeekEntryKind kind) => kind switch
        {
            WeekEntryKind.Official => officialColor,
            WeekEntryKind.Rumour => rumourColor,
            WeekEntryKind.Leaked => leakedColor,
            _ => eventColor
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

                bool isToday = cell.Date == Today;
                bool isSelected = cell.Date == SelectedDate;
                bool inCurrentWeek = timeManager != null && timeManager.DateToWeek(cell.Date) == timeManager.CurrentWeek;

                if (cell.Number != null)
                {
                    cell.Number.text = cell.Date.Day.ToString();
                    cell.Number.color = isToday ? textToday
                        : inCurrentWeek ? textCurrentWeek
                        : inMonth ? textCurrentMonth
                        : textOtherMonth;

                    var style = cell.NumberStyle;
                    if (isToday) style |= FontStyles.Bold;
                    if (isSelected && !isToday) style |= FontStyles.Underline;
                    cell.Number.fontStyle = style;
                }

                if (cell.TodayFrame != null)
                {
                    cell.TodayFrame.enabled = isToday;
                    cell.TodayFrame.effectColor = todayOutline;
                    cell.TodayFrame.effectDistance = todayOutlineDistance;
                }

                if (cell.Background != null)
                {
                    if (isToday) cell.Background.color = cellToday;
                    else if (isSelected) cell.Background.color = cellSelected;
                    else if (inCurrentWeek) cell.Background.color = cellCurrentWeek;
                    else cell.Background.color = cellNormal;
                }

                if (timeManager != null)
                {
                    GetEntries(timeManager.DateToWeek(cell.Date), entryBuffer);
                    RenderMarkers(cell, entryBuffer);
                }
            }

            if (hideUnusedLastRow)
                UpdateGridHeight(rowsNeeded);

            UpdateNavigationButtons();
        }

        /// <summary>One dot per category present on that day (Official, Rumour, Leaked, Event).</summary>
        private void RenderMarkers(Cell cell, List<WeekEntry> entries)
        {
            if (cell.Markers == null) return;

            int used = 0;
            for (int k = 0; k <= (int)WeekEntryKind.Event; k++)
            {
                var kind = (WeekEntryKind)k;
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

            GetEntries(timeManager.DateToWeek(SelectedDate), entryBuffer);

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

            UpdateViewAllButton(entryBuffer.Count, shown);
        }

        /// <summary>
        /// "View all 5 in the week of 3 March  >" when the list above is cut, "Open the week of …" when it
        /// shows everything. Disabled for empty weeks and for future weeks (nothing is known about them yet).
        /// </summary>
        private void UpdateViewAllButton(int total, int shown)
        {
            int week = timeManager.DateToWeek(SelectedDate);
            string weekStart = TimeManager.FormatDayMonth(timeManager.WeekToDate(week));

            bool future = week > timeManager.CurrentWeek;
            bool available = !future && total > 0;

            if (viewAllEventsButton != null) viewAllEventsButton.interactable = available;

            if (viewAllEventsText == null) return;

            viewAllEventsText.text = future ? "Nothing is known about this week yet"
                : total == 0 ? $"Nothing happened in the week of {weekStart}"
                : total > shown ? $"View all {total} in the week of {weekStart}  >"
                : $"Open the week of {weekStart}  >";

            // The Button only tints its Image; dim the label too so the disabled state reads clearly.
            var color = viewAllTextColor;
            if (!available) color.a *= 0.45f;
            viewAllEventsText.color = color;
        }

        private void FillItem(Transform item, WeekEntry entry)
        {
            var button = item.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(() => EntryClicked?.Invoke(entry));
            }

            var title = item.Find("MainInfo/TitleText");
            if (title != null) title.GetComponent<TMP_Text>().text = entry.Title;

            var type = item.Find("MainInfo/TypeText");
            if (type != null)
            {
                var text = type.GetComponent<TMP_Text>();
                text.text = WeekEntries.LabelOf(entry.Kind);
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
            DateTime last = timeManager.WeekToDate(timeManager.CampaignLength);
            var minMonth = new DateTime(first.Year, first.Month, 1);
            var maxMonth = new DateTime(last.Year, last.Month, 1);

            if (previousMonthButton != null) previousMonthButton.interactable = displayedMonth > minMonth;
            if (nextMonthButton != null) nextMonthButton.interactable = displayedMonth < maxMonth;
        }

        private void UpdateSelectedDateTexts()
        {
            // News and events belong to a whole week, so a day shows its week's entries.
            DateTime weekStart = timeManager != null
                ? timeManager.WeekToDate(timeManager.DateToWeek(SelectedDate))
                : SelectedDate;

            bool thisWeek = timeManager != null && timeManager.DateToWeek(SelectedDate) == timeManager.CurrentWeek;

            if (selectedDateText != null)
                selectedDateText.text = thisWeek
                    ? $"Events this week ({TimeManager.FormatDayMonth(weekStart)} – {TimeManager.FormatDayMonth(weekStart.AddDays(TimeManager.DaysPerWeek - 1))})"
                    : $"Events in the week of {TimeManager.FormatLong(weekStart)}";

            // The "View all" label depends on the number of entries – set in RenderSelectedDayList.
        }
    }
}