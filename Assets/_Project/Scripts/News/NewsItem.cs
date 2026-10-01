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
        public string EventDefinitionID { get; }
        public string EventInstanceId { get; }
        public bool Resolved { get; private set; }

        public NewsItem(List<NewsEventData> newsData, NewsType type, float credibility, int dayPublished, InfoAccessLevel requiredAccessLevel, int eventTriggerDay, string eventDefinitionId = null, string eventInstanceId = null)
        {
            NewsData = newsData;
            Type = type;
            Credibility = credibility;
            DayPublished = dayPublished;
            RequiredAccessLevel = requiredAccessLevel;
            EventTriggerDay = eventTriggerDay;
            EventDefinitionID = eventDefinitionId;
            EventInstanceId = eventInstanceId;
        }

        /// <summary>Marks the news as resolved. Called by NewsManager when the event starts.</summary>
        public void MarkResolved() => Resolved = true;
    }
}
