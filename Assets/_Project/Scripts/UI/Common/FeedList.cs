using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Builds the rows of a history feed (Market History, Contract History) into a scroll view's
    /// content, so both feeds look the same:
    ///
    ///   Summary text (can be two lines)
    ///   THIS WEEK                                        ← heading
    ///   ■ Hargreaves bought your Ormolu clock   +1 240 £ ← entry: marker, text, amount
    ///       + 14 new listings                            ← note
    ///
    /// Plain C# helper owned by a feed view: Clear() and add the rows again whenever the feed changes.
    /// </summary>
    public class FeedList
    {
        private const float MarkerSize = 8f;
        private const float EntryIndent = 28f; // notes line up with the entries' text

        private readonly RectTransform _content;
        private readonly TMP_FontAsset _font;
        private readonly float _fontSize;
        private readonly Color _rowBackground;
        private readonly List<GameObject> _rows = new List<GameObject>();

        /// <summary>Theme colours for the feeds to pick from (fallbacks match the default UITheme).</summary>
        public Color Primary { get; } = new Color32(0xEA, 0xDF, 0xC8, 0xFF);
        public Color Muted { get; }
        public Color Accent { get; } = new Color32(0xC9, 0x9A, 0x3C, 0xFF);
        public Color Positive { get; } = new Color32(0x6E, 0xC8, 0x64, 0xFF);
        public Color Negative { get; } = new Color32(0xE0, 0x50, 0x40, 0xFF);

        /// <param name="muted">Week headings, notes and other quiet text (the view's row text colour).</param>
        public FeedList(RectTransform content, TMP_FontAsset font, float fontSize, Color rowBackground, Color muted)
        {
            _content = content;
            _font = font;
            _fontSize = fontSize;
            _rowBackground = rowBackground;
            Muted = muted;

            var theme = UITheme.Load();
            if (theme != null)
            {
                Primary = theme.TextPrimary;
                Accent = theme.Accent;
                Positive = theme.Positive;
                Negative = theme.Negative;
            }
        }

        /// <summary>"This week", "Last week" or "Week of 7 July".</summary>
        public static string WeekHeading(int week, int currentWeek)
        {
            if (week == currentWeek) return "This week";
            if (week == currentWeek - 1) return "Last week";
            return $"Week of {UIFormat.GameDate(week)}";
        }

        public void Clear()
        {
            foreach (var row in _rows)
            {
                if (row == null) continue;
                row.SetActive(false); // hide now; Destroy happens at the end of the frame
                Object.Destroy(row);
            }

            _rows.Clear();
        }

        public void AddSummary(string text)
        {
            var row = NewRow("Summary", withBackground: true, new RectOffset(10, 10, 8, 8));
            AddText(row.transform, "Text", text, _fontSize, Primary, TextAlignmentOptions.TopLeft, wrap: true, flexible: true);
        }

        public void AddHeading(string text)
        {
            var row = NewRow("WeekHeading", withBackground: false, new RectOffset(4, 4, 12, 3));
            var label = AddText(row.transform, "Text", text, _fontSize - 3f, Muted, TextAlignmentOptions.TopLeft,
                wrap: false, flexible: true);
            label.fontStyle = FontStyles.UpperCase;
        }

        /// <summary>A quiet line without a marker; indented to the entries' text unless <paramref name="indented"/> is false.</summary>
        public void AddNote(string text, bool indented = true)
        {
            int left = indented ? Mathf.RoundToInt(EntryIndent) : 10;
            var row = NewRow("Note", withBackground: false, new RectOffset(left, 10, 2, 4));
            AddText(row.transform, "Text", text, _fontSize - 2f, Muted, TextAlignmentOptions.TopLeft, wrap: true, flexible: true);
        }

        /// <param name="amount">Shown on the right; null or empty for none.</param>
        public void AddEntry(Color marker, string text, Color textColor, string amount = null, Color amountColor = default)
        {
            var row = NewRow("Entry", withBackground: true, new RectOffset(10, 10, 6, 6));

            var markerObject = NewChild("Marker", row.transform);
            var markerImage = markerObject.AddComponent<Image>();
            markerImage.color = marker;
            markerImage.raycastTarget = false;
            var markerLayout = markerObject.AddComponent<LayoutElement>();
            markerLayout.minWidth = markerLayout.preferredWidth = MarkerSize;
            markerLayout.minHeight = markerLayout.preferredHeight = MarkerSize;
            markerLayout.flexibleWidth = 0f;

            AddText(row.transform, "Text", text, _fontSize, textColor, TextAlignmentOptions.MidlineLeft, wrap: true, flexible: true);

            if (!string.IsNullOrEmpty(amount))
                AddText(row.transform, "Amount", amount, _fontSize, amountColor, TextAlignmentOptions.MidlineRight,
                    wrap: false, flexible: false);
        }

        // ------------------------------------------------------------------ helpers

        private GameObject NewRow(string name, bool withBackground, RectOffset padding)
        {
            var row = NewChild(name, _content);
            _rows.Add(row);

            if (withBackground)
            {
                var background = row.AddComponent<Image>();
                background.color = _rowBackground;
                background.raycastTarget = true; // lets the scroll view be dragged over rows
            }

            var layout = row.AddComponent<HorizontalLayoutGroup>();
            layout.padding = padding;
            layout.spacing = 10f;
            layout.childAlignment = TextAnchor.MiddleLeft;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            return row;
        }

        private static GameObject NewChild(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            go.transform.SetParent(parent, false);
            return go;
        }

        private TextMeshProUGUI AddText(Transform parent, string name, string text, float size, Color color,
            TextAlignmentOptions alignment, bool wrap, bool flexible)
        {
            var go = NewChild(name, parent);

            var label = go.AddComponent<TextMeshProUGUI>();
            if (_font != null) label.font = _font;
            label.text = text;
            label.fontSize = size;
            label.color = color;
            label.alignment = alignment;
            label.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            label.raycastTarget = false;

            var layout = go.AddComponent<LayoutElement>();
            layout.flexibleWidth = flexible ? 1f : 0f;

            return label;
        }
    }
}
