using System;
using System.Globalization;
using AntiqueTradingSimulator.Seasons;
using UnityEngine;
using UnityEngine.Serialization;

namespace AntiqueTradingSimulator.Core
{
    /// <summary>
    /// Minimal time system: tracks the current period (one market step) and advances it
    /// after a fixed real-time duration. Can be paused/resumed. Other systems
    /// subscribe to OnDayChanged to react to the passage of time.
    ///
    /// ONE PERIOD = ONE IN-GAME WEEK. The period counter and everything scheduled on it is
    /// still called "day" in code (CurrentDay, OnDayChanged, ArrivalDay, ...Days settings), but
    /// every such value counts weeks. Inside a week the ticks walk through the 7 calendar days
    /// (TicksPerDay ticks each), which is used for display (weekday, hour) and for auction start
    /// times; the economy itself only steps once per week.
    /// Maps periods to calendar dates: period 1 = the week starting on the campaign start date.
    /// </summary>
    public class TimeManager : MonoBehaviour
    {
        public const int DaysPerWeek = 7;
        public const int DefaultTicksPerDay = 4;
        public const int DefaultTicksPerPeriod = DaysPerWeek * DefaultTicksPerDay;

        /// <summary>Hour of the first tick of a calendar day; the day's ticks spread over WorkingHours.</summary>
        public const int DayStartHour = 8;
        public const int WorkingHours = 12;

        [Header("Real-time")]
        [Tooltip("Real-time seconds one game week lasts at 1x speed. 150 = 2.5 minutes (≈4.3 h for a 104-week campaign).")]
        [FormerlySerializedAs("secondsPerDay")]
        [SerializeField] private float secondsPerWeek = 150f;
        [SerializeField] private float[] speedSteps = { 1f, 2f, 4f };

        [Header("Ticks")]
        [Tooltip("Ticks per calendar day. A week has 7 × this many ticks (NPC actions and auctions happen on ticks).")]
        [SerializeField] [Min(1)] private int ticksPerDay = DefaultTicksPerDay;

        [Header("Calendar")]
        [Tooltip("Calendar date of the first day of week 1 (a Monday keeps weeks Monday–Sunday).")]
        [SerializeField] private int startYear = 1884;
        [SerializeField] private int startMonth = 4;
        [SerializeField] private int startDay = 21; // Monday 21 April 1884
        [Tooltip("Campaign length in weeks. 104 = two years.")]
        [SerializeField] private int campaignLength = 104;

        [Header("Seasons")]
        [Tooltip("What each season changes (transport weather, …). Optional — defaults are used if empty.")]
        [SerializeField] private SeasonSettings seasonSettings;
        private SeasonSettings _defaultSeasonSettings;

        /// <summary>The current period = the current in-game week (1-based). See the class summary.</summary>
        public int CurrentDay { get; private set; } = 1;

        /// <summary>Same as <see cref="CurrentDay"/>, under its real meaning.</summary>
        public int CurrentWeek => CurrentDay;

        public bool IsRunning { get; private set; } = true;

        /// <summary>Tick within the current week, 0 .. TicksPerPeriod - 1.</summary>
        public int CurrentTick { get; private set; }

        /// <summary>True while AdvanceToNextPeriod is running out a period's ticks in one frame.</summary>
        public bool IsFastForwarding { get; private set; }

        /// <summary>
        /// Real seconds spent in the game over the whole campaign (across saves and loads), paused
        /// time included — "time played" for the save list. Independent of the game speed.
        /// </summary>
        public double PlaytimeSeconds { get; private set; }
        public float SpeedMultiplier { get; private set; } = 1f;

        public event Action<int> OnDayChanged;
        /// <summary>Raised after OnDayChanged when the new week starts a new season.</summary>
        public event Action<Season> OnSeasonChanged;
        public event Action<float> OnSpeedChanged;
        /// <summary>Raised when the clock is paused or resumed (true = running).</summary>
        public event Action<bool> OnRunningChanged;
        public event Action<int, int> OnTick;

        public event Action OnTimeRestored;

        private float _timer;
        private int _speedIndex = 0;

        private bool _firstTickRaised;

        public int TicksPerDay => Mathf.Max(1, ticksPerDay);
        /// <summary>Ticks in one period (week): 7 × TicksPerDay.</summary>
        public int TicksPerPeriod => DaysPerWeek * TicksPerDay;
        public float SecondsPerWeek => secondsPerWeek;
        /// <summary>Real seconds (at 1x) until the next period, i.e. the next week.</summary>
        public float TimeUntilNextDay => Mathf.Max(0f, secondsPerWeek - _timer);
        public float DayProgress01 => Mathf.Clamp01(_timer / secondsPerWeek);

        private static readonly CultureInfo English = CultureInfo.InvariantCulture;

        /// <summary>Campaign length in periods (weeks).</summary>
        public int CampaignLength => campaignLength;
        public DateTime StartDate => new DateTime(startYear, startMonth, startDay);

        /// <summary>Today's calendar date: the current week's first day plus the weekday reached by the ticks.</summary>
        public DateTime CurrentDate => TickToDate(CurrentDay, CurrentTick);

        /// <summary>First calendar day (Monday) of a period/week.</summary>
        public DateTime DayToDate(int day) => StartDate.AddDays((day - 1) * DaysPerWeek);

        /// <summary>The period/week a calendar date falls into.</summary>
        public int DateToDay(DateTime date) =>
            (int)Math.Floor((date.Date - StartDate).Days / (double)DaysPerWeek) + 1;

        /// <summary>Calendar date of a tick of a week (tick / TicksPerDay = days after the week's Monday).</summary>
        public DateTime TickToDate(int week, int tick) =>
            DayToDate(week).AddDays(Mathf.Clamp(tick, 0, TicksPerPeriod - 1) / TicksPerDay);

        /// <summary>Season of a period/week — decided by the week's first day, so a week never spans two seasons.</summary>
        public Season SeasonOf(int week) => SeasonCalendar.Of(DayToDate(week));

        public Season CurrentSeason => SeasonOf(CurrentDay);

        /// <summary>The assigned SeasonSettings, or an in-memory instance with the default values.</summary>
        public SeasonSettings SeasonSettings
        {
            get
            {
                if (seasonSettings != null) return seasonSettings;
                if (_defaultSeasonSettings == null)
                    _defaultSeasonSettings = ScriptableObject.CreateInstance<SeasonSettings>();
                return _defaultSeasonSettings;
            }
        }

        /// <summary>Hour of the day a tick falls on: the day's ticks spread evenly from DayStartHour over WorkingHours.</summary>
        public int TickToHour(int tick) =>
            DayStartHour + (Mathf.Max(0, tick) % TicksPerDay) * WorkingHours / TicksPerDay;

        public static string FormatLong(DateTime date) => date.ToString("d MMMM yyyy", English);

        public static string FormatDayMonth(DateTime date) => date.ToString("d MMMM", English);

        public static string FormatMonthYear(DateTime date) => date.ToString("MMMM yyyy", English);

        public static string FormatWithWeekday(DateTime date) => date.ToString("dddd, d MMMM yyyy", English);

        void Update()
        {
            PlaytimeSeconds += Time.unscaledDeltaTime;

            if (!_firstTickRaised)
            {
                _firstTickRaised = true;
                OnTick?.Invoke(CurrentDay, CurrentTick);
            }

            if (!IsRunning) return;

            _timer += Time.deltaTime * SpeedMultiplier;

            if (_timer >= secondsPerWeek)
            {
                FastForwardDay();
            }

            if (Mathf.FloorToInt(_timer / secondsPerWeek * TicksPerPeriod) > CurrentTick)
            {
                IncrementTick();
            }
        }

        private void IncrementTick()
        {
            CurrentTick++;
            OnTick?.Invoke(CurrentDay, CurrentTick);
        }

        private void AdvanceDay()
        {
            Season previousSeason = CurrentSeason;

            _timer = 0f;
            CurrentDay++;
            CurrentTick = 0;
            Debug.Log("Week " + CurrentDay);
            OnDayChanged?.Invoke(CurrentDay);

            if (CurrentSeason != previousSeason)
            {
                Debug.Log($"Season: {CurrentSeason}");
                OnSeasonChanged?.Invoke(CurrentSeason);
            }

            _firstTickRaised = true;
            OnTick?.Invoke(CurrentDay, CurrentTick);
        }

        private void FastForwardDay()
        {
            while (CurrentTick < TicksPerPeriod - 1)
            {
                IncrementTick();
            }

            AdvanceDay();
        }

        /// <summary>
        /// Manually advances the period (week) by one, bypassing the timer. Useful for debug UI and testing.
        /// </summary>
        public void ForceAdvanceDay()
        {
            FastForwardDay();
        }

        public void Pause() => SetRunning(false);

        public void Resume() => SetRunning(true);

        public void ToggleRunning() => SetRunning(!IsRunning);

        private void SetRunning(bool running)
        {
            if (IsRunning == running) return;

            IsRunning = running;
            OnRunningChanged?.Invoke(running);
        }

        public void CycleSpeed()
        {
            _speedIndex = (_speedIndex + 1) % speedSteps.Length;
            SpeedMultiplier = speedSteps[_speedIndex];
            OnSpeedChanged?.Invoke(SpeedMultiplier);
        }

        // ---------------------------------------------------------------- save / load

        public TimeManagerState CaptureState() => new TimeManagerState
        {
            CurrentDay = CurrentDay,
            CurrentTick = CurrentTick,
            DayTimer = _timer,
            SpeedIndex = _speedIndex,
            IsRunning = IsRunning,
            PlaytimeSeconds = PlaytimeSeconds
        };

        public void RestoreState(TimeManagerState state)
        {
            if (state == null) return;

            CurrentDay = Mathf.Max(1, state.CurrentDay);
            _timer = Mathf.Clamp(state.DayTimer, 0f, secondsPerWeek);

            CurrentTick = Mathf.Clamp(state.CurrentTick, 0, TicksPerPeriod - 1);

            _speedIndex = speedSteps != null && speedSteps.Length > 0
                ? Mathf.Clamp(state.SpeedIndex, 0, speedSteps.Length - 1)
                : 0;
            SpeedMultiplier = speedSteps != null && speedSteps.Length > 0 ? speedSteps[_speedIndex] : 1f;

            IsRunning = state.IsRunning;

            // Saves from before play time was tracked carry 0; the count simply starts here.
            PlaytimeSeconds = System.Math.Max(0d, state.PlaytimeSeconds);

            OnSpeedChanged?.Invoke(SpeedMultiplier);
            OnRunningChanged?.Invoke(IsRunning);
            OnTimeRestored?.Invoke();

            _firstTickRaised = false;
        }

    }
}