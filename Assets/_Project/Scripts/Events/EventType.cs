namespace AntiqueTradingSimulator.Events
{
    /// <summary>
    /// Types of events available
    /// spontaneous - minor events that appear spontaneously and either affect the player directly have minor effects on the state of the market
    /// minor - daily events that affect the market. They are scheduled days ahead. News about them may appear before their trigger date
    /// major - major events occuring every few days. They have major effects on the market. News about these events may appear prior to their trigger date
    /// player - events created by the player. TBD whether npcs wil receive news about their occurence.
    /// </summary>
    public enum EventType
    {
        Spontaneous,
        Minor,
        Major,
        Player
    }
}
