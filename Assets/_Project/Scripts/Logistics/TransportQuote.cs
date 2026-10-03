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
        public int DispatchDay { get; }
        public int DurationDays { get; }
        public int ArrivalDay => DispatchDay + DurationDays;

        public TransportQuote(TransportOption option, ShippingZone zone, float cost, int dispatchDay, int durationDays)
        {
            Option = option;
            Zone = zone;
            Cost = cost;
            DispatchDay = dispatchDay;
            DurationDays = durationDays;
        }

        public override string ToString() =>
            $"{Option.ToDisplayString()} / {Zone.ToDisplayString()} — {Cost:F0} €, {DurationDays} day(s), arrives day {ArrivalDay}";
    }
}
