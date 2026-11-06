using System;
using System.Globalization;
using Newtonsoft.Json;

namespace AntiqueTradingSimulator.Saving
{
    /// <summary>
    /// The few fields a slot list needs: when, which day, and how the company stood. GameState
    /// inherits them, so they are written into every save; reading a file as a SaveHeader skips
    /// the rest of the game state (unknown members are ignored by the serializer).
    /// </summary>
    public class SaveHeader
    {
        public int Version;

        /// <summary>Real-world time of the save, ISO 8601 round-trip format, UTC.</summary>
        public string SavedAtUtc;

        public int SavedOnDay;

        /// <summary>Length of the campaign the save belongs to, for "Day 34 / 90". 0 in older saves.</summary>
        public int CampaignLength;

        /// <summary>Real seconds played up to this save. 0 in older saves.</summary>
        public double PlaytimeSeconds;

        /// <summary>In-game date, already formatted for display ("14 March 1885").</summary>
        public string InGameDate;

        public string CompanyName;

        public float Cash;

        /// <summary>Cash plus the value of everything the player holds.</summary>
        public float NetWorth;

        /// <summary>SavedAtUtc parsed back, or null when the field is missing or malformed.</summary>
        [JsonIgnore]
        public DateTime? SavedAt =>
            DateTime.TryParse(SavedAtUtc, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var time)
                ? time.ToUniversalTime()
                : (DateTime?)null;
    }

    /// <summary>What a slot list shows for one slot.</summary>
    public class SaveSlotInfo
    {
        public SaveSlot Slot;

        /// <summary>Null when the file is missing or could not be read.</summary>
        public SaveHeader Header;

        public bool Exists;

        /// <summary>The file exists but is not a readable save.</summary>
        public bool IsCorrupt;

        /// <summary>A readable save in a format this build does not load.</summary>
        public bool IsIncompatible;

        /// <summary>When the file was last written (from the file system), UTC.</summary>
        public DateTime LastWriteUtc;

        public bool CanLoad => Exists && !IsCorrupt && !IsIncompatible && Header != null;

        /// <summary>Save time from the header, falling back to the file's write time.</summary>
        public DateTime SavedAtUtc => Header?.SavedAt ?? LastWriteUtc;
    }
}
