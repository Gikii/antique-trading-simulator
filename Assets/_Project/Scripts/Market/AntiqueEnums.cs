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

        // Explicit int values equal to the century number, so a Century can be
        // cast directly to int for range comparisons (e.g. filtering XV-XVIII)
        // without needing a separate numeric field.
        public enum Century
        {
            Unknown = 0,
            XII = 12,
            XIII = 13,
            XIV = 14,
            XV = 15,
            XVI = 16,
            XVII = 17,
            XVIII = 18,
            XIX = 19,
            XX = 20
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
        public static string ToDisplayString(this Century century) =>
            century == Century.Unknown ? "Unknown" : $"{century} century";

        public static string ToDisplayString(this AntiqueType type) => type.ToString();

        public static string ToDisplayString(this Country country) =>
            country == Country.UnitedStates ? "United States" : country.ToString();
    }
}