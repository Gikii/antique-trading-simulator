using System.Collections.Generic;
using AntiqueTradingSimulator.Events;

namespace AntiqueTradingSimulator.News
{

    public class NewsItemState
    {
        public List<NewsEventData> NewsData = new List<NewsEventData>();

        public string OfficialName;
        public string EventDescription;
        public NewsType Type;
        public float Credibility;
        public int DayPublished;
        public InfoAccessLevel RequiredAccessLevel;
        public int EventTriggerDay;

        public static NewsItemState Capture(NewsItem news)
        {
            if (news == null) return null;

            return new NewsItemState
            {
                NewsData = news.NewsData != null ? new List<NewsEventData>(news.NewsData) : new List<NewsEventData>(),
                OfficialName = news.OfficialName,
                EventDescription = news.EventDescription,
                Type = news.Type,
                Credibility = news.Credibility,
                DayPublished = news.DayPublished,
                RequiredAccessLevel = news.RequiredAccessLevel,
                EventTriggerDay = news.EventTriggerDay
            };
        }

        public NewsItem Restore() => new NewsItem(
            NewsData ?? new List<NewsEventData>(),
            OfficialName,
            EventDescription,
            Type,
            Credibility,
            DayPublished,
            RequiredAccessLevel,
            EventTriggerDay);
    }
}
