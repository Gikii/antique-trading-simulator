using System;
using UnityEngine;

namespace AntiqueTradingSimulator.Core
{
    /// <summary>
    /// Minimal time system: tracks the current in-game day and advances it
    /// after a fixed real-time duration. Other systems subscribe to OnDayChanged
    /// to react to the passage of time.
    /// </summary>
    public class TimeManager : MonoBehaviour
    {
        [SerializeField] private float secondsPerDay = 10f;

        public int CurrentDay { get; private set; } = 1;

        public event Action<int> OnDayChanged;

        private float _timer;

        void Update()
        {
            _timer += Time.deltaTime;

            if (_timer >= secondsPerDay)
            {
                AdvanceDay();
            }
        }

        private void AdvanceDay()
        {
            _timer = 0f;
            CurrentDay++;
            OnDayChanged?.Invoke(CurrentDay);
        }

        /// <summary>
        /// Manually advances the day by one, bypassing the timer. Useful for debug UI and testing.
        /// </summary>
        public void ForceAdvanceDay()
        {
            AdvanceDay();
        }
    }
}