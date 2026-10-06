using UnityEngine;

namespace AntiqueTradingSimulator.Company
{
    /// <summary>
    /// The company's key numbers at one moment — recorded once per day by CompanyManager
    /// (history for charts and 7-day trends) or captured live for the current state.
    /// </summary>
    public readonly struct CompanySnapshot
    {
        public readonly int Day;
        public readonly float Cash;
        public readonly float Wealth;
        public readonly float CollectionValue;
        public readonly int Reputation;
        public readonly float Credibility;   // 0..1
        public readonly float MarketShare;   // 0..1
        public readonly int ContractsFulfilled;

        public CompanySnapshot(int day, float cash, float wealth, float collectionValue, int reputation,
            float credibility, float marketShare, int contractsFulfilled)
        {
            Day = day;
            Cash = cash;
            Wealth = wealth;
            CollectionValue = collectionValue;
            Reputation = reputation;
            Credibility = credibility;
            MarketShare = marketShare;
            ContractsFulfilled = contractsFulfilled;
        }

        public float Value(ScoreCriterion criterion) => criterion switch
        {
            ScoreCriterion.Wealth => Wealth,
            ScoreCriterion.CollectionValue => CollectionValue,
            ScoreCriterion.ContractsFulfilled => ContractsFulfilled,
            ScoreCriterion.Reputation => Reputation,
            ScoreCriterion.Credibility => Credibility,
            ScoreCriterion.MarketShare => MarketShare,
            _ => 0f
        };
    }

    /// <summary>Turns a snapshot into the 0..100 Congress readiness scores (ScoringSettings).</summary>
    public static class CampaignScoring
    {
        /// <summary>0..100 for one criterion: the share of its target reached, capped at 100.</summary>
        public static float CriterionScore(ScoringSettings settings, ScoreCriterion criterion, float value)
        {
            var c = settings != null ? settings.Get(criterion) : null;
            if (c == null || c.Target <= 0f) return 0f;
            return Mathf.Clamp01(value / c.Target) * 100f;
        }

        /// <summary>Weighted average of all criterion scores, 0..100.</summary>
        public static float TotalScore(ScoringSettings settings, CompanySnapshot snapshot)
        {
            if (settings == null) return 0f;

            float weighted = 0f;
            float weights = 0f;
            foreach (var c in settings.Criteria)
            {
                if (c == null || c.Weight <= 0f) continue;
                weighted += CriterionScore(settings, c.Criterion, snapshot.Value(c.Criterion)) * c.Weight;
                weights += c.Weight;
            }

            return weights > 0f ? weighted / weights : 0f;
        }

        public static string Verdict(ScoringSettings settings, float score)
        {
            if (settings == null) return "";

            string text = "";
            int best = int.MinValue;
            foreach (var v in settings.Verdicts)
            {
                if (v == null || score < v.MinScore || v.MinScore < best) continue;
                best = v.MinScore;
                text = v.Text;
            }
            return text;
        }
    }
}
