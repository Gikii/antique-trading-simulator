using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Horizontal bar: a background with a fill child whose right anchor follows the value
    /// (same technique as the warehouse bar in the InfoBar), so it needs no sprite.
    /// </summary>
    public class ProgressBarUI : MonoBehaviour
    {
        [SerializeField] private RectTransform fill;
        [SerializeField] private Image fillImage;

        public float Value { get; private set; }

        public void SetValue(float value01)
        {
            Value = Mathf.Clamp01(value01);
            if (fill == null) return;

            fill.anchorMin = new Vector2(0f, 0f);
            fill.anchorMax = new Vector2(Value, 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
        }

        public void SetColor(Color color)
        {
            if (fillImage != null) fillImage.color = color;
        }
    }
}
