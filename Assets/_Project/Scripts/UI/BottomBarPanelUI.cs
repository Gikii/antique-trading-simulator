using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Shared "tab" header for one panel of the bottom summary bar (Transports /
    /// Warehouse / Latest News / Upcoming Events) — a title and an action
    /// button/link, plus a content area below. One prefab, four instances;
    /// each instance's title text and button label are just set per-instance
    /// (directly on the prefab instance's fields, or via the methods below),
    /// and each button's onClick is wired per-instance in the Inspector since
    /// they each go somewhere different.
    ///
    /// Only the Upcoming Events instance currently populates ContentContainer
    /// (see UpcomingEventsListUI) — the other three leave it empty for now.
    /// </summary>
    public class BottomBarPanelUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private Button actionButton;
        [SerializeField] private TMP_Text actionButtonText;
        [SerializeField] private RectTransform contentContainer;

        public TMP_Text TitleText => titleText;
        public Button ActionButton => actionButton;
        public RectTransform ContentContainer => contentContainer;

        public void SetTitle(string title)
        {
            if (titleText != null) titleText.text = title;
        }

        public void SetActionButton(string label, UnityAction onClick)
        {
            if (actionButtonText != null) actionButtonText.text = label;
            if (actionButton == null) return;

            actionButton.onClick.RemoveAllListeners();
            if (onClick != null) actionButton.onClick.AddListener(onClick);
        }
    }
}
