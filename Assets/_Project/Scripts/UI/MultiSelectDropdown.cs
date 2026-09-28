using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{

    [RequireComponent(typeof(Image))]
    public class MultiSelectDropdown : MonoBehaviour, IPointerClickHandler
    {
        [SerializeField] private TMP_Text captionText;
        [SerializeField] private RectTransform template;
        [SerializeField] private RectTransform content;
        [SerializeField] private Toggle itemTemplate;
        [SerializeField] private float itemHeight = 20f;

        [SerializeField] private string allLabel = "All";
        [SerializeField] private List<string> options = new List<string>();

        public UnityEvent<List<int>> onSelectionChanged = new UnityEvent<List<int>>();

        private readonly HashSet<int> _selected = new HashSet<int>();
        private readonly List<Toggle> _items = new List<Toggle>();
        private GameObject _blocker;

        public IReadOnlyCollection<int> Selected => _selected;

        public void SetOptions(IEnumerable<string> newOptions, string newAllLabel = null)
        {
            options = newOptions.ToList();
            if (newAllLabel != null) allLabel = newAllLabel;
            _selected.Clear();
            BuildItems();
            RefreshCaption();
        }

        public void ClearSelection()
        {
            _selected.Clear();
            foreach (var item in _items)
                item.SetIsOnWithoutNotify(false);
            RefreshCaption();
        }

        private void Awake()
        {
            if (template != null)
                template.gameObject.SetActive(false);
            BuildItems();
            RefreshCaption();
        }

        private void BuildItems()
        {
            foreach (var item in _items)
            {
                if (item == null) continue;
                if (Application.isPlaying) Destroy(item.gameObject);
                else DestroyImmediate(item.gameObject);
            }
            _items.Clear();

            if (content == null || itemTemplate == null)
                return;

            for (int i = 0; i < options.Count; i++)
            {
                var item = Instantiate(itemTemplate, content);
                item.gameObject.SetActive(true);

                var itemRect = (RectTransform)item.transform;
                itemRect.anchorMin = new Vector2(0f, 1f);
                itemRect.anchorMax = new Vector2(1f, 1f);
                itemRect.pivot = new Vector2(0.5f, 1f);
                itemRect.sizeDelta = new Vector2(0f, itemHeight);
                itemRect.anchoredPosition = new Vector2(0f, -itemHeight * i);

                var label = item.GetComponentInChildren<TMP_Text>(true);
                if (label != null) label.text = options[i];

                item.SetIsOnWithoutNotify(false);

                int index = i;
                item.onValueChanged.AddListener(isOn => OnItemToggled(index, isOn));
                _items.Add(item);
            }

            content.sizeDelta = new Vector2(content.sizeDelta.x, itemHeight * options.Count);
        }

        private void OnItemToggled(int index, bool isOn)
        {
            if (isOn) _selected.Add(index);
            else _selected.Remove(index);

            RefreshCaption();
            onSelectionChanged.Invoke(_selected.OrderBy(i => i).ToList());
        }

        private void RefreshCaption()
        {
            if (captionText == null) return;

            if (_selected.Count == 0)
                captionText.text = allLabel;
            else if (_selected.Count <= 2)
                captionText.text = string.Join(", ", _selected.OrderBy(i => i).Select(i => options[i]));
            else
                captionText.text = $"{_selected.Count} selected";
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (template == null) return;

            if (template.gameObject.activeSelf) Hide();
            else Show();
        }

        private void Show()
        {
            if (template == null || template.gameObject.activeSelf) return;

            template.gameObject.SetActive(true);
            template.SetAsLastSibling();

            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;

            _blocker = new GameObject("MultiSelectDropdown Blocker",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                typeof(Canvas), typeof(GraphicRaycaster));
            var blockerRect = (RectTransform)_blocker.transform;
            blockerRect.SetParent(canvas.transform, false);
            blockerRect.anchorMin = Vector2.zero;
            blockerRect.anchorMax = Vector2.one;
            blockerRect.sizeDelta = Vector2.zero;
            blockerRect.anchoredPosition = Vector2.zero;

            var blockerCanvas = _blocker.GetComponent<Canvas>();
            blockerCanvas.overrideSorting = true;
            blockerCanvas.sortingOrder = 999;

            _blocker.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);

            var blockerButton = _blocker.AddComponent<Button>();
            blockerButton.transition = Selectable.Transition.None;
            blockerButton.onClick.AddListener(Hide);
        }

        private void Hide()
        {
            if (_blocker != null)
            {
                Destroy(_blocker);
                _blocker = null;
            }
            if (template != null)
                template.gameObject.SetActive(false);
        }

        private void OnDisable() => Hide();
    }
}
