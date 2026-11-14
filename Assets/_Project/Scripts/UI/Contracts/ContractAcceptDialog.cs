using System;
using AntiqueTradingSimulator.Core;
using UnityEngine;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// "Accept this contract?" — the save menu's ConfirmDialog, reused for gameplay. The scene's only
    /// ConfirmDialog sits under the game menu's backdrop (hidden while playing), so <see cref="Create"/>
    /// copies it under the gameplay modals at runtime: no extra prefab or scene object to maintain.
    ///
    /// This component adds what gameplay modals do (see BuyModalUI): it registers with ModalTracker so
    /// Escape cancels it, and pauses the clock while it is open so the deadline and prices it describes
    /// can't change before the player decides.
    /// </summary>
    [RequireComponent(typeof(ConfirmDialogUI))]
    public class ContractAcceptDialog : MonoBehaviour, IModalPanel
    {
        private ConfirmDialogUI _dialog;
        private TimeManager _timeManager;
        private bool _pausedByDialog;

        /// <summary>
        /// Copies the scene's ConfirmDialog under <paramref name="parent"/> (the modals' container).
        /// Null if the scene has none — the caller then accepts without asking.
        /// </summary>
        public static ContractAcceptDialog Create(Transform parent)
        {
            var template = FindFirstObjectByType<ConfirmDialogUI>(FindObjectsInactive.Include);
            if (template == null)
            {
                Debug.LogWarning("ContractAcceptDialog: no ConfirmDialogUI in the scene to copy — contracts will be accepted without confirmation.");
                return null;
            }

            var copy = Instantiate(template.gameObject, parent, false);
            copy.name = "ContractAcceptDialog";
            copy.SetActive(false);

            var dialog = copy.AddComponent<ContractAcceptDialog>();
            dialog._dialog = copy.GetComponent<ConfirmDialogUI>();
            dialog._timeManager = FindFirstObjectByType<TimeManager>();
            return dialog;
        }

        public void Open(string title, string message, string confirmText, Action onConfirm)
        {
            if (_dialog == null) _dialog = GetComponent<ConfirmDialogUI>();

            // Pause before showing, so nothing ticks between reading and deciding.
            if (_timeManager != null && _timeManager.IsRunning)
            {
                _timeManager.Pause();
                _pausedByDialog = true;
            }

            _dialog.Show(title, message, confirmText, onConfirm, "Cancel");
        }

        /// <summary>Escape (via ModalTracker) = Cancel.</summary>
        public void Close()
        {
            if (_dialog != null && _dialog.IsOpen) _dialog.Cancel();
        }

        private void OnEnable() => ModalTracker.SetOpen(this);

        private void OnDisable()
        {
            ModalTracker.SetClosed(this);

            // Resume however the dialog was closed (confirm, cancel, Escape, scene change).
            if (_pausedByDialog && _timeManager != null)
                _timeManager.Resume();

            _pausedByDialog = false;
        }
    }
}
