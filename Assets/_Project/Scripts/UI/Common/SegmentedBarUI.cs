using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Row of equal segments, the first N filled — e.g. upgrade level "2 / 5". Segments are
    /// plain Images created on demand inside a HorizontalLayoutGroup, so it needs no sprite.
    /// </summary>
    public class SegmentedBarUI : MonoBehaviour
    {
        [Tooltip("Parent of the segments (needs a HorizontalLayoutGroup). Defaults to this object.")]
        [SerializeField] private RectTransform container;
        [SerializeField] private Color filledColor = new Color32(0xC9, 0x9A, 0x3C, 0xFF);
        [SerializeField] private Color emptyColor = new Color(0f, 0f, 0f, 0.25f);

        private readonly List<Image> _segments = new List<Image>();

        public void Set(int filled, int total)
        {
            if (container == null) container = (RectTransform)transform;
            total = Mathf.Max(0, total);

            while (_segments.Count < total)
            {
                var go = new GameObject($"Segment{_segments.Count + 1}", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
                go.layer = gameObject.layer;
                go.transform.SetParent(container, false);
                go.GetComponent<LayoutElement>().flexibleWidth = 1f;

                var image = go.GetComponent<Image>();
                image.raycastTarget = false;
                _segments.Add(image);
            }

            for (int i = 0; i < _segments.Count; i++)
            {
                bool visible = i < total;
                _segments[i].gameObject.SetActive(visible);
                if (visible)
                    _segments[i].color = i < filled ? filledColor : emptyColor;
            }
        }
    }
}
