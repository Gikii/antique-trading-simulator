using System.IO;
using UnityEngine;

namespace AntiqueTradingSimulator.Saving
{
    /// <summary>
    /// Where saves live and how many slots there are. One asset in Data/Resources, read by
    /// SaveManager in the game scene and by the save screens in the main menu (which has no
    /// SaveManager), so both always look at the same files.
    /// </summary>
    [CreateAssetMenu(fileName = "SaveSettings", menuName = "AntiqueTradingSimulator/Save Settings")]
    public class SaveSettings : ScriptableObject
    {
        /// <summary>Path under any Resources folder, without extension.</summary>
        public const string ResourcePath = "SaveSettings";

        [Header("Files")]
        [Tooltip("Folder inside Application.persistentDataPath that holds every save slot.")]
        public string SaveFolderName = "saves";

        [Min(1)] public int ManualSlotCount = 3;

        [Min(1)] public int AutosaveSlotCount = 3;

        [Tooltip("Save file from before slots existed, inside Application.persistentDataPath. " +
                 "Moved into manual slot 1 on start if that slot is still empty.")]
        public string LegacyFileName = "save.json";

        [Header("Autosave defaults (the player's own choice is kept in PlayerPrefs)")]
        public bool AutosaveEnabledByDefault = true;

        [Min(1)] public int AutosaveIntervalDaysByDefault = 1;

        [Header("Unsaved progress")]
        [Tooltip("Play time since the last save (manual or autosave) after which leaving the game asks for confirmation.")]
        [Min(0f)] public float UnsavedProgressWarningSeconds = 30f;

        public string SaveFolder => Path.Combine(Application.persistentDataPath, SaveFolderName);

        public SaveSlotStore CreateStore() => new SaveSlotStore(SaveFolder, ManualSlotCount, AutosaveSlotCount);

        private static SaveSettings _cached;

        /// <summary>
        /// The asset from Resources. When it is missing, an in-memory instance with the default
        /// values is used (and a warning logged once), so saving still works.
        /// </summary>
        public static SaveSettings Load()
        {
            if (_cached != null) return _cached;

            _cached = Resources.Load<SaveSettings>(ResourcePath);
            if (_cached == null)
            {
                Debug.LogWarning($"SaveSettings: no {ResourcePath} asset in a Resources folder — using the defaults.");
                _cached = CreateInstance<SaveSettings>();
            }

            return _cached;
        }
    }
}
