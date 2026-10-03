namespace AntiqueTradingSimulator.Logistics
{
    /// <summary>
    /// How far an antique has to travel to reach its buyer. A simplified stand-in for
    /// real geography: later, when several regional markets exist (arbitrage), the zone
    /// will be derived from the (seller region, buyer region) pair instead of being
    /// stored on the listing — everything downstream of the zone stays the same.
    /// </summary>
    public enum ShippingZone
    {
        Local = 0,
        Domestic = 1,
        International = 2
    }

    /// <summary>
    /// Transport service picked by the buyer. Trades cost against delivery time and
    /// the risk of the item being damaged on the way (see TransportSettings).
    /// </summary>
    public enum TransportOption
    {
        Economy = 0,
        Standard = 1,
        Express = 2
    }

    public static class LogisticsEnumExtensions
    {
        public static string ToDisplayString(this ShippingZone zone) => zone switch
        {
            ShippingZone.Local => "Local",
            ShippingZone.Domestic => "Domestic",
            ShippingZone.International => "International",
            _ => zone.ToString()
        };

        public static string ToDisplayString(this TransportOption option) => option switch
        {
            TransportOption.Economy => "Economy",
            TransportOption.Standard => "Standard",
            TransportOption.Express => "Express (secured)",
            _ => option.ToString()
        };
    }
}
