using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.Market;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.News
{
    /// <summary>
    /// The words for every antique attribute value, in a chip form for headlines and rows
    /// ("clocks", "France", "XVIII c.") and a prose form for sentences ("French pieces").
    /// </summary>
    public static class NewsVocabulary
    {
        public static string Chip(AntiqueType type) => type switch
        {
            AntiqueType.Other => "other pieces",
            AntiqueType.Silverware => "silverware",
            AntiqueType.Porcelain => "porcelain",
            AntiqueType.Furniture => "furniture",
            AntiqueType.Jewelry => "jewellery",
            AntiqueType.Textile => "textiles",
            AntiqueType.Ceramic => "ceramics",
            _ => type.ToString().ToLowerInvariant() + "s"
        };

        public static string Prose(AntiqueType type) => Chip(type);

        public static string Chip(Country country) =>
            country == Country.Other ? "unattributed" : AntiqueEnumDisplay.ToDisplayString(country);

        public static string Prose(Country country) =>
            country == Country.Other ? "pieces of unknown origin" : Demonym(country) + " pieces";

        public static string Demonym(Country country) => country switch
        {
            Country.England => "English",
            Country.France => "French",
            Country.Italy => "Italian",
            Country.Germany => "German",
            Country.Spain => "Spanish",
            Country.Portugal => "Portuguese",
            Country.Netherlands => "Dutch",
            Country.Austria => "Austrian",
            Country.Russia => "Russian",
            Country.China => "Chinese",
            Country.Japan => "Japanese",
            Country.India => "Indian",
            Country.Turkey => "Turkish",
            Country.UnitedStates => "American",
            Country.Poland => "Polish",
            _ => country.ToDisplayString()
        };

        public static string Chip(Century century) =>
            century == Century.Unknown ? "undated" : $"{century} c.";

        public static string Prose(Century century) =>
            century == Century.Unknown ? "undated pieces" : $"{century} century pieces";

        public static string Chip(ShippingZone zone) => zone.ToDisplayString().ToLowerInvariant();

        public static string Prose(ShippingZone zone) => Chip(zone) + " shipping";

        public static string Categories(int count) => count == 1 ? "category" : "categories";

        public static string Regions(int count) => count == 1 ? "region" : "regions";

        public static string Periods(int count) => count == 1 ? "period" : "periods";

        public static string Routes(int count) => count == 1 ? "route" : "routes";

        /// <summary>Spells counts up to nine, then falls back to digits.</summary>
        public static string Spell(int count) => count switch
        {
            0 => "no",
            1 => "one",
            2 => "two",
            3 => "three",
            4 => "four",
            5 => "five",
            6 => "six",
            7 => "seven",
            8 => "eight",
            9 => "nine",
            _ => count.ToString()
        };
    }
}
