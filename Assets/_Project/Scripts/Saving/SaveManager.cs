using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Company;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Dependency;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Events;
using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.News;
using AntiqueTradingSimulator.Restoration;
using AntiqueTradingSimulator.UI;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace AntiqueTradingSimulator.Saving
{
    /// <summary>
    /// Writes and reads saves: manual slots the player picks, and rotating autosaves taken every
    /// few in-game days. The files themselves are handled by <see cref="SaveSlotStore"/>, the
    /// timing of autosaves by <see cref="AutosaveScheduler"/>. Order of restoring objects is not
    /// to be changed.
    /// </summary>
    public class SaveManager : MonoBehaviour
    {
        [Header("Settings (loaded from Resources if empty)")]
        [Tooltip("Save folder, slot counts and autosave defaults — shared with the main menu.")]
        [SerializeField] private SaveSettings settings;

        [Header("References (found automatically if empty)")]
        [SerializeField] private TimeManager timeManager;
        [SerializeField] private NPCManager npcManager;
        [SerializeField] private EconomyManager economyManager;
        [SerializeField] private DependencyManager dependencyManager;
        [SerializeField] private TransportManager transportManager;
        [SerializeField] private RestorationManager restorationManager;
        [SerializeField] private ContractManager contractManager;
        [SerializeField] private CompanyManager companyManager;
        [SerializeField] private CampaignManager campaignManager;
        [SerializeField] private EventManager eventManager;
        [SerializeField] private NewsManager newsManager;
        [SerializeField] private PlayerTrader playerTrader;

        private AutosaveScheduler _autosave;
        private double _playtimeAtLastSave;

        public SaveSettings Settings => settings != null ? settings : (settings = SaveSettings.Load());

        public SaveSlotStore Store => SaveFiles.Store;

        public int ManualSlotCount => Store.ManualSlotCount;

        /// <summary>Slot of the last save written or loaded in this session; null in a new game not saved yet.</summary>
        public SaveSlot? LastSaveSlot { get; private set; }

        /// <summary>In-game day of that save; 0 when there is none.</summary>
        public int LastSaveDay { get; private set; }

        /// <summary>Play time since the last save or load (since the start, in a new game never saved).</summary>
        public double SecondsSinceLastSave => Math.Max(0d, Playtime - _playtimeAtLastSave);

        /// <summary>True when leaving now would lose more than a few seconds of play — ask first.</summary>
        public bool HasUnsavedProgress => SecondsSinceLastSave > Settings.UnsavedProgressWarningSeconds;

        private int CurrentDay => timeManager != null ? timeManager.CurrentDay : 0;

        private double Playtime => timeManager != null ? timeManager.PlaytimeSeconds : 0d;

        private TraderInventory PlayerInventory => playerTrader != null ? playerTrader.Inventory : null;

        /// <summary>Raised after a successful write, with the slot written to.</summary>
        public event Action<SaveSlot> OnSaved;

        /// <summary>Raised when a write failed, with a reason to show the player.</summary>
        public event Action<string> OnSaveFailed;

        /// <summary>Raised after a save has been fully restored, with the slot it came from.</summary>
        public event Action<SaveSlot, GameState> OnLoaded;

        /// <summary>Raised when a load could not be completed, with a reason to show the player.</summary>
        public event Action<string> OnLoadFailed;

        private void Awake()
        {
            if (economyManager == null) economyManager = FindFirstObjectByType<EconomyManager>();
            if (timeManager == null)
                timeManager = economyManager != null && economyManager.TimeManager != null
                    ? economyManager.TimeManager
                    : FindFirstObjectByType<TimeManager>();
            if (dependencyManager == null) dependencyManager = FindFirstObjectByType<DependencyManager>();
            if (npcManager == null) npcManager = FindFirstObjectByType<NPCManager>();
            if (transportManager == null) transportManager = FindFirstObjectByType<TransportManager>();
            if (restorationManager == null) restorationManager = FindFirstObjectByType<RestorationManager>();
            if (contractManager == null) contractManager = FindFirstObjectByType<ContractManager>();
            if (eventManager == null) eventManager = FindFirstObjectByType<EventManager>();
            if (newsManager == null) newsManager = FindFirstObjectByType<NewsManager>();
            if (playerTrader == null) playerTrader = FindFirstObjectByType<PlayerTrader>();
            if (companyManager == null)
                companyManager = playerTrader != null ? playerTrader.Company : FindFirstObjectByType<CompanyManager>();
            if (campaignManager == null) campaignManager = FindFirstObjectByType<CampaignManager>();

            _autosave = new AutosaveScheduler(SavePreferences.AutosaveIntervalDays, CurrentDay)
            {
                Enabled = SavePreferences.AutosaveEnabled
            };
        }

        private void OnEnable()
        {
            if (timeManager != null) timeManager.OnDayChanged += HandleDayChanged;
            SavePreferences.Changed += ApplyPreferences;
        }

        private void OnDisable()
        {
            if (timeManager != null) timeManager.OnDayChanged -= HandleDayChanged;
            SavePreferences.Changed -= ApplyPreferences;
        }

        private void HandleDayChanged(int day) => _autosave?.NotifyDayChanged(day);

        // Every OnDayChanged listener has finished by LateUpdate, so the autosave never captures
        // a day that is only half processed. An open modal (a purchase in progress) delays it.
        private void LateUpdate()
        {
            if (_autosave != null && _autosave.ShouldAutosaveNow(CurrentDay, ModalTracker.IsAnyOpen))
                Autosave();
        }

        /// <summary>The settings screen changed the player's preferences: apply them to this game now.</summary>
        private void ApplyPreferences()
        {
            if (_autosave == null) return;

            _autosave.Enabled = SavePreferences.AutosaveEnabled;
            _autosave.IntervalDays = SavePreferences.AutosaveIntervalDays;
        }

        private void MarkSaved(SaveSlot slot, int day)
        {
            LastSaveSlot = slot;
            LastSaveDay = day;
            _playtimeAtLastSave = Playtime;
        }

        // ---------------------------------------------------------------- slots

        public bool HasAnySave() => Store.AnyExists();

        public bool HasSave(SaveSlot slot) => Store.Exists(slot);

        /// <summary>Every slot, filled or empty: manual slots first, then autosaves.</summary>
        public List<SaveSlotInfo> ListSlots() => Store.List();

        /// <summary>The newest loadable save, manual or autosave — what "Continue" loads. Null if none.</summary>
        public SaveSlotInfo LatestSave() => Store.Latest();

        public bool DeleteSave(SaveSlot slot)
        {
            try
            {
                return Store.Delete(slot);
            }
            catch (Exception exception)
            {
                Debug.LogError($"SaveManager: could not delete {slot.DisplayName} — {exception.Message}");
                return false;
            }
        }

        // ---------------------------------------------------------------- save

        /// <summary>Captures the game and writes it to a slot. Returns false (and logs) if anything failed.</summary>
        public bool Save(SaveSlot slot)
        {
            if (!Store.IsValid(slot))
            {
                FailSave($"{slot.DisplayName} does not exist (there are {Store.ManualSlotCount} manual and {Store.AutosaveSlotCount} autosave slots).");
                return false;
            }

            try
            {
                Store.Write(slot, SaveSerializer.ToJson(CaptureGame()));
            }
            catch (Exception exception)
            {
                FailSave($"Saving to {slot.DisplayName} failed — {exception.Message}");
                return false;
            }

            if (!slot.IsAutosave) _autosave?.Reset(CurrentDay);
            MarkSaved(slot, CurrentDay);

            Debug.Log($"SaveManager: saved day {CurrentDay} to {Store.PathFor(slot)}");
            OnSaved?.Invoke(slot);
            return true;
        }

        /// <summary>Writes the next autosave, over the oldest one once every autosave slot is used.</summary>
        public bool Autosave()
        {
            IsAutosaving = true;
            try
            {
                SaveSlot slot;
                try
                {
                    slot = Store.NextAutosaveSlot();
                }
                catch (Exception exception)
                {
                    FailSave($"Autosave failed — {exception.Message}");
                    return false;
                }

                return Save(slot);
            }
            finally
            {
                IsAutosaving = false;
            }
        }

        /// <summary>True while an autosave is being written — lets OnSaveFailed listeners tell it from a manual save.</summary>
        public bool IsAutosaving { get; private set; }

        // ---------------------------------------------------------------- load

        /// <summary>
        /// Reads a slot and restores it. Returns false (and logs, and raises OnLoadFailed) if the
        /// slot is empty or unreadable; the running game is then left untouched.
        /// </summary>
        public bool Load(SaveSlot slot)
        {
            if (!Store.Exists(slot))
            {
                FailLoad($"{slot.DisplayName} is empty.");
                return false;
            }

            GameState state;
            try
            {
                state = SaveSerializer.FromJson<GameState>(Store.Read(slot));
            }
            catch (Exception exception)
            {
                FailLoad($"{slot.DisplayName} could not be read — {exception.Message}");
                return false;
            }

            if (state == null)
            {
                FailLoad($"{slot.DisplayName} is empty or not a save.");
                return false;
            }

            if (state.Version != SaveSerializer.SaveFormatVersion)
            {
                // No migrations exist yet. When the format changes, convert the old state here
                // (and only then let it through) rather than loosening this check.
                FailLoad(state.Version > SaveSerializer.SaveFormatVersion
                    ? $"{slot.DisplayName} was written by a newer version of the game (format {state.Version}, this build reads {SaveSerializer.SaveFormatVersion})."
                    : $"{slot.DisplayName} uses format {state.Version}, which this build can no longer read (it reads {SaveSerializer.SaveFormatVersion}).");
                return false;
            }

            RestoreGame(state);
            _autosave?.Reset(CurrentDay);
            MarkSaved(slot, state.SavedOnDay);

            Debug.Log($"SaveManager: loaded {slot.DisplayName} (day {state.SavedOnDay}, saved {state.SavedAtUtc}).");
            OnLoaded?.Invoke(slot, state);
            return true;
        }

        /// <summary>Loads the newest loadable save ("Continue"). False if there is none.</summary>
        public bool LoadLatest()
        {
            var latest = LatestSave();
            if (latest == null)
            {
                FailLoad("There is no save to continue from.");
                return false;
            }

            return Load(latest.Slot);
        }

        private void FailSave(string reason)
        {
            Debug.LogError($"SaveManager: {reason}");
            OnSaveFailed?.Invoke(reason);
        }

        private void FailLoad(string reason)
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
            CampaignLength = timeManager != null ? timeManager.CampaignLength : 0,
            PlaytimeSeconds = Playtime,
            InGameDate = timeManager != null ? TimeManager.FormatLong(timeManager.CurrentDate) : null,
            CompanyName = companyManager != null ? companyManager.CompanyName : null,
            Cash = PlayerInventory != null ? PlayerInventory.Cash : 0f,
            NetWorth = PlayerInventory != null ? PlayerInventory.Wealth : 0f,
            CampaignFinished = campaignManager != null && campaignManager.IsFinished,

            Time = timeManager != null ? timeManager.CaptureState() : null,
            Npcs = npcManager != null ? npcManager.CaptureState() : null,
            Economy = economyManager != null ? economyManager.CaptureState() : null,
            Dependencies = dependencyManager != null ? dependencyManager.CaptureState() : null,
            Logistics = transportManager != null ? transportManager.CaptureState() : null,
            Restoration = restorationManager != null ? restorationManager.CaptureState() : null,
            Contracts = contractManager != null ? contractManager.CaptureState() : null,
            Company = companyManager != null ? companyManager.CaptureState() : null,
            Campaign = campaignManager != null ? campaignManager.CaptureState() : null,
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
        ///    Restoration — same for antiques at a workshop (a save without it releases none).
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

            if (dependencyManager != null) dependencyManager.RestoreState(state.Dependencies);
            else WarnMissing("DependencyManager", state.Dependencies);

            if (transportManager != null) transportManager.RestoreState(state.Logistics);
            else WarnMissing("TransportManager", state.Logistics);

            // Runs even when the save has no restoration state (older saves), so the manager
            // drops this session's jobs.
            if (restorationManager != null) restorationManager.RestoreState(state.Restoration);
            else WarnMissing("RestorationManager", state.Restoration);

            if (contractManager != null) contractManager.RestoreState(state.Contracts);
            else WarnMissing("ContractManager", state.Contracts);

            if (companyManager != null) companyManager.RestoreState(state.Company);
            if (campaignManager != null) campaignManager.RestoreState(state.Campaign);
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
