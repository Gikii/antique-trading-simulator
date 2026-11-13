using System;
using AntiqueTradingSimulator.Saving;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// The player's settings, shared by the pause menu and the main menu. Every change is stored
    /// straight away (PlayerPrefs, through SavePreferences) and a running game picks it up at once —
    /// there is no Apply button.
    /// </summary>
    public class SettingsPanelUI : MonoBehaviour
    {
        [Header("Autosave")]
        [SerializeField] private Toggle autosaveToggle;
        [SerializeField] private TMP_Text intervalText;
        [SerializeField] private Button intervalDownButton;
        [SerializeField] private Button intervalUpButton;
        [Tooltip("The interval row, dimmed while autosave is off.")]
        [SerializeField] private CanvasGroup intervalGroup;

        [SerializeField] private Button closeButton;

        [Tooltip("Interval steps offered, in in-game weeks.")]
        [SerializeField] private int[] intervalSteps = { 1, 2, 3, 5, 7 };

        public bool IsOpen => gameObject.activeSelf;

        public event Action Closed;

        private void Awake()
        {
            if (autosaveToggle != null) autosaveToggle.onValueChanged.AddListener(SetAutosave);
            if (intervalDownButton != null) intervalDownButton.onClick.AddListener(() => StepInterval(-1));
            if (intervalUpButton != null) intervalUpButton.onClick.AddListener(() => StepInterval(+1));
            if (closeButton != null) closeButton.onClick.AddListener(Close);
        }

        public void Show()
        {
            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Refresh();
        }

        public void Close()
        {
            if (!IsOpen) return;

            gameObject.SetActive(false);
            Closed?.Invoke();
        }

        private void Refresh()
        {
            bool isOn = SavePreferences.AutosaveEnabled;
            if (autosaveToggle != null) autosaveToggle.SetIsOnWithoutNotify(isOn);

            int days = SavePreferences.AutosaveIntervalDays;
            if (intervalText != null) intervalText.text = days == 1 ? "Every week" : $"Every {days} weeks";

            int index = StepIndex(days);
            if (intervalDownButton != null) intervalDownButton.interactable = isOn && index > 0;
            if (intervalUpButton != null) intervalUpButton.interactable = isOn && index < intervalSteps.Length - 1;

            if (intervalGroup != null)
            {
                intervalGroup.alpha = isOn ? 1f : 0.45f;
                intervalGroup.interactable = isOn;
            }
        }

        private void SetAutosave(bool isOn)
        {
            SavePreferences.AutosaveEnabled = isOn;
            Refresh();
        }

        private void StepInterval(int direction)
        {
            if (intervalSteps == null || intervalSteps.Length == 0) return;

            int index = Mathf.Clamp(StepIndex(SavePreferences.AutosaveIntervalDays) + direction, 0, intervalSteps.Length - 1);
            SavePreferences.AutosaveIntervalDays = intervalSteps[index];
            Refresh();
        }

        /// <summary>The step equal to <paramref name="days"/>, or the nearest one below it.</summary>
        private int StepIndex(int days)
        {
            int index = 0;
            for (int i = 0; i < intervalSteps.Length; i++)
                if (intervalSteps[i] <= days) index = i;
            return index;
        }
    }
}
