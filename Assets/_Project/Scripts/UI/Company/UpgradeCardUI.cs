using System;
using AntiqueTradingSimulator.Company;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// One upgrade on Company → Development: name and description, level, current and next
    /// effect and cost. Clicking the card selects it; buying is done from the details panel
    /// (UpgradeDetailsUI), so the card's own Upgrade button is hidden.
    /// </summary>
    public class UpgradeCardUI : MonoBehaviour
    {
        [SerializeField] private Image background;
        [Tooltip("Covers the whole card; selects it. Transition should be None (the card colours itself).")]
        [SerializeField] private Button selectButton;

        [Tooltip("Optional. Shows the upgrade's icon from UIIcons.")]
        [SerializeField] private Image icon;
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private TMP_Text statusText;
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private SegmentedBarUI levelBar;
        [SerializeField] private TMP_Text currentEffectText;
        [SerializeField] private TMP_Text nextEffectText;
        [SerializeField] private TMP_Text costText;
        [Tooltip("Hidden at runtime — the details panel has the Upgrade button. Can be removed from the prefab.")]
        [SerializeField] private Button upgradeButton;

        [SerializeField] private Color normalColor = new Color(0.6f, 0.6f, 0.6f, 0.3608f);
        [SerializeField] private Color selectedColor = new Color32(0xC9, 0x9A, 0x3C, 0x8C);

        private Action<CompanyUpgradeType> _onSelect;

        public CompanyUpgradeType Type { get; private set; }

        public void Initialize(CompanyUpgradeType type, Action<CompanyUpgradeType> onSelect)
        {
            Type = type;
            _onSelect = onSelect;

            if (selectButton != null)
            {
                selectButton.onClick.RemoveAllListeners();
                selectButton.onClick.AddListener(() => _onSelect?.Invoke(Type));
            }

            if (upgradeButton != null) upgradeButton.gameObject.SetActive(false);

            // A long level never spills over the next card: the details panel has the full text.
            if (currentEffectText != null) currentEffectText.overflowMode = TextOverflowModes.Ellipsis;
            if (nextEffectText != null) nextEffectText.overflowMode = TextOverflowModes.Ellipsis;
        }

        public void Set(UpgradeInfo info, bool selected)
        {
            if (info == null) return;

            if (background != null) background.color = selected ? selectedColor : normalColor;

            var icons = UIIcons.Load();
            if (icons != null) UIIcons.Show(icon, icons.ForUpgrade(info.Type));

            SetText(nameText, info.Name);
            SetText(descriptionText, info.Description);
            SetText(statusText, info.EffectActive ? "" : UIFormat.Colorize(UpgradePresentation.EffectComingSoon, UIFormat.MutedColor));
            SetText(levelText, UpgradePresentation.LevelLabel(info));
            if (levelBar != null) levelBar.Set(info.DisplayLevel, info.LevelCount);

            SetText(currentEffectText, UpgradePresentation.EffectBlock("Current effect:", info.CurrentEffects, info.CurrentUpkeep,
                valuesOnly: true));
            SetText(nextEffectText, info.IsMaxed
                ? UpgradePresentation.MaxedBlock("Next level:")
                : UpgradePresentation.EffectBlock("Next level:", info.NextEffects, info.NextUpkeep, UIFormat.PositiveColor,
                    valuesOnly: true));

            string requirement = UpgradePresentation.RequirementLine(info);
            SetText(costText,
                $"<size=80%>{UIFormat.Colorize("Cost:", UIFormat.MutedColor)}</size>\n" +
                UIFormat.Colorize(UpgradePresentation.CostValue(info), info.CanAfford || info.IsMaxed ? Color.white : UIFormat.NegativeColor) +
                (requirement.Length > 0 ? $"\n<size=80%>{requirement}</size>" : ""));
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null) text.text = value;
        }
    }
}
