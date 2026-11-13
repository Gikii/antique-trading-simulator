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
    /// Refreshes itself when the week changes (e.g. "In 5 weeks" → "In 4 weeks").
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
            if (timeManager != null) timeManager.OnWeekChanged += HandleWeekChanged;
        }

        private void OnDisable()
        {
            if (timeManager != null) timeManager.OnWeekChanged -= HandleWeekChanged;
        }

        private void HandleWeekChanged(int week) => Refresh();

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
            int thisWeek = timeManager != null ? timeManager.CurrentWeek : news.WeekPublished;

            SetText(titleText, NewsPresentation.GetTitle(news));
            SetType(NewsPresentation.TypeLabel(news.Type), NewsPresentation.TypeColor(news.Type));
            SetText(descriptionText, NewsPresentation.GetNewsDescription(news));

            SetRowValue(expectedDateRow,
                $"{NewsPresentation.RelativeWeeks(news.EventTriggerWeek - thisWeek)}\n" +
                NewsPresentation.FormatWeek(news.EventTriggerWeek, timeManager));

            // Rumours may reveal only some effects – the rest stays "Unknown".
            FillTargets(news.NewsData, UnknownValue);

            SetText(noteText, NewsPresentation.GetNote(news.Type));
        }

        private void RenderEvent(ActiveEvent activeEvent)
        {
            EventDefinition definition = activeEvent.Definition;
            List<NewsEventData> effects = BuildEventEffects(definition);

            int thisWeek = timeManager != null ? timeManager.CurrentWeek : activeEvent.StartWeek;
            int lastWeek = activeEvent.EndWeek - 1; // EndWeek is exclusive

            SetText(titleText, definition != null ? definition.DisplayName : "Unknown event");
            SetType("Event", NewsPresentation.EventColor);
            SetText(descriptionText, NewsPresentation.GetEventDescription(definition, effects));

            if (timeRemainingRow != null)
            {
                SetRowLabel(timeRemainingRow, NewsPresentation.WeeksLeft(activeEvent.EndWeek - thisWeek));
                SetRowValue(timeRemainingRow, FormatRange(activeEvent.StartWeek, lastWeek));
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

        /// <summary>"12 May – 1 June 1884": from the Monday of the first week to the Sunday of the last one.</summary>
        private string FormatRange(int firstWeek, int lastWeek)
        {
            if (timeManager == null) return $"Week {firstWeek} – {lastWeek}";

            return $"{TimeManager.FormatDayMonth(timeManager.WeekToDate(firstWeek))} – " +
                   TimeManager.FormatLong(timeManager.WeekToDate(lastWeek).AddDays(TimeManager.DaysPerWeek - 1));
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