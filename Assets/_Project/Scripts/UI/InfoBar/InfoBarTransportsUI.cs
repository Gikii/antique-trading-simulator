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
    public class InfoBarTransportsUI : InfoBarListUI
    {
        [Header("Dependencies")]
        [SerializeField] private TransportManager transportManager;
        [SerializeField] private RestorationManager restorationManager;
        [SerializeField] private PlayerTrader playerTrader;

        [Tooltip("Deliveries from the last N days are listed after the shipments in transit. 1 = today only.")]
        [SerializeField] private int recentDeliveryDays = 1;

        protected override string EmptyLabel => restorationManager != null ? "No shipments or restorations" : "No shipments";

        protected override bool HasAction => true;
        protected override string ActionTooltip => "Track your shipments\n<size=85%>Inventory</size>";
        protected override void OnActionClicked() => InfoBarNavigation.OpenInventory();

        protected override void Awake()
        {
            base.Awake();

            if (transportManager == null) transportManager = FindFirstObjectByType<TransportManager>();
            if (restorationManager == null) restorationManager = FindFirstObjectByType<RestorationManager>();
            if (playerTrader == null) playerTrader = FindFirstObjectByType<PlayerTrader>();
        }

        protected override void SubscribeToSources()
        {
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
        }

        protected override void UnsubscribeFromSources()
        {
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
        protected override void BuildLines(List<RowData> lines)
        {
            var inventory = playerTrader != null ? playerTrader.Inventory : null;
            if (inventory == null)
                return;

            int today = CurrentDay;
            int sinceDay = today - Mathf.Max(1, recentDeliveryDays) + 1;

            if (transportManager != null)
                AddShipmentLines(lines, inventory, today, sinceDay);

            if (restorationManager != null)
                AddRestorationLines(lines, inventory, today, sinceDay);
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
    }
}
