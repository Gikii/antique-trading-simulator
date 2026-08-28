using UnityEngine;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.Market
{

    public class AntiqueEnums
    {
        public enum AntiqueType
        {
            Other = 0,
            Clock,
            Vase,
            Furniture,
            Painting,
            Sculpture,
            Jewelry,
            Coin,
            Book,
            Rug,
            Silverware,
            Ceramic,
            Porcelain,
            Mirror,
            Lamp,
            Weapon,
            Instrument,
            Textile,
            Toy
        }

        public enum TimePeriod
        {
            Unknown = 0,
            Ancient,
            Medieval,
            Renaissance,
            Baroque,
            Rococo,
            Georgian,
            Victorian,
            Edwardian,
            ArtNouveau,
            ArtDeco,
            MidCentury,
            Contemporary
        }

        public enum Country
        {
            Other = 0,
            England,
            France,
            Italy,
            Germany,
            Spain,
            Portugal,
            Netherlands,
            Austria,
            Russia,
            China,
            Japan,
            India,
            Turkey,
            UnitedStates,
            Poland
        }

    }

    public static class AntiqueEnumDisplay
    {
        public static string ToDisplayString(this TimePeriod period) => period switch
        {
            TimePeriod.ArtNouveau => "Art Nouveau",
            TimePeriod.ArtDeco => "Art Deco",
            TimePeriod.MidCentury => "Mid-Century",
            _ => period.ToString()
        };

        public static string ToDisplayString(this AntiqueType type) => type.ToString();

        public static string ToDisplayString(this Country country) =>
            country == Country.UnitedStates ? "United States" : country.ToString();
    }
}