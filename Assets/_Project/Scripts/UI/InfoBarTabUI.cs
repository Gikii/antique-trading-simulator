using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{

    public class InfoBarTabUI : MonoBehaviour
    {
        [Header("Header")]
        [SerializeField] private TMP_Text titleText;
        [SerializeField] private Button actionButton;
        [SerializeField] private string title;

        [Header("Body (optional — only the Events tab uses this today)")]
        [SerializeField] private RectTransform contentContainer;

        public Button ActionButton => actionButton;
        public RectTransform ContentContainer => contentContainer;

        void Awake()
        {
            ApplyTitle();
        }

        void OnValidate()
        {
            ApplyTitle();
        }

        public void SetTitle(string newTitle)
        {
            title = newTitle;
            ApplyTitle();
        }

        private void ApplyTitle()
        {
            if (titleText != null)
                titleText.text = title;
        }
    }
}
