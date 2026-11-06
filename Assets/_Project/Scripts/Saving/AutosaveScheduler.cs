using System;

namespace AntiqueTradingSimulator.Saving
{
    /// <summary>
    /// Decides when an autosave happens. A new day only marks an autosave as due; it is taken
    /// later, from LateUpdate, once every OnDayChanged listener has finished its daily work and
    /// no modal is open. Several days skipped in one frame still produce a single autosave.
    /// Plain C#: the caller feeds it days and asks it each frame.
    /// </summary>
    public class AutosaveScheduler
    {
        private int _lastAutosaveDay;
        private int _intervalDays = 1;

        public bool Enabled { get; set; } = true;

        /// <summary>Autosave every this many in-game days (1 = every day). Values below 1 become 1.</summary>
        public int IntervalDays
        {
            get => _intervalDays;
            set => _intervalDays = Math.Max(1, value);
        }

        /// <summary>True when an autosave is waiting for a moment it is allowed to run.</summary>
        public bool IsDue { get; private set; }

        public AutosaveScheduler(int intervalDays, int currentDay)
        {
            IntervalDays = intervalDays;
            _lastAutosaveDay = currentDay;
        }

        /// <summary>Call on every new day.</summary>
        public void NotifyDayChanged(int day)
        {
            if (Enabled && day - _lastAutosaveDay >= IntervalDays)
                IsDue = true;
        }

        /// <summary>
        /// Call once per frame, after the day's work. Returns true when the caller should
        /// autosave now; it then counts as done for <paramref name="currentDay"/>.
        /// </summary>
        public bool ShouldAutosaveNow(int currentDay, bool blocked)
        {
            if (!IsDue) return false;

            if (!Enabled)
            {
                IsDue = false;
                return false;
            }

            if (blocked) return false;

            IsDue = false;
            _lastAutosaveDay = currentDay;
            return true;
        }

        /// <summary>
        /// Call after a load or a manual save: the interval counts from that day, and nothing
        /// that was due before it still fires.
        /// </summary>
        public void Reset(int currentDay)
        {
            IsDue = false;
            _lastAutosaveDay = currentDay;
        }
    }
}
