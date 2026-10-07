using System.Collections.Generic;

namespace AntiqueTradingSimulator.News
{

    public class NewsState
    {
        public List<NewsItemState> PublishedNews = new List<NewsItemState>();

        public List<PendingNewsState> PendingNews = new List<PendingNewsState>();
    }


    public class PendingNewsState
    {
        public int PublishDay;
        public string EventDefinitionId;
        public int EventTriggerDay;
        public NewsType Type;
    }
}
