using System;
using UnityEngine;

namespace AntiqueTradingSimulator.Core
{
    /// <summary>
    /// Minimal time system: tracks the current in-game day and advances it
    /// after a fixed real-time duration. Can be paused/resumed. Other systems
    /// subscribe to OnDayChanged to react to the passage of time.
    /// </summary>
    public class TimeManager : MonoBehaviour
    {
        [SerializeField] private float secondsPerDay = 10f;
        [SerializeField] private float[] speedSteps = { 1f, 2f, 4f };

        public int CurrentDay { get; private set; } = 1;
        public bool IsRunning { get; private set; } = true;
        public float SpeedMultiplier { get; private set; } = 1f;

        public event Action<int> OnDayChanged;
        public event Action<float> OnSpeedChanged;

        private float _timer;
        private int _speedIndex = 0;

        public float SecondsPerDay => secondsPerDay;
        public float TimeUntilNextDay => Mathf.Max(0f, secondsPerDay - _timer);
        public float DayProgress01 => Mathf.Clamp01(_timer / secondsPerDay);

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

        public void Pause()
        {
            IsRunning = false;
        }

        public void Resume()
        {
            IsRunning = true;
        }

        public void ToggleRunning()
        {
            IsRunning = !IsRunning;
        }

        public void CycleSpeed()
        {
            _speedIndex = (_speedIndex + 1) % speedSteps.Length;
            SpeedMultiplier = speedSteps[_speedIndex];
            OnSpeedChanged?.Invoke(SpeedMultiplier);
        }

    }
}