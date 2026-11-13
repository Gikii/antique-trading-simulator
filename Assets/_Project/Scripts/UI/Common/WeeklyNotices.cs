using System;
using System.Collections.Generic;
using System.Text;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.Seasons;
using UnityEngine;

namespace AntiqueTradingSimulator.UI
{
    /// <summary>
    /// Collects what happened to the player during a week change and turns it into at most two toasts:
    /// the start of a season (its weather, and whether the almanac got it right), and the player's
    /// shipments — held up by the weather, arrived early, or damaged in transit. Nothing is posted in
    /// a quiet week. Plain C#, owned by ToastUI.
    /// </summary>
    public sealed class WeeklyNotices : IDisposable
    {
        private const int MaxNamesPerLine = 3;

        private readonly Action<string> _post;
        private readonly TimeManager _time;
        private readonly TransportManager _transport;
        private readonly PlayerTrader _player;

        private string _seasonNotice;
        private readonly List<string> _heldUp = new();
        private readonly List<string> _early = new();
        private readonly List<string> _damaged = new();
        private readonly List<string> _heldUpByEvents = new();

        public WeeklyNotices(Action<string> post)
        {
            _post = post;
            _time = UnityEngine.Object.FindFirstObjectByType<TimeManager>();
            _transport = UnityEngine.Object.FindFirstObjectByType<TransportManager>();
            _player = UnityEngine.Object.FindFirstObjectByType<PlayerTrader>();

            if (_time != null) _time.OnTick += HandleTick;

            if (_transport != null)
            {
                _transport.OnShipmentShiftedBySeason += HandleShipmentShifted;
                _transport.OnShipmentDelivered += HandleShipmentDelivered;
                _transport.OnShipmentsDelayed += HandleShipmentsDelayed;
                if (_transport.Weather != null) _transport.Weather.OnSeasonWeatherRevealed += HandleSeasonRevealed;
            }
        }

        public void Dispose()
        {
            if (_time != null) _time.OnTick -= HandleTick;

            if (_transport != null)
            {
                _transport.OnShipmentShiftedBySeason -= HandleShipmentShifted;
                _transport.OnShipmentDelivered -= HandleShipmentDelivered;
                _transport.OnShipmentsDelayed -= HandleShipmentsDelayed;
                if (_transport.Weather != null) _transport.Weather.OnSeasonWeatherRevealed -= HandleSeasonRevealed;
            }
        }

        // ---------------------------------------------------------------- collecting (during OnDayChanged)

        private bool IsPlayers(Shipment shipment) =>
            shipment != null && _player != null && shipment.Recipient == _player.Inventory;

        private void HandleShipmentShifted(Shipment shipment, int weeks, Season season)
        {
            if (!IsPlayers(shipment)) return;
            (weeks > 0 ? _heldUp : _early).Add(shipment.AntiqueName);
        }

        // An event (storms, frozen ports…) held up everything travelling in a zone.
        private void HandleShipmentsDelayed(ShippingZone zone, int weeks)
        {
            if (_player == null) return;

            foreach (var shipment in _transport.GetShipmentsFor(_player.Inventory))
                if (shipment.Zone == zone && !_heldUpByEvents.Contains(shipment.AntiqueName))
                    _heldUpByEvents.Add(shipment.AntiqueName);
        }

        private void HandleShipmentDelivered(Shipment shipment, Antique antique, float conditionLost)
        {
            if (conditionLost <= 0f || !IsPlayers(shipment)) return;

            int after = Mathf.RoundToInt((antique != null ? antique.Condition : 0f) * 100f);
            int before = Mathf.RoundToInt(((antique != null ? antique.Condition : 0f) + conditionLost) * 100f);
            _damaged.Add($"{shipment.AntiqueName}: condition {before}% → {after}%");
        }

        private void HandleSeasonRevealed(Season season, WeatherSeverity weather, WeatherSeverity? almanac)
        {
            var settings = _time != null ? _time.SeasonSettings : null;

            var text = new StringBuilder();
            text.Append($"<b>{season.ToDisplayString()} has begun</b>\n");
            text.Append(Capitalize(weather.Describe(season)));

            if (weather == WeatherSeverity.Normal || settings == null)
            {
                text.Append(": the usual risks of the season.");
            }
            else
            {
                var effect = settings.GetSeverityEffect(weather);
                text.Append($": weather delays ×{effect.DelayMultiplier:0.##}, damage risk ×{effect.DamageMultiplier:0.##}.");
            }

            if (almanac.HasValue)
            {
                text.Append('\n');
                text.Append(almanac.Value == weather
                    ? UIFormat.Colorize("The almanac was right.", UIFormat.PositiveColor)
                    : UIFormat.Colorize($"The almanac predicted {almanac.Value.Describe(season)}.", UIFormat.NegativeColor));
            }

            _seasonNotice = text.ToString();
        }

        // ---------------------------------------------------------------- posting (first tick of the week)

        // OnDayChanged handlers run in no fixed order; the week's first tick comes after all of them.
        private void HandleTick(int week, int tick)
        {
            if (_seasonNotice != null)
            {
                _post?.Invoke(_seasonNotice);
                _seasonNotice = null;
            }

            if (_heldUp.Count == 0 && _early.Count == 0 && _damaged.Count == 0 && _heldUpByEvents.Count == 0) return;

            var text = new StringBuilder("<b>Your shipments this week</b>");
            if (_heldUp.Count > 0)
                text.Append('\n').Append(UIFormat.Colorize("Held up a week by the weather: ", UIFormat.NegativeColor)).Append(Names(_heldUp));
            if (_heldUpByEvents.Count > 0)
                text.Append('\n').Append(UIFormat.Colorize("Held up by transport disruption: ", UIFormat.NegativeColor)).Append(Names(_heldUpByEvents));
            if (_early.Count > 0)
                text.Append('\n').Append(UIFormat.Colorize("Arrived a week early: ", UIFormat.PositiveColor)).Append(Names(_early));
            if (_damaged.Count > 0)
            {
                text.Append('\n').Append(UIFormat.Colorize("Damaged in transit:", UIFormat.NegativeColor));
                for (int i = 0; i < _damaged.Count && i < MaxNamesPerLine; i++)
                    text.Append("\n  ").Append(_damaged[i]);
                if (_damaged.Count > MaxNamesPerLine)
                    text.Append($"\n  and {_damaged.Count - MaxNamesPerLine} more");
            }

            _heldUp.Clear();
            _heldUpByEvents.Clear();
            _early.Clear();
            _damaged.Clear();
            _post?.Invoke(text.ToString());
        }

        /// <summary>"A, B and C" / "A, B, C and 2 more"</summary>
        private static string Names(List<string> names)
        {
            if (names.Count == 1) return names[0];

            int shown = Mathf.Min(names.Count, MaxNamesPerLine);
            int rest = names.Count - shown;

            if (rest > 0)
                return string.Join(", ", names.GetRange(0, shown)) + $" and {rest} more";

            return string.Join(", ", names.GetRange(0, shown - 1)) + " and " + names[shown - 1];
        }

        private static string Capitalize(string text) =>
            string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
    }
}
