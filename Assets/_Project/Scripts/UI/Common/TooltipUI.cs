using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// The single hover tooltip panel. TooltipTrigger components ask it to show their text
    /// next to the pointer. Put one in the scene to style it yourself; if there isn't one,
    /// the first trigger that needs it builds a plain one at runtime (see GetOrCreate).
    /// </summary>
    public class TooltipUI : MonoBehaviour
    {
        [SerializeField] private RectTransform panel;
        [SerializeField] private TMP_Text text;
        [SerializeField] private Vector2 pointerOffset = new Vector2(14f, 14f);
        [SerializeField] private float maxWidth = 320f;

        private static TooltipUI _instance;

        private Canvas _canvas;
        private RectTransform _canvasRect;

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }

            _instance = this;
            if (panel == null) panel = transform as RectTransform;
            ResolveCanvas();
            Hide();
        }

        private void OnDestroy()
        {
            if (_instance == this)
                _instance = null;
        }

        // ------------------------------------------------------------------
        // API
        // ------------------------------------------------------------------

        /// <summary>The scene's tooltip; builds a plain one under <paramref name="canvas"/> if there is none.</summary>
        public static TooltipUI GetOrCreate(Canvas canvas, TMP_FontAsset font = null)
        {
            if (_instance != null)
                return _instance;

            _instance = FindFirstObjectByType<TooltipUI>(FindObjectsInactive.Include);
            if (_instance != null)
                return _instance;

            if (canvas == null)
                return null;

            return CreateDefault(canvas.rootCanvas, font);
        }

        public void Show(string message, Vector2 screenPosition)
        {
            if (panel == null) panel = transform as RectTransform;

            if (string.IsNullOrEmpty(message) || panel == null || text == null)
            {
                Hide();
                return;
            }

            text.text = message;

            // Wrap long messages at maxWidth, keep short ones tight.
            var layout = text.GetComponent<LayoutElement>();
            if (layout != null)
                layout.preferredWidth = Mathf.Min(text.GetPreferredValues(message).x, maxWidth);

            panel.gameObject.SetActive(true);
            panel.SetAsLastSibling();
            // Twice: the first pass settles the width, the second the wrapped height.
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
            LayoutRebuilder.ForceRebuildLayoutImmediate(panel);
            Position(screenPosition);
        }

        public void Hide()
        {
            if (panel != null)
                panel.gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------
        // Positioning
        // ------------------------------------------------------------------

        private void ResolveCanvas()
        {
            _canvas = GetComponentInParent<Canvas>();
            if (_canvas != null) _canvas = _canvas.rootCanvas;
            _canvasRect = _canvas != null ? _canvas.transform as RectTransform : null;
        }

        private void Position(Vector2 screenPosition)
        {
            if (_canvasRect == null) ResolveCanvas();
            if (_canvasRect == null) return;

            Camera cam = _canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : _canvas.worldCamera;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenPosition, cam, out Vector2 local);

            Vector2 size = panel.rect.size;
            Rect bounds = _canvasRect.rect;

            // Default: up and to the right of the pointer; flip when it would leave the screen.
            float x = local.x + pointerOffset.x;
            float y = local.y + pointerOffset.y;
            if (x + size.x > bounds.xMax) x = local.x - pointerOffset.x - size.x;
            if (y + size.y > bounds.yMax) y = local.y - pointerOffset.y - size.y;
            x = Mathf.Clamp(x, bounds.xMin, Mathf.Max(bounds.xMin, bounds.xMax - size.x));
            y = Mathf.Clamp(y, bounds.yMin, Mathf.Max(bounds.yMin, bounds.yMax - size.y));

            // Panel pivot is bottom-left (set in CreateDefault / expected for custom panels).
            panel.pivot = Vector2.zero;
            panel.anchorMin = panel.anchorMax = new Vector2(0.5f, 0.5f);
            panel.anchoredPosition = new Vector2(x, y);
        }

        // ------------------------------------------------------------------
        // Runtime fallback
        // ------------------------------------------------------------------

        private static TooltipUI CreateDefault(Canvas canvas, TMP_FontAsset font)
        {
            var root = new GameObject("Tooltip", typeof(RectTransform));
            root.transform.SetParent(canvas.transform, false);

            var image = root.AddComponent<Image>();
            image.color = new Color(0.08f, 0.08f, 0.08f, 0.95f);
            image.raycastTarget = false;

            var group = root.AddComponent<CanvasGroup>();
            group.blocksRaycasts = false;
            group.interactable = false;

            var layout = root.AddComponent<HorizontalLayoutGroup>();
            layout.padding = new RectOffset(10, 10, 6, 6);
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var fitter = root.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var textGo = new GameObject("Text", typeof(RectTransform));
            textGo.transform.SetParent(root.transform, false);
            var tmp = textGo.AddComponent<TextMeshProUGUI>();
            if (font != null) tmp.font = font;
            tmp.fontSize = 15f;
            tmp.color = new Color(0.92f, 0.92f, 0.92f, 1f);
            tmp.raycastTarget = false;
            textGo.AddComponent<LayoutElement>();

            // Awake runs inside AddComponent and needs the references set first,
            // so add the component on an inactive object and activate afterwards.
            root.SetActive(false);
            var tooltip = root.AddComponent<TooltipUI>();
            tooltip.panel = root.transform as RectTransform;
            tooltip.text = tmp;
            root.SetActive(true);
            return tooltip;
        }
    }
}
