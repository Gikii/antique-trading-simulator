using AntiqueTradingSimulator.Events;
using System.Collections.Generic;

namespace AntiqueTradingSimulator.News
{
    /// <summary>
    /// A piece of information as it exists FOR a recipient — not the ground truth itself.
    /// Multiple NewsItems (different type/credibility) can be derived from the same GameEvent
    /// and handed to different audiences with different access levels.
    /// </summary>
    public class NewsItem
    {
        public List<NewsEventData> NewsData;
        public NewsType Type { get; }
        public float Credibility { get; }           // 0..1, how trustworthy it LOOKS
        public int DayPublished { get; }
        public InfoAccessLevel RequiredAccessLevel { get; }

        public NewsItem(List<NewsEventData> newsData, NewsType type, float credibility, int dayPublished, InfoAccessLevel requiredAccessLevel)
        {
            NewsData = newsData;
            Type = type;
            Credibility = credibility;
            DayPublished = dayPublished;
            RequiredAccessLevel = requiredAccessLevel;
        }
    }
}