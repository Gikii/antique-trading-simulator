using System;
using System.Collections.Generic;
using UnityEngine;

namespace AntiqueTradingSimulator.Restoration
{
    /// <summary>
    /// One workshop the player can send an antique to. Higher tiers restore further and fail
    /// less often, but cost more and take longer — the choice is about risk, time and the
    /// final condition, not about which option is simply best.
    /// </summary>
    [Serializable]
    public class RestorationOption
    {
        [Tooltip("Stable key written to save files. Don't change it once saves exist.")]
        public string Id;

        public string DisplayName;
        [TextArea(1, 3)] public string Description;

        [Tooltip("Condition the antique reaches on full success (0.3..1). Items already at or above it can't use this workshop.")]
        [Range(0.3f, 1f)] public float MaxCondition = 0.7f;

        [Tooltip("Fee as a share of the value the work adds at full success (value at perfect condition × condition gained).")]
        [Range(0f, 2f)] public float CostRate = 0.3f;

        [Tooltip("The fee never goes below this amount (€).")]
        [Min(0f)] public float MinimumFee = 50f;

        [Min(1)] public int DurationDays = 3;

        [Tooltip("Chance the antique reaches Max Condition.")]
        [Range(0f, 1f)] public float SuccessChance = 0.7f;

        [Tooltip("Chance the work only gets halfway. Failure takes the rest (1 - success - partial).")]
        [Range(0f, 1f)] public float PartialChance = 0.2f;

        [Tooltip("Condition lost on failure — rolled uniformly between Min and Max (never below Antique.MinCondition).")]
        [Range(0f, 0.5f)] public float FailureLossMin = 0.05f;
        [Range(0f, 0.5f)] public float FailureLossMax = 0.10f;

        [Tooltip("Reputation the player needs to use this workshop (0 = anyone). Reputation gives access, never better prices.")]
        [Min(0)] public int RequiredReputation;

        public float FailureChance => Mathf.Max(0f, 1f - SuccessChance - PartialChance);

        public RestorationOption() { }

        public RestorationOption(string id, string displayName, string description, float maxCondition, float costRate,
            float minimumFee, int durationDays, float successChance, float partialChance, int requiredReputation)
        {
            Id = id;
            DisplayName = displayName;
            Description = description;
            MaxCondition = maxCondition;
            CostRate = costRate;
            MinimumFee = minimumFee;
            DurationDays = durationDays;
            SuccessChance = successChance;
            PartialChance = partialChance;
            RequiredReputation = requiredReputation;
        }
    }

    /// <summary>
    /// Workshops available for restoration. If none is assigned to RestorationManager, an
    /// in-memory default is used. The UI and the reputation unlock list are built from this data.
    /// </summary>
    [CreateAssetMenu(fileName = "RestorationSettings", menuName = "AntiqueTradingSimulator/Restoration/Restoration Settings")]
    public class RestorationSettings : ScriptableObject
    {
        public List<RestorationOption> Options = new List<RestorationOption>
        {
            new RestorationOption("local", "Local Workshop",
                "A craftsman around the corner. Quick and cheap, but not every job turns out well.",
                0.70f, 0.30f, 50f, 3, 0.70f, 0.20f, 0),
            new RestorationOption("certified", "Certified Conservator",
                "A qualified conservator with a good track record.",
                0.85f, 0.45f, 100f, 6, 0.85f, 0.12f, 0),
            new RestorationOption("master", "Master Workshop",
                "A renowned studio that works with museums. Takes its time and almost never fails.",
                0.95f, 0.60f, 200f, 10, 0.95f, 0.05f, 500),
        };

        [Header("History")]
        [Tooltip("How many finished restorations are kept for the info bar and the save.")]
        [Min(5)] public int MaxCompletedLog = 30;

        public RestorationOption Get(string id)
        {
            foreach (var option in Options)
                if (option != null && option.Id == id)
                    return option;
            return null;
        }
    }
}
