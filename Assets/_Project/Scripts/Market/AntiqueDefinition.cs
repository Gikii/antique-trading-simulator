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
        public Century Century = Century.Unknown;
        public Country Country = Country.Other;

        public float BasePrice;

        // 0 = ordinary market item, not part of a tracked limited edition.
        // A positive value caps how many physical examples of this exact
        // definition are ever created (e.g. 5 for a "1/5" auction piece).
        public int EditionSize = 0;

        [TextArea]
        public string Description;
    }
}