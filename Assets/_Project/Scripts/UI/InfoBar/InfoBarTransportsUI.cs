using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.Restoration;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// TRANSPORTS tab of the InfoBar: the player's shipments still on the way and antiques at a
    /// restoration workshop (soonest first), followed by what was delivered or restored recently —
    /// flagged when it arrived damaged or the restoration went wrong.
    /// </summary>
    public class InfoBarTransportsUI : MonoBehaviour
    {
        [Header("Dependencies (auto-found if empty)")]
        [SerializeField] private TransportManager transportManager;
        [SerializeField] private RestorationManager restorationManager;
        [SerializeField] private PlayerTrader playerTrader;
        [SerializeField] private TimeManager timeManager;

        [Header("List")]
        [SerializeField] private RectTransform listContainer;
        [SerializeField] private InfoBarRowUI rowPrefab;
        [SerializeField] private int maxRows = 3;

        [Tooltip("Deliveries from the last N days are listed after the shipments in transit. 1 = today only.")]
        [SerializeField] private int recentDeliveryDays = 1;

        private readonly List<InfoBarRowUI> _rows = new();
        private bool _subscribed;

        private int CurrentDay => timeManager != null ? timeManager.CurrentDay : 0;

        private void Awake()
        {
            if (transportManager == null) transportManager = FindFirstObjectByType<TransportManager>();
            if (restorationManager == null) restorationManager = FindFirstObjectByType<RestorationManager>();
            if (playerTrader == null) playerTrader = FindFirstObjectByType<PlayerTrader>();
            if (timeManager == null) timeManager = FindFirstObjectByType<TimeManager>();
        }

        private void OnEnable()
        {
            Subscribe();
            Refresh();
        }

        // PlayerTrader creates its inventory in Awake — make sure the first list is right.
        private void Start() => Refresh();

        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (_subscribed) return;

            if (transportManager != null)
            {
                transportManager.OnShipmentDispatched += HandleShipmentChanged;
                transportManager.OnShipmentDelivered += HandleShipmentDelivered;
                transportManager.OnShipmentsDelayed += HandleShipmentsDelayed;
                transportManager.OnShipmentsRestored += Refresh;
            }

            if (restorationManager != null)
            {
                restorationManager.OnRestorationStarted += HandleRestorationStarted;
                restorationManager.OnRestorationCompleted += HandleRestorationCompleted;
                restorationManager.OnRestorationsRestored += Refresh;
            }

            if (timeManager != null)
                timeManager.OnDayChanged += HandleDayChanged;

            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;

            if (transportManager != null)
            {
                transportManager.OnShipmentDispatched -= HandleShipmentChanged;
                transportManager.OnShipmentDelivered -= HandleShipmentDelivered;
                transportManager.OnShipmentsDelayed -= HandleShipmentsDelayed;
                transportManager.OnShipmentsRestored -= Refresh;
            }

            if (restorationManager != null)
            {
                restorationManager.OnRestorationStarted -= HandleRestorationStarted;
                restorationManager.OnRestorationCompleted -= HandleRestorationCompleted;
                restorationManager.OnRestorationsRestored -= Refresh;
            }

            if (timeManager != null)
                timeManager.OnDayChanged -= HandleDayChanged;

            _subscribed = false;
        }

        private void HandleShipmentChanged(Shipment shipment) => Refresh();
        private void HandleShipmentDelivered(Shipment shipment, Antique antique, float conditionLost) => Refresh();
        private void HandleShipmentsDelayed(ShippingZone zone, int days) => Refresh();
        private void HandleDayChanged(int day) => Refresh();
        private void HandleRestorationStarted(RestorationJob job) => Refresh();
        private void HandleRestorationCompleted(RestorationRecord record) => Refresh();

        // ------------------------------------------------------------------
        // Content
        // ------------------------------------------------------------------

        private struct RowData
        {
            public string Label;
            public string Detail;
            public string Tooltip;
        }

        public void Refresh()
        {
            ClearRows();

            if (listContainer == null || rowPrefab == null)
                return;

            var lines = BuildLines();
            if (lines.Count == 0)
            {
                AddRow(new RowData
                {
                    Label = UIFormat.Colorize(restorationManager != null ? "No shipments or restorations" : "No shipments", UIFormat.MutedColor),
                    Detail = ""
                });
                return;
            }

            int max = Mathf.Max(1, maxRows);
            bool overflow = lines.Count > max;
            int shown = overflow ? max - 1 : lines.Count;

            for (int i = 0; i < shown; i++)
                AddRow(lines[i]);

            if (overflow)
                AddRow(new RowData
                {
                    Label = UIFormat.Colorize($"+{lines.Count - shown} more", UIFormat.MutedColor),
                    Detail = ""
                });
        }

        private List<RowData> BuildLines()
        {
            var lines = new List<RowData>();
            var inventory = playerTrader != null ? playerTrader.Inventory : null;
            if (inventory == null)
                return lines;

            int today = CurrentDay;
            int sinceDay = today - Mathf.Max(1, recentDeliveryDays) + 1;

            if (transportManager != null)
                AddShipmentLines(lines, inventory, today, sinceDay);

            if (restorationManager != null)
                AddRestorationLines(lines, inventory, today, sinceDay);

            return lines;
        }

        private void AddRestorationLines(List<RowData> lines, Economy.TraderInventory inventory, int today, int sinceDay)
        {
            foreach (var job in restorationManager.GetJobsFor(inventory).OrderBy(j => j.EndDay))
            {
                int daysLeft = job.DaysRemaining(today);
                lines.Add(new RowData
                {
                    Label = job.AntiqueName,
                    Detail = UIFormat.Colorize(
                        daysLeft <= 0 ? "restoring · today" : $"restoring · {UIFormat.Days(daysLeft)}",
                        UIFormat.InRestorationColor),
                    Tooltip = $"{job.AntiqueName}\nAt {job.OptionName}\nReady: {UIFormat.GameDate(job.EndDay)}"
                });
            }

            foreach (var record in restorationManager.GetCompletedSince(sinceDay))
            {
                string when = record.Day == today ? "today" : UIFormat.GameDate(record.Day);
                string change = $"{UIFormat.ConditionLabel(record.StartCondition)} → {UIFormat.ConditionLabel(record.ResultCondition)} " +
                                $"({UIFormat.SignedMoney(record.ValueChange)})";

                lines.Add(new RowData
                {
                    Label = record.AntiqueName,
                    Detail = record.Outcome switch
                    {
                        RestorationOutcome.Success => UIFormat.Colorize($"restored · {when}", UIFormat.PositiveColor),
                        RestorationOutcome.Partial => UIFormat.Colorize($"partly restored · {when}", UIFormat.PositiveColor),
                        _ => UIFormat.Colorize($"damaged in restoration · {when}", UIFormat.NegativeColor)
                    },
                    Tooltip = record.Outcome switch
                    {
                        RestorationOutcome.Success => $"Restored by {record.OptionName}: {change}.",
                        RestorationOutcome.Partial => $"Only partly restored by {record.OptionName}: {change}.",
                        _ => $"The work at {record.OptionName} went wrong: {change}."
                    }
                });
            }
        }

        private void AddShipmentLines(List<RowData> lines, Economy.TraderInventory inventory, int today, int sinceDay)
        {
            foreach (var shipment in transportManager.GetShipmentsFor(inventory).OrderBy(s => s.ArrivalDay))
            {
                int daysLeft = shipment.DaysRemaining(today);
                lines.Add(new RowData
                {
                    Label = shipment.AntiqueName,
                    Detail = UIFormat.Colorize(
                        daysLeft <= 0 ? "today" : $"{UIFormat.GameDate(shipment.ArrivalDay)} · {UIFormat.Days(daysLeft)}",
                        UIFormat.InTransitColor),
                    Tooltip = $"{shipment.AntiqueName}\n{shipment.Option.ToDisplayString()} transport from {shipment.Zone.ToDisplayString()}\n" +
                              $"Arrival: {UIFormat.GameDate(shipment.ArrivalDay)}"
                });
            }

            foreach (var delivery in transportManager.GetDeliveriesFor(inventory, sinceDay))
            {
                string when = delivery.Day == today ? "today" : UIFormat.GameDate(delivery.Day);
                lines.Add(new RowData
                {
                    Label = delivery.Shipment.AntiqueName,
                    Detail = delivery.Damaged
                        ? UIFormat.Colorize($"damaged · {when}", UIFormat.NegativeColor)
                        : UIFormat.Colorize($"delivered · {when}", UIFormat.PositiveColor),
                    Tooltip = delivery.Damaged
                        ? $"Arrived damaged {when} — condition -{UIFormat.Percent(delivery.ConditionLost)}."
                        : $"Delivered {when} — now in your warehouse."
                });
            }
        }

        private void AddRow(RowData data)
        {
            InfoBarRowUI row = Instantiate(rowPrefab, listContainer);
            row.Setup(data.Label, data.Detail, data.Tooltip);
            _rows.Add(row);
        }

        private void ClearRows()
        {
            foreach (var row in _rows)
                if (row != null)
                    Destroy(row.gameObject);

            _rows.Clear();
        }
    }
}
