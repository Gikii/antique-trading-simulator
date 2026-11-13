using AntiqueTradingSimulator.Events;
using System.Collections.Generic;

namespace AntiqueTradingSimulator.News
{

    public class NewsItem
    {
        public List<NewsEventData> NewsData;
        public string OfficialName { get; }
        public string EventDescription { get; }
        public NewsType Type { get; }
        public float Credibility { get; }
        public int WeekPublished { get; }
        public int EventTriggerWeek { get; }
        public InfoAccessLevel RequiredAccessLevel { get; }
        public bool EventStarted(int currentWeek) => currentWeek >= EventTriggerWeek;

        public NewsItem(List<NewsEventData> newsData, string officialName, string eventDescription, NewsType type, float credibility, int weekPublished, InfoAccessLevel requiredAccessLevel, int eventTriggerWeek)
        {
            NewsData = newsData;
            OfficialName = officialName;
            EventDescription = eventDescription;
            Type = type;
            Credibility = credibility;
            WeekPublished = weekPublished;
            RequiredAccessLevel = requiredAccessLevel;
            EventTriggerWeek = eventTriggerWeek;
        }
    }
}