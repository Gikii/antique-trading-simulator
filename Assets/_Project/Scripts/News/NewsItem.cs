using AntiqueTradingSimulator.Events;
using System.Collections.Generic;

namespace AntiqueTradingSimulator.News
{

    public class NewsItem
    {
        public string Id { get; }

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

        /// <summary>Trader id of whoever planted this fake rumour; null for genuine news. Not read by NPC decision logic.</summary>
        public string PlantedBy { get; set; }

        /// <summary>Fake rumour whose claimed date has passed.</summary>
        public bool Debunked { get; set; }

        /// <summary>The player's experts flagged this rumour as fabricated (Experts upgrade).</summary>
        public bool PlayerSuspectsFake { get; set; }

        /// <summary>Set on a debunk notice: Id of the rumour it debunks.</summary>
        public string DebunksNewsId { get; set; }

        public bool PlantedByPlayer => PlantedBy == Market.Antique.PlayerOwnerId;

        public bool IsDebunkNotice => !string.IsNullOrEmpty(DebunksNewsId);

        public bool EventStarted(int currentWeek) => currentWeek >= EventTriggerWeek;

        public bool PlayerKnows(int currentWeek) =>
            PlayerDeliveryWeek != NotDelivered && currentWeek >= PlayerDeliveryWeek;

        public NewsItem(List<NewsEventData> newsData, string officialName, string eventDescription, NewsType type, float credibility, int weekPublished, int eventTriggerWeek, bool guaranteedDelivery = false, int playerDeliveryWeek = NotDelivered, string id = null)
        {
            Id = string.IsNullOrEmpty(id) ? Core.IdFactory.NewId() : id;
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
