using System;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Saving;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// The Escape menu: resume, save and load (through the slot list), settings, exit to the main
    /// menu, quit. Leaving or loading with unsaved progress asks first.
    ///
    /// Escape: with the menu open it closes the topmost panel on it (confirmation, slot list,
    /// settings), then the menu itself. With the menu closed it first closes an open game modal,
    /// and only then opens the menu.
    /// </summary>
    public class GameMenuUI : MonoBehaviour
    {
        [Header("Panel")]
        [Tooltip("The menu panel, as a CHILD of this object. Hidden on start. " +
                 "It must not be this object: deactivating that would stop Update and " +
                 "Escape could never reopen the menu.")]
        [SerializeField] private GameObject panelRoot;

        [Header("Buttons")]
        [SerializeField] private Button resumeButton;
        [SerializeField] private Button saveButton;
        [SerializeField] private Button loadButton;
        [SerializeField] private Button settingsButton;
        [Tooltip("Hidden while there is no main menu scene in the build settings.")]
        [SerializeField] private Button mainMenuButton;
        [Tooltip("Quits to the desktop.")]
        [SerializeField] private Button exitButton;

        [Tooltip("Optional one-line feedback under the buttons.")]
        [SerializeField] private TMP_Text statusText;

        [Header("Panels on top of the menu (optional)")]
        [Tooltip("Without it, Save writes to Slot 1 and Load continues from the newest save.")]
        [SerializeField] private SaveSlotListUI slotList;
        [SerializeField] private SettingsPanelUI settingsPanel;
        [Tooltip("Without it, nothing asks about unsaved progress.")]
        [SerializeField] private ConfirmDialogUI confirmDialog;

        [Header("Dependencies (auto-found if empty)")]
        [SerializeField] private SaveManager saveManager;
        [SerializeField] private TimeManager timeManager;

        [Header("Behaviour")]
        [SerializeField] private Key toggleKey = Key.Escape;

        [Tooltip("Pause the clock while the menu is open, as the modals already do.")]
        [SerializeField] private bool pauseTimeWhileOpen = true;

        private bool _pausedByMenu;

        public bool IsOpen => panelRoot != null && panelRoot.activeSelf;

        private void Awake()
        {
            if (panelRoot == null || panelRoot == gameObject)
            {
                Debug.LogError("GameMenuUI: panelRoot must be a child object, not this one and " +
                               "not empty — this component has to keep receiving Update while " +
                               "the menu is hidden, so it cannot be what gets deactivated.", this);
                enabled = false;
                return;
            }

            if (saveManager == null) saveManager = FindFirstObjectByType<SaveManager>();
            if (timeManager == null) timeManager = FindFirstObjectByType<TimeManager>();

            if (resumeButton != null) resumeButton.onClick.AddListener(Close);
            if (saveButton != null) saveButton.onClick.AddListener(SaveGame);
            if (loadButton != null) loadButton.onClick.AddListener(LoadGame);
            if (settingsButton != null) settingsButton.onClick.AddListener(OpenSettings);
            if (mainMenuButton != null) mainMenuButton.onClick.AddListener(ExitToMainMenu);
            if (exitButton != null) exitButton.onClick.AddListener(ExitGame);

            // Hidden until Escape, even if left visible in the scene.
            if (slotList != null) slotList.gameObject.SetActive(false);
            if (settingsPanel != null) settingsPanel.gameObject.SetActive(false);
            if (confirmDialog != null) confirmDialog.gameObject.SetActive(false);
            panelRoot.SetActive(false);
        }

        private void OnEnable()
        {
            if (saveManager == null) return;

            saveManager.OnLoadFailed += HandleLoadFailed;
            saveManager.OnSaveFailed += HandleSaveFailed;
        }

        private void OnDisable()
        {
            if (saveManager == null) return;

            saveManager.OnLoadFailed -= HandleLoadFailed;
            saveManager.OnSaveFailed -= HandleSaveFailed;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || toggleKey == Key.None) return;

            var control = keyboard[toggleKey];
            if (control == null || !control.wasPressedThisFrame) return;

            if (IsOpen)
            {
                if (!CloseTopPanel()) Close();
                return;
            }

            // Modals are only dismissed while the menu is closed, so Escape with the menu up
            // never reaches past it to a modal underneath.
            if (ModalTracker.CloseCurrent()) return;

            Open();
        }

        // ---------------------------------------------------------------- open / close

        public void Open()
        {
            panelRoot.SetActive(true);
            panelRoot.transform.SetAsLastSibling(); // above the views and the HUD
            RefreshButtons();
            SetStatus(string.Empty);

            if (pauseTimeWhileOpen && timeManager != null && timeManager.IsRunning)
            {
                timeManager.Pause();
                _pausedByMenu = true;
            }
        }

        public void Close()
        {
            CloseAllPanels();
            panelRoot.SetActive(false);

            if (_pausedByMenu && timeManager != null)
                timeManager.Resume();

            _pausedByMenu = false;
        }

        /// <summary>Closes the topmost panel on the menu. False when there was none.</summary>
        private bool CloseTopPanel()
        {
            if (confirmDialog != null && confirmDialog.IsOpen)
            {
                confirmDialog.Cancel();
                return true;
            }

            if (slotList != null && slotList.IsOpen)
            {
                slotList.Close();
                return true;
            }

            if (settingsPanel != null && settingsPanel.IsOpen)
            {
                settingsPanel.Close();
                return true;
            }

            return false;
        }

        private void CloseAllPanels()
        {
            while (CloseTopPanel()) { }
        }

        // ---------------------------------------------------------------- actions

        public void SaveGame()
        {
            if (saveManager == null)
            {
                SetStatus("No SaveManager in the scene.");
                return;
            }

            if (slotList != null)
            {
                slotList.ShowSave(slot => saveManager.Save(slot));
                return;
            }

            // No slot list wired up: the quick save of the first version.
            SaveSlot quickSlot = SaveSlot.Manual(1);
            if (saveManager.Save(quickSlot))
                SetStatus($"Saved day {saveManager.LastSaveDay} to {quickSlot.DisplayName}.");
            RefreshButtons(); // Load becomes available after the first save
        }

        public void LoadGame()
        {
            if (saveManager == null)
            {
                SetStatus("No SaveManager in the scene.");
                return;
            }

            if (slotList != null)
            {
                slotList.ShowLoad(RequestLoad);
                return;
            }

            // No slot list wired up: continue from the newest save. Latest() only returns saves
            // whose header reads and whose version matches.
            var latest = saveManager.LatestSave();
            if (latest == null)
            {
                SetStatus(saveManager.HasAnySave() ? "No save can be loaded." : "No save yet.");
                return;
            }

            RequestLoad(latest.Slot);
        }

        public void OpenSettings()
        {
            if (settingsPanel != null) settingsPanel.Show();
        }

        public void ExitToMainMenu()
        {
            ConfirmIfUnsaved("Exit to the main menu?", "Exit", () =>
            {
                if (!GameSession.ReturnToMainMenu())
                    SetStatus("There is no main menu yet.");
            });
        }

        public void ExitGame()
        {
            ConfirmIfUnsaved("Quit the game?", "Quit", Quit);
        }

        private void RequestLoad(SaveSlot slot)
        {
            ConfirmIfUnsaved($"Load {slot.DisplayName}?", "Load", () =>
            {
                ModalTracker.CloseCurrent();
                _pausedByMenu = false;

                // Reloads the game scene and restores the save into it (see GameSession).
                GameSession.Load(slot);
            });
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>Runs <paramref name="action"/> at once, or after a confirmation when play since the last save would be lost.</summary>
        private void ConfirmIfUnsaved(string title, string confirmText, Action action)
        {
            if (confirmDialog == null || saveManager == null || !saveManager.HasUnsavedProgress)
            {
                action();
                return;
            }

            confirmDialog.Show(title, UnsavedProgressMessage(), confirmText, action);
        }

        private string UnsavedProgressMessage()
        {
            string since = SaveSlotFormat.Playtime(saveManager.SecondsSinceLastSave);

            return saveManager.LastSaveSlot.HasValue
                ? $"Your last save is from day {saveManager.LastSaveDay} ({saveManager.LastSaveSlot.Value.DisplayName}). " +
                  $"The last {since} of play will be lost."
                : "This game hasn't been saved yet. Everything since the start will be lost.";
        }

        // ---------------------------------------------------------------- helpers

        private void HandleLoadFailed(string reason) => SetStatus(reason);

        private void HandleSaveFailed(string reason)
        {
            SetStatus(reason);
            if (slotList != null && slotList.IsOpen) slotList.SetStatus(reason);
        }

        private void RefreshButtons()
        {
            if (loadButton != null)
                loadButton.interactable = saveManager != null && saveManager.HasAnySave();

            if (saveButton != null)
                saveButton.interactable = saveManager != null;

            if (mainMenuButton != null)
                mainMenuButton.gameObject.SetActive(GameSession.HasMainMenu);
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
        }
    }
}
