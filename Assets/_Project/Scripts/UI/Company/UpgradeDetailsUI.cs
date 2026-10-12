using System;
using System.Collections.Generic;
using AntiqueTradingSimulator.Company;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Right-hand panel of Company → Development: the selected upgrade in detail —
    /// current and next level as rows, requirements, a hint and a big Upgrade button.
    /// </summary>
    public class UpgradeDetailsUI : MonoBehaviour
    {
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text levelText;
        [SerializeField] private SegmentedBarUI levelBar;
        [SerializeField] private TMP_Text descriptionText;
        [SerializeField] private TMP_Text effectNoteText;

        [Header("Current level")]
        [SerializeField] private TMP_Text currentHeaderText;
        [SerializeField] private RectTransform currentRows;

        [Header("Next level")]
        [SerializeField] private TMP_Text nextHeaderText;
        [SerializeField] private RectTransform nextRows;

        [Tooltip("Inactive row cloned into both lists.")]
        [SerializeField] private KeyValueRowUI rowTemplate;

        [SerializeField] private TMP_Text hintText;
        [SerializeField] private Button upgradeButton;
        [SerializeField] private TMP_Text upgradeButtonText;

        private readonly List<KeyValueRowUI> _currentPool = new List<KeyValueRowUI>();
        private readonly List<KeyValueRowUI> _nextPool = new List<KeyValueRowUI>();
        private readonly List<(string label, string value)> _rows = new List<(string, string)>();

        private Action<CompanyUpgradeType> _onUpgrade;
        private UpgradeInfo _info;
        private TooltipTrigger _buttonTooltip;

        public void Initialize(Action<CompanyUpgradeType> onUpgrade)
        {
            _onUpgrade = onUpgrade;

            if (rowTemplate != null) rowTemplate.gameObject.SetActive(false);

            if (upgradeButton != null)
            {
                upgradeButton.onClick.RemoveAllListeners();
                upgradeButton.onClick.AddListener(() =>
                {
                    if (_info != null) _onUpgrade?.Invoke(_info.Type);
                });
                _buttonTooltip = TooltipTrigger.On(upgradeButton);
            }
        }

        public void Show(UpgradeInfo info)
        {
            _info = info;
            if (info == null) return;

            SetText(nameText, info.Name);
            SetText(levelText, UpgradePresentation.LevelLabel(info));
            if (levelBar != null) levelBar.Set(info.DisplayLevel, info.LevelCount);
            SetText(descriptionText, info.Description);

            if (effectNoteText != null)
            {
                effectNoteText.gameObject.SetActive(!info.EffectActive);
                effectNoteText.text = UIFormat.Colorize(
                    $"{UpgradePresentation.EffectComingSoon} — this upgrade can be bought, but no game system uses its level yet.",
                    UIFormat.MutedColor);
            }

            // Current level
            SetText(currentHeaderText, $"CURRENT LEVEL (Level {info.DisplayLevel})");
            _rows.Clear();
            _rows.AddRange(UpgradePresentation.EffectRows(info.CurrentEffects));
            _rows.Add(("Daily upkeep", UpgradePresentation.PerDay(info.CurrentUpkeep)));
            Fill(_currentPool, currentRows, _rows);

            // Next level
            _rows.Clear();
            if (info.IsMaxed)
            {
                SetText(nextHeaderText, "NEXT LEVEL");
                _rows.Add((UIFormat.Colorize("Maximum level reached", UIFormat.MutedColor), ""));
            }
            else
            {
                SetText(nextHeaderText, $"NEXT LEVEL (Level {info.DisplayLevel + 1})");
                foreach (var (label, value) in UpgradePresentation.EffectRows(info.NextEffects))
                    _rows.Add(value.Length > 0
                        ? (label, UIFormat.Colorize(value, UIFormat.PositiveColor))
                        : (UIFormat.Colorize(label, UIFormat.PositiveColor), ""));

                _rows.Add(("Daily upkeep", UpgradePresentation.PerDay(info.NextUpkeep)));
                if (info.RequiredReputation > 0)
                    _rows.Add(("Required reputation", UIFormat.Colorize(UIFormat.Number(info.RequiredReputation),
                        info.MeetsReputation ? UIFormat.PositiveColor : UIFormat.NegativeColor)));
            }
            Fill(_nextPool, nextRows, _rows);

            SetText(hintText, Hint(info));

            if (upgradeButton != null) upgradeButton.interactable = info.CanUpgrade;
            SetText(upgradeButtonText, info.IsMaxed ? "Maximum level reached" : $"Upgrade for {UIFormat.Money(info.NextCost)}");
            if (_buttonTooltip != null) _buttonTooltip.Text = UpgradePresentation.ButtonTooltip(info);
        }

        private static string Hint(UpgradeInfo info)
        {
            if (info.IsMaxed)
                return "This upgrade is fully developed.";
            if (!info.MeetsReputation)
                return $"Reach {UIFormat.Number(info.RequiredReputation)} reputation to unlock the next level. " +
                       "Complete contracts and sell antiques to grow your reputation.";
            if (!info.CanAfford)
                return $"You need {UIFormat.Money(info.NextCost - info.Cash)} more to buy the next level.";
            if (info.NextUpkeep > info.CurrentUpkeep)
                return $"The next level raises daily upkeep by {UIFormat.Money(info.NextUpkeep - info.CurrentUpkeep)}.";
            return "";
        }

        private void Fill(List<KeyValueRowUI> pool, RectTransform container, List<(string label, string value)> rows)
        {
            if (container == null || rowTemplate == null) return;

            while (pool.Count < rows.Count)
            {
                var row = Instantiate(rowTemplate, container);
                row.name = $"Row{pool.Count + 1}";
                pool.Add(row);
            }

            for (int i = 0; i < pool.Count; i++)
            {
                bool visible = i < rows.Count;
                pool[i].gameObject.SetActive(visible);
                if (visible) pool[i].Set(rows[i].label, rows[i].value);
            }
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null) text.text = value;
        }
    }
}
