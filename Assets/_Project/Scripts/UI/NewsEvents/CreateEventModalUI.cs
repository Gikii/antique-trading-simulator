using System;
using System.Collections.Generic;
using System.Linq;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Events;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// "Create Event" modal: pick an event template, see its details, choose in how many days
    /// it starts and schedule it through EventManager (news are then generated as for any other event).
    /// The hierarchy is built by Tools > UI > News & Events > Build Create Event Modal.
    /// Children are found by name.
    /// </summary>
    public class CreateEventModalUI : MonoBehaviour
    {
        [Header("Game systems (auto-found if empty)")]
        [SerializeField] private TimeManager timeManager;
        [SerializeField] private EventManager eventManager;

        [Header("Colors")]
        [SerializeField] private Color itemNormal = new Color32(0x40, 0x40, 0x40, 0xFF);
        [SerializeField] private Color itemSelected = new Color32(0x6B, 0x5E, 0x40, 0xFF);
        [SerializeField] private Color hintColor = new Color32(0x9E, 0x9E, 0x9E, 0xFF);
        [SerializeField] private Color errorColor = new Color32(0xD1, 0x4C, 0x4C, 0xFF);

        [Header("Options")]
        [Tooltip("Pause the game clock while the modal is open.")]
        [SerializeField] private bool pauseTimeWhileOpen = true;

        [Tooltip("How many templates fit in the list without scrolling (6 with the default layout).")]
        [SerializeField] private int maxVisibleTemplates = 6;

        /// <summary>Raised after an event was scheduled: definition, trigger day.</summary>
        public event Action<EventDefinition, int> EventCreated;

        private const string NoValue = "—";

        // References
        private Transform templateList;
        private GameObject itemTemplate;
        private TMP_Text selectedNameText;
        private TMP_Text selectedDescriptionText;
        private TMP_Text categoryValue, regionValue, periodValue, durationValue, newsValue;
        private TMP_Text leadText, dateText, hintText;
        private Button closeButton, cancelButton, createButton, decreaseButton, increaseButton;

        // State
        private readonly List<(EventDefinition definition, GameObject item)> items =
            new List<(EventDefinition, GameObject)>();

        private EventDefinition selected;
        private int leadDays;
        private bool initialized;
        private bool pausedByModal;

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        public void Open()
        {
            EnsureInitialized();

            gameObject.SetActive(true);
            transform.SetAsLastSibling(); // draw above everything else

            if (pauseTimeWhileOpen && timeManager != null && timeManager.IsRunning)
            {
                timeManager.Pause();
                pausedByModal = true;
            }

            BuildTemplateList();
            Select(items.Count > 0 ? items[0].definition : null);
        }

        public void Close() => gameObject.SetActive(false);

        // ------------------------------------------------------------------
        // Unity
        // ------------------------------------------------------------------

        private void OnDisable()
        {
            // Resume even if something else hides the modal.
            if (pausedByModal && timeManager != null)
                timeManager.Resume();

            pausedByModal = false;
        }

        // ------------------------------------------------------------------
        // Setup
        // ------------------------------------------------------------------

        private void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;

            if (timeManager == null) timeManager = FindAnyObjectByType<TimeManager>();
            if (eventManager == null) eventManager = FindAnyObjectByType<EventManager>();

            const string details = "Window/Body/DetailsColumn/";

            closeButton = FindComponent<Button>("Window/Header/CloseButton");
            cancelButton = FindComponent<Button>("Window/Footer/CancelButton");
            createButton = FindComponent<Button>("Window/Footer/CreateButton");

            templateList = transform.Find("Window/Body/TemplatesColumn/TemplateList");
            if (templateList != null && templateList.childCount > 0)
            {
                itemTemplate = templateList.GetChild(0).gameObject;
                itemTemplate.SetActive(false);
            }
            else
            {
                Debug.LogError("[CreateEventModalUI] TemplateList or its template item not found.", this);
            }

            selectedNameText = FindComponent<TMP_Text>(details + "SelectedTemplate/Info/NameText");
            selectedDescriptionText = FindComponent<TMP_Text>(details + "SelectedTemplate/Info/DescriptionText");

            categoryValue = FindComponent<TMP_Text>(details + "DetailRows/TargetCategory/Value");
            regionValue = FindComponent<TMP_Text>(details + "DetailRows/TargetRegion/Value");
            periodValue = FindComponent<TMP_Text>(details + "DetailRows/TargetPeriod/Value");
            durationValue = FindComponent<TMP_Text>(details + "DetailRows/Duration/Value");
            newsValue = FindComponent<TMP_Text>(details + "DetailRows/GeneratedNews/Value");

            decreaseButton = FindComponent<Button>(details + "ScheduleRow/LeadStepper/DecreaseButton");
            increaseButton = FindComponent<Button>(details + "ScheduleRow/LeadStepper/IncreaseButton");
            leadText = FindComponent<TMP_Text>(details + "ScheduleRow/LeadStepper/LeadText");
            dateText = FindComponent<TMP_Text>(details + "ScheduleRow/DateText");
            hintText = FindComponent<TMP_Text>(details + "ScheduleHint");

            AddListener(closeButton, Close);
            AddListener(cancelButton, Close);
            AddListener(createButton, CreateSelectedEvent);
            AddListener(decreaseButton, () => ChangeLeadDays(-1));
            AddListener(increaseButton, () => ChangeLeadDays(+1));
        }

        private T FindComponent<T>(string path) where T : Component
        {
            var t = transform.Find(path);
            if (t == null)
            {
                Debug.LogWarning($"[CreateEventModalUI] '{path}' not found.", this);
                return null;
            }
            return t.GetComponent<T>();
        }

        private static void AddListener(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null) button.onClick.AddListener(action);
        }

        // ------------------------------------------------------------------
        // Template list
        // ------------------------------------------------------------------

        private void BuildTemplateList()
        {
            foreach (var entry in items)
            {
                entry.item.SetActive(false);
                Destroy(entry.item);
            }
            items.Clear();

            if (itemTemplate == null) return;

            List<EventDefinition> definitions = EventDatabase.GetAll();
            int count = Mathf.Min(definitions.Count, maxVisibleTemplates);

            if (definitions.Count > maxVisibleTemplates)
                Debug.LogWarning($"[CreateEventModalUI] {definitions.Count} templates, only {maxVisibleTemplates} fit. " +
                                 "Add paging/categories when the list grows.", this);

            for (int i = 0; i < count; i++)
            {
                EventDefinition definition = definitions[i];
                if (definition == null) continue;

                GameObject item = Instantiate(itemTemplate, templateList);
                item.name = $"Template_{definition.DisplayName}";
                item.SetActive(true);

                SetChildText(item.transform, "Info/NameText", definition.DisplayName);
                SetChildText(item.transform, "Info/DescriptionText", GetDescription(definition));

                var button = item.GetComponent<Button>();
                if (button != null) button.onClick.AddListener(() => Select(definition));

                items.Add((definition, item));
            }
        }

        private void Select(EventDefinition definition)
        {
            selected = definition;

            foreach (var entry in items)
            {
                var background = entry.item.GetComponent<Image>();
                if (background != null)
                    background.color = entry.definition == selected ? itemSelected : itemNormal;
            }

            FillDetails();

            if (selected != null)
                leadDays = MinLead(selected); // start from the earliest possible day

            UpdateSchedule();
        }

        // ------------------------------------------------------------------
        // Details
        // ------------------------------------------------------------------

        private void FillDetails()
        {
            if (selected == null)
            {
                SetText(selectedNameText, "No event templates");
                SetText(selectedDescriptionText, "There are no event definitions in the database.");
                SetText(categoryValue, NoValue);
                SetText(regionValue, NoValue);
                SetText(periodValue, NoValue);
                SetText(durationValue, NoValue);
                SetText(newsValue, NoValue);
                return;
            }

            var effects = NewsPresentation.BuildEventEffects(selected);

            SetText(selectedNameText, selected.DisplayName);
            SetText(selectedDescriptionText, GetDescription(selected));

            SetText(categoryValue, NewsPresentation.JoinTargets(effects, EventEffect.TargetScope.AntiqueType, NoValue));
            SetText(regionValue, NewsPresentation.JoinTargets(effects, EventEffect.TargetScope.Country, NoValue));
            SetText(periodValue, NewsPresentation.JoinTargets(effects, EventEffect.TargetScope.Century, NoValue));
            SetText(durationValue, NewsPresentation.DaysLabel(Mathf.Max(1, selected.DurationDays)));
            SetText(newsValue, NewsPresentation.GeneratedNewsLabel(selected));
        }

        /// <summary>Description from the definition, or a generated one if it's empty.</summary>
        private static string GetDescription(EventDefinition definition)
        {
            return string.IsNullOrWhiteSpace(definition.Description)
                ? NewsPresentation.DescribeEffects(NewsPresentation.BuildEventEffects(definition))
                : definition.Description;
        }

        // ------------------------------------------------------------------
        // Schedule
        // ------------------------------------------------------------------

        private static int MinLead(EventDefinition definition) => Mathf.Max(1, definition.MinLeadDays);

        private static int MaxLead(EventDefinition definition) => Mathf.Max(MinLead(definition), definition.MaxLeadDays);

        private void ChangeLeadDays(int delta)
        {
            if (selected == null) return;

            leadDays = Mathf.Clamp(leadDays + delta, MinLead(selected), MaxLead(selected));
            UpdateSchedule();
        }

        private void UpdateSchedule()
        {
            if (selected == null || timeManager == null)
            {
                SetText(leadText, NoValue);
                SetText(dateText, "");
                SetHint("", false);
                SetInteractable(decreaseButton, false);
                SetInteractable(increaseButton, false);
                SetInteractable(createButton, false);
                return;
            }

            int min = MinLead(selected);
            int max = MaxLead(selected);
            int triggerDay = timeManager.CurrentDay + leadDays;

            SetText(leadText, NewsPresentation.DaysLabel(leadDays));
            SetText(dateText, TimeManager.FormatLong(timeManager.DayToDate(triggerDay)));

            SetInteractable(decreaseButton, leadDays > min);
            SetInteractable(increaseButton, leadDays < max);

            bool afterCampaign = triggerDay > timeManager.CampaignLength;
            bool dayTaken = IsDayTaken(triggerDay);

            if (afterCampaign)
                SetHint("This date is after the end of the campaign.", true);
            else if (dayTaken)
                SetHint("This day is not available. Choose another day.", true);
            else
                SetHint($"This event can be scheduled between {NewsPresentation.DaysLabel(min)} " +
                        $"and {NewsPresentation.DaysLabel(max)} from today.", false);

            SetInteractable(createButton, !afterCampaign && !dayTaken);
        }

        /// <summary>EventManager allows only one scheduled event per day.</summary>
        private bool IsDayTaken(int day) =>
            eventManager != null && eventManager.ScheduledEvents.Any(s => s.TriggerDay == day);

        private void CreateSelectedEvent()
        {
            if (selected == null || eventManager == null || timeManager == null) return;

            int triggerDay = timeManager.CurrentDay + leadDays;

            if (!eventManager.ScheduleEvent(selected, triggerDay))
            {
                SetHint("The event could not be scheduled. Choose another day.", true);
                return;
            }

            EventCreated?.Invoke(selected, triggerDay);
            Close();
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private void SetHint(string text, bool isError)
        {
            if (hintText == null) return;
            hintText.text = text;
            hintText.color = isError ? errorColor : hintColor;
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null) text.text = value;
        }

        private static void SetChildText(Transform root, string path, string value)
        {
            var t = root.Find(path);
            if (t != null) SetText(t.GetComponent<TMP_Text>(), value);
        }

        private static void SetInteractable(Button button, bool value)
        {
            if (button != null) button.interactable = value;
        }
    }
}