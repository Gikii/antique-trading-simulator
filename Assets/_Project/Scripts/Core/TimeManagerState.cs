namespace AntiqueTradingSimulator.Core
{

    public class TimeManagerState
    {
        public int CurrentDay = 1;

        public float DayTimer;

        public int SpeedIndex;

        public bool IsRunning = true;

        /// <summary>Real seconds spent in the game scene over the whole campaign, paused time included.</summary>
        public double PlaytimeSeconds;
    }
}
