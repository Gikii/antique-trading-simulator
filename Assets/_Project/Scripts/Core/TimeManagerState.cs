namespace AntiqueTradingSimulator.Core
{

    public class TimeManagerState
    {
        /// <summary>Current period = in-game week.</summary>
        public int CurrentDay = 1;

        public int CurrentTick;

        /// <summary>Real seconds elapsed in the current period (week).</summary>
        public float DayTimer;

        public int SpeedIndex;

        public bool IsRunning = true;

        /// <summary>Real seconds spent in the game scene over the whole campaign, paused time included.</summary>
        public double PlaytimeSeconds;
    }
}
