using AntiqueTradingSimulator.Events;
using System.Collections.Generic;

namespace AntiqueTradingSimulator.News
{

    public class NewsItem
    {
        public List<NewsEventData> NewsData;
        public NewsType Type { get; }
        public float Credibility { get; }
        public int DayPublished { get; }
        public int EventTriggerDay { get; }
        public InfoAccessLevel RequiredAccessLevel { get; }

        public NewsItem(List<NewsEventData> newsData, NewsType type, float credibility, int dayPublished, InfoAccessLevel requiredAccessLevel, int eventTriggerDay)
        {
            NewsData = newsData;
            Type = type;
            Credibility = credibility;
            DayPublished = dayPublished;
            RequiredAccessLevel = requiredAccessLevel;
            EventTriggerDay = eventTriggerDay;
        }
    }
}