using System;
using System.Collections.Generic;
using UnityEngine;

namespace AntiqueTradingSimulator.Company
{
    /// <summary>The criteria the final Congress score is built from (design doc §1).</summary>
    public enum ScoreCriterion
    {
        Wealth,
        CollectionValue,
        ContractsFulfilled,
        Reputation,
        Credibility,
        MarketShare
    }

    [Serializable]
    public class ScoreCriterionSettings
    {
        public ScoreCriterion Criterion;

        [Tooltip("Value that earns the full 100 points for this criterion " +
                 "(€ for Wealth/Collection, count for contracts, points for reputation, 0..1 for credibility/market share).")]
        [Min(0.0001f)] public float Target = 1f;

        [Tooltip("Relative weight in the final score. Weights don't need to sum to 1.")]
        [Min(0f)] public float Weight = 1f;

        public ScoreCriterionSettings() { }

        public ScoreCriterionSettings(ScoreCriterion criterion, float target, float weight)
        {
            Criterion = criterion;
            Target = target;
            Weight = weight;
        }
    }

    [Serializable]
    public class ScoreVerdict
    {
        [Range(0, 100)] public int MinScore;
        [TextArea(2, 3)] public string Text;

        public ScoreVerdict() { }

        public ScoreVerdict(int minScore, string text)
        {
            MinScore = minScore;
            Text = text;
        }
    }

    /// <summary>
    /// Targets and weights of the end-of-campaign evaluation, used for the "Congress
    /// readiness" preview. Placeholder numbers — tune once the economy is balanced.
    /// If none is assigned to CompanyManager, an in-memory default is used.
    /// </summary>
    [CreateAssetMenu(fileName = "ScoringSettings", menuName = "AntiqueTradingSimulator/Company/Scoring Settings")]
    public class ScoringSettings : ScriptableObject
    {
        public List<ScoreCriterionSettings> Criteria = new List<ScoreCriterionSettings>
        {
            new ScoreCriterionSettings(ScoreCriterion.Wealth, 20000f, 0.25f),
            new ScoreCriterionSettings(ScoreCriterion.CollectionValue, 10000f, 0.25f),
            new ScoreCriterionSettings(ScoreCriterion.ContractsFulfilled, 12f, 0.15f),
            new ScoreCriterionSettings(ScoreCriterion.Reputation, 2500f, 0.15f),
            new ScoreCriterionSettings(ScoreCriterion.Credibility, 1f, 0.10f),
            new ScoreCriterionSettings(ScoreCriterion.MarketShare, 0.25f, 0.10f),
        };

        [Tooltip("Ascending by MinScore. The highest verdict whose MinScore the score reaches is shown.")]
        public List<ScoreVerdict> Verdicts = new List<ScoreVerdict>
        {
            new ScoreVerdict(0, "Your company is far from ready. Build up your collection and complete contracts."),
            new ScoreVerdict(35, "You are on the right track, but there is still time to improve."),
            new ScoreVerdict(60, "A strong position. Keep growing to secure a top place at the Congress."),
            new ScoreVerdict(85, "Your company is ready to compete with the best houses in Europe."),
        };

        public ScoreCriterionSettings Get(ScoreCriterion criterion)
        {
            foreach (var c in Criteria)
                if (c != null && c.Criterion == criterion)
                    return c;
            return null;
        }
    }
}
