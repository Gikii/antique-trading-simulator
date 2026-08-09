using UnityEngine;

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
        public string Category;
        public float BasePrice;

        [TextArea]
        public string Description;
    }
}