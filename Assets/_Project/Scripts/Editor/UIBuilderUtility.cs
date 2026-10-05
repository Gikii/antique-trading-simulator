using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.EditorTools
{
    /// <summary>
    /// Small helpers for editor scripts that build greybox UI out of plain layout groups,
    /// in the same style as the existing views (translucent grey panels, default sprite,
    /// default TMP font). Shared by the Company view builders.
    /// </summary>
    public static class UIBuilderUtility
    {
        // Colours taken from the existing views (My Antiques, Collection summary).
        public static readonly Color PanelColor = new Color(0.6f, 0.6f, 0.6f, 0.3608f);
        public static readonly Color TileColor = new Color(1f, 1f, 1f, 0.0941f);
        public static readonly Color InsetColor = new Color(0f, 0f, 0f, 0.15f);
        public static readonly Color PlaceholderColor = new Color(1f, 1f, 1f, 0.25f);
        public static readonly Color BarBackgroundColor = new Color(0f, 0f, 0f, 0.25f);
        public static readonly Color LabelColor = new Color(0.72f, 0.72f, 0.72f, 1f);
        public static readonly Color ValueColor = Color.white;
        public static readonly Color DarkTextColor = new Color(0.1961f, 0.1961f, 0.1961f, 1f);
        public static readonly Color Clear = new Color(0f, 0f, 0f, 0f);

        private const int UILayer = 5;

        // ------------------------------------------------------------------ scene lookup

        /// <summary>Finds a GameObject by name anywhere in the scene, inactive ones included.</summary>
        public static GameObject FindInScene(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                var found = FindRecursive(root.transform, name);
                if (found != null) return found.gameObject;
            }
            return null;
        }

        private static Transform FindRecursive(Transform t, string name)
        {
            if (t.name == name) return t;
            for (int i = 0; i < t.childCount; i++)
            {
                var found = FindRecursive(t.GetChild(i), name);
                if (found != null) return found;
            }
            return null;
        }

        public static void DestroyChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
                Undo.DestroyObjectImmediate(parent.GetChild(i).gameObject);
        }

        // ------------------------------------------------------------------ nodes

        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = UILayer;
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        public static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        public static Image AddImage(GameObject go, Color color, bool uiSprite = false)
        {
            var image = go.GetComponent<Image>();
            if (image == null) image = go.AddComponent<Image>();
            image.color = color;
            if (uiSprite)
            {
                image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");
                image.type = Image.Type.Sliced;
            }
            return image;
        }

        /// <summary>Panel = node with a background image.</summary>
        public static RectTransform Panel(string name, Transform parent, Color color)
        {
            var rt = Node(name, parent);
            AddImage(rt.gameObject, color);
            return rt;
        }

        /// <summary>Grey box standing in for an illustration or icon from the mockups.</summary>
        public static RectTransform Placeholder(string name, Transform parent, float width, float height)
        {
            var rt = Panel(name, parent, PlaceholderColor);
            rt.GetComponent<Image>().raycastTarget = false;
            Layout(rt.gameObject, width, height, 0, 0, width, height);
            return rt;
        }

        public static TextMeshProUGUI CreateText(string name, Transform parent, string text, float size, Color color,
            TextAlignmentOptions alignment = TextAlignmentOptions.MidlineLeft, FontStyles style = FontStyles.Normal,
            bool wrap = false)
        {
            var rt = Node(name, parent);
            var tmp = rt.gameObject.AddComponent<TextMeshProUGUI>();
            if (TMP_Settings.defaultFontAsset != null) tmp.font = TMP_Settings.defaultFontAsset;
            tmp.text = text;
            tmp.fontSize = size;
            tmp.color = color;
            tmp.alignment = alignment;
            tmp.fontStyle = style;
            tmp.textWrappingMode = wrap ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
            tmp.overflowMode = wrap ? TextOverflowModes.Truncate : TextOverflowModes.Ellipsis;
            tmp.raycastTarget = false;
            return tmp;
        }

        // ------------------------------------------------------------------ layout

        /// <summary>
        /// Adds/updates a LayoutElement. A flexible axis without an explicit preferred size gets
        /// preferred 0, so the flexible weights alone split the space — otherwise the unwrapped
        /// width of a TMP text inside would skew the split.
        /// </summary>
        public static LayoutElement Layout(GameObject go, float preferredWidth = -1, float preferredHeight = -1,
            float flexibleWidth = -1, float flexibleHeight = -1, float minWidth = -1, float minHeight = -1)
        {
            if (flexibleWidth > 0f && preferredWidth < 0f) preferredWidth = 0f;
            if (flexibleHeight > 0f && preferredHeight < 0f) preferredHeight = 0f;

            var le = go.GetComponent<LayoutElement>();
            if (le == null) le = go.AddComponent<LayoutElement>();
            le.preferredWidth = preferredWidth;
            le.preferredHeight = preferredHeight;
            le.flexibleWidth = flexibleWidth;
            le.flexibleHeight = flexibleHeight;
            le.minWidth = minWidth;
            le.minHeight = minHeight;
            return le;
        }

        public static VerticalLayoutGroup Vertical(GameObject go, float spacing, int padding = 0,
            bool expandWidth = true, TextAnchor alignment = TextAnchor.UpperLeft)
        {
            var g = go.AddComponent<VerticalLayoutGroup>();
            g.spacing = spacing;
            g.padding = new RectOffset(padding, padding, padding, padding);
            g.childAlignment = alignment;
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = expandWidth;
            g.childForceExpandHeight = false;
            return g;
        }

        public static HorizontalLayoutGroup Horizontal(GameObject go, float spacing, int padding = 0,
            bool expandHeight = true, TextAnchor alignment = TextAnchor.MiddleLeft)
        {
            var g = go.AddComponent<HorizontalLayoutGroup>();
            g.spacing = spacing;
            g.padding = new RectOffset(padding, padding, padding, padding);
            g.childAlignment = alignment;
            g.childControlWidth = true;
            g.childControlHeight = true;
            g.childForceExpandWidth = false;
            g.childForceExpandHeight = expandHeight;
            return g;
        }

        /// <summary>Empty element that soaks up the remaining space in a layout group.</summary>
        public static RectTransform Spacer(Transform parent, float flexibleWidth = 1, float flexibleHeight = -1)
        {
            var rt = Node("Spacer", parent);
            Layout(rt.gameObject, 0, 0, flexibleWidth, flexibleHeight);
            return rt;
        }

        // ------------------------------------------------------------------ controls

        /// <summary>Light button with dark text, like the tab and navigation buttons.</summary>
        public static Button CreateButton(string name, Transform parent, string label, float fontSize,
            float preferredWidth, float preferredHeight, out TextMeshProUGUI labelText)
        {
            var rt = Node(name, parent);
            var image = AddImage(rt.gameObject, Color.white, true);
            var button = rt.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            Layout(rt.gameObject, preferredWidth, preferredHeight, 0, 0);

            labelText = CreateText("Text (TMP)", rt, label, fontSize, DarkTextColor, TextAlignmentOptions.Center);
            Stretch(labelText.rectTransform);
            return button;
        }

        /// <summary>Bar background with a fill child, driven by ProgressBarUI.</summary>
        public static RectTransform Bar(string name, Transform parent, float preferredWidth, float height,
            Color fillColor, out RectTransform fill, out Image fillImage)
        {
            var rt = Panel(name, parent, BarBackgroundColor);
            rt.GetComponent<Image>().raycastTarget = false;
            Layout(rt.gameObject, preferredWidth, height, preferredWidth < 0 ? 1 : 0, 0, -1, height);

            fill = Panel("Fill", rt, fillColor);
            fillImage = fill.GetComponent<Image>();
            fillImage.raycastTarget = false;
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = new Vector2(0.5f, 1f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            return rt;
        }

        // ------------------------------------------------------------------ wiring

        /// <summary>Assigns a (usually private) serialized object reference.</summary>
        public static void SetRef(Object target, string property, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(property);
            if (p == null)
            {
                Debug.LogError($"UIBuilder: {target.GetType().Name} has no serialized field '{property}'.");
                return;
            }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetRefs(Object target, string property, Object[] values)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(property);
            if (p == null || !p.isArray)
            {
                Debug.LogError($"UIBuilder: {target.GetType().Name} has no serialized array '{property}'.");
                return;
            }
            p.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
                p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        public static void SetString(Object target, string property, string value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(property);
            if (p == null)
            {
                Debug.LogError($"UIBuilder: {target.GetType().Name} has no serialized field '{property}'.");
                return;
            }
            p.stringValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
