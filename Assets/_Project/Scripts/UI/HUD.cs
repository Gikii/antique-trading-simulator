using System.Globalization;
using TMPro;
using UnityEngine;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Core;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Always-visible top bar (layout built by Tools > UI > HUD > Build Top Bar).
    /// Day, date, Cash and Wealth (cash + market value of owned antiques) are wired to real systems.
    /// Reputation, Credibility and Market Share are placeholders — those systems
    /// don't exist yet, so they show "—" until they're built.
    /// Labels ("Cash", "Wealth"...) live in the layout; this script writes only the values.
    /// </summary>
    public class HUD : MonoBehaviour
    {
        private const string NoValue = "—";

        [SerializeField] private PlayerTrader playerTrader;
        [SerializeField] private TimeManager timeManager;

        [Header("Day")]
        [SerializeField] private TMP_Text dayText;   // "Day 23 / 90"
        [SerializeField] private TMP_Text dateText;  // "Tuesday, 14 May 1884"

        [Header("Stats (values only)")]
        [SerializeField] private TMP_Text cashText;
        [SerializeField] private TMP_Text wealthText;
        [SerializeField] private TMP_Text reputationText;
        [SerializeField] private TMP_Text credibilityText;
        [SerializeField] private TMP_Text marketShareText;

        // "128 450 €" – space as thousands separator, like in the mockup.
        private static readonly NumberFormatInfo MoneyFormat = new NumberFormatInfo
        {
            NumberGroupSeparator = " ",
            NumberDecimalSeparator = ","
        };

        void Awake()
        {
            if (playerTrader == null) playerTrader = FindFirstObjectByType<PlayerTrader>();
            if (timeManager == null) timeManager = FindFirstObjectByType<TimeManager>();
        }

        private TooltipTrigger _wealthTooltip;

        void Start()
        {
            var inventory = playerTrader.Inventory;
            inventory.OnCashChanged += UpdateCash;
            UpdateCash(inventory.Cash);

            // Wealth moves with cash, with every bought/sold antique and with market prices.
            if (wealthText != null)
            {
                wealthText.raycastTarget = true; // needed for the hover breakdown
                _wealthTooltip = TooltipTrigger.On(wealthText);
            }
            inventory.OnHoldingChanged += HandleHoldingChanged;
            inventory.OnHoldingsRevalued += UpdateWealth;
            UpdateWealth();

            if (timeManager != null)
            {
                timeManager.OnDayChanged += UpdateDay;
                UpdateDay(timeManager.CurrentDay);
            }

            // Placeholders until the systems exist.
            SetText(reputationText, NoValue);
            SetText(credibilityText, NoValue);
            SetText(marketShareText, NoValue);
        }

        void OnDestroy()
        {
            if (playerTrader != null && playerTrader.Inventory != null)
            {
                playerTrader.Inventory.OnCashChanged -= UpdateCash;
                playerTrader.Inventory.OnHoldingChanged -= HandleHoldingChanged;
                playerTrader.Inventory.OnHoldingsRevalued -= UpdateWealth;
            }

            if (timeManager != null)
                timeManager.OnDayChanged -= UpdateDay;
        }

        private void UpdateDay(int day)
        {
            if (timeManager == null)
            {
                SetText(dayText, $"Day {day}");
                return;
            }

            SetText(dayText, $"Day {day} / {timeManager.CampaignLength}");
            SetText(dateText, TimeManager.FormatWithWeekday(timeManager.DayToDate(day)));
        }

        private void UpdateCash(float cash)
        {
            SetText(cashText, FormatMoney(cash));
            UpdateWealth();
        }

        private void HandleHoldingChanged(string listingId, Market.Antique antique) => UpdateWealth();

        private void UpdateWealth()
        {
            var inventory = playerTrader != null ? playerTrader.Inventory : null;
            if (inventory == null)
            {
                SetText(wealthText, NoValue);
                return;
            }

            float antiques = inventory.TotalHoldingsValue;
            SetText(wealthText, FormatMoney(inventory.Wealth));

            if (_wealthTooltip != null)
                _wealthTooltip.Text =
                    $"Cash: {FormatMoney(inventory.Cash)}\n" +
                    $"Antiques ({inventory.Holdings.Count}): {FormatMoney(antiques)}\n" +
                    "<size=85%>Antiques at current market value, incl. items in transit.</size>";
        }

        /// <summary>1000 → "1 000 €", 128450 → "128 450 €"</summary>
        public static string FormatMoney(float amount) => amount.ToString("#,0", MoneyFormat) + " €";

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null) text.text = value;
        }
    }
}