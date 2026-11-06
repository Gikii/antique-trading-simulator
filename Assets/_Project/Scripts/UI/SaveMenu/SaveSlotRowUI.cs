using System;
using AntiqueTradingSimulator.Saving;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>One slot in the save list: name, day and date, figures, save time, action and delete.</summary>
    public class SaveSlotRowUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text slotNameText;
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private TMP_Text detailText;
        [SerializeField] private TMP_Text savedAtText;
        [SerializeField] private Button actionButton;
        [SerializeField] private TMP_Text actionLabel;
        [SerializeField] private Button deleteButton;

        [Header("Colours (set by the builder from UITheme)")]
        [SerializeField] private Color textColor = new Color32(0xEA, 0xDF, 0xC8, 0xFF);
        [SerializeField] private Color mutedColor = new Color32(0xAA, 0x98, 0x78, 0xFF);
        [SerializeField] private Color problemColor = new Color32(0xE0, 0x50, 0x40, 0xFF);

        private Action _onAction;
        private Action _onDelete;

        public SaveSlotInfo Info { get; private set; }

        private void Awake()
        {
            if (actionButton != null) actionButton.onClick.AddListener(() => _onAction?.Invoke());
            if (deleteButton != null) deleteButton.onClick.AddListener(() => _onDelete?.Invoke());
        }

        /// <param name="actionText">"Load" or "Save"; null hides the action button.</param>
        /// <param name="onDelete">Null hides the delete button.</param>
        public void Bind(SaveSlotInfo info, string actionText, Action onAction, Action onDelete)
        {
            Info = info;
            _onAction = onAction;
            _onDelete = onDelete;

            bool readable = info != null && info.CanLoad;
            string problem = SaveSlotFormat.Problem(info);

            SetText(slotNameText, info != null ? info.Slot.DisplayName : string.Empty, info != null && info.Exists ? textColor : mutedColor);

            if (readable)
            {
                SetText(titleText, SaveSlotFormat.DayAndDate(info.Header), textColor);
                SetText(detailText, SaveSlotFormat.Figures(info.Header), mutedColor);
            }
            else
            {
                bool isProblem = info != null && info.Exists;
                SetText(titleText, problem, isProblem ? problemColor : mutedColor);
                SetText(detailText, string.Empty, mutedColor);
            }

            // Kept in place even when empty, so the action buttons of all rows line up.
            SetText(savedAtText, info != null && info.Exists ? SaveSlotFormat.SavedAt(info.SavedAtUtc) : string.Empty, mutedColor, collapseWhenEmpty: false);

            if (actionButton != null)
            {
                actionButton.gameObject.SetActive(!string.IsNullOrEmpty(actionText));
                if (actionLabel != null) actionLabel.text = actionText ?? string.Empty;
            }

            if (deleteButton != null)
                deleteButton.gameObject.SetActive(onDelete != null);
        }

        private static void SetText(TMP_Text text, string value, Color color, bool collapseWhenEmpty = true)
        {
            if (text == null) return;

            text.text = value;
            text.color = color;
            if (collapseWhenEmpty)
                text.gameObject.SetActive(!string.IsNullOrEmpty(value));
        }
    }
}
