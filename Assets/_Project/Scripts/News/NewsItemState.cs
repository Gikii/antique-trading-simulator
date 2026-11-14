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
        public int WeekPublished;
        public int EventTriggerWeek;

        public bool GuaranteedDelivery;
        public int PlayerDeliveryWeek = NewsItem.NotDelivered;

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
                WeekPublished = news.WeekPublished,
                EventTriggerWeek = news.EventTriggerWeek,
                GuaranteedDelivery = news.GuaranteedDelivery,
                PlayerDeliveryWeek = news.PlayerDeliveryWeek
            };
        }

        public NewsItem Restore() => new NewsItem(
            NewsData ?? new List<NewsEventData>(),
            OfficialName,
            EventDescription,
            Type,
            Credibility,
            WeekPublished,
            EventTriggerWeek,
            GuaranteedDelivery,
            PlayerDeliveryWeek);
    }
}
