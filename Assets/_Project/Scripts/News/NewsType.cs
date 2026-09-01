namespace AntiqueTradingSimulator.News
{
    /// <summary>
    /// Classifies a NewsItem by how it reached its recipient — straight fact, unverified
    /// rumor, early leak, or deliberate misinformation. Drives how much an NPC trusts it.
    /// </summary>
    public enum NewsType { Official, Rumor, Leak, Fake }
}