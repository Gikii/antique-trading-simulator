using System.Collections.Generic;
using AntiqueTradingSimulator.News;

namespace AntiqueTradingSimulator.Rumours
{
    /// <summary>What a rumour would cost and do, as RumourMill.Quote works it out. Shown live in the modal.</summary>
    public class RumourQuote
    {
        /// <summary>What the rumour claims: direction, and the attribute values it names.</summary>
        public RumourClaim Claim;

        /// <summary>The claim as news data, one entry per named value. This is what NPCs read.</summary>
        public List<NewsEventData> Effects = new List<NewsEventData>();

        public int PlantWeek;
        public int SpreadWeek;
        public int ClaimWeek;

        public float Spend;
        public float MinSpend;
        public float MaxSpend;

        /// <summary>Average current reference price of the named antiques.</summary>
        public float ScopeValue;

        /// <summary>How many antique definitions the claim covers.</summary>
        public int AffectedCount;

        /// <summary>How much the claim's breadth widens the cost (FakeRumourSettings.ClaimWeight).</summary>
        public float ClaimWeight;

        /// <summary>K in the credibility formula, after the level's cost multiplier.</summary>
        public float CostScale;

        /// <summary>Discount from the Information Network level, 0 at level 1 (1 − cost multiplier).</summary>
        public float LevelDiscount;

        public float Cap;
        public float Fatigue = 1f;
        public float Credibility;

        /// <summary>Expected number of NPCs that act on it.</summary>
        public float ExpectedReactors;

        public float TraceChance;

        public bool Rising => Claim != null && Claim.Rising;

        /// <summary>Why the rumour can't be planted, or null.</summary>
        public string Blocker;

        public bool CanPlant => string.IsNullOrEmpty(Blocker);
    }
}
