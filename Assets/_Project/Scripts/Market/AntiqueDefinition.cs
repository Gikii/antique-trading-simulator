using UnityEngine;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.Market
{
    /// <summary>
    /// Static definition of an antique type, authored as a ScriptableObject asset in Data/Antiques/.
    /// Holds data that never changes during gameplay.
    /// </summary>
    [CreateAssetMenu(fileName = "NewAntique", menuName = "AntiqueTradingSimulator/Antique Definition")]
    public class AntiqueDefinition : ScriptableObject
    {
        public string Id;
        public string DisplayName;

        public AntiqueType Type = AntiqueType.Other;
        public TimePeriod TimePeriod = TimePeriod.Unknown;
        public Country Country = Country.Other;

        public float BasePrice;

        [TextArea]
        public string Description;
    }
}