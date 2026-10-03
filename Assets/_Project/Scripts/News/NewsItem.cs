using AntiqueTradingSimulator.Events;
using System.Collections.Generic;

namespace AntiqueTradingSimulator.News
{

    public class NewsItem
    {
        public List<NewsEventData> NewsData;
        public string OfficialName { get; }
        public NewsType Type { get; }
        public float Credibility { get; }
        public int DayPublished { get; }
        public int EventTriggerDay { get; }
        public InfoAccessLevel RequiredAccessLevel { get; }
        public bool EventStarted(int currentDay) => currentDay >= EventTriggerDay;

        public NewsItem(List<NewsEventData> newsData, string officialName, NewsType type, float credibility, int dayPublished, InfoAccessLevel requiredAccessLevel, int eventTriggerDay)
        {
            NewsData = newsData;
            OfficialName = officialName;
            Type = type;
            Credibility = credibility;
            DayPublished = dayPublished;
            RequiredAccessLevel = requiredAccessLevel;
            EventTriggerDay = eventTriggerDay;
        }
    }
}