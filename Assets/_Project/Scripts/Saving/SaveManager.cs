using System;
using System.IO;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Company;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Events;
using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.News;
using UnityEngine;

namespace AntiqueTradingSimulator.Saving
{
    /// <summary>
    /// Writes and reads the save file. Order of restoring objects is not to be changed
    /// </summary>
    public class SaveManager : MonoBehaviour
    {
        [Header("File")]
        [Tooltip("File name inside Application.persistentDataPath.")]
        [SerializeField] private string fileName = "save.json";

        [Header("References (found automatically if empty)")]
        [SerializeField] private TimeManager timeManager;
        [SerializeField] private NPCManager npcManager;
        [SerializeField] private EconomyManager economyManager;
        [SerializeField] private TransportManager transportManager;
        [SerializeField] private ContractManager contractManager;
        [SerializeField] private CompanyManager companyManager;
        [SerializeField] private EventManager eventManager;
        [SerializeField] private NewsManager newsManager;
        [SerializeField] private PlayerTrader playerTrader;

        /// <summary>Full path of the save file.</summary>
        public string SavePath => Path.Combine(Application.persistentDataPath, fileName);

        /// <summary>Raised after a successful write, with the path written to.</summary>
        public event Action<string> OnSaved;

        /// <summary>Raised after a save has been fully restored.</summary>
        public event Action<GameState> OnLoaded;

        /// <summary>Raised when a load could not be completed, with a reason to show the player.</summary>
        public event Action<string> OnLoadFailed;

        private void Awake()
        {
            if (economyManager == null) economyManager = FindFirstObjectByType<EconomyManager>();
            if (timeManager == null)
                timeManager = economyManager != null && economyManager.TimeManager != null
                    ? economyManager.TimeManager
                    : FindFirstObjectByType<TimeManager>();
            if (npcManager == null) npcManager = FindFirstObjectByType<NPCManager>();
            if (transportManager == null) transportManager = FindFirstObjectByType<TransportManager>();
            if (contractManager == null) contractManager = FindFirstObjectByType<ContractManager>();
            if (eventManager == null) eventManager = FindFirstObjectByType<EventManager>();
            if (newsManager == null) newsManager = FindFirstObjectByType<NewsManager>();
            if (playerTrader == null) playerTrader = FindFirstObjectByType<PlayerTrader>();
            if (companyManager == null)
                companyManager = playerTrader != null ? playerTrader.Company : FindFirstObjectByType<CompanyManager>();
        }

        // ---------------------------------------------------------------- file level

        public bool HasSave() => File.Exists(SavePath);

        /// <summary>Captures the game and writes it. Returns false (and logs) if anything failed.</summary>
        public bool Save()
        {
            try
            {
                string json = SaveSerializer.ToJson(CaptureGame());
                WriteAtomically(SavePath, json);

                Debug.Log($"SaveManager: saved to {SavePath}");
                OnSaved?.Invoke(SavePath);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"SaveManager: save failed — {exception.Message}");
                return false;
            }
        }

        /// <summary>
        /// Reads the save file and restores it. Returns false (and logs, and raises OnLoadFailed) if the file is missing or unreadable.
        /// </summary>
        public bool Load()
        {
            string path = SavePath;

            if (!File.Exists(path))
            {
                Fail($"No save file at {path}.");
                return false;
            }

            GameState state;
            try
            {
                state = SaveSerializer.FromJson<GameState>(File.ReadAllText(path));
            }
            catch (Exception exception)
            {
                Fail($"Save file could not be read — {exception.Message}");
                return false;
            }

            if (state == null)
            {
                Fail("Save file is empty or not a save.");
                return false;
            }

            if (state.Version != SaveSerializer.SaveFormatVersion)
            {
                // No migrations exist yet. When the format changes, convert the old state here
                // (and only then let it through) rather than loosening this check.
                Fail(state.Version > SaveSerializer.SaveFormatVersion
                    ? $"Save was written by a newer version of the game (format {state.Version}, this build reads {SaveSerializer.SaveFormatVersion})."
                    : $"Save uses format {state.Version}, which this build can no longer read (it reads {SaveSerializer.SaveFormatVersion}).");
                return false;
            }

            RestoreGame(state);

            Debug.Log($"SaveManager: loaded {path} (day {state.SavedOnDay}, saved {state.SavedAtUtc}).");
            OnLoaded?.Invoke(state);
            return true;
        }

        /// <summary>Deletes the save file. Returns false if there was nothing to delete.</summary>
        public bool DeleteSave()
        {
            if (!HasSave()) return false;

            try
            {
                File.Delete(SavePath);
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogError($"SaveManager: could not delete the save — {exception.Message}");
                return false;
            }
        }

        /// <summary>
        /// Writes through a temporary file so an interrupted write cannot leave a half-written
        /// save behind: the old file stays intact until the new one is complete on disk.
        /// </summary>
        private static void WriteAtomically(string path, string contents)
        {
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string temporaryPath = path + ".tmp";
            File.WriteAllText(temporaryPath, contents);

            if (!File.Exists(path))
            {
                File.Move(temporaryPath, path);
                return;
            }

            try
            {
                File.Replace(temporaryPath, path, null);
            }
            catch (PlatformNotSupportedException)
            {
                // File.Replace is not implemented everywhere Unity runs. The fallback has a
                // brief window with no save file, which is still better than a truncated one.
                File.Delete(path);
                File.Move(temporaryPath, path);
            }
            catch (IOException)
            {
                File.Delete(path);
                File.Move(temporaryPath, path);
            }
        }

        private void Fail(string reason)
        {
            Debug.LogWarning($"SaveManager: load failed — {reason}");
            OnLoadFailed?.Invoke(reason);
        }

        // ---------------------------------------------------------------- capture

        /// <summary>
        /// Builds a GameState from the live systems. A manager that is not in the scene simply
        /// contributes null, and loading that save will skip the matching step.
        /// </summary>
        public GameState CaptureGame() => new GameState
        {
            Version = SaveSerializer.SaveFormatVersion,
            SavedAtUtc = DateTime.UtcNow.ToString("o"),
            SavedOnDay = timeManager != null ? timeManager.CurrentDay : 0,

            Time = timeManager != null ? timeManager.CaptureState() : null,
            Npcs = npcManager != null ? npcManager.CaptureState() : null,
            Economy = economyManager != null ? economyManager.CaptureState() : null,
            Logistics = transportManager != null ? transportManager.CaptureState() : null,
            Contracts = contractManager != null ? contractManager.CaptureState() : null,
            Company = companyManager != null ? companyManager.CaptureState() : null,
            Events = eventManager != null ? eventManager.CaptureState() : null,
            News = newsManager != null ? newsManager.CaptureState() : null
        };

        // ---------------------------------------------------------------- restore

        /// <summary>
        /// Pushes a save back into the running scene, in dependency order:
        ///
        /// 1. Time     — first, so anything that writes a ledger entry afterwards dates it right.
        /// 2. NPCs     — recreated with their saved Ids; each registers an empty inventory.
        /// 3. Economy  — fills those inventories, then the market (which re-links owner-set
        ///               listings to the antiques the inventories now hold).
        /// 4. Logistics— re-attaches shipments to recipients, whose holdings must already exist.
        /// 5. Contracts— independent of the above, but traders must be registered (steps 2-3).
        /// 6. Company  — needs the player's restored inventory and warehouse for its upgrades.
        /// 7. Events   — restored without re-applying; their effect is already in the market.
        /// 8. News     — restored without re-publishing to receivers.
        /// </summary>
        public void RestoreGame(GameState state)
        {
            if (state == null) return;

            if (timeManager != null) timeManager.RestoreState(state.Time);
            else WarnMissing("TimeManager", state.Time);

            if (npcManager != null) npcManager.RestoreState(state.Npcs);
            else WarnMissing("NPCManager", state.Npcs);

            if (economyManager != null)
                economyManager.RestoreState(state.Economy, playerTrader != null ? playerTrader.WarehouseFactory : null);
            else WarnMissing("EconomyManager", state.Economy);

            if (transportManager != null) transportManager.RestoreState(state.Logistics);
            else WarnMissing("TransportManager", state.Logistics);

            if (contractManager != null) contractManager.RestoreState(state.Contracts);
            else WarnMissing("ContractManager", state.Contracts);

            if (companyManager != null) companyManager.RestoreState(state.Company);
            else WarnMissing("CompanyManager", state.Company);

            if (eventManager != null) eventManager.RestoreState(state.Events);
            else WarnMissing("EventManager", state.Events);

            if (newsManager != null) newsManager.RestoreState(state.News);
            else WarnMissing("NewsManager", state.News);
        }

        private static void WarnMissing(string managerName, object savedState)
        {
            if (savedState == null) return;

            Debug.LogWarning($"SaveManager: the save contains {managerName} state, but there is no {managerName} in the scene — that part of the save was not restored.");
        }
    }
}
