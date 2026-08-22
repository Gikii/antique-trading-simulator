using TMPro;
using UnityEngine;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Core;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Always-visible top bar. Day and Cash are wired to real systems.
    /// Reputation and Wealth are placeholders — those systems don't exist
    /// yet, so these fields just show a static "—" until they're built.
    /// </summary>
    public class HUD : MonoBehaviour
    {
        [SerializeField] private PlayerTrader playerTrader;
        [SerializeField] private TimeManager timeManager;

        [SerializeField] private TMP_Text dayText;
        [SerializeField] private TMP_Text cashText;
        [SerializeField] private TMP_Text reputationText;
        [SerializeField] private TMP_Text wealthText;

        void Awake()
        {
            if (playerTrader == null) playerTrader = FindFirstObjectByType<PlayerTrader>();
            if (timeManager == null) timeManager = FindFirstObjectByType<TimeManager>();
        }

        void Start()
        {
            playerTrader.Inventory.OnCashChanged += UpdateCash;
            UpdateCash(playerTrader.Inventory.Cash);

            if (timeManager != null)
            {
                timeManager.OnDayChanged += UpdateDay;
                UpdateDay(timeManager.CurrentDay);
            }

            if (reputationText != null) reputationText.text = "Reputation: —";
            if (wealthText != null) wealthText.text = "Wealth: —";
        }

        void OnDestroy()
        {
            if (playerTrader != null)
                playerTrader.Inventory.OnCashChanged -= UpdateCash;

            if (timeManager != null)
                timeManager.OnDayChanged -= UpdateDay;
        }

        private void UpdateDay(int day) => dayText.text = $"Day {day}";
        private void UpdateCash(float cash) => cashText.text = $"Cash {cash:F2} $";
    }
}