using UnityEngine;

namespace AntiqueTradingSimulator.Restoration
{
    public enum RestorationOutcome
    {
        Success,  // reached the workshop's target condition
        Partial,  // got halfway to the target
        Failure   // the work went wrong — the antique lost some condition
    }

    /// <summary>
    /// What restoring one antique at one workshop would cost, how long it takes, what it can
    /// achieve and how likely each outcome is — computed by RestorationManager.Quote before the
    /// player commits. Values are at today's market prices. Pure data; changes nothing.
    /// </summary>
    public sealed class RestorationQuote
    {
        public RestorationOption Option { get; }

        public float StartCondition { get; }
        public float TargetCondition { get; }
        public float PartialCondition { get; }
        /// <summary>Lowest condition a failure can leave (worst case).</summary>
        public float WorstCondition { get; }

        public float Cost { get; }
        public int StartDay { get; }
        public int DurationDays { get; }
        public int EndDay => StartDay + DurationDays;

        public float SuccessChance => Option.SuccessChance;
        public float PartialChance => Option.PartialChance;
        public float FailureChance => Option.FailureChance;

        /// <summary>Market value now and at the target condition, at today's prices.</summary>
        public float ValueNow { get; }
        public float ValueAtTarget { get; }

        /// <summary>
        /// Average value change over all outcomes minus the fee, at today's prices. Positive = worth
        /// it on average (before the market moves and before the time spent).
        /// </summary>
        public float ExpectedProfit { get; }

        /// <summary>Why this workshop can't take the job right now, or null if it can.</summary>
        public string BlockReason { get; }
        public bool CanStart => BlockReason == null;

        public RestorationQuote(RestorationOption option, float startCondition, float targetCondition, float partialCondition,
            float worstCondition, float cost, int startDay, int durationDays, float perfectValue, string blockReason)
        {
            Option = option;
            StartCondition = startCondition;
            TargetCondition = targetCondition;
            PartialCondition = partialCondition;
            WorstCondition = worstCondition;
            Cost = cost;
            StartDay = startDay;
            DurationDays = Mathf.Max(1, durationDays);
            BlockReason = blockReason;

            ValueNow = perfectValue * startCondition;
            ValueAtTarget = perfectValue * targetCondition;

            float averageFailureCondition = startCondition - (option.FailureLossMin + option.FailureLossMax) * 0.5f;
            averageFailureCondition = Mathf.Max(worstCondition, averageFailureCondition);

            float expectedCondition =
                option.SuccessChance * targetCondition +
                option.PartialChance * partialCondition +
                option.FailureChance * averageFailureCondition;

            ExpectedProfit = perfectValue * (expectedCondition - startCondition) - cost;
        }
    }
}
