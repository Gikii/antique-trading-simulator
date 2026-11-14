using AntiqueTradingSimulator.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Debug/testing time controls next to the week counter in the HUD: a countdown to the
    /// next week, pause/resume, speed (1x / 2x / 4x) and "skip to next week".
    /// Hidden automatically in non-development builds unless hideInReleaseBuilds is off.
    /// Every reference is optional.
    /// </summary>
    public class TimeControlsUI : MonoBehaviour
    {
        [SerializeField] private TimeManager timeManager;

        [Header("Countdown")]
        [SerializeField] private TMP_Text timerText;     // "2:41"
        [SerializeField] private Color runningColor = Color.white;
        [SerializeField] private Color pausedColor = new Color32(0xE0, 0xA0, 0x3C, 0xFF);

        [Header("Buttons")]
        [SerializeField] private Button pauseButton;
        [SerializeField] private TMP_Text pauseLabel;    // "||" / ">"
        [SerializeField] private Button speedButton;
        [SerializeField] private TMP_Text speedLabel;    // "x1" / "x2" / "x4"
        [FormerlySerializedAs("skipDayButton")]
        [SerializeField] private Button skipWeekButton;

        [Header("Visibility")]
        [Tooltip("Hide these controls in release (non-development) builds.")]
        [SerializeField] private bool hideInReleaseBuilds = true;

        private int _lastShownSeconds = -1;
        private bool _lastShownRunning;

        private void Awake()
        {
            if (hideInReleaseBuilds && !Debug.isDebugBuild)
            {
                gameObject.SetActive(false);
                return;
            }

            if (timeManager == null) timeManager = FindFirstObjectByType<TimeManager>();

            if (pauseButton != null) pauseButton.onClick.AddListener(TogglePause);
            if (speedButton != null) speedButton.onClick.AddListener(CycleSpeed);
            if (skipWeekButton != null) skipWeekButton.onClick.AddListener(SkipWeek);

            SetTooltip(pauseButton, "Pause / resume time (debug)");
            SetTooltip(speedButton, "Time speed (debug)");
            SetTooltip(skipWeekButton, "Skip to the next week (debug)");
        }

        private void OnEnable()
        {
            if (timeManager == null) return;

            timeManager.OnRunningChanged += HandleRunningChanged;
            timeManager.OnSpeedChanged += HandleSpeedChanged;
            timeManager.OnTimeRestored += HandleTimeRestored;
            RefreshButtons();
        }

        private void OnDisable()
        {
            if (timeManager == null) return;

            timeManager.OnRunningChanged -= HandleRunningChanged;
            timeManager.OnSpeedChanged -= HandleSpeedChanged;
            timeManager.OnTimeRestored -= HandleTimeRestored;
        }

        private void Update()
        {
            if (timeManager == null || timerText == null) return;

            // Countdown on the week's own clock (2:30 at the start of a week, whatever the speed).
            // Higher speeds make it run down faster instead of shortening it; refreshed only when it changes.
            int seconds = Mathf.CeilToInt(timeManager.TimeUntilNextWeek);
            bool running = timeManager.IsRunning;
            if (seconds == _lastShownSeconds && running == _lastShownRunning) return;

            _lastShownSeconds = seconds;
            _lastShownRunning = running;
            timerText.text = $"{seconds / 60}:{seconds % 60:00}";
            timerText.color = running ? runningColor : pausedColor;
        }

        // ------------------------------------------------------------------
        // Actions
        // ------------------------------------------------------------------

        private void TogglePause() => timeManager?.ToggleRunning();

        private void CycleSpeed() => timeManager?.CycleSpeed();

        private void SkipWeek() => timeManager?.ForceAdvanceWeek();

        private void HandleRunningChanged(bool running) => RefreshButtons();
        private void HandleSpeedChanged(float speed) => RefreshButtons();

        private void HandleTimeRestored()
        {
            _lastShownSeconds = -1;
            RefreshButtons();
        }

        private static void SetTooltip(Component target, string text)
        {
            var trigger = TooltipTrigger.On(target);
            if (trigger != null) trigger.Text = text;
        }

        private void RefreshButtons()
        {
            if (timeManager == null) return;

            if (pauseLabel != null)
                pauseLabel.text = timeManager.IsRunning ? "||" : ">";

            if (speedLabel != null)
                speedLabel.text = $"x{timeManager.SpeedMultiplier:0.#}";
        }
    }
}
