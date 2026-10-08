using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Saving;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// The Escape menu: save, load, exit. It also handles logic for closing modals with ESC key, this will need to be changed.
    /// </summary>
    public class GameMenuUI : MonoBehaviour
    {
        [Header("Panel")]
        [Tooltip("The menu panel, as a CHILD of this object. Hidden on start. " +
                 "It must not be this object: deactivating that would stop Update and " +
                 "Escape could never reopen the menu.")]
        [SerializeField] private GameObject panelRoot;

        [Header("Buttons")]
        [SerializeField] private Button saveButton;
        [SerializeField] private Button loadButton;
        [SerializeField] private Button exitButton;

        [Tooltip("Optional one-line feedback under the buttons.")]
        [SerializeField] private TMP_Text statusText;

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

            if (saveButton != null) saveButton.onClick.AddListener(SaveGame);
            if (loadButton != null) loadButton.onClick.AddListener(LoadGame);
            if (exitButton != null) exitButton.onClick.AddListener(ExitGame);

            // Hidden until Escape, even if it was left visible in the scene.
            panelRoot.SetActive(false);
        }

        private void OnEnable()
        {
            if (saveManager != null)
                saveManager.OnLoadFailed += HandleLoadFailed;
        }

        private void OnDisable()
        {
            if (saveManager != null)
                saveManager.OnLoadFailed -= HandleLoadFailed;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || toggleKey == Key.None) return;

            var control = keyboard[toggleKey];
            if (control == null || !control.wasPressedThisFrame) return;

            // Modals are only dismissed while the menu is closed, so Escape with the menu up
            // always means "close the menu" rather than reaching past it.
            if (!IsOpen && ModalTracker.CloseCurrent()) return;

            if (IsOpen) Close();
            else Open();
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
            panelRoot.SetActive(false);

            if (_pausedByMenu && timeManager != null)
                timeManager.Resume();

            _pausedByMenu = false;
        }

        // ---------------------------------------------------------------- actions

        public void SaveGame()
        {
            if (saveManager == null)
            {
                SetStatus("No SaveManager in the scene.");
                return;
            }

            bool saved = saveManager.Save();
            int day = timeManager != null ? timeManager.CurrentDay : 0;

            SetStatus(saved ? $"Saved on day {day}." : "Save failed — see the console.");
            RefreshButtons(); // Load becomes available after the first save
        }

        public void LoadGame()
        {
            if (saveManager == null)
            {
                SetStatus("No SaveManager in the scene.");
                return;
            }

            if (!saveManager.HasSave())
            {
                SetStatus("No save file yet.");
                return;
            }

            ModalTracker.CloseCurrent();

            // A failed load leaves the running game untouched; HandleLoadFailed writes the reason into the status line, so the menu stays open for another try.
            if (!saveManager.Load()) return;

            _pausedByMenu = false;
            Close();
        }

        public void ExitGame()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        // ---------------------------------------------------------------- helpers

        private void HandleLoadFailed(string reason) => SetStatus(reason);

        private void RefreshButtons()
        {
            if (loadButton != null)
                loadButton.interactable = saveManager != null && saveManager.HasSave();

            if (saveButton != null)
                saveButton.interactable = saveManager != null;
        }

        private void SetStatus(string message)
        {
            if (statusText != null)
                statusText.text = message;
        }
    }
}
