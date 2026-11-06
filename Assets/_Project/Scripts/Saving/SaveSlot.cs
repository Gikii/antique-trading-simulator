using System;

namespace AntiqueTradingSimulator.Saving
{
    public enum SaveSlotKind
    {
        Manual,
        Autosave
    }

    /// <summary>
    /// One place a game can be saved to: a manual slot the player picks, or one of the rotating
    /// autosave slots. Each slot is one file in the saves folder ("manual_1.json", "autosave_2.json").
    /// </summary>
    public readonly struct SaveSlot : IEquatable<SaveSlot>
    {
        public const string Extension = ".json";

        public readonly SaveSlotKind Kind;

        /// <summary>1-based, as shown to the player.</summary>
        public readonly int Index;

        public SaveSlot(SaveSlotKind kind, int index)
        {
            Kind = kind;
            Index = index;
        }

        public static SaveSlot Manual(int index) => new SaveSlot(SaveSlotKind.Manual, index);

        public static SaveSlot Autosave(int index) => new SaveSlot(SaveSlotKind.Autosave, index);

        public bool IsAutosave => Kind == SaveSlotKind.Autosave;

        public string FileName => $"{Prefix(Kind)}{Index}{Extension}";

        public string DisplayName => IsAutosave ? $"Autosave {Index}" : $"Slot {Index}";

        /// <summary>Reads a slot back from its file name. False for any file that is not a slot.</summary>
        public static bool TryParseFileName(string fileName, out SaveSlot slot)
        {
            slot = default;
            if (string.IsNullOrEmpty(fileName) || !fileName.EndsWith(Extension, StringComparison.OrdinalIgnoreCase))
                return false;

            string stem = fileName.Substring(0, fileName.Length - Extension.Length);

            foreach (SaveSlotKind kind in Enum.GetValues(typeof(SaveSlotKind)))
            {
                string prefix = Prefix(kind);
                if (!stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

                if (int.TryParse(stem.Substring(prefix.Length), out int index) && index > 0)
                {
                    slot = new SaveSlot(kind, index);
                    return true;
                }
            }

            return false;
        }

        private static string Prefix(SaveSlotKind kind) => kind == SaveSlotKind.Autosave ? "autosave_" : "manual_";

        public bool Equals(SaveSlot other) => Kind == other.Kind && Index == other.Index;

        public override bool Equals(object obj) => obj is SaveSlot other && Equals(other);

        public override int GetHashCode() => ((int)Kind * 397) ^ Index;

        public static bool operator ==(SaveSlot a, SaveSlot b) => a.Equals(b);

        public static bool operator !=(SaveSlot a, SaveSlot b) => !a.Equals(b);

        public override string ToString() => DisplayName;
    }
}
