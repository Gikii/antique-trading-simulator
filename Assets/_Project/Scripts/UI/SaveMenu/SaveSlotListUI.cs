using System;
using System.Collections.Generic;
using System.Linq;
using AntiqueTradingSimulator.Saving;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    public enum SaveSlotListMode
    {
        Load,
        Save
    }

    /// <summary>
    /// The list of save slots, used by the pause menu and the main menu.
    /// Load mode: manual slots and autosaves (newest first); Load on every readable save.
    /// Save mode: manual slots only; Save on every slot, asking before it overwrites one.
    /// Deleting asks first. What a Load or a Save actually does is up to the menu that opened
    /// the list (it may ask about unsaved progress first, or load through a scene change).
    /// Reads the files through SaveFiles, so it works without a game running.
    /// </summary>
    public class SaveSlotListUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private Button closeButton;

        [Header("Sections")]
        [SerializeField] private Transform manualSlotsContainer;
        [SerializeField] private GameObject autosaveSection;
        [SerializeField] private Transform autosaveSlotsContainer;
        [SerializeField] private SaveSlotRowUI rowPrefab;

        [Tooltip("Asked before overwriting or deleting a save.")]
        [SerializeField] private ConfirmDialogUI confirmDialog;

        private readonly List<SaveSlotRowUI> _rows = new List<SaveSlotRowUI>();
        private Action<SaveSlot> _onLoad;
        private Func<SaveSlot, bool> _onSave;

        public SaveSlotListMode Mode { get; private set; }

        public bool IsOpen => gameObject.activeSelf;

        /// <summary>Raised when the list closes, however it was closed.</summary>
        public event Action Closed;

        private void Awake()
        {
            if (closeButton != null) closeButton.onClick.AddListener(Close);
        }

        /// <summary>Opens the list for loading. <paramref name="onLoad"/> runs for the chosen slot; the list stays open.</summary>
        public void ShowLoad(Action<SaveSlot> onLoad)
        {
            Mode = SaveSlotListMode.Load;
            _onLoad = onLoad;
            _onSave = null;
            Open("Load game");
        }

        /// <summary>
        /// Opens the list for saving. <paramref name="onSave"/> writes the chosen slot and returns
        /// whether it worked; the list then refreshes and reports it.
        /// </summary>
        public void ShowSave(Func<SaveSlot, bool> onSave)
        {
            Mode = SaveSlotListMode.Save;
            _onSave = onSave;
            _onLoad = null;
            Open("Save game");
        }

        public void Close()
        {
            if (!IsOpen) return;

            if (confirmDialog != null && confirmDialog.IsOpen)
                confirmDialog.Cancel();

            gameObject.SetActive(false);
            _onLoad = null;
            _onSave = null;
            Closed?.Invoke();
        }

        /// <summary>A line under the list: a save that worked or a reason something failed.</summary>
        public void SetStatus(string message)
        {
            if (statusText == null) return;

            statusText.text = message ?? string.Empty;
            statusText.gameObject.SetActive(!string.IsNullOrEmpty(message));
        }

        public void Refresh()
        {
            foreach (var row in _rows)
                if (row != null) Destroy(row.gameObject);
            _rows.Clear();

            List<SaveSlotInfo> slots;
            try
            {
                slots = SaveFiles.Store.List();
            }
            catch (Exception exception)
            {
                SetStatus($"Saves couldn't be read. {exception.Message}");
                return;
            }

            foreach (var info in slots.Where(s => !s.Slot.IsAutosave).OrderBy(s => s.Slot.Index))
                AddRow(manualSlotsContainer, info);

            bool showAutosaves = Mode == SaveSlotListMode.Load;
            if (autosaveSection != null) autosaveSection.SetActive(showAutosaves);

            if (showAutosaves)
            {
                // Newest first; empty autosave slots are left out, nobody picks those.
                foreach (var info in slots.Where(s => s.Slot.IsAutosave && s.Exists).OrderByDescending(s => s.SavedAtUtc))
                    AddRow(autosaveSlotsContainer, info);
            }
        }

        private void Open(string title)
        {
            if (titleText != null) titleText.text = title;
            SetStatus(string.Empty);

            gameObject.SetActive(true);
            transform.SetAsLastSibling();
            Refresh();
        }

        private void AddRow(Transform container, SaveSlotInfo info)
        {
            if (container == null || rowPrefab == null) return;

            var row = Instantiate(rowPrefab, container);
            row.gameObject.SetActive(true);
            _rows.Add(row);

            SaveSlot slot = info.Slot;
            Action onDelete = info.Exists ? () => AskDelete(info) : (Action)null;

            if (Mode == SaveSlotListMode.Load)
                row.Bind(info, info.CanLoad ? "Load" : null, () => _onLoad?.Invoke(slot), onDelete);
            else
                row.Bind(info, "Save", () => AskSave(info), onDelete);
        }

        private void AskSave(SaveSlotInfo info)
        {
            if (!info.Exists)
            {
                SaveTo(info.Slot);
                return;
            }

            string what = info.CanLoad ? SaveSlotFormat.WeekAndDate(info.Header) : SaveSlotFormat.Problem(info);
            Confirm($"Overwrite {info.Slot.DisplayName}?",
                    $"{what}\nThis save will be replaced.",
                    "Overwrite",
                    () => SaveTo(info.Slot));
        }

        private void SaveTo(SaveSlot slot)
        {
            if (_onSave == null) return;

            bool saved = _onSave(slot);
            Refresh();
            // A failure reason comes from whoever saved (SaveManager.OnSaveFailed → SetStatus).
            if (saved) SetStatus($"Saved to {slot.DisplayName}.");
        }

        private void AskDelete(SaveSlotInfo info)
        {
            string what = info.CanLoad ? SaveSlotFormat.WeekAndDate(info.Header) : SaveSlotFormat.Problem(info);
            Confirm($"Delete {info.Slot.DisplayName}?",
                    $"{what}\nA deleted save can't be brought back.",
                    "Delete",
                    () => Delete(info.Slot));
        }

        private void Delete(SaveSlot slot)
        {
            bool deleted;
            try
            {
                deleted = SaveFiles.Store.Delete(slot);
            }
            catch (Exception exception)
            {
                SetStatus($"{slot.DisplayName} couldn't be deleted. {exception.Message}");
                return;
            }

            Refresh();
            SetStatus(deleted ? $"{slot.DisplayName} deleted." : string.Empty);
        }

        private void Confirm(string title, string message, string confirmText, Action onConfirm)
        {
            // Without a dialog wired up, act at once rather than do nothing.
            if (confirmDialog == null)
            {
                onConfirm();
                return;
            }

            confirmDialog.Show(title, message, confirmText, onConfirm);
        }
    }
}
