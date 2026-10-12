using System;
using System.Collections.Generic;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.Restoration;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// "Restore?" modal opened from the collection: a card per workshop (condition reached, fee,
    /// time, risk), and for the selected one the value now and after (at today's prices), the
    /// chance of each outcome, the expected gain, when it's ready and cash left. Confirm stays
    /// disabled — with the reason shown — when the job can't start.
    /// Built by Tools → Antique Trading Simulator → Build Restoration.
    /// </summary>
    public class RestorationModalUI : MonoBehaviour, IModalPanel
    {
        [Header("Game systems (auto-found if empty)")]
        [SerializeField] private PlayerTrader playerTrader;
        [SerializeField] private RestorationManager restorationManager;
        [SerializeField] private TimeManager timeManager;

        [Header("Options")]
        [Tooltip("Pause the game clock while the modal is open, so prices and quotes can't change before confirming.")]
        [SerializeField] private bool pauseTimeWhileOpen = true;

        [Header("Item")]
        [SerializeField] private TMP_Text titleText;    // "Restore Chinese Vase?"
        [SerializeField] private TMP_Text itemInfoText; // "Poor condition (40%) · XVIII century · China"

        [Header("Workshops")]
        [SerializeField] private RectTransform optionsContainer;
        [Tooltip("Inactive card cloned once per workshop.")]
        [SerializeField] private RestorationOptionUI optionTemplate;

        [Header("Summary")]
        [SerializeField] private TMP_Text conditionText; // "Poor 40% → Good 85%"
        [SerializeField] private TMP_Text valueText;     // "4 000 € → 8 500 €"
        [SerializeField] private TMP_Text costText;      // "2 025 €"
        [SerializeField] private TMP_Text readyText;     // "18 October (in 6 days)"
        [SerializeField] private TMP_Text cashAfterText; // "2 630 €"
        [SerializeField] private TMP_Text outcomesText;  // chances of each result
        [SerializeField] private TMP_Text expectedText;  // average gain
        [SerializeField] private TMP_Text warningText;   // why Confirm is disabled

        [Header("Buttons")]
        [SerializeField] private Button closeButton;
        [SerializeField] private Button cancelButton;
        [SerializeField] private Button confirmButton;

        /// <summary>Raised after the antique was sent to a workshop.</summary>
        public event Action<Antique> Started;

        private readonly List<RestorationOptionUI> _cards = new();
        private Antique _antique;
        private string _selectedOptionId;
        private bool _initialized;
        private bool _pausedByModal;
        private bool _subscribed;

        private TraderInventory Inventory => playerTrader != null ? playerTrader.Inventory : null;

        // ------------------------------------------------------------------
        // Open / close
        // ------------------------------------------------------------------

        public void Open(Antique antique)
        {
            if (antique == null)
                return;

            EnsureInitialized();

            if (restorationManager == null)
            {
                Debug.LogWarning("RestorationModalUI: no RestorationManager in the scene — run Tools > Antique Trading Simulator > Build Restoration.");
                return;
            }

            _antique = antique;
            _selectedOptionId = DefaultOptionId(antique);

            gameObject.SetActive(true);
            transform.SetAsLastSibling();

            if (pauseTimeWhileOpen && timeManager != null && timeManager.IsRunning)
            {
                timeManager.Pause();
                _pausedByModal = true;
            }

            Refresh();
        }

        public void Close()
        {
            _antique = null;
            gameObject.SetActive(false);
        }

        private void OnEnable()
        {
            ModalTracker.SetOpen(this);
            Subscribe();
        }

        private void OnDisable()
        {
            ModalTracker.SetClosed(this);
            Unsubscribe();

            // Resume even if something else hides the modal.
            if (_pausedByModal && timeManager != null)
                timeManager.Resume();

            _pausedByModal = false;
        }

        private void Subscribe()
        {
            if (_subscribed || Inventory == null) return;

            Inventory.OnCashChanged += HandleCashChanged;
            Inventory.OnStateRestored += Close;
            if (timeManager != null) timeManager.OnDayChanged += HandleDayChanged;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;

            if (Inventory != null)
            {
                Inventory.OnCashChanged -= HandleCashChanged;
                Inventory.OnStateRestored -= Close;
            }
            if (timeManager != null) timeManager.OnDayChanged -= HandleDayChanged;
            _subscribed = false;
        }

        // Only relevant when the clock keeps running while the modal is open.
        private void HandleCashChanged(float cash) => Refresh();
        private void HandleDayChanged(int day) => Refresh();

        private void EnsureInitialized()
        {
            if (_initialized)
                return;

            _initialized = true;

            if (playerTrader == null) playerTrader = FindFirstObjectByType<PlayerTrader>();
            if (restorationManager == null) restorationManager = FindFirstObjectByType<RestorationManager>();
            if (timeManager == null) timeManager = FindFirstObjectByType<TimeManager>();

            if (closeButton != null) closeButton.onClick.AddListener(Close);
            if (cancelButton != null) cancelButton.onClick.AddListener(Close);
            if (confirmButton != null) confirmButton.onClick.AddListener(Confirm);

            if (optionTemplate != null) optionTemplate.gameObject.SetActive(false);

            // OnEnable may have run before the references were resolved.
            if (isActiveAndEnabled)
                Subscribe();
        }

        // The first workshop that can take the job; the first one at all if none can.
        private string DefaultOptionId(Antique antique)
        {
            string first = null;
            foreach (var option in restorationManager.Options)
            {
                if (option == null) continue;
                first ??= option.Id;

                var quote = restorationManager.Quote(antique, option);
                if (quote != null && quote.CanStart)
                    return option.Id;
            }
            return first;
        }

        private void SelectOption(string optionId)
        {
            _selectedOptionId = optionId;
            Refresh();
        }

        // ------------------------------------------------------------------
        // Content
        // ------------------------------------------------------------------

        private void Refresh()
        {
            if (_antique == null || restorationManager == null)
                return;

            var antique = _antique;

            SetText(titleText, $"Restore <b>{antique.Name}</b>?");
            SetText(itemInfoText,
                $"{UIFormat.ConditionLabel(antique.Condition)} condition ({UIFormat.Percent(antique.Condition)}) · " +
                $"{antique.Century.ToDisplayString()} · {antique.Country.ToDisplayString()}");

            RestorationQuote selected = RefreshCards(antique);
            RefreshSummary(selected);

            string itemReason = restorationManager.ItemBlockReason(antique);
            string reason = itemReason ?? selected?.BlockReason;
            SetText(warningText, reason != null ? UIFormat.Colorize(reason, UIFormat.NegativeColor) : "");

            if (confirmButton != null)
                confirmButton.interactable = selected != null && selected.CanStart;
        }

        /// <summary>Fills one card per workshop and returns the selected workshop's quote.</summary>
        private RestorationQuote RefreshCards(Antique antique)
        {
            var options = restorationManager.Options;
            EnsureCards(options.Count);

            RestorationQuote selected = null;
            for (int i = 0; i < _cards.Count; i++)
            {
                var card = _cards[i];
                if (i >= options.Count || options[i] == null)
                {
                    card.gameObject.SetActive(false);
                    continue;
                }

                var quote = restorationManager.Quote(antique, options[i]);
                card.Show(quote);

                bool isSelected = options[i].Id == _selectedOptionId;
                card.SetSelected(isSelected);
                if (isSelected) selected = quote;
            }

            return selected;
        }

        private void EnsureCards(int count)
        {
            if (optionTemplate == null || optionsContainer == null) return;

            while (_cards.Count < count)
            {
                var card = Instantiate(optionTemplate, optionsContainer);
                card.name = $"Option{_cards.Count + 1}";
                card.Clicked += SelectOption;
                _cards.Add(card);
            }
        }

        private void RefreshSummary(RestorationQuote quote)
        {
            if (quote == null)
            {
                SetText(conditionText, "—");
                SetText(valueText, "—");
                SetText(costText, "—");
                SetText(readyText, "—");
                SetText(cashAfterText, "—");
                SetText(outcomesText, "");
                SetText(expectedText, "");
                return;
            }

            bool improves = quote.TargetCondition > quote.StartCondition;

            SetText(conditionText,
                $"{ConditionLabel(quote.StartCondition)} → " +
                UIFormat.Colorize(ConditionLabel(quote.TargetCondition), improves ? UIFormat.PositiveColor : UIFormat.MutedColor));

            SetText(valueText,
                $"{UIFormat.Money(quote.ValueNow)} → " +
                UIFormat.Colorize(UIFormat.Money(quote.ValueAtTarget), improves ? UIFormat.PositiveColor : UIFormat.MutedColor) +
                UIFormat.Colorize("  (today's prices)", UIFormat.MutedColor));

            SetText(costText, improves ? $"<b>{UIFormat.Money(quote.Cost)}</b>" : "—");
            SetText(readyText, ReadyDate(quote));

            float cash = Inventory != null ? Inventory.Cash : 0f;
            float after = cash - quote.Cost;
            SetText(cashAfterText, UIFormat.ColorBySign(UIFormat.Money(after), after < 0f ? -1f : 0f));

            if (!improves)
            {
                SetText(outcomesText, "");
                SetText(expectedText, "");
                return;
            }

            SetText(outcomesText, OutcomesLine(quote));
            SetText(expectedText,
                $"Expected gain: {UIFormat.ColorBySign(UIFormat.SignedMoney(quote.ExpectedProfit), quote.ExpectedProfit)} " +
                UIFormat.Colorize("on average after the fee, if prices stay as they are today.", UIFormat.MutedColor));
        }

        private static string OutcomesLine(RestorationQuote q)
        {
            var parts = new List<string>
            {
                UIFormat.Colorize($"{UIFormat.Percent(q.SuccessChance)} full success → {UIFormat.Percent(q.TargetCondition)}", UIFormat.PositiveColor)
            };

            if (q.PartialChance > 0f)
                parts.Add($"{UIFormat.Percent(q.PartialChance)} partial → {UIFormat.Percent(q.PartialCondition)}");

            if (q.FailureChance > 0f)
                parts.Add(UIFormat.Colorize($"{UIFormat.Percent(q.FailureChance)} damaged → down to {UIFormat.Percent(q.WorstCondition)}", UIFormat.NegativeColor));

            return string.Join("  ·  ", parts);
        }

        private string ReadyDate(RestorationQuote quote)
        {
            if (timeManager == null)
                return $"Day {quote.EndDay} (in {UIFormat.Days(quote.DurationDays)})";

            string date = TimeManager.FormatDayMonth(timeManager.DayToDate(quote.EndDay));
            return $"{date} (in {UIFormat.Days(quote.DurationDays)})";
        }

        private static string ConditionLabel(float condition) =>
            $"{UIFormat.ConditionLabel(condition)} {UIFormat.Percent(condition)}";

        // ------------------------------------------------------------------
        // Confirm
        // ------------------------------------------------------------------

        private void Confirm()
        {
            if (_antique == null || restorationManager == null || string.IsNullOrEmpty(_selectedOptionId))
                return;

            Antique antique = _antique;
            if (!restorationManager.StartRestoration(antique, _selectedOptionId))
            {
                Refresh(); // shows the concrete reason if there is one
                if (confirmButton == null || confirmButton.interactable)
                    SetText(warningText, UIFormat.Colorize("The restoration couldn't be started.", UIFormat.NegativeColor));
                return;
            }

            Close();
            Started?.Invoke(antique);
        }

        private static void SetText(TMP_Text target, string value)
        {
            if (target != null)
                target.text = value;
        }
    }
}
