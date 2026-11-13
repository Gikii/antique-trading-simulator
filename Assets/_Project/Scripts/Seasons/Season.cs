using System;

namespace AntiqueTradingSimulator.Seasons
{
    /// <summary>Season of the year, derived from the calendar date (meteorological seasons).</summary>
    public enum Season
    {
        Spring,
        Summer,
        Autumn,
        Winter
    }

    /// <summary>How the weather of one season turns out — rolled before the season, hinted at by forecasts.</summary>
    public enum WeatherSeverity
    {
        Normal,
        Mild,
        Harsh
    }

    /// <summary>A set of seasons, e.g. the seasons an event may happen in.</summary>
    [Flags]
    public enum SeasonMask
    {
        None = 0,
        Spring = 1 << 0,
        Summer = 1 << 1,
        Autumn = 1 << 2,
        Winter = 1 << 3,
        All = Spring | Summer | Autumn | Winter
    }

    public static class SeasonCalendar
    {
        /// <summary>March–May spring, June–August summer, September–November autumn, December–February winter.</summary>
        public static Season Of(DateTime date) => date.Month switch
        {
            3 or 4 or 5 => Season.Spring,
            6 or 7 or 8 => Season.Summer,
            9 or 10 or 11 => Season.Autumn,
            _ => Season.Winter
        };

        public static SeasonMask ToMask(this Season season) => season switch
        {
            Season.Spring => SeasonMask.Spring,
            Season.Summer => SeasonMask.Summer,
            Season.Autumn => SeasonMask.Autumn,
            Season.Winter => SeasonMask.Winter,
            _ => SeasonMask.None
        };

        public static bool Contains(this SeasonMask mask, Season season) => (mask & season.ToMask()) != 0;

        /// <summary>"Spring", "Summer", "Autumn", "Winter"</summary>
        public static string ToDisplayString(this Season season) => season.ToString();

        /// <summary>"a mild winter" / "an ordinary winter" / "a harsh winter"</summary>
        public static string Describe(this WeatherSeverity severity, Season season) => severity switch
        {
            WeatherSeverity.Mild => $"a mild {season.ToLowerString()}",
            WeatherSeverity.Harsh => $"a harsh {season.ToLowerString()}",
            _ => $"an ordinary {season.ToLowerString()}"
        };

        /// <summary>"spring", "summer", … — for use inside sentences.</summary>
        public static string ToLowerString(this Season season) => season.ToString().ToLowerInvariant();
    }
}
