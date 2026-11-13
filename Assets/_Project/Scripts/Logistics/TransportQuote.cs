namespace AntiqueTradingSimulator.Logistics
{
    /// <summary>
    /// Price and timing of shipping one specific antique with one transport option,
    /// computed by TransportManager.Quote before the purchase. The same quote is then
    /// charged by TraderInventory.Buy and used by TransportManager.Dispatch, so what
    /// the buyer was shown is exactly what happens.
    /// </summary>
    public sealed class TransportQuote
    {
        public TransportOption Option { get; }
        public ShippingZone Zone { get; }
        public float Cost { get; }
        public int DispatchWeek { get; }
        public int DurationWeeks { get; }
        public int ArrivalWeek => DispatchWeek + DurationWeeks;

        public TransportQuote(TransportOption option, ShippingZone zone, float cost, int dispatchWeek, int durationWeeks)
        {
            Option = option;
            Zone = zone;
            Cost = cost;
            DispatchWeek = dispatchWeek;
            DurationWeeks = durationWeeks;
        }

        public override string ToString() =>
            $"{Option.ToDisplayString()} / {Zone.ToDisplayString()} — {Cost:F0} £, {DurationWeeks} week(s), arrives week {ArrivalWeek}";
    }
}
