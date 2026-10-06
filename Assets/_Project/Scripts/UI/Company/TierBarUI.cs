using System;
using System.Collections.Generic;
using AntiqueTradingSimulator.Company;
using UnityEngine;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// The ladder of reputation or credibility tiers as equal segments: passed tiers full,
    /// the current one filled up to the value, higher ones empty. Segments are cloned from
    /// an inactive TierSegmentUI template.
    /// </summary>
    public class TierBarUI : MonoBehaviour
    {
        [SerializeField] private RectTransform container;
        [SerializeField] private TierSegmentUI segmentTemplate;

        private readonly List<TierSegmentUI> _segments = new List<TierSegmentUI>();

        private void Awake()
        {
            if (segmentTemplate != null) segmentTemplate.gameObject.SetActive(false);
        }

        /// <param name="rangeLabel">Label under tier i, e.g. "200 - 499".</param>
        /// <param name="color">Fill colour of tier i.</param>
        public void Set(IReadOnlyList<ReputationTier> tiers, float value, Func<int, string> rangeLabel, Func<int, Color> color)
        {
            if (tiers == null || segmentTemplate == null) return;
            if (container == null) container = (RectTransform)transform;

            while (_segments.Count < tiers.Count)
            {
                var segment = Instantiate(segmentTemplate, container);
                segment.name = $"Tier{_segments.Count + 1}";
                _segments.Add(segment);
            }

            for (int i = 0; i < _segments.Count; i++)
            {
                bool visible = i < tiers.Count;
                _segments[i].gameObject.SetActive(visible);
                if (!visible) continue;

                float min = tiers[i].MinValue;
                bool isLast = i == tiers.Count - 1;
                float max = isLast ? min : tiers[i + 1].MinValue;

                bool reached = value >= min;
                bool isCurrent = reached && (isLast || value < max);
                float fill = !reached ? 0f
                    : isLast || value >= max ? 1f
                    : Mathf.Clamp01((value - min) / Mathf.Max(0.0001f, max - min));

                _segments[i].Set(tiers[i].Name, rangeLabel != null ? rangeLabel(i) : "", fill,
                    color != null ? color(i) : UIFormat.AccentColor, isCurrent);
            }
        }
    }
}
