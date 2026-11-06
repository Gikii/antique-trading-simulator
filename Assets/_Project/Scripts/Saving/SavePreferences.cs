using System;
using UnityEngine;

namespace AntiqueTradingSimulator.Saving
{
    /// <summary>
    /// The player's own save settings, kept in PlayerPrefs (not in a save file) so they apply to
    /// every game. The settings screen writes them; SaveManager listens to <see cref="Changed"/>
    /// and applies them to the running game straight away.
    /// </summary>
    public static class SavePreferences
    {
        public const string AutosaveEnabledKey = "Settings.AutosaveEnabled";
        public const string AutosaveIntervalDaysKey = "Settings.AutosaveIntervalDays";

        /// <summary>Raised after any preference here changed.</summary>
        public static event Action Changed;

        public static bool AutosaveEnabled
        {
            get => PlayerPrefs.GetInt(AutosaveEnabledKey, SaveSettings.Load().AutosaveEnabledByDefault ? 1 : 0) == 1;
            set => Set(AutosaveEnabledKey, value ? 1 : 0);
        }

        /// <summary>Autosave every this many in-game days, at least 1.</summary>
        public static int AutosaveIntervalDays
        {
            get => Mathf.Max(1, PlayerPrefs.GetInt(AutosaveIntervalDaysKey, SaveSettings.Load().AutosaveIntervalDaysByDefault));
            set => Set(AutosaveIntervalDaysKey, Mathf.Max(1, value));
        }

        private static void Set(string key, int value)
        {
            if (PlayerPrefs.HasKey(key) && PlayerPrefs.GetInt(key) == value) return;

            PlayerPrefs.SetInt(key, value);
            PlayerPrefs.Save();
            Changed?.Invoke();
        }

        // Static events survive between play sessions when domain reload is off; start clean.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => Changed = null;
    }
}
