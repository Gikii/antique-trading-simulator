using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI.Charts
{
    /// <summary>
    /// Minimal line chart drawn directly as a UI mesh (no textures, no extra packages).
    /// Put it on its own GameObject inside a layout cell and call SetValues().
    /// Optional TMP labels show the min/max of the plotted range.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class UILineChart : MaskableGraphic
    {
        [Header("Style")]
        [SerializeField] private Color lineColor = new Color32(0xE6, 0xB2, 0x40, 0xFF);
        [SerializeField] private Color fillColor = new Color32(0xE6, 0xB2, 0x40, 0x40);
        [SerializeField] private float lineWidth = 3f;
        [SerializeField] private bool drawFill = true;
        [Tooltip("Inner padding in pixels (left, bottom, right, top).")]
        [SerializeField] private Vector4 padding = new Vector4(4f, 4f, 4f, 4f);
        [Tooltip("Adds this fraction of the value range above and below the data so the line doesn't touch the edges.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float verticalMargin = 0.1f;

        [Header("Optional labels")]
        [SerializeField] private TMP_Text maxLabel;
        [SerializeField] private TMP_Text minLabel;
        [SerializeField] private TMP_Text emptyLabel;

        private readonly List<float> _values = new List<float>();
        private float _min;
        private float _max;

        private bool _hasFixedRange;
        private float _fixedMin;
        private float _fixedMax;

        /// <summary>Formats the min/max labels. Null = money ("128 450 €").</summary>
        public Func<float, string> LabelFormatter { get; set; }

        /// <summary>
        /// Plots against a fixed value range instead of one fitted to the data (e.g. 0..1 for a
        /// percentage, or a range shared by two charts drawn on top of each other).
        /// </summary>
        public void SetFixedRange(float min, float max)
        {
            _hasFixedRange = max > min;
            _fixedMin = min;
            _fixedMax = max;
            Refresh();
        }

        public void ClearFixedRange()
        {
            _hasFixedRange = false;
            Refresh();
        }

        public void SetColors(Color line, Color fill)
        {
            lineColor = line;
            fillColor = fill;
            SetVerticesDirty();
        }

        private void Refresh()
        {
            CalculateRange();
            UpdateLabels();
            SetVerticesDirty();
        }

        public void SetValues(IReadOnlyList<float> values)
        {
            _values.Clear();
            if (values != null)
                _values.AddRange(values);

            CalculateRange();
            UpdateLabels();
            SetVerticesDirty();
        }

        public void Clear() => SetValues(null);

        private void CalculateRange()
        {
            if (_hasFixedRange)
            {
                _min = _fixedMin;
                _max = _fixedMax;
                return;
            }

            if (_values.Count == 0)
            {
                _min = 0f;
                _max = 1f;
                return;
            }

            float min = float.MaxValue;
            float max = float.MinValue;
            foreach (float v in _values)
            {
                if (v < min) min = v;
                if (v > max) max = v;
            }

            // Flat series: give it some height so it's drawn in the middle.
            if (Mathf.Approximately(min, max))
            {
                float pad = Mathf.Max(1f, Mathf.Abs(min) * 0.1f);
                min -= pad;
                max += pad;
            }

            float margin = (max - min) * verticalMargin;
            _min = Mathf.Max(0f, min - margin);
            _max = max + margin;
        }

        private void UpdateLabels()
        {
            bool hasData = _values.Count > 0;

            if (maxLabel != null)
            {
                maxLabel.gameObject.SetActive(hasData);
                maxLabel.text = FormatLabel(_max);
            }

            if (minLabel != null)
            {
                minLabel.gameObject.SetActive(hasData);
                minLabel.text = FormatLabel(_min);
            }

            if (emptyLabel != null)
                emptyLabel.gameObject.SetActive(!hasData);
        }

        private string FormatLabel(float value) =>
            LabelFormatter != null ? LabelFormatter(value) : UIFormat.Money(value);

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            if (_values.Count == 0)
                return;

            Rect rect = GetPixelAdjustedRect();
            float left = rect.xMin + padding.x;
            float bottom = rect.yMin + padding.y;
            float width = Mathf.Max(1f, rect.width - padding.x - padding.z);
            float height = Mathf.Max(1f, rect.height - padding.y - padding.w);
            float range = Mathf.Max(0.0001f, _max - _min);

            var points = new List<Vector2>(_values.Count);
            if (_values.Count == 1)
            {
                // One sample: a flat line across the whole width.
                float y = bottom + (_values[0] - _min) / range * height;
                points.Add(new Vector2(left, y));
                points.Add(new Vector2(left + width, y));
            }
            else
            {
                for (int i = 0; i < _values.Count; i++)
                {
                    float x = left + width * i / (_values.Count - 1);
                    float y = bottom + (_values[i] - _min) / range * height;
                    points.Add(new Vector2(x, y));
                }
            }

            if (drawFill)
            {
                for (int i = 0; i < points.Count - 1; i++)
                {
                    Vector2 a = points[i];
                    Vector2 b = points[i + 1];
                    AddQuad(vh,
                        new Vector2(a.x, bottom), a, b, new Vector2(b.x, bottom),
                        fillColor);
                }
            }

            float half = lineWidth * 0.5f;
            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[i + 1];
                Vector2 dir = (b - a).normalized;
                Vector2 normal = new Vector2(-dir.y, dir.x) * half;

                AddQuad(vh, a - normal, a + normal, b + normal, b - normal, lineColor);

                // Small square on each joint hides gaps between segments.
                AddQuad(vh,
                    b + new Vector2(-half, -half), b + new Vector2(-half, half),
                    b + new Vector2(half, half), b + new Vector2(half, -half),
                    lineColor);
            }
        }

        private static void AddQuad(VertexHelper vh, Vector2 v0, Vector2 v1, Vector2 v2, Vector2 v3, Color32 color)
        {
            int start = vh.currentVertCount;
            vh.AddVert(v0, color, Vector2.zero);
            vh.AddVert(v1, color, Vector2.zero);
            vh.AddVert(v2, color, Vector2.zero);
            vh.AddVert(v3, color, Vector2.zero);
            vh.AddTriangle(start, start + 1, start + 2);
            vh.AddTriangle(start + 2, start + 3, start);
        }
    }
}
