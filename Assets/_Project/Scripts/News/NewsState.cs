using System.Collections.Generic;

namespace AntiqueTradingSimulator.News
{

    public class NewsState
    {
        public List<NewsItemState> PublishedNews = new List<NewsItemState>();

        public List<PendingNewsState> PendingNews = new List<PendingNewsState>();

        public List<InformationChannelState> Channels = new List<InformationChannelState>();
    }


    public class PendingNewsState
    {
        public int PublishWeek;
        public string EventDefinitionId;
        public int EventTriggerWeek;
        public NewsType Type;
    }

    public class InformationChannelState
    {
        public string ReceiverId;
        public NewsType Type;
        public int MissStreak;
    }
}
