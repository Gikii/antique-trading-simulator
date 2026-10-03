using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Shows <see cref="Text"/> in the TooltipUI while the pointer is over this element.
    /// Empty text = no tooltip, so code can switch it on only when there's something
    /// to explain (e.g. why a button is disabled). Works on non-interactable buttons too —
    /// pointer enter/exit events still reach them.
    /// </summary>
    public class TooltipTrigger : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        [TextArea]
        [SerializeField] private string text;

        private bool _hovered;
        private Vector2 _lastPointerPosition;

        public string Text
        {
            get => text;
            set
            {
                text = value;
                // Update a tooltip that is currently showing (e.g. state changed under the pointer).
                if (_hovered)
                {
                    if (string.IsNullOrEmpty(text)) Tooltip()?.Hide();
                    else Tooltip()?.Show(text, _lastPointerPosition);
                }
            }
        }

        /// <summary>Returns the trigger on <paramref name="target"/>, adding one if needed.</summary>
        public static TooltipTrigger On(Component target)
        {
            if (target == null) return null;
            var trigger = target.GetComponent<TooltipTrigger>();
            return trigger != null ? trigger : target.gameObject.AddComponent<TooltipTrigger>();
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _hovered = true;
            _lastPointerPosition = eventData.position;

            if (!string.IsNullOrEmpty(text))
                Tooltip()?.Show(text, eventData.position);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _hovered = false;
            Tooltip()?.Hide();
        }

        private void OnDisable()
        {
            if (_hovered)
                Tooltip()?.Hide();
            _hovered = false;
        }

        private TooltipUI Tooltip()
        {
            // Reuse this element's font for the runtime-built tooltip so it matches the UI.
            var font = GetComponentInChildren<TMP_Text>(true);
            return TooltipUI.GetOrCreate(GetComponentInParent<Canvas>(), font != null ? font.font : null);
        }
    }
}
