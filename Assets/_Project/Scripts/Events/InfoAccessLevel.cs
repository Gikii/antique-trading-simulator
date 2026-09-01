namespace AntiqueTradingSimulator.Events
{
    /// <summary>
    /// Mirrors the 5 information tiers from the design doc (local press → international
    /// informant network). Higher = access to rarer/earlier information. Both Player
    /// and NPCs carry this on their TraderAgent.
    /// </summary>
    public enum InfoAccessLevel
    {
        LocalPress = 1,
        IndustrySources = 2,
        InformantNetwork = 3,
        Expert = 4,
        InternationalNetwork = 5
    }
}