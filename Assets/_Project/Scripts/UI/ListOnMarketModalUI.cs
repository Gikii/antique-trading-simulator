using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Market;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// "List on market" modal: the player sets an asking price for an owned antique and
    /// sees the market value, a suggested price range and what they'd receive after the
    /// market fee. Confirming calls PlayerTrader.ListOnMarket — the antique stays in the
    /// collection until an NPC buys it or the player cancels the listing.
    /// </summary>
    public class ListOnMarketModalUI : MonoBehaviour, IModalPanel
    {
        // Price range suggested to the player: the item's own value over this many days.
        private const int SuggestedRangeDays = 30;
        // Used when there's not enough price history yet: current value ± this fraction.
        private const float FallbackRangeSpread = 0.1f;
        private const int MaxPriceDigits = 9;

        [Header("Game systems (auto-found if empty)")]
        [SerializeField] private PlayerTrader playerTrader;
        [SerializeField] private EconomyManager economyManager;
        [SerializeField] private TimeManager timeManager;

        [Header("Options")]
        [Tooltip("Pause the game clock while the modal is open, so prices don't move while the player decides.")]
        [SerializeField] private bool pauseTimeWhileOpen = true;

        [Header("Header")]
        [SerializeField] private Button closeButton;

        [Header("Left column")]
        [SerializeField] private Image antiqueImage;
        [SerializeField] private TMP_Text descriptionTitleText;
        [SerializeField] private TMP_Text descriptionText;

        [Header("Antique info")]
        [SerializeField] private TMP_Text nameText;
        [SerializeField] private TMP_Text categoryValue;
        [SerializeField] private TMP_Text centuryValue;
        [SerializeField] private TMP_Text originValue;
        [SerializeField] private RectTransform conditionFill;
        [SerializeField] private Image conditionFillImage;
        [SerializeField] private TMP_Text conditionValue;
        [SerializeField] private TMP_Text editionValue;

        [Header("Price")]
        [SerializeField] private TMP_InputField priceInput;
        [SerializeField] private TMP_Text marketValueText;
        [SerializeField] private TMP_Text suggestedRangeText;

        [Header("Estimated outcome")]
        [SerializeField] private TMP_Text listingPriceValue;
        [SerializeField] private TMP_Text feeLabel;
        [SerializeField] private TMP_Text feeValue;
        [SerializeField] private TMP_Text proceedsValue;
        [SerializeField] private TMP_Text priceHintText;

        [Header("Footer")]
        [SerializeField] private TMP_Text noteText;
        [SerializeField] private Button cancelButton;
        [SerializeField] private Button confirmButton;

        [Header("Colors")]
        [SerializeField] private Color warningColor = new Color32(0xE6, 0xB2, 0x40, 0xFF);

        /// <summary>Raised after the antique was listed: antique, asking price.</summary>
        public event Action<Antique, float> Listed;

        private Antique _antique;
        private float _rangeMin;
        private float _rangeMax;
        private bool _initialized;
        private bool _pausedByModal;

        private Market.Market CurrentMarket => economyManager != null ? economyManager.Market : null;

        // ------------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------------

        public void Open(Antique antique)
        {
            if (antique == null)
                return;

            EnsureInitialized();

            _antique = antique;

            gameObject.SetActive(true);
            transform.SetAsLastSibling(); // draw above other modals

            if (pauseTimeWhileOpen && timeManager != null && timeManager.IsRunning)
            {
                timeManager.Pause();
                _pausedByModal = true;
            }

            FillAntiqueInfo(antique);
            FillMarketInfo(antique);

            // Start from the current market value — the most common choice.
            if (priceInput != null)
            {
                priceInput.SetTextWithoutNotify(Mathf.RoundToInt(antique.CurrentPrice).ToString(CultureInfo.InvariantCulture));
                priceInput.Select();
                priceInput.ActivateInputField();
            }

            RefreshOutcome();
        }

        public void Close()
        {
            _antique = null;
            gameObject.SetActive(false);
        }

        // ------------------------------------------------------------------
        // Unity
        // ------------------------------------------------------------------

        private void OnEnable()
        {
            ModalTracker.SetOpen(this);

            var inventory = playerTrader != null ? playerTrader.Inventory : null;
            if (inventory != null) inventory.OnStateRestored += Close;
        }

        private void OnDisable()
        {
            ModalTracker.SetClosed(this);

            var inventory = playerTrader != null ? playerTrader.Inventory : null;
            if (inventory != null) inventory.OnStateRestored -= Close;

            // Resume even if something else hides the modal.
            if (_pausedByModal && timeManager != null)
                timeManager.Resume();

            _pausedByModal = false;
        }

        // ------------------------------------------------------------------
        // Setup
        // ------------------------------------------------------------------

        private void EnsureInitialized()
        {
            if (_initialized)
                return;

            _initialized = true;

            if (playerTrader == null) playerTrader = FindFirstObjectByType<PlayerTrader>();
            if (economyManager == null) economyManager = FindFirstObjectByType<EconomyManager>();
            if (timeManager == null) timeManager = FindFirstObjectByType<TimeManager>();

            AddListener(closeButton, Close);
            AddListener(cancelButton, Close);
            AddListener(confirmButton, Confirm);

            if (priceInput != null)
            {
                priceInput.contentType = TMP_InputField.ContentType.IntegerNumber;
                priceInput.characterLimit = MaxPriceDigits;
                priceInput.onValueChanged.AddListener(_ => RefreshOutcome());
                priceInput.onSubmit.AddListener(_ => Confirm());
            }

            SetText(descriptionTitleText, "Description");
            SetText(feeLabel, $"Market fee ({UIFormat.Percent(Market.Market.ListingFeeRate)})");
            SetText(noteText, BuildNote());
        }

        private static void AddListener(Button button, UnityEngine.Events.UnityAction action)
        {
            if (button != null)
                button.onClick.AddListener(action);
        }

        private static string BuildNote()
        {
            var lines = new[]
            {
                "After listing, your antique is visible on the market immediately.",
                "It stays listed until a buyer takes it or you cancel the listing (free of charge).",
                $"The {UIFormat.Percent(Market.Market.ListingFeeRate)} fee is only paid when it sells. While listed, it can't be sold instantly or used for contracts.",
            };

            return string.Join("\n", lines.Select(l => "• " + l));
        }

        // ------------------------------------------------------------------
        // Content
        // ------------------------------------------------------------------

        private void FillAntiqueInfo(Antique antique)
        {
            SetText(nameText, antique.Name);
            SetText(categoryValue, antique.Category);
            SetText(centuryValue, antique.Century.ToDisplayString());
            SetText(originValue, antique.Country.ToDisplayString());
            SetText(editionValue, antique.IsLimitedEdition
                ? $"Limited edition {antique.RarityLabel}"
                : "— (regular item)");

            Color conditionColor = ConditionColor(antique.Condition);
            SetText(conditionValue, UIFormat.Colorize(
                $"{UIFormat.ConditionLabel(antique.Condition)} ({UIFormat.Percent(antique.Condition)})", conditionColor));

            if (conditionFill != null)
                conditionFill.anchorMax = new Vector2(Mathf.Clamp01(antique.Condition), 1f);

            if (conditionFillImage != null)
                conditionFillImage.color = conditionColor;

            var description = new StringBuilder(
                string.IsNullOrWhiteSpace(antique.Description) ? "No description available." : antique.Description.Trim());

            if (!string.IsNullOrWhiteSpace(antique.History))
                description.Append("\n\n<i>Provenance: ").Append(antique.History.Trim()).Append("</i>");

            SetText(descriptionText, description.ToString());
        }

        private void FillMarketInfo(Antique antique)
        {
            SetText(marketValueText, UIFormat.Colorize(UIFormat.Money(antique.CurrentPrice), UIFormat.PositiveColor));

            List<float> history = CollectionAnalytics.ItemValueHistory(antique, CurrentMarket, SuggestedRangeDays);
            history.Add(antique.CurrentPrice);

            if (history.Count > 2)
            {
                _rangeMin = history.Min();
                _rangeMax = history.Max();
            }
            else
            {
                _rangeMin = antique.CurrentPrice * (1f - FallbackRangeSpread);
                _rangeMax = antique.CurrentPrice * (1f + FallbackRangeSpread);
            }

            SetText(suggestedRangeText,
                $"Suggested price range:\n{UIFormat.Money(_rangeMin)} – {UIFormat.Money(_rangeMax)}");
        }

        private void RefreshOutcome()
        {
            bool valid = TryGetPrice(out float price);

            if (!valid)
            {
                SetText(listingPriceValue, "—");
                SetText(feeValue, "—");
                SetText(proceedsValue, "—");
                SetText(priceHintText, UIFormat.Colorize("Enter a price above 0 €.", UIFormat.NegativeColor));
                SetConfirmInteractable(false);
                return;
            }

            float fee = Market.Market.ListingFee(price);
            SetText(listingPriceValue, UIFormat.Money(price));
            SetText(feeValue, "-" + UIFormat.Money(fee));
            SetText(proceedsValue, UIFormat.Colorize(UIFormat.Money(Market.Market.ListingProceeds(price)), UIFormat.PositiveColor));
            SetText(priceHintText, PriceHint(price));
            SetConfirmInteractable(_antique != null);
        }

        // Tells the player how the price compares to the recent market, without
        // revealing what NPCs are actually willing to pay.
        private string PriceHint(float price)
        {
            if (price > _rangeMax)
                return UIFormat.Colorize("Above the recent market range — buyers may take a long time to show up.", warningColor);

            if (price < _rangeMin)
                return UIFormat.Colorize("Below the recent market range — likely to sell quickly, but you may be leaving money on the table.", warningColor);

            return UIFormat.Colorize("Within the recent market range.", UIFormat.MutedColor);
        }

        private static Color ConditionColor(float condition)
        {
            if (condition >= 0.7f) return UIFormat.PositiveColor;
            if (condition >= 0.5f) return new Color32(0xE6, 0xB2, 0x40, 0xFF);
            return UIFormat.NegativeColor;
        }

        // ------------------------------------------------------------------
        // Actions
        // ------------------------------------------------------------------

        private void Confirm()
        {
            if (_antique == null || !TryGetPrice(out float price))
                return;

            if (playerTrader == null)
            {
                Debug.LogWarning("ListOnMarketModalUI: PlayerTrader reference is missing.");
                return;
            }

            Antique antique = _antique;
            if (!playerTrader.ListOnMarket(antique.Id, price))
            {
                Debug.LogWarning($"ListOnMarketModalUI: failed to list {antique.Id} for {price:F0} €.");
                SetText(priceHintText, UIFormat.Colorize("This antique can't be listed right now.", UIFormat.NegativeColor));
                return;
            }

            Debug.Log($"ListOnMarketModalUI: listed {antique.Name} ({antique.Id}) for {price:F0} €.");
            Close();
            Listed?.Invoke(antique, price);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        private bool TryGetPrice(out float price)
        {
            price = 0f;

            if (priceInput == null)
                return false;

            string digits = new string(priceInput.text.Where(char.IsDigit).ToArray());
            if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out long value) || value <= 0)
                return false;

            price = value;
            return true;
        }

        private void SetConfirmInteractable(bool interactable)
        {
            if (confirmButton != null)
                confirmButton.interactable = interactable;
        }

        private static void SetText(TMP_Text target, string value)
        {
            if (target != null)
                target.text = value;
        }
    }
}