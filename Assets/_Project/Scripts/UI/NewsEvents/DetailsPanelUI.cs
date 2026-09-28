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
    /// Right-side details panel (NewsDetails / EventDetails).
    /// Fills the texts of the clicked news item or active event.
    /// Children are found by name, so the same component works for both panels.
    /// Refreshes itself when the day changes (e.g. "In 5 days" → "In 4 days").
    /// </summary>
    public class DetailsPanelUI : MonoBehaviour
    {
        private const string UnknownValue = "Unknown";
        private const string NoValue = "—";

        private TMP_Text titleText;
        private TMP_Text typeText;
        private Image typeDot;
        private TMP_Text descriptionText;
        private TMP_Text noteText;

        // Detail rows (each has "Label" and "Value" children). A row may be missing in one of the panels.
        private Transform expectedDateRow;   // NewsDetails
        private Transform timeRemainingRow;  // EventDetails
        private Transform categoryRow;
        private Transform regionRow;
        private Transform periodRow;

        private TimeManager timeManager;
        private bool cached;

        private NewsItem currentNews;
        private ActiveEvent currentEvent;

        // ------------------------------------------------------------------
        // Unity
        // ------------------------------------------------------------------

        private void Awake() => Cache();

        private void OnEnable()
        {
            Cache();
            if (timeManager != null) timeManager.OnDayChanged += HandleDayChanged;
        }

        private void OnDisable()
        {
            if (timeManager != null) timeManager.OnDayChanged -= HandleDayChanged;
        }

        private void HandleDayChanged(int day) => Refresh();

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        public void ShowNews(NewsItem news)
        {
            currentNews = news;
            currentEvent = null;
            Refresh();
        }

        public void ShowEvent(ActiveEvent activeEvent)
        {
            currentEvent = activeEvent;
            currentNews = null;
            Refresh();
        }

        // ------------------------------------------------------------------
        // Rendering
        // ------------------------------------------------------------------

        private void Refresh()
        {
            Cache();

            if (currentNews != null) RenderNews(currentNews);
            else if (currentEvent != null) RenderEvent(currentEvent);
        }

        private void RenderNews(NewsItem news)
        {
            int today = timeManager != null ? timeManager.CurrentDay : news.DayPublished;

            SetText(titleText, NewsPresentation.GetTitle(news));
            SetType(NewsPresentation.TypeLabel(news.Type), NewsPresentation.TypeColor(news.Type));
            SetText(descriptionText, NewsPresentation.GetDescription(news));

            SetRowValue(expectedDateRow,
                $"{NewsPresentation.RelativeDays(news.EventTriggerDay - today)}\n" +
                NewsPresentation.FormatDay(news.EventTriggerDay, timeManager));

            // Rumours may reveal only some effects – the rest stays "Unknown".
            FillTargets(news.NewsData, UnknownValue);

            SetText(noteText, NewsPresentation.GetNote(news.Type));
        }

        private void RenderEvent(ActiveEvent activeEvent)
        {
            EventDefinition definition = activeEvent.Definition;
            string eventName = definition != null ? definition.DisplayName : "Unknown event";
            List<NewsEventData> effects = BuildEventEffects(definition);

            int today = timeManager != null ? timeManager.CurrentDay : activeEvent.StartDay;
            int lastDay = activeEvent.EndDay - 1; // EndDay is exclusive

            SetText(titleText, eventName);
            SetType("Event", NewsPresentation.EventColor);
            SetText(descriptionText, NewsPresentation.GetEventDescription(eventName, effects));

            if (timeRemainingRow != null)
            {
                SetRowLabel(timeRemainingRow, NewsPresentation.DaysLeft(activeEvent.EndDay - today));
                SetRowValue(timeRemainingRow, FormatRange(activeEvent.StartDay, lastDay));
            }

            // Events are fully known – missing targets mean "not affected".
            FillTargets(effects, NoValue);

            SetText(noteText, NewsPresentation.EventNote);
        }

        /// <summary>Puts every effect into the matching row: antique type → category, country → region, century → period.</summary>
        private void FillTargets(IReadOnlyList<NewsEventData> effects, string emptyValue)
        {
            var categories = new List<string>();
            var regions = new List<string>();
            var periods = new List<string>();

            if (effects != null)
            {
                foreach (NewsEventData data in effects)
                {
                    string text = NewsPresentation.GetSubjectWithArrow(data);

                    switch (data.targetScope)
                    {
                        case EventEffect.TargetScope.AntiqueType: categories.Add(text); break;
                        case EventEffect.TargetScope.Country: regions.Add(text); break;
                        case EventEffect.TargetScope.Century: periods.Add(text); break;
                    }
                }
            }

            SetRowValue(categoryRow, categories.Count > 0 ? string.Join(", ", categories) : emptyValue);
            SetRowValue(regionRow, regions.Count > 0 ? string.Join(", ", regions) : emptyValue);
            SetRowValue(periodRow, periods.Count > 0 ? string.Join(", ", periods) : emptyValue);
        }

        private static List<NewsEventData> BuildEventEffects(EventDefinition definition)
        {
            var result = new List<NewsEventData>();
            if (definition == null || definition.Effects == null) return result;

            foreach (EventEffect effect in definition.Effects)
                result.Add(effect.CreateNewsData());

            return result;
        }

        /// <summary>"14 May – 19 May 1884"</summary>
        private string FormatRange(int firstDay, int lastDay)
        {
            if (timeManager == null) return $"Day {firstDay} – {lastDay}";

            return $"{TimeManager.FormatDayMonth(timeManager.DayToDate(firstDay))} – " +
                   TimeManager.FormatLong(timeManager.DayToDate(lastDay));
        }

        // ------------------------------------------------------------------
        // Setup / helpers
        // ------------------------------------------------------------------

        private void Cache()
        {
            if (cached) return;
            cached = true;

            timeManager = FindAnyObjectByType<TimeManager>();

            titleText = FindText(transform, "Header/TitleText");
            typeText = FindText(transform, "Header/Type/TypeText");
            descriptionText = FindText(transform, "DescriptionText");
            noteText = FindText(transform, "InformationNote/Text");

            var dot = transform.Find("Header/Type/Dot");
            if (dot != null) typeDot = dot.GetComponent<Image>();

            expectedDateRow = FindDeep(transform, "ExpectedEventDate");
            timeRemainingRow = FindDeep(transform, "TimeRemaining");
            categoryRow = FindDeep(transform, "TargetCategory");
            regionRow = FindDeep(transform, "TargetRegion");
            periodRow = FindDeep(transform, "TargetPeriod");
        }

        private void SetType(string label, Color color)
        {
            if (typeText != null)
            {
                typeText.text = label;
                typeText.color = color;
            }

            if (typeDot != null)
                typeDot.color = color;
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null) text.text = value;
        }

        private static void SetRowValue(Transform row, string value)
        {
            if (row != null) SetText(FindText(row, "Value"), value);
        }

        private static void SetRowLabel(Transform row, string value)
        {
            if (row != null) SetText(FindText(row, "Label"), value);
        }

        private static TMP_Text FindText(Transform root, string path)
        {
            var t = root.Find(path);
            return t != null ? t.GetComponent<TMP_Text>() : null;
        }

        /// <summary>Finds a descendant by name at any depth.</summary>
        private static Transform FindDeep(Transform root, string name)
        {
            foreach (Transform child in root)
            {
                if (child.name == name) return child;

                var found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }
}