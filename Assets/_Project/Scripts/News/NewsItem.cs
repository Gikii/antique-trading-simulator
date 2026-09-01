using AntiqueTradingSimulator.Events;

namespace AntiqueTradingSimulator.News
{
    /// <summary>
    /// A piece of information as it exists FOR a recipient — not the ground truth itself.
    /// Multiple NewsItems (different type/credibility) can be derived from the same GameEvent
    /// and handed to different audiences with different access levels.
    /// </summary>
    public class NewsItem
    {
        public GameEvent SourceEvent { get; }      // null for fully fabricated fake news
        public NewsType Type { get; }
        public float Credibility { get; }           // 0..1, how trustworthy it LOOKS
        public bool IsActuallyTrue { get; }          // ground truth, hidden from agents
        public int DayPublished { get; }
        public InfoAccessLevel RequiredAccessLevel { get; }
        public string AffectedAntiqueTypeId { get; } // null = whole category

        public NewsItem(GameEvent sourceEvent, NewsType type, float credibility,
            bool isActuallyTrue, int dayPublished, InfoAccessLevel requiredAccessLevel,
            string affectedAntiqueTypeId = null)
        {
            SourceEvent = sourceEvent;
            Type = type;
            Credibility = credibility;
            IsActuallyTrue = isActuallyTrue;
            DayPublished = dayPublished;
            RequiredAccessLevel = requiredAccessLevel;
            AffectedAntiqueTypeId = affectedAntiqueTypeId;
        }
    }
}