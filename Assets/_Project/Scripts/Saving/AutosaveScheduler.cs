using System;

namespace AntiqueTradingSimulator.Saving
{
    /// <summary>
    /// Decides when an autosave happens. A new day only marks an autosave as due; it is taken
    /// later, from LateUpdate, once every OnWeekChanged listener has finished its daily work and
    /// no modal is open. Several days skipped in one frame still produce a single autosave.
    /// Plain C#: the caller feeds it days and asks it each frame.
    /// </summary>
    public class AutosaveScheduler
    {
        private int _lastAutosaveWeek;
        private int _intervalWeeks = 1;

        public bool Enabled { get; set; } = true;

        /// <summary>Autosave every this many in-game days (1 = every day). Values below 1 become 1.</summary>
        public int IntervalWeeks
        {
            get => _intervalWeeks;
            set => _intervalWeeks = Math.Max(1, value);
        }

        /// <summary>True when an autosave is waiting for a moment it is allowed to run.</summary>
        public bool IsDue { get; private set; }

        public AutosaveScheduler(int intervalWeeks, int currentWeek)
        {
            IntervalWeeks = intervalWeeks;
            _lastAutosaveWeek = currentWeek;
        }

        /// <summary>Call on every new day.</summary>
        public void NotifyWeekChanged(int week)
        {
            if (Enabled && week - _lastAutosaveWeek >= IntervalWeeks)
                IsDue = true;
        }

        /// <summary>
        /// Call once per frame, after the day's work. Returns true when the caller should
        /// autosave now; it then counts as done for <paramref name="currentDay"/>.
        /// </summary>
        public bool ShouldAutosaveNow(int currentWeek, bool blocked)
        {
            if (!IsDue) return false;

            if (!Enabled)
            {
                IsDue = false;
                return false;
            }

            if (blocked) return false;

            IsDue = false;
            _lastAutosaveWeek = currentWeek;
            return true;
        }

        /// <summary>
        /// Call after a load or a manual save: the interval counts from that day, and nothing
        /// that was due before it still fires.
        /// </summary>
        public void Reset(int currentWeek)
        {
            IsDue = false;
            _lastAutosaveWeek = currentWeek;
        }
    }
}
