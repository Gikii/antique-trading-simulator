using System.Collections.Generic;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Market;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Fills the Market view's side panel with the market activity feed whenever the
    /// Antique Details panel is closed. Shows Market.Feed's messages in a list.
    /// </summary>
    public class TransactionHistoryUI : MonoBehaviour
    {
        [Header("Dependencies")]
        [SerializeField] private EconomyManager economyManager;

        [Header("Scroll view")]
        [Tooltip("The ScrollRect whose content holds the message rows.")]
        [SerializeField] private ScrollRect scrollRect;

        [Tooltip("The ScrollRect's Content (a VerticalLayoutGroup + ContentSizeFitter). Rows are parented here.")]
        [SerializeField] private RectTransform content;

        [Tooltip("Optional: shown while there's no activity to display yet.")]
        [SerializeField] private TMP_Text emptyText;

        [Header("Row style")]
        [SerializeField] private float rowFontSize = 15f;

        [Tooltip("Row background. Matches the shade every other list item in the game uses " +
                 "(ContractListItem / ContractFulfillListItem / InventoryListItem).")]
        [SerializeField] private Color rowBackgroundColor = new Color(0.7f, 0.7f, 0.7f, 1f);

        [Tooltip("Message text. Dark by default, since the row background above is a light grey.")]
        [SerializeField] private Color rowTextColor = new Color(0.1961f, 0.1961f, 0.1961f, 1f);

        private readonly struct Row
        {
            public readonly GameObject Background;
            public readonly TextMeshProUGUI Label;

            public Row(GameObject background, TextMeshProUGUI label)
            {
                Background = background;
                Label = label;
            }
        }

        // Pooled rows, top to bottom. Never longer than MarketFeed.MaxMessages.
        private readonly List<Row> _rows = new List<Row>();

        private MarketFeed _feed;
        private bool _subscribed;

        private void Awake()
        {
            if (economyManager == null)
                economyManager = FindFirstObjectByType<EconomyManager>();

            if (scrollRect == null)
                scrollRect = GetComponentInChildren<ScrollRect>(true);

            if (content == null && scrollRect != null)
                content = scrollRect.content;
        }

        private void OnEnable()
        {
            Subscribe();
            Rebuild();
            ScrollToTop();
        }

        private void Start()
        {
            if (_subscribed)
                return;

            Subscribe();

            if (_subscribed)
            {
                Rebuild();
                ScrollToTop();
            }
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (_subscribed)
                return;

            var market = economyManager != null ? economyManager.Market : null;
            if (market == null)
                return;

            _feed = market.Feed;
            _feed.OnMessageAdded += HandleMessageAdded;
            _feed.OnCleared += HandleCleared;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed || _feed == null)
                return;

            _feed.OnMessageAdded -= HandleMessageAdded;
            _feed.OnCleared -= HandleCleared;
            _subscribed = false;
        }

        private void HandleMessageAdded(string message)
        {
            bool wasAtTop = scrollRect == null || scrollRect.verticalNormalizedPosition >= 0.999f;

            Rebuild();

            if (wasAtTop)
                ScrollToTop();
        }

        private void HandleCleared()
        {
            Rebuild();
            ScrollToTop();
        }

        private void Rebuild()
        {
            if (content == null)
                return;

            IReadOnlyList<string> messages = _feed != null ? _feed.Messages : null;
            int count = messages != null ? messages.Count : 0;

            for (int i = 0; i < count; i++)
            {
                var row = GetOrCreateRow(i);
                row.Label.text = messages[i];
                if (!row.Background.activeSelf)
                    row.Background.SetActive(true);
            }

            for (int i = count; i < _rows.Count; i++)
            {
                if (_rows[i].Background.activeSelf)
                    _rows[i].Background.SetActive(false);
            }

            if (emptyText != null)
                emptyText.gameObject.SetActive(count == 0);
        }

        private Row GetOrCreateRow(int index)
        {
            if (index < _rows.Count)
                return _rows[index];

            int uiLayer = content.gameObject.layer;

            var background = new GameObject($"MessageRow ({index})", typeof(RectTransform));
            background.layer = uiLayer;
            ((RectTransform)background.transform).SetParent(content, false);

            var backgroundImage = background.AddComponent<Image>();
            backgroundImage.color = rowBackgroundColor;
            backgroundImage.raycastTarget = true;

            var rowLayout = background.AddComponent<VerticalLayoutGroup>();
            rowLayout.padding = new RectOffset(8, 8, 5, 5);
            rowLayout.childAlignment = TextAnchor.UpperLeft;
            rowLayout.childControlWidth = true;
            rowLayout.childControlHeight = true;
            rowLayout.childForceExpandWidth = true;
            rowLayout.childForceExpandHeight = false;

            var labelGO = new GameObject("Text", typeof(RectTransform));
            labelGO.layer = uiLayer;
            ((RectTransform)labelGO.transform).SetParent((RectTransform)background.transform, false);

            var label = labelGO.AddComponent<TextMeshProUGUI>();
            label.fontSize = rowFontSize;
            label.color = rowTextColor;
            label.alignment = TextAlignmentOptions.TopLeft;
            label.raycastTarget = false;

            var row = new Row(background, label);
            _rows.Add(row);
            return row;
        }

        private void ScrollToTop()
        {
            if (scrollRect == null || content == null)
                return;

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            scrollRect.verticalNormalizedPosition = 1f;
        }
    }
}
