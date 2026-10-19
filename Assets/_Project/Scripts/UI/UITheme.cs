using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Look of the game UI in one asset: the frame sprites and the base colours taken from the
    /// visual mockup (dark warm panels, thin bronze edges, antique gold accent).
    /// Read by the editor tools that restyle the scene (Tools → Antique Trading Simulator → UI → …),
    /// so changing a value here and re-running the tool restyles every element of that role.
    /// Lives in Data/Resources so runtime code can load it later (UITheme.Load()).
    /// </summary>
    [CreateAssetMenu(fileName = "UITheme", menuName = "AntiqueTradingSimulator/UI Theme")]
    public class UITheme : ScriptableObject
    {
        /// <summary>Path under any Resources folder, without extension.</summary>
        public const string ResourcePath = "UITheme";

        [Header("Frame sprites (9-sliced)")]
        [Tooltip("Large containers: view panels, side panels, modal windows (ui_panel.png).")]
        public Sprite Panel;
        [Tooltip("Sections inside a panel, list rows (ui_row.png).")]
        public Sprite Inset;
        public Sprite RowSelected;
        public Sprite Button;
        public Sprite ButtonPrimary;
        [Tooltip("Destructive / irreversible confirmations, e.g. Sell now (ui_button_danger.png).")]
        public Sprite ButtonDanger;
        public Sprite NavItem;
        public Sprite NavActive;
        public Sprite Tab;
        public Sprite TabActive;
        public Sprite Input;
        public Sprite BadgeGreen;
        public Sprite Divider;

        [Header("Base colours")]
        [Tooltip("Flat strips without a frame (InfoBar base).")]
        public Color ScreenBase = new Color32(0x0B, 0x09, 0x05, 0xFF);
        [Tooltip("Average colour of the Panel sprite — used where something must blend into a panel (e.g. illustration fades).")]
        public Color PanelFill = new Color32(0x11, 0x0E, 0x09, 0xFF);
        [Tooltip("Thin bronze edge of panels and rows.")]
        public Color Edge = new Color32(0x3A, 0x2E, 0x1C, 0xFF);

        [Header("Text colours")]
        public Color TextPrimary = new Color32(0xEA, 0xDF, 0xC8, 0xFF);
        public Color TextMuted = new Color32(0xAA, 0x98, 0x78, 0xFF);
        public Color Accent = new Color32(0xC9, 0x9A, 0x3C, 0xFF);
        public Color Positive = new Color32(0x6E, 0xC8, 0x64, 0xFF);
        public Color Negative = new Color32(0xE0, 0x50, 0x40, 0xFF);
        public Color Warning = new Color32(0xE2, 0xB8, 0x62, 0xFF);

        private static UITheme _cached;

        /// <summary>
        /// Shows a tab button as active or idle: the theme's tab frames when the theme exists,
        /// otherwise the given greybox colours (so nothing breaks before the theme is applied).
        /// </summary>
        public static void ApplyTabState(Selectable button, bool active, Color fallbackActive, Color fallbackInactive)
        {
            if (button == null || !(button.targetGraphic is Image image))
                return;

            var theme = Load();
            if (theme != null && theme.Tab != null && theme.TabActive != null)
            {
                image.sprite = active ? theme.TabActive : theme.Tab;
                image.type = Image.Type.Sliced;
                image.color = Color.white;
                return;
            }

            image.color = active ? fallbackActive : fallbackInactive;
        }

        /// <summary>The theme from Resources, or null if it hasn't been created yet.</summary>
        public static UITheme Load()
        {
            if (_cached == null)
                _cached = Resources.Load<UITheme>(ResourcePath);
            return _cached;
        }
    }
}
