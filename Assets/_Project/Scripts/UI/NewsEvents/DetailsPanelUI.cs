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

        /// <summary>Optional: shown only on panels that have a TargetRoute row.</summary>
        private Transform routeRow;

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

            SetText(titleText, NewsPresentation.GetTitle(news, TextBudget.Wide));
            SetType(NewsPresentation.TypeLabel(news), NewsPresentation.TypeColor(news));
            SetText(descriptionText, NewsPresentation.GetNewsDescription(news));

            SetRowValue(expectedDateRow, news.IsDebunkNotice
                ? "No event (debunked)"
                : $"{NewsPresentation.RelativeWeeks(news.EventTriggerWeek - thisWeek)}\n" +
                  NewsPresentation.FormatWeek(news.EventTriggerWeek, timeManager));

            // Rumours may reveal only some effects – the rest stays "Unknown".
            FillTargets(news.NewsData, UnknownValue);

            SetText(noteText, NewsPresentation.GetNote(news));
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

        /// <summary>Puts every effect into the matching row: antique type → category, country → region, century → period, shipping zone → route.</summary>
        private void FillTargets(IReadOnlyList<NewsEventData> effects, string emptyValue)
        {
            var summary = NewsSummary.Build(effects);

            SetRowValue(categoryRow, Row(summary.Types, emptyValue, arrows: true));
            SetRowValue(regionRow, Row(summary.Regions, emptyValue, arrows: true));
            SetRowValue(periodRow, Row(summary.Periods, emptyValue, arrows: true));

            if (routeRow != null)
            {
                routeRow.gameObject.SetActive(summary.HasRoutes);
                SetRowValue(routeRow, Row(summary.Routes, emptyValue, arrows: false));
            }
        }

        /// <summary>"clocks ↑, vases ↑" with tinted arrows, or emptyValue when nothing is named.</summary>
        private static string Row(List<NewsSummary.Token> tokens, string emptyValue, bool arrows)
        {
            if (tokens == null || tokens.Count == 0) return emptyValue;

            var builder = new System.Text.StringBuilder();

            for (int i = 0; i < tokens.Count; i++)
            {
                if (i > 0) builder.Append(", ");
                builder.Append(tokens[i].Label);
                if (arrows) builder.Append(tokens[i].Up ? " ↑" : " ↓");
            }

            return NewsPresentation.ColorizeArrows(builder.ToString());
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
            routeRow = FindDeep(transform, "TargetRoute");
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