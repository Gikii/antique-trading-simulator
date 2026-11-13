using AntiqueTradingSimulator.Economy;
using Newtonsoft.Json;

namespace AntiqueTradingSimulator.Restoration
{
    /// <summary>
    /// An antique at a workshop. The outcome is rolled when the job starts and kept here (hidden
    /// from the player) so reloading a save before the end day can't re-roll it.
    /// </summary>
    public class RestorationJob
    {
        public string AntiqueId;
        public string AntiqueName;
        public string OptionId;
        public string OptionName;

        public int StartWeek;
        public int EndWeek;
        public float Cost;

        public float StartCondition;
        public float TargetCondition;

        public RestorationOutcome Outcome;
        public float ResultCondition;

        /// <summary>Whose antique it is. Not saved — resolved from the owner Id on load.</summary>
        public TraderInventory Owner;

        public int WeeksRemaining(int thisWeek) => System.Math.Max(0, EndWeek - thisWeek);
    }

    /// <summary>A finished restoration — kept briefly for the info bar.</summary>
    public class RestorationRecord
    {
        public string AntiqueId;
        public string AntiqueName;
        public string OptionName;
        public int Week;
        public RestorationOutcome Outcome;
        public float StartCondition;
        public float ResultCondition;
        /// <summary>Market value change caused by the new condition, at the prices of the day it finished.</summary>
        public float ValueChange;

        [JsonIgnore]
        public float ConditionChange => ResultCondition - StartCondition;
    }
}
