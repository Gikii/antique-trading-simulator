using System;
using System.IO;
using UnityEngine;

namespace AntiqueTradingSimulator.Saving
{
    /// <summary>
    /// The one shared <see cref="SaveSlotStore"/>, built from <see cref="SaveSettings"/>. Used by
    /// SaveManager in the game scene and directly by the main menu, which lists and deletes
    /// saves without a game running. The first access also moves a save from before slots
    /// existed into manual slot 1.
    /// </summary>
    public static class SaveFiles
    {
        private static SaveSlotStore _store;

        public static SaveSlotStore Store
        {
            get
            {
                if (_store == null)
                {
                    var settings = SaveSettings.Load();
                    _store = settings.CreateStore();
                    AdoptLegacySave(_store, settings);
                }

                return _store;
            }
        }

        private static void AdoptLegacySave(SaveSlotStore store, SaveSettings settings)
        {
            if (string.IsNullOrEmpty(settings.LegacyFileName)) return;

            string legacyPath = Path.Combine(Application.persistentDataPath, settings.LegacyFileName);
            SaveSlot target = SaveSlot.Manual(1);
            try
            {
                if (store.AdoptLegacyFile(legacyPath, target))
                    Debug.Log($"SaveFiles: moved the old {settings.LegacyFileName} into {target.DisplayName}.");
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"SaveFiles: could not move the old {settings.LegacyFileName} into a slot — {exception.Message}");
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlay() => _store = null;
    }
}
