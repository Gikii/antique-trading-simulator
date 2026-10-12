using System.Collections.Generic;

namespace AntiqueTradingSimulator.Restoration
{
    public class RestorationState
    {
        public List<RestorationJobState> Jobs = new List<RestorationJobState>();
        public List<RestorationRecord> Completed = new List<RestorationRecord>();
    }

    public class RestorationJobState
    {
        public string OwnerId;

        public string AntiqueId;
        public string AntiqueName;
        public string OptionId;
        public string OptionName;

        public int StartDay;
        public int EndDay;
        public float Cost;

        public float StartCondition;
        public float TargetCondition;

        public RestorationOutcome Outcome;
        public float ResultCondition;

        public static RestorationJobState Capture(RestorationJob job, string ownerId) => new RestorationJobState
        {
            OwnerId = ownerId,
            AntiqueId = job.AntiqueId,
            AntiqueName = job.AntiqueName,
            OptionId = job.OptionId,
            OptionName = job.OptionName,
            StartDay = job.StartDay,
            EndDay = job.EndDay,
            Cost = job.Cost,
            StartCondition = job.StartCondition,
            TargetCondition = job.TargetCondition,
            Outcome = job.Outcome,
            ResultCondition = job.ResultCondition
        };

        public RestorationJob Restore(Economy.TraderInventory owner) => new RestorationJob
        {
            Owner = owner,
            AntiqueId = AntiqueId,
            AntiqueName = AntiqueName,
            OptionId = OptionId,
            OptionName = OptionName,
            StartDay = StartDay,
            EndDay = EndDay,
            Cost = Cost,
            StartCondition = StartCondition,
            TargetCondition = TargetCondition,
            Outcome = Outcome,
            ResultCondition = ResultCondition
        };
    }
}
