using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Company;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Seasons;
using UnityEngine.Serialization;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Always-visible top bar (layout built by Tools > UI > HUD > Build Top Bar).
    /// Day, date, Cash and Wealth (cash + market value of owned antiques) come from the
    /// player's inventory; Reputation, Credibility and Market Share from the CompanyManager.
    /// Labels ("Cash", "Wealth"...) live in the layout; this script writes only the values.
    /// </summary>
    public class HUD : MonoBehaviour
    {
        private const string NoValue = "—";

        [SerializeField] private PlayerTrader playerTrader;
        [SerializeField] private TimeManager timeManager;

        [Header("Week")]
        [FormerlySerializedAs("dayText")]
        [SerializeField] private TMP_Text weekText;   // "Week 23 / 104"
        [SerializeField] private TMP_Text dateText;  // "Tuesday, 14 May 1884" — follows the weekday through the week

        [Header("Stats (values only)")]
        [SerializeField] private TMP_Text cashText;
        [SerializeField] private TMP_Text wealthText;
        [SerializeField] private TMP_Text reputationText;
        [SerializeField] private TMP_Text credibilityText;
        [SerializeField] private TMP_Text marketShareText;

        // "128 450 £" – space as thousands separator, like in the mockup.
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
        private TooltipTrigger _reputationTooltip;
        private TooltipTrigger _credibilityTooltip;
        private TooltipTrigger _marketShareTooltip;
        private CompanyManager _company;

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
            inventory.OnStateRestored += HandleStateRestored;
            UpdateWealth();

            if (timeManager != null)
            {
                timeManager.OnWeekChanged += UpdateWeek;
                timeManager.OnTick += HandleTick;
                timeManager.OnTimeRestored += HandleTimeRestored;
                UpdateWeek(timeManager.CurrentWeek);
            }

            _company = playerTrader.Company;
            _reputationTooltip = AddTooltip(reputationText);
            _credibilityTooltip = AddTooltip(credibilityText);
            _marketShareTooltip = AddTooltip(marketShareText);
            if (_company != null)
                _company.OnCompanyChanged += UpdateCompany;
            UpdateCompany();
        }

        private static TooltipTrigger AddTooltip(TMP_Text text)
        {
            if (text == null) return null;
            text.raycastTarget = true; // needed for the hover text
            return TooltipTrigger.On(text);
        }

        void OnDestroy()
        {
            if (playerTrader != null && playerTrader.Inventory != null)
            {
                playerTrader.Inventory.OnCashChanged -= UpdateCash;
                playerTrader.Inventory.OnHoldingChanged -= HandleHoldingChanged;
                playerTrader.Inventory.OnHoldingsRevalued -= UpdateWealth;
                playerTrader.Inventory.OnStateRestored -= HandleStateRestored;
            }

            if (_company != null)
                _company.OnCompanyChanged -= UpdateCompany;

            if (timeManager != null)
            {
                timeManager.OnWeekChanged -= UpdateWeek;
                timeManager.OnTick -= HandleTick;
                timeManager.OnTimeRestored -= HandleTimeRestored;
            }
        }

        private void UpdateWeek(int week)
        {
            if (timeManager == null)
            {
                SetText(weekText, $"Week {week}");
                return;
            }

            // Past the campaign (play goes on as a sandbox) there is no "/ 104" to count towards.
            SetText(weekText, UIFormat.WeekCounter(week, timeManager.CampaignLength));
            UpdateDate();
        }

        // The week only changes on OnWeekChanged, but the calendar day moves on with the ticks.
        private DateTime _shownDate;

        private void HandleTick(int week, int tick) => UpdateDate();

        private Logistics.TransportManager _transportManager;

        private WeatherSeverity CurrentWeather()
        {
            if (_transportManager == null) _transportManager = FindFirstObjectByType<Logistics.TransportManager>();
            return _transportManager != null && _transportManager.Weather != null
                ? _transportManager.Weather.Current
                : WeatherSeverity.Normal;
        }

        private void UpdateDate()
        {
            if (timeManager == null) return;

            DateTime date = timeManager.CurrentDate;
            if (date == _shownDate) return;

            _shownDate = date;
            Season season = timeManager.CurrentSeason;
            SetText(dateText, $"{TimeManager.FormatWithWeekday(date)} · {season.ToDisplayString()}");

            if (dateText != null)
            {
                var trigger = TooltipTrigger.On(dateText);
                if (trigger != null)
                    trigger.Text = SeasonPresentation.SeasonTooltip(season, timeManager.SeasonSettings, CurrentWeather());
            }
        }

        private void UpdateCash(float cash)
        {
            SetText(cashText, FormatMoney(cash));
            UpdateWealth();
            UpdateMarketShare(); // the player's own trades move it
        }

        private void UpdateCompany()
        {
            if (_company == null)
            {
                SetText(reputationText, NoValue);
                SetText(credibilityText, NoValue);
                SetText(marketShareText, NoValue);
                return;
            }

            var reputation = _company.Reputation;
            SetText(reputationText, UIFormat.Number(reputation.Reputation));
            SetText(credibilityText, UIFormat.Percent(reputation.Credibility));

            if (_reputationTooltip != null)
            {
                var next = reputation.NextTier(ReputationKind.Reputation);
                _reputationTooltip.Text = $"{reputation.Title}\n" +
                    (next != null
                        ? $"<size=85%>Next: {next.Name} at {UIFormat.Number(next.MinValue)}</size>"
                        : "<size=85%>Highest reputation level</size>");
            }

            if (_credibilityTooltip != null)
                _credibilityTooltip.Text = $"{reputation.CurrentTier(ReputationKind.Credibility)?.Name}\n" +
                    "<size=85%>How reliable partners consider your company.</size>";

            UpdateMarketShare();
        }

        private void UpdateMarketShare()
        {
            if (_company == null) return;

            SetText(marketShareText, UIFormat.PercentOneDecimal(_company.MarketShare()));
            if (_marketShareTooltip != null)
                _marketShareTooltip.Text = $"Last 7 weeks: {UIFormat.PercentOneDecimal(_company.MarketShare(7))}\n" +
                    "<size=85%>Your share of all market purchases and sales.</size>";
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

        /// <summary>1000 → "1 000 €", 128450 → "128 450 £"</summary>
        public static string FormatMoney(float amount) => amount.ToString("#,0", MoneyFormat) + " £";

        private static void SetText(TMP_Text text, string value)
        {
            if (text != null) text.text = value;
        }

        private void HandleStateRestored()
        {
            var inventory = playerTrader != null ? playerTrader.Inventory : null;
            if (inventory != null)
                UpdateCash(inventory.Cash);

            UpdateCompany();
        }

        private void HandleTimeRestored()
        {
            if (timeManager != null)
                UpdateWeek(timeManager.CurrentWeek);
        }

    }
}