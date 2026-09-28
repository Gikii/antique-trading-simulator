using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using AntiqueTradingSimulator.News;

namespace AntiqueTradingSimulator.UI
{
    public class NewsListItemUI : MonoBehaviour
    {
        private Button button;

        private TMP_Text titleText;
        private TMP_Text previewText;
        private TMP_Text typeText;
        private TMP_Text dateText;

        private NewsItem news;

        public NewsItem News => news;

        private void Awake()
        {
            CacheReferences();
        }

        private void CacheReferences()
        {
            button = GetComponent<Button>();

            titleText = transform
                .Find("MainInfo/TitleText")
                ?.GetComponent<TMP_Text>();

            previewText = transform
                .Find("MainInfo/PreviewText")
                ?.GetComponent<TMP_Text>();

            typeText = transform
                .Find("TypeInfo/TypeRow/TypeText")
                ?.GetComponent<TMP_Text>();

            dateText = transform
                .Find("TypeInfo/DateText")
                ?.GetComponent<TMP_Text>();

            if (button == null)
                Debug.LogError("NewsListItemUI: Button not found.", this);

            if (titleText == null)
                Debug.LogError("NewsListItemUI: TitleText not found.", this);

            if (previewText == null)
                Debug.LogError("NewsListItemUI: PreviewText not found.", this);

            if (typeText == null)
                Debug.LogError("NewsListItemUI: TypeText not found.", this);

            if (dateText == null)
                Debug.LogError("NewsListItemUI: DateText not found.", this);
        }

        /// <param name="timeManager">Optional. When given, the date is shown as "14 May 1884" instead of "Day 23".</param>
        public void Setup(
            NewsItem newsItem,
            Action<NewsItem> onClicked,
            Core.TimeManager timeManager = null)
        {
            news = newsItem;

            if (button == null)
                CacheReferences();

            // All wording comes from NewsPresentation, so the calendar and details panel show the same texts.
            titleText.text = NewsPresentation.GetTitle(news);
            previewText.text = NewsPresentation.GetPreview(news);
            typeText.text = NewsPresentation.TypeLabel(news.Type);
            dateText.text = NewsPresentation.FormatDay(news.DayPublished, timeManager);

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                onClicked?.Invoke(news);
            });
        }
    }
}