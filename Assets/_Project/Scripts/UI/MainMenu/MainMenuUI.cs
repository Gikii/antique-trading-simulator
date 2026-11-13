using AntiqueTradingSimulator.Saving;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// The main menu: Continue (the newest save, shown under the button), New game, Load game,
    /// Settings, About, Quit. Every way into the game goes through GameSession, which loads the
    /// game scene fresh. A message left by GameSession (e.g. a save that failed to load) is
    /// shown once when the menu opens. Escape closes the topmost panel.
    /// </summary>
    public class MainMenuUI : MonoBehaviour
    {
        [Header("Buttons")]
        [SerializeField] private Button continueButton;
        [Tooltip("Week and date of the save Continue would load.")]
        [SerializeField] private TMP_Text continueDetailText;
        [SerializeField] private Button newGameButton;
        [SerializeField] private Button loadButton;
        [SerializeField] private Button settingsButton;
        [SerializeField] private Button aboutButton;
        [SerializeField] private Button quitButton;

        [SerializeField] private TMP_Text versionText;

        [Header("Panels")]
        [SerializeField] private SaveSlotListUI slotList;
        [SerializeField] private SettingsPanelUI settingsPanel;
        [SerializeField] private ConfirmDialogUI confirmDialog;

        [Header("About")]
        [SerializeField] private string aboutTitle = "Antique Trading Simulator";
        [TextArea(3, 8)]
        [SerializeField] private string aboutText =
            "A trading game about the antiques market, where prices follow what traders do and what the news says.\n\n" +
            "Engineering thesis: \"System ekonomii w grze komputerowej reagującej na działania gracza\".";

        [Header("Behaviour")]
        [SerializeField] private Key backKey = Key.Escape;

        private void Awake()
        {
            if (continueButton != null) continueButton.onClick.AddListener(Continue);
            if (newGameButton != null) newGameButton.onClick.AddListener(GameSession.StartNewGame);
            if (loadButton != null) loadButton.onClick.AddListener(OpenLoad);
            if (settingsButton != null) settingsButton.onClick.AddListener(OpenSettings);
            if (aboutButton != null) aboutButton.onClick.AddListener(ShowAbout);
            if (quitButton != null) quitButton.onClick.AddListener(Quit);

            if (slotList != null)
            {
                slotList.gameObject.SetActive(false);
                slotList.Closed += Refresh; // a deleted save may change what Continue loads
            }
            if (settingsPanel != null) settingsPanel.gameObject.SetActive(false);
            if (confirmDialog != null) confirmDialog.gameObject.SetActive(false);

            if (versionText != null) versionText.text = $"Version {Application.version}";
        }

        private void Start()
        {
            Refresh();

            string message = GameSession.TakeMessage();
            if (!string.IsNullOrEmpty(message))
            {
                if (confirmDialog != null) confirmDialog.ShowMessage("Something went wrong", message);
                else Debug.LogWarning($"MainMenuUI: {message}");
            }
        }

        private void OnDestroy()
        {
            if (slotList != null) slotList.Closed -= Refresh;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || backKey == Key.None) return;

            var control = keyboard[backKey];
            if (control == null || !control.wasPressedThisFrame) return;

            if (confirmDialog != null && confirmDialog.IsOpen) confirmDialog.Cancel();
            else if (slotList != null && slotList.IsOpen) slotList.Close();
            else if (settingsPanel != null && settingsPanel.IsOpen) settingsPanel.Close();
        }

        /// <summary>Shows Continue only when there is a save to continue from, with its day and date.</summary>
        public void Refresh()
        {
            SaveSlotInfo latest = null;
            try
            {
                latest = SaveFiles.Store.Latest();
            }
            catch (System.Exception exception)
            {
                Debug.LogWarning($"MainMenuUI: saves couldn't be read — {exception.Message}");
            }

            if (continueButton != null) continueButton.gameObject.SetActive(latest != null);
            if (continueDetailText != null)
            {
                continueDetailText.gameObject.SetActive(latest != null);
                continueDetailText.text = latest != null
                    ? $"{SaveSlotFormat.WeekAndDate(latest.Header)} · {latest.Slot.DisplayName}"
                    : string.Empty;
            }

            if (loadButton != null)
            {
                bool anySave = false;
                try { anySave = SaveFiles.Store.AnyExists(); }
                catch (System.Exception) { /* reported above */ }
                loadButton.interactable = anySave;
            }
        }

        private void Continue()
        {
            if (!GameSession.ContinueLatest())
            {
                Refresh();
                if (confirmDialog != null) confirmDialog.ShowMessage("Nothing to continue", "There is no save that can be loaded.");
            }
        }

        private void OpenLoad()
        {
            if (slotList != null) slotList.ShowLoad(GameSession.Load);
        }

        private void OpenSettings()
        {
            if (settingsPanel != null) settingsPanel.Show();
        }

        private void ShowAbout()
        {
            if (confirmDialog != null) confirmDialog.ShowMessage(aboutTitle, aboutText, "Close");
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
