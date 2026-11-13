using System;
using System.Collections.Generic;
using AntiqueTradingSimulator.Core;
using AntiqueTradingSimulator.Events;
using AntiqueTradingSimulator.News;
using UnityEngine;

namespace AntiqueTradingSimulator.Seasons
{
    /// <summary>Saved part of SeasonWeather.</summary>
    [Serializable]
    public class SeasonWeatherState
    {
        public WeatherSeverity Current = WeatherSeverity.Normal;

        /// <summary>First week of the season the forecasts were made for (-1 = none yet).</summary>
        public int ForecastSeasonStart = -1;

        /// <summary>The real weather of that season — rolled when the forecasts came out, known only to the game.</summary>
        public WeatherSeverity Upcoming = WeatherSeverity.Normal;

        /// <summary>Week the insiders' report comes out (-1 = not pending).</summary>
        public int InsiderForecastWeek = -1;

        /// <summary>What the public almanac predicted for the forecast season (if it came out).</summary>
        public bool HasAlmanacPrediction;
        public WeatherSeverity AlmanacPrediction = WeatherSeverity.Normal;
    }

    /// <summary>
    /// How harsh each season turns out, and the forecasts that hint at it. A few weeks before a season
    /// starts its weather is rolled (mild / ordinary / harsh, SeasonSettings) and forecasts go out as news:
    /// the almanac (a rumour everyone reads, often wrong) and, a week later, the shipping insurers'
    /// report (a leak for traders with an informant network, rarely wrong). Each forecast is right with
    /// its accuracy, otherwise it names one of the other outcomes. Once the season starts, its real
    /// weather scales the transport risks (SeasonSettings.SeverityEffect) and is shown to everyone.
    ///
    /// Plain C#: TransportManager owns it, advances it every week and saves it.
    /// </summary>
    public class SeasonWeather
    {
        private const int MaxSeasonWeeks = 16;

        private readonly TimeManager _time;
        private readonly Func<SeasonSettings> _settings;
        private readonly Action<NewsItem> _publish;

        public SeasonWeatherState State { get; private set; } = new SeasonWeatherState();

        /// <summary>The weather of the season under way — known to everyone.</summary>
        public WeatherSeverity Current => State.Current;

        /// <summary>
        /// A season has started and its weather is now known: (season, its weather, what the public almanac
        /// predicted for it — null if no almanac came out, e.g. at the start of the campaign).
        /// </summary>
        public event Action<Season, WeatherSeverity, WeatherSeverity?> OnSeasonWeatherRevealed;

        public SeasonWeather(TimeManager time, Func<SeasonSettings> settings, Action<NewsItem> publish)
        {
            _time = time;
            _settings = settings;
            _publish = publish;
        }

        /// <summary>Call once per week, before anything rolls transport risks for that week.</summary>
        public void AdvanceWeek(int week)
        {
            var settings = _settings?.Invoke();
            if (_time == null || settings == null) return;

            Season season = _time.SeasonOf(week);

            // A new season starts: its weather is the one rolled for the forecasts (or a fresh roll).
            if (week > 1 && season != _time.SeasonOf(week - 1))
            {
                bool forecast = State.ForecastSeasonStart == week;
                WeatherSeverity? almanac = forecast && State.HasAlmanacPrediction ? State.AlmanacPrediction : (WeatherSeverity?)null;

                State.Current = forecast ? State.Upcoming : settings.RollSeverity();
                State.ForecastSeasonStart = -1;
                State.InsiderForecastWeek = -1;
                State.HasAlmanacPrediction = false;

                Debug.Log($"SeasonWeather: {State.Current.Describe(season)} begins.");
                OnSeasonWeatherRevealed?.Invoke(season, State.Current, almanac);
            }

            int nextStart = NextSeasonStart(week);
            int lead = Mathf.Max(1, settings.ForecastLeadWeeks);

            if (nextStart > 0 && State.ForecastSeasonStart != nextStart && nextStart - week <= lead)
            {
                State.ForecastSeasonStart = nextStart;
                State.Upcoming = settings.RollSeverity();
                Debug.Log($"SeasonWeather: the {_time.SeasonOf(nextStart)} from week {nextStart} will be {State.Upcoming} (hidden).");

                PublishForecast(week, isInsider: false, settings);

                // The insiders' report follows a week later — or right away if the season is about to start.
                if (nextStart - week >= 2)
                    State.InsiderForecastWeek = week + 1;
                else
                    PublishForecast(week, isInsider: true, settings);
            }
            else if (State.InsiderForecastWeek == week && State.ForecastSeasonStart > week)
            {
                State.InsiderForecastWeek = -1;
                PublishForecast(week, isInsider: true, settings);
            }
        }

        /// <summary>
        /// The weather that really applies to a week: the current season's, the forecast season's
        /// (secret until it starts), or ordinary for anything further away.
        /// </summary>
        public WeatherSeverity ActualFor(int week)
        {
            if (IsCurrentSeason(week)) return State.Current;

            if (State.ForecastSeasonStart > 0 && week >= State.ForecastSeasonStart &&
                week < State.ForecastSeasonStart + MaxSeasonWeeks &&
                _time.SeasonOf(week) == _time.SeasonOf(State.ForecastSeasonStart))
                return State.Upcoming;

            return WeatherSeverity.Normal;
        }

        /// <summary>The weather a player can know for a week: the current season's, otherwise ordinary.</summary>
        public WeatherSeverity KnownFor(int week) => IsCurrentSeason(week) ? State.Current : WeatherSeverity.Normal;

        public void Restore(SeasonWeatherState state) => State = state ?? new SeasonWeatherState();

        // ---------------------------------------------------------------- helpers

        private bool IsCurrentSeason(int week)
        {
            int now = _time.CurrentWeek;
            return week >= now && week - now < MaxSeasonWeeks && _time.SeasonOf(week) == _time.SeasonOf(now)
                   && NextSeasonStart(now) > week;
        }

        /// <summary>First week after <paramref name="week"/> that belongs to another season (-1 if none found).</summary>
        private int NextSeasonStart(int week)
        {
            Season season = _time.SeasonOf(week);
            for (int w = week + 1; w <= week + MaxSeasonWeeks; w++)
                if (_time.SeasonOf(w) != season)
                    return w;
            return -1;
        }

        private void PublishForecast(int week, bool isInsider, SeasonSettings settings)
        {
            if (_publish == null) return;

            Season season = _time.SeasonOf(State.ForecastSeasonStart);
            float accuracy = isInsider ? settings.InsiderAccuracy : settings.AlmanacAccuracy;
            WeatherSeverity predicted = UnityEngine.Random.value < accuracy ? State.Upcoming : OtherThan(State.Upcoming);

            if (!isInsider)
            {
                State.HasAlmanacPrediction = true;
                State.AlmanacPrediction = predicted;
            }

            string source = isInsider ? "Shipping insurers" : "The almanac";
            string title = $"{source}: {Capitalize(predicted.Describe(season))} ahead";
            string description = $"{source} {(isInsider ? "expect" : "predicts")} {predicted.Describe(season)}. " + Consequence(predicted);

            var item = new NewsItem(
                new List<NewsEventData>(),
                title,
                description,
                isInsider ? NewsType.Leak : NewsType.Rumor,
                accuracy,
                week,
                isInsider ? InfoAccessLevel.InformantNetwork : InfoAccessLevel.LocalPress,
                State.ForecastSeasonStart);

            Debug.Log($"SeasonWeather: forecast published — {title} (accuracy {accuracy:P0}, real: {State.Upcoming}).");
            _publish(item);
        }

        private static string Consequence(WeatherSeverity severity) => severity switch
        {
            WeatherSeverity.Mild => "Fewer shipments should be held up by the weather, and fragile pieces should travel more safely.",
            WeatherSeverity.Harsh => "Expect more shipments held up by the weather and more damage in transit.",
            _ => "Transport should face the usual risks of the season."
        };

        private static WeatherSeverity OtherThan(WeatherSeverity severity)
        {
            var options = new List<WeatherSeverity> { WeatherSeverity.Mild, WeatherSeverity.Normal, WeatherSeverity.Harsh };
            options.Remove(severity);
            return options[UnityEngine.Random.Range(0, options.Count)];
        }

        private static string Capitalize(string text) =>
            string.IsNullOrEmpty(text) ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);
    }
}
