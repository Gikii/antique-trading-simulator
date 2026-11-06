using System;
using System.Collections.Generic;
using System.IO;
using AntiqueTradingSimulator.Core;

namespace AntiqueTradingSimulator.Saving
{
    /// <summary>
    /// The save files on disk: one file per slot inside one folder. Knows nothing about the game
    /// state itself — it writes and reads JSON text, reads headers for the slot list and picks
    /// the autosave slot to overwrite. Plain C#, no scene needed, so it can be tested on its own.
    /// </summary>
    public class SaveSlotStore
    {
        public string Folder { get; }
        public int ManualSlotCount { get; }
        public int AutosaveSlotCount { get; }

        public SaveSlotStore(string folder, int manualSlotCount, int autosaveSlotCount)
        {
            if (string.IsNullOrEmpty(folder)) throw new ArgumentException("A save folder is required.", nameof(folder));

            Folder = folder;
            ManualSlotCount = Math.Max(1, manualSlotCount);
            AutosaveSlotCount = Math.Max(1, autosaveSlotCount);
        }

        public string PathFor(SaveSlot slot) => Path.Combine(Folder, slot.FileName);

        public bool IsValid(SaveSlot slot) =>
            slot.Index >= 1 && slot.Index <= (slot.IsAutosave ? AutosaveSlotCount : ManualSlotCount);

        /// <summary>Every slot this store manages: manual slots first, then autosaves.</summary>
        public IEnumerable<SaveSlot> AllSlots()
        {
            for (int i = 1; i <= ManualSlotCount; i++) yield return SaveSlot.Manual(i);
            for (int i = 1; i <= AutosaveSlotCount; i++) yield return SaveSlot.Autosave(i);
        }

        public bool Exists(SaveSlot slot) => File.Exists(PathFor(slot));

        public bool AnyExists()
        {
            foreach (var slot in AllSlots())
                if (Exists(slot)) return true;
            return false;
        }

        // ---------------------------------------------------------------- headers

        /// <summary>Reads what the slot list shows. Never throws: problems end up in the flags.</summary>
        public SaveSlotInfo Inspect(SaveSlot slot)
        {
            var info = new SaveSlotInfo { Slot = slot };
            string path = PathFor(slot);

            if (!File.Exists(path)) return info;

            info.Exists = true;
            info.LastWriteUtc = File.GetLastWriteTimeUtc(path);

            try
            {
                info.Header = SaveSerializer.FromJson<SaveHeader>(File.ReadAllText(path));
            }
            catch (Exception)
            {
                info.Header = null;
            }

            if (info.Header == null)
            {
                info.IsCorrupt = true;
                return info;
            }

            info.IsIncompatible = info.Header.Version != SaveSerializer.SaveFormatVersion;
            return info;
        }

        /// <summary>Every slot, filled or empty, in the order of <see cref="AllSlots"/>.</summary>
        public List<SaveSlotInfo> List()
        {
            var result = new List<SaveSlotInfo>();
            foreach (var slot in AllSlots())
                result.Add(Inspect(slot));
            return result;
        }

        /// <summary>The most recently saved loadable slot, manual or autosave. Null if there is none.</summary>
        public SaveSlotInfo Latest()
        {
            SaveSlotInfo latest = null;
            foreach (var info in List())
            {
                if (!info.CanLoad) continue;
                if (latest == null || info.SavedAtUtc > latest.SavedAtUtc)
                    latest = info;
            }
            return latest;
        }

        /// <summary>
        /// Where the next autosave goes: the first empty autosave slot, otherwise an unreadable one,
        /// otherwise the one saved longest ago. The newest autosaves are always kept.
        /// </summary>
        public SaveSlot NextAutosaveSlot()
        {
            SaveSlot? oldest = null;
            DateTime oldestTime = DateTime.MaxValue;

            for (int i = 1; i <= AutosaveSlotCount; i++)
            {
                var info = Inspect(SaveSlot.Autosave(i));
                if (!info.Exists) return info.Slot;

                DateTime time = info.CanLoad ? info.SavedAtUtc : DateTime.MinValue;
                if (time < oldestTime)
                {
                    oldestTime = time;
                    oldest = info.Slot;
                }
            }

            return oldest ?? SaveSlot.Autosave(1);
        }

        // ---------------------------------------------------------------- files

        public string Read(SaveSlot slot) => File.ReadAllText(PathFor(slot));

        /// <summary>
        /// Writes through a temporary file so an interrupted write cannot leave a half-written
        /// save behind: the old file stays intact until the new one is complete on disk.
        /// </summary>
        public void Write(SaveSlot slot, string json)
        {
            Directory.CreateDirectory(Folder);

            string path = PathFor(slot);
            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, json);

            if (!File.Exists(path))
            {
                File.Move(temporaryPath, path);
                return;
            }

            try
            {
                File.Replace(temporaryPath, path, null);
            }
            catch (Exception exception) when (exception is PlatformNotSupportedException || exception is IOException)
            {
                // File.Replace is not implemented everywhere Unity runs. The fallback has a
                // brief window with no save file, which is still better than a truncated one.
                File.Delete(path);
                File.Move(temporaryPath, path);
            }
        }

        public bool Delete(SaveSlot slot)
        {
            string path = PathFor(slot);
            if (!File.Exists(path)) return false;

            File.Delete(path);
            return true;
        }

        /// <summary>
        /// Moves a save written before slots existed into <paramref name="target"/>, once: only
        /// when the old file exists and the target slot is still empty. Returns true if it moved.
        /// </summary>
        public bool AdoptLegacyFile(string legacyPath, SaveSlot target)
        {
            if (string.IsNullOrEmpty(legacyPath) || !File.Exists(legacyPath) || Exists(target)) return false;

            Directory.CreateDirectory(Folder);
            File.Move(legacyPath, PathFor(target));
            return true;
        }
    }
}
