using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// A question with Confirm and Cancel: overwriting a slot, deleting a save, leaving with
    /// unsaved progress. The menu that owns it closes it with Escape (= Cancel). The root object
    /// is a full-screen dim that blocks clicks to whatever lies underneath.
    /// </summary>
    public class ConfirmDialogUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text messageText;
        [SerializeField] private Button confirmButton;
        [SerializeField] private TMP_Text confirmLabel;
        [SerializeField] private Button cancelButton;
        [SerializeField] private TMP_Text cancelLabel;

        private Action _onConfirm;
        private Action _onCancel;

        public bool IsOpen => gameObject.activeSelf;

        private void Awake()
        {
            if (confirmButton != null) confirmButton.onClick.AddListener(Confirm);
            if (cancelButton != null) cancelButton.onClick.AddListener(Cancel);
        }

        /// <param name="cancelText">Null hides the Cancel button (a message with only an OK).</param>
        public void Show(string title, string message, string confirmText, Action onConfirm,
                         string cancelText = "Cancel", Action onCancel = null)
        {
            _onConfirm = onConfirm;
            _onCancel = onCancel;

            if (titleText != null) titleText.text = title ?? string.Empty;
            if (messageText != null) messageText.text = message ?? string.Empty;
            if (confirmLabel != null) confirmLabel.text = confirmText ?? "Confirm";
            if (cancelLabel != null && cancelText != null) cancelLabel.text = cancelText;
            if (cancelButton != null) cancelButton.gameObject.SetActive(cancelText != null);

            gameObject.SetActive(true);
            transform.SetAsLastSibling(); // above the panel that asked
        }

        /// <summary>A message with a single button.</summary>
        public void ShowMessage(string title, string message, string okText = "OK") =>
            Show(title, message, okText, null, cancelText: null);

        /// <summary>Same as pressing Cancel.</summary>
        public void Cancel()
        {
            var onCancel = _onCancel;
            Hide();
            onCancel?.Invoke();
        }

        private void Confirm()
        {
            var onConfirm = _onConfirm;
            Hide();
            onConfirm?.Invoke();
        }

        private void Hide()
        {
            _onConfirm = null;
            _onCancel = null;
            gameObject.SetActive(false);
        }
    }
}
