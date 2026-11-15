using System;
using System.Collections.Generic;
using System.Linq;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Events;
using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.News;
using AntiqueTradingSimulator.UI;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.Rumours
{
    /// <summary>
    /// "Create Rumour" modal: choose whether prices are said to rise or fall, name the antique types,
    /// countries and centuries the rumour covers, set when it spreads, the date it claims and how much
    /// to spend, then plant it through RumourMill.
    /// </summary>
    public class CreateRumourModalUI : MonoBehaviour
    {
        [Header("Game systems (auto-found if empty)")]
        [SerializeField] private TimeManager timeManager;
        [SerializeField] private NewsManager newsManager;

        [Header("Chip colors")]
        [SerializeField] private Color chipNormal = new Color32(0x3A, 0x33, 0x29, 0xFF);
        [SerializeField] private Color chipSelected = new Color32(0xB8, 0x86, 0x2B, 0xFF);
        [SerializeField] private Color chipBlocked = new Color32(0x2A, 0x26, 0x20, 0xFF);
        [SerializeField] private Color chipText = new Color32(0xD8, 0xCF, 0xBE, 0xFF);
        [SerializeField] private Color chipTextSelected = new Color32(0x1A, 0x15, 0x0E, 0xFF);
        [SerializeField] private Color chipTextBlocked = new Color32(0x6B, 0x63, 0x57, 0xFF);
        [SerializeField] private Color hintColor = new Color32(0x9E, 0x9E, 0x9E, 0xFF);
        [SerializeField] private Color errorColor = new Color32(0xD1, 0x4C, 0x4C, 0xFF);

        [Header("Chip layout")]
        [Tooltip("Chips per row in each value list.")]
        [SerializeField, Min(1)] private int chipColumns = 5;
        [SerializeField, Min(12f)] private float chipHeight = 22f;
        [SerializeField, Min(0f)] private float chipSpacing = 4f;
        [SerializeField, Min(6f)] private float chipFontSize = 10f;
        [Tooltip("Used when the value list has no measured width yet.")]
        [SerializeField, Min(100f)] private float fallbackListWidth = 420f;

        [Header("Options")]
        [Tooltip("Pause the game clock while the modal is open.")]
        [SerializeField] private bool pauseTimeWhileOpen = true;

        [Tooltip("Claimed date preselected, in weeks after spreading (3 lets every NPC react in time).")]
        [SerializeField] private int defaultClaimLeadWeeks = 3;

        /// <summary>Raised after a rumour was planted: what it claimed, spread week, claimed week.</summary>
        public event Action<RumourClaim, int, int> RumourPlanted;

        private const string NoValue = "—";
        private const string Details = "Window/Body/DetailsColumn/";
        private const string Claims = "Window/Body/ClaimColumn/";

        // References
        private TMP_Text claimSummaryText, affectedValue, credibilityValue, costValue, reachValue, traceValue;
        private TMP_Text hintText;
        private Stepper spreadStepper, claimStepper, spendStepper;
        private Button closeButton, cancelButton, createButton;
        private Chip riseChip, fallChip;
        private readonly List<ChipGroup> groups = new List<ChipGroup>();
        private TMP_FontAsset font;

        private class Stepper
        {
            public Button Decrease, Increase;
            public TMP_Text Value, Date;
        }

        private class Chip
        {
            public Button Button;
            public Image Background;
            public TMP_Text Text;
            public string Label;
            public Func<bool> IsNamed;
            public Action Toggle;
        }

        /// <summary>One attribute's chips, with the heading that lists what is named in it.</summary>
        private class ChipGroup
        {
            public TMP_Text Heading;
            public string Title;
            public readonly List<Chip> Chips = new List<Chip>();
        }

        // State
        private readonly RumourClaim claim = new RumourClaim(RumourDirection.Rise);
        private List<float> spendOptions = new List<float>();
        private int spreadDelay, claimLead, spendIndex;
        private bool initialized, pausedByModal;

        private RumourMill Mill => newsManager != null ? newsManager.Rumours : null;
        private FakeRumourSettings Settings => Mill != null ? Mill.Settings : FakeRumourSettings.Active;

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        public void Open()
        {
            // Active first: the chip grid is sized from the measured width of its list.
            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            EnsureInitialized();

            if (pauseTimeWhileOpen && timeManager != null && timeManager.IsRunning)
            {
                timeManager.Pause();
                pausedByModal = true;
            }

            var s = Settings;
            claim.Clear();
            claim.Direction = RumourDirection.Rise;
            spreadDelay = s.SpreadDelayWeeks.x;
            claimLead = Mathf.Clamp(defaultClaimLeadWeeks, s.ClaimLeadWeeks.x, s.ClaimLeadWeeks.y);
            spendIndex = 0;

            RebuildSpendOptions(keepIndex: false);
            UpdateQuote();
        }

        public void Close() => gameObject.SetActive(false);

        // ------------------------------------------------------------------
        // Unity
        // ------------------------------------------------------------------

        private void OnDisable()
        {
            if (pausedByModal && timeManager != null)
                timeManager.Resume();

            pausedByModal = false;
        }

        // ------------------------------------------------------------------
        // Setup
        // ------------------------------------------------------------------

        private void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;

            if (timeManager == null) timeManager = FindAnyObjectByType<TimeManager>();
            if (newsManager == null) newsManager = FindAnyObjectByType<NewsManager>();

            var sample = GetComponentInChildren<TMP_Text>(true);
            font = sample != null ? sample.font : null;

            closeButton = FindComponent<Button>("Window/Header/CloseButton");
            cancelButton = FindComponent<Button>("Window/Footer/CancelButton");
            createButton = FindComponent<Button>("Window/Footer/CreateButton");

            claimSummaryText = FindComponent<TMP_Text>(Details + "ClaimSummary/Value");
            affectedValue = FindComponent<TMP_Text>(Details + "DetailRows/Affected/Value");
            credibilityValue = FindComponent<TMP_Text>(Details + "DetailRows/Credibility/Value");
            costValue = FindComponent<TMP_Text>(Details + "DetailRows/Cost/Value");
            reachValue = FindComponent<TMP_Text>(Details + "DetailRows/Reach/Value");
            traceValue = FindComponent<TMP_Text>(Details + "DetailRows/Trace/Value");

            spreadStepper = FindStepper("SpreadRow");
            claimStepper = FindStepper("ClaimRow");
            spendStepper = FindStepper("SpendRow");
            hintText = FindComponent<TMP_Text>(Details + "ScheduleHint");

            BuildDirectionButtons();
            BuildChips();

            // Labels live in code so the copied Create Event texts can't stay behind.
            SetPathText("Window/Header/HeaderInfo/TitleText", "CREATE RUMOUR");
            SetPathText("Window/Header/HeaderInfo/SubtitleText",
                "Put it about that prices are moving. Name more things and it reaches further, but costs more to make believed.");
            SetPathText(Claims + "SectionTitle", "1. WHAT THE RUMOUR SAYS");
            SetPathText(Details + "SectionTitle", "2. WHAT IT WILL COST");
            SetPathText(Details + "ClaimSummary/Label", "Rumour:");
            SetPathText(Details + "DetailRows/Affected/Label", "Antiques covered:");
            SetPathText(Details + "DetailRows/Credibility/Label", "Credibility:");
            SetPathText(Details + "DetailRows/Cost/Label", "Cost:");
            SetPathText(Details + "DetailRows/Reach/Label", "Traders acting:");
            SetPathText(Details + "DetailRows/Trace/Label", "Trace risk:");
            SetPathText(Details + "ScheduleTitle", "3. SPREAD AND SPEND");
            SetPathText(Details + "SpreadRow/Label", "Spreads in:");
            SetPathText(Details + "ClaimRow/Label", "Claims move in:");
            SetPathText(Details + "SpendRow/Label", "Spend:");
            SetPathText("Window/Footer/NoteText",
                "Every trader hears the rumour in the week it spreads. It is debunked when the claimed date passes.");
            SetPathText("Window/Footer/CreateButton/Text", "Plant Rumour");

            AddListener(closeButton, Close);
            AddListener(cancelButton, Close);
            AddListener(createButton, PlantRumour);

            BindStepper(spreadStepper, delta => { spreadDelay += delta; UpdateQuote(); });
            BindStepper(claimStepper, delta => { claimLead += delta; UpdateQuote(); });
            BindStepper(spendStepper, delta => { spendIndex += delta; UpdateQuote(); });
        }

        private void BuildDirectionButtons()
        {
            var row = transform.Find(Claims + "DirectionRow");
            if (row == null)
            {
                Debug.LogError($"[CreateRumourModalUI] '{Claims}DirectionRow' not found.", this);
                return;
            }

            ClearBuiltChildren(row);

            var label = row.Find("Label");
            if (label != null) SetText(label.GetComponent<TMP_Text>(), "Prices are said to:");

            riseChip = MakeChip(row, "RiseButton", "Rise", 76f,
                () => claim.Rising, () => SetDirection(RumourDirection.Rise));

            fallChip = MakeChip(row, "FallButton", "Fall", 76f,
                () => !claim.Rising, () => SetDirection(RumourDirection.Fall));
        }

        /// <summary>Fills the three value lists from the enums. The "any" members are skipped.</summary>
        private void BuildChips()
        {
            groups.Clear();

            AddGroup("TypeSection", "ANTIQUE TYPES",
                ((AntiqueType[])Enum.GetValues(typeof(AntiqueType))).Where(v => v != AntiqueType.Other),
                v => v.ToDisplayString(), v => claim.Contains(v), v => claim.Toggle(v));

            AddGroup("CountrySection", "COUNTRIES",
                ((Country[])Enum.GetValues(typeof(Country))).Where(v => v != Country.Other),
                v => v.ToDisplayString(), v => claim.Contains(v), v => claim.Toggle(v));

            AddGroup("CenturySection", "CENTURIES",
                ((Century[])Enum.GetValues(typeof(Century))).Where(v => v != Century.Unknown),
                v => v.ToString(), v => claim.Contains(v), v => claim.Toggle(v));
        }

        private void AddGroup<T>(string section, string title, IEnumerable<T> values,
            Func<T, string> label, Func<T, bool> isNamed, Func<T, bool> toggle)
        {
            var sectionRoot = transform.Find(Claims + section);
            var list = sectionRoot != null ? sectionRoot.Find("ValueList") as RectTransform : null;

            if (list == null)
            {
                Debug.LogError($"[CreateRumourModalUI] '{Claims}{section}/ValueList' not found. " +
                               "Rebuild the modal with Tools > UI > News & Events > Build Create Rumour Modal.", this);
                return;
            }

            ClearBuiltChildren(list);

            var group = new ChipGroup
            {
                Title = title,
                Heading = sectionRoot.Find("SectionLabel")?.GetComponent<TMP_Text>()
            };

            float cellWidth = ConfigureGrid(list, values.Count());

            foreach (var value in values)
            {
                T captured = value;
                group.Chips.Add(MakeChip(list, $"Chip_{captured}", label(captured), cellWidth,
                    () => isNamed(captured), () => { toggle(captured); OnClaimChanged(); }));
            }

            groups.Add(group);
        }

        /// <summary>Lays the list out as a grid of chipColumns, sized to the list's own width.</summary>
        private float ConfigureGrid(RectTransform list, int count)
        {
            var grid = list.GetComponent<GridLayoutGroup>();
            if (grid == null) grid = list.gameObject.AddComponent<GridLayoutGroup>();

            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = Mathf.Max(1, chipColumns);
            grid.spacing = new Vector2(chipSpacing, chipSpacing);

            float width = list.rect.width;
            if (width <= 1f) width = fallbackListWidth;
            width -= grid.padding.horizontal;

            int columns = grid.constraintCount;
            float cellWidth = Mathf.Max(40f, (width - chipSpacing * (columns - 1)) / columns);
            grid.cellSize = new Vector2(cellWidth, chipHeight);

            // The grid does not size itself, so the column layout needs the height it will take.
            int rows = Mathf.CeilToInt(count / (float)columns);
            var element = list.GetComponent<LayoutElement>();
            if (element == null) element = list.gameObject.AddComponent<LayoutElement>();
            element.preferredHeight = rows * chipHeight + (rows - 1) * chipSpacing;
            element.minHeight = element.preferredHeight;

            return cellWidth;
        }

        /// <summary>A chip: its own background, button and centred label. Nothing is cloned.</summary>
        private Chip MakeChip(Transform parent, string name, string label, float width,
            Func<bool> isNamed, Action toggle)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            var rect = (RectTransform)go.transform;
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(width, chipHeight);

            var background = go.GetComponent<Image>();
            background.color = chipNormal;

            var button = go.GetComponent<Button>();
            button.targetGraphic = background;
            // The chip's own colours say what is named, so the button must not tint over them.
            button.transition = Selectable.Transition.None;

            var textObject = new GameObject("Text", typeof(RectTransform));
            var textRect = (RectTransform)textObject.transform;
            textRect.SetParent(rect, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(3f, 0f);
            textRect.offsetMax = new Vector2(-3f, 0f);

            var text = textObject.AddComponent<TextMeshProUGUI>();
            if (font != null) text.font = font;
            text.text = label;
            text.fontSize = chipFontSize;
            text.color = chipText;
            text.alignment = TextAlignmentOptions.Center;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;

            // A fixed width in a horizontal row; the grid overrides it in a value list.
            var element = go.AddComponent<LayoutElement>();
            element.preferredWidth = width;
            element.preferredHeight = chipHeight;

            var chip = new Chip
            {
                Button = button,
                Background = background,
                Text = text,
                Label = label,
                IsNamed = isNamed,
                Toggle = toggle
            };

            AddListener(button, () => chip.Toggle());
            return chip;
        }

        /// <summary>Removes chips from an earlier build, and any template item the builder left behind.</summary>
        private static void ClearBuiltChildren(Transform parent)
        {
            for (int i = parent.childCount - 1; i >= 0; i--)
            {
                var child = parent.GetChild(i);
                if (child.name == "Label") continue;

                // Hidden at once: Destroy is deferred, and an active leftover would take a grid cell.
                child.gameObject.SetActive(false);
                Destroy(child.gameObject);
            }
        }

        private Stepper FindStepper(string row)
        {
            string path = Details + row + "/";
            return new Stepper
            {
                Decrease = FindComponent<Button>(path + "LeadStepper/DecreaseButton"),
                Increase = FindComponent<Button>(path + "LeadStepper/IncreaseButton"),
                Value = FindComponent<TMP_Text>(path + "LeadStepper/LeadText"),
                Date = FindComponent<TMP_Text>(path + "DateText")
            };
        }

        private static void BindStepper(Stepper stepper, Action<int> change)
        {
            if (stepper == null) return;
            AddListener(stepper.Decrease, () => change(-1));
            AddListener(stepper.Increase, () => change(+1));
        }

        private T FindComponent<T>(string path) where T : Component
        {
            var t = transform.Find(path);
            if (t == null)
            {
                Debug.LogWarning($"[CreateRumourModalUI] '{path}' not found.", this);
                return null;
            }
            return t.GetComponent<T>();
        }

        private void SetPathText(string path, string value)
        {
            var t = transform.Find(path);
            if (t != null) SetText(t.GetComponent<TMP_Text>(), value);
        }

        private static void AddListener(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null) button.onClick.AddListener(action);
        }

        // ------------------------------------------------------------------
        // Editing the claim
        // ------------------------------------------------------------------

        private void SetDirection(RumourDirection direction)
        {
            claim.Direction = direction;
            OnClaimChanged();
        }

        /// <summary>The claim decides the spend range, so the options are rebuilt on every change.</summary>
        private void OnClaimChanged()
        {
            RebuildSpendOptions(keepIndex: true);
            UpdateQuote();
        }

        private void RebuildSpendOptions(bool keepIndex)
        {
            float previous = keepIndex && spendOptions.Count > 0
                ? spendOptions[Mathf.Clamp(spendIndex, 0, spendOptions.Count - 1)]
                : -1f;

            spendOptions = Mill != null ? Mill.SpendOptions(claim) : new List<float>();

            if (spendOptions.Count == 0)
            {
                spendIndex = 0;
                return;
            }

            // Keep the spend the player picked where the new range still allows it.
            if (previous > 0f)
            {
                int nearest = 0;
                for (int i = 1; i < spendOptions.Count; i++)
                    if (Mathf.Abs(spendOptions[i] - previous) < Mathf.Abs(spendOptions[nearest] - previous))
                        nearest = i;

                spendIndex = nearest;
                return;
            }

            spendIndex = DefaultSpendIndex();
        }

        /// <summary>First option at or above K (63% of the way to the cap).</summary>
        private int DefaultSpendIndex()
        {
            if (spendOptions.Count == 0 || Mill == null) return 0;

            var probe = Mill.Quote(claim, spreadDelay, claimLead, 0f);
            for (int i = 0; i < spendOptions.Count; i++)
                if (spendOptions[i] >= probe.CostScale) return i;

            return spendOptions.Count - 1;
        }

        // ------------------------------------------------------------------
        // Quote
        // ------------------------------------------------------------------

        private void UpdateQuote()
        {
            var s = Settings;

            RefreshChips();

            if (Mill == null || timeManager == null)
            {
                SetHint("No RumourMill in the scene.", true);
                SetInteractable(createButton, false);
                return;
            }

            spreadDelay = Mathf.Clamp(spreadDelay, s.SpreadDelayWeeks.x, s.SpreadDelayWeeks.y);
            claimLead = Mathf.Clamp(claimLead, s.ClaimLeadWeeks.x, s.ClaimLeadWeeks.y);

            RumourQuote quote = Mill.Quote(claim, spreadDelay, claimLead,
                spendOptions.Count > 0 ? spendOptions[Mathf.Clamp(spendIndex, 0, spendOptions.Count - 1)] : 0f);

            // The headline traders will actually read, from the same wording a genuine rumour uses.
            SetText(claimSummaryText, claim.IsEmpty
                ? "Name what the rumour covers"
                : NewsWording.Headline(quote.Effects, TextBudget.Normal));

            SetStepper(spreadStepper, NewsPresentation.WeeksLabel(spreadDelay),
                TimeManager.FormatLong(timeManager.WeekToDate(quote.SpreadWeek)),
                spreadDelay > s.SpreadDelayWeeks.x, spreadDelay < s.SpreadDelayWeeks.y);

            SetStepper(claimStepper, $"+{NewsPresentation.WeeksLabel(claimLead)}",
                TimeManager.FormatLong(timeManager.WeekToDate(quote.ClaimWeek)),
                claimLead > s.ClaimLeadWeeks.x, claimLead < s.ClaimLeadWeeks.y);

            bool locked = quote.Cap <= 0f;
            bool priceable = !claim.IsEmpty && spendOptions.Count > 0 && !locked;

            if (!priceable)
            {
                SetText(affectedValue, claim.IsEmpty ? NoValue : quote.AffectedCount.ToString());
                SetText(credibilityValue, locked ? "Locked" : NoValue);
                SetText(costValue, NoValue);
                SetText(reachValue, NoValue);
                SetText(traceValue, NoValue);
                SetStepper(spendStepper, NoValue, "", false, false);
                SetHint(quote.Blocker ?? "Name at least one type, country or century.", !claim.IsEmpty);
                SetInteractable(createButton, false);
                return;
            }

            float cash = Mill.Player != null && Mill.Player.Inventory != null ? Mill.Player.Inventory.Cash : 0f;
            SetStepper(spendStepper, UIFormat.Money(quote.Spend), $"Cash: {UIFormat.Money(cash)}",
                spendIndex > 0, spendIndex < spendOptions.Count - 1);

            SetText(affectedValue, $"{quote.AffectedCount} ({claim.TotalValues} of {s.MaxTotalValues} named)");

            string credibility = $"{UIFormat.Percent(quote.Credibility)}  (cap {UIFormat.Percent(quote.Cap)})";
            if (quote.Fatigue < 0.999f) credibility += $"  · fatigue ×{quote.Fatigue:0.00}";
            SetText(credibilityValue, credibility);

            string cost = UIFormat.Money(quote.Spend);
            if (quote.LevelDiscount > 0.001f) cost += $"  · network -{UIFormat.PercentCompact(quote.LevelDiscount)}";
            SetText(costValue, cost);

            int traders = Mill.Npcs != null ? Mill.Npcs.NPCs.Count : 0;
            SetText(reachValue, $"about {quote.ExpectedReactors:0.0} of {traders}");
            SetText(traceValue, UIFormat.Percent(quote.TraceChance));

            if (!quote.CanPlant)
                SetHint(quote.Blocker, true);
            else
                SetHint($"Market skepticism {UIFormat.Percent(Mill.Skepticism)} · " +
                        "a wider rumour reaches more traders but needs more spending to be believed.", false);

            SetInteractable(createButton, quote.CanPlant);
        }

        /// <summary>
        /// Colours every chip by whether it is named, and lists what is named in each heading, so the
        /// chosen values can be read without hunting for the highlight.
        /// </summary>
        private void RefreshChips()
        {
            bool atLimit = claim.TotalValues >= Settings.MaxTotalValues;

            Paint(riseChip, false);
            Paint(fallChip, false);

            foreach (var group in groups)
            {
                var chosen = new List<string>();

                foreach (var chip in group.Chips)
                {
                    bool named = chip.IsNamed();
                    if (named) chosen.Add(chip.Label);

                    Paint(chip, atLimit && !named);
                }

                if (group.Heading != null)
                    group.Heading.text = chosen.Count > 0
                        ? $"{group.Title}  ·  {string.Join(", ", chosen)}"
                        : group.Title;
            }
        }

        private void Paint(Chip chip, bool blocked)
        {
            if (chip == null) return;

            bool named = chip.IsNamed();

            if (chip.Background != null)
                chip.Background.color = named ? chipSelected : blocked ? chipBlocked : chipNormal;

            if (chip.Text != null)
                chip.Text.color = named ? chipTextSelected : blocked ? chipTextBlocked : chipText;

            SetInteractable(chip.Button, named || !blocked);
        }

        private void PlantRumour()
        {
            if (Mill == null || claim.IsEmpty || spendOptions.Count == 0) return;

            float spend = spendOptions[Mathf.Clamp(spendIndex, 0, spendOptions.Count - 1)];
            if (!Mill.TryPlant(claim, spreadDelay, claimLead, spend, out string error))
            {
                SetHint(error, true);
                return;
            }

            int week = timeManager != null ? timeManager.CurrentWeek : 0;
            RumourPlanted?.Invoke(claim.Copy(), week + spreadDelay, week + spreadDelay + claimLead);
            Close();
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private static void SetStepper(Stepper stepper, string value, string date, bool canDecrease, bool canIncrease)
        {
            if (stepper == null) return;
            SetText(stepper.Value, value);
            SetText(stepper.Date, date);
            SetInteractable(stepper.Decrease, canDecrease);
            SetInteractable(stepper.Increase, canIncrease);
        }

        private void SetHint(string text, bool isError)
        {
            if (hintText == null) return;
            hintText.text = text;
            hintText.color = isError ? errorColor : hintColor;
        }

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null) text.text = value;
        }

        private static void SetInteractable(Button button, bool value)
        {
            if (button != null) button.interactable = value;
        }
    }
}
