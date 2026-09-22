using System;
using System.Text;
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

        public void Setup(
            NewsItem newsItem,
            Action<NewsItem> onClicked)
        {
            news = newsItem;

            if (button == null)
                CacheReferences();

            titleText.text = BuildTitle(news);
            previewText.text = BuildPreview(news);
            typeText.text = FormatNewsType(news.Type);
            dateText.text = $"Day {news.DayPublished}";

            button.onClick.RemoveAllListeners();
            button.onClick.AddListener(() =>
            {
                onClicked?.Invoke(news);
            });
        }

        private static string BuildTitle(NewsItem item)
        {
            if (item.NewsData == null || item.NewsData.Count == 0)
                return "Market information";

            NewsEventData data = item.NewsData[0];

            string subject = BuildSubject(data);

            return data.affectsPriceUp
                ? $"{subject} gaining market interest"
                : $"{subject} facing market pressure";
        }

        private static string BuildPreview(NewsItem item)
        {
            if (item.NewsData == null || item.NewsData.Count == 0)
                return "No additional information available.";

            StringBuilder builder = new StringBuilder();

            for (int i = 0; i < item.NewsData.Count; i++)
            {
                NewsEventData data = item.NewsData[i];

                if (i > 0)
                    builder.Append(" • ");

                builder.Append(BuildSubject(data));

                builder.Append(
                    data.affectsPriceUp
                        ? " ↑"
                        : " ↓"
                );
            }

            return builder.ToString();
        }

        private static string BuildSubject(NewsEventData data)
        {
            switch (data.targetScope)
            {
                case Events.EventEffect.TargetScope.AntiqueType:
                    return data.AntiqueType.ToString();

                case Events.EventEffect.TargetScope.Country:
                    return data.Country.ToString();

                case Events.EventEffect.TargetScope.Century:
                    return data.Century.ToString();

                case Events.EventEffect.TargetScope.Other:
                    return "Antique market";

                default:
                    return "Antique market";
            }
        }
        private static string FormatNewsType(NewsType type)
        {
            return type switch
            {
                NewsType.Official => "Official",
                NewsType.Rumor => "Rumour",
                NewsType.Leak => "Leaked",
                _ => type.ToString()
            };
        }
    }
}