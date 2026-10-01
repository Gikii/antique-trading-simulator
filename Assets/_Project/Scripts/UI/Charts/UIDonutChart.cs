using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI.Charts
{
    /// <summary>
    /// Donut (ring) chart drawn as a UI mesh. Each segment's angle is proportional to its
    /// value. Colors come from the caller, so the legend next to it can use the same ones.
    /// </summary>
    [RequireComponent(typeof(CanvasRenderer))]
    public class UIDonutChart : MaskableGraphic
    {
        public readonly struct Segment
        {
            public readonly float Value;
            public readonly Color Color;

            public Segment(float value, Color color)
            {
                Value = value;
                Color = color;
            }
        }

        [Tooltip("Inner radius as a fraction of the outer radius (0 = pie chart).")]
        [Range(0f, 0.95f)]
        [SerializeField] private float innerRadiusRatio = 0.6f;
        [Tooltip("Number of mesh steps used for a full circle.")]
        [SerializeField] private int resolution = 96;
        [SerializeField] private Color emptyColor = new Color32(0x6E, 0x6E, 0x6E, 0xFF);

        private readonly List<Segment> _segments = new List<Segment>();

        public void SetSegments(IEnumerable<Segment> segments)
        {
            _segments.Clear();
            if (segments != null)
            {
                foreach (var s in segments)
                    if (s.Value > 0f)
                        _segments.Add(s);
            }

            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();

            Rect rect = GetPixelAdjustedRect();
            Vector2 center = rect.center;
            float outer = Mathf.Min(rect.width, rect.height) * 0.5f;
            float inner = outer * innerRadiusRatio;

            float total = 0f;
            foreach (var s in _segments)
                total += s.Value;

            if (total <= 0f)
            {
                AddArc(vh, center, inner, outer, 0f, 1f, emptyColor);
                return;
            }

            float start = 0f;
            foreach (var s in _segments)
            {
                float fraction = s.Value / total;
                AddArc(vh, center, inner, outer, start, start + fraction, s.Color);
                start += fraction;
            }
        }

        // from/to are fractions of a full turn, starting at 12 o'clock and going clockwise.
        private void AddArc(VertexHelper vh, Vector2 center, float inner, float outer, float from, float to, Color32 color)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(resolution * (to - from)));

            for (int i = 0; i < steps; i++)
            {
                float a0 = Mathf.Lerp(from, to, (float)i / steps);
                float a1 = Mathf.Lerp(from, to, (float)(i + 1) / steps);

                Vector2 d0 = Direction(a0);
                Vector2 d1 = Direction(a1);

                int startIndex = vh.currentVertCount;
                vh.AddVert(center + d0 * inner, color, Vector2.zero);
                vh.AddVert(center + d0 * outer, color, Vector2.zero);
                vh.AddVert(center + d1 * outer, color, Vector2.zero);
                vh.AddVert(center + d1 * inner, color, Vector2.zero);
                vh.AddTriangle(startIndex, startIndex + 1, startIndex + 2);
                vh.AddTriangle(startIndex + 2, startIndex + 3, startIndex);
            }
        }

        private static Vector2 Direction(float turnFraction)
        {
            float radians = (0.25f - turnFraction) * Mathf.PI * 2f;
            return new Vector2(Mathf.Cos(radians), Mathf.Sin(radians));
        }
    }
}
