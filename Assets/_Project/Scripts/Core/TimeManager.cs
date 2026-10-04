using System;
using System.Globalization;
using UnityEngine;

namespace AntiqueTradingSimulator.Core
{
    /// <summary>
    /// Minimal time system: tracks the current in-game day and advances it
    /// after a fixed real-time duration. Can be paused/resumed. Other systems
    /// subscribe to OnDayChanged to react to the passage of time.
    /// Also maps game days to calendar dates (Day 1 = campaign start date).
    /// </summary>
    public class TimeManager : MonoBehaviour
    {
        [Tooltip("Real-time seconds one game day lasts at 1x speed. 180 = 3 minutes (≈4.5 h for a 90-day campaign).")]
        [SerializeField] private float secondsPerDay = 180f;
        [SerializeField] private float[] speedSteps = { 1f, 2f, 4f };

        [Header("Calendar")]
        [Tooltip("Calendar date of Day 1.")]
        [SerializeField] private int startYear = 1884;
        [SerializeField] private int startMonth = 4;
        [SerializeField] private int startDay = 22; // Day 23 = 14 May 1884
        [SerializeField] private int campaignLength = 90;

        public int CurrentDay { get; private set; } = 1;
        public bool IsRunning { get; private set; } = true;
        public float SpeedMultiplier { get; private set; } = 1f;

        public event Action<int> OnDayChanged;
        public event Action<float> OnSpeedChanged;
        /// <summary>Raised when the clock is paused or resumed (true = running).</summary>
        public event Action<bool> OnRunningChanged;

        private float _timer;
        private int _speedIndex = 0;

        public float SecondsPerDay => secondsPerDay;
        public float TimeUntilNextDay => Mathf.Max(0f, secondsPerDay - _timer);
        public float DayProgress01 => Mathf.Clamp01(_timer / secondsPerDay);

        private static readonly CultureInfo English = CultureInfo.InvariantCulture;

        public int CampaignLength => campaignLength;
        public DateTime StartDate => new DateTime(startYear, startMonth, startDay);
        public DateTime CurrentDate => DayToDate(CurrentDay);

        public DateTime DayToDate(int day) => StartDate.AddDays(day - 1);
        public int DateToDay(DateTime date) => (date.Date - StartDate).Days + 1;

        public static string FormatLong(DateTime date) => date.ToString("d MMMM yyyy", English);

        public static string FormatDayMonth(DateTime date) => date.ToString("d MMMM", English);

        public static string FormatMonthYear(DateTime date) => date.ToString("MMMM yyyy", English);

        public static string FormatWithWeekday(DateTime date) => date.ToString("dddd, d MMMM yyyy", English);

        void Update()
        {
            if (!IsRunning) return;

            _timer += Time.deltaTime * SpeedMultiplier;

            if (_timer >= secondsPerDay)
            {
                AdvanceDay();
            }
        }

        private void AdvanceDay()
        {
            _timer = 0f;
            CurrentDay++;
            Debug.Log("Day " + CurrentDay);
            OnDayChanged?.Invoke(CurrentDay);
        }

        /// <summary>
        /// Manually advances the day by one, bypassing the timer. Useful for debug UI and testing.
        /// </summary>
        public void ForceAdvanceDay()
        {
            AdvanceDay();
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
    }
}