using AntiqueTradingSimulator.Events;
using System.Collections.Generic;

namespace AntiqueTradingSimulator.News
{

    public class NewsItem
    {
        public const int NotDelivered = -1;

        public List<NewsEventData> NewsData;
        public string OfficialName { get; }
        public string EventDescription { get; }
        public NewsType Type { get; }
        public float Credibility { get; }
        public int WeekPublished { get; }
        public int EventTriggerWeek { get; }
        public bool GuaranteedDelivery { get; }
        public int PlayerDeliveryWeek { get; set; } = NotDelivered;

        public bool EventStarted(int currentWeek) => currentWeek >= EventTriggerWeek;

        public bool PlayerKnows(int currentWeek) =>
            PlayerDeliveryWeek != NotDelivered && currentWeek >= PlayerDeliveryWeek;

        public NewsItem(List<NewsEventData> newsData, string officialName, string eventDescription, NewsType type, float credibility, int weekPublished, int eventTriggerWeek, bool guaranteedDelivery = false, int playerDeliveryWeek = NotDelivered)
        {
            NewsData = newsData;
            OfficialName = officialName;
            EventDescription = eventDescription;
            Type = type;
            Credibility = credibility;
            WeekPublished = weekPublished;
            EventTriggerWeek = eventTriggerWeek;
            GuaranteedDelivery = guaranteedDelivery;
            PlayerDeliveryWeek = playerDeliveryWeek;
        }
    }
}
