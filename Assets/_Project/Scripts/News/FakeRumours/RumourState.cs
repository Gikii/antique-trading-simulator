using System.Collections.Generic;

namespace AntiqueTradingSimulator.Rumours
{
    /// <summary>RumourMill's part of a save (stored inside NewsState).</summary>
    public class RumourState
    {
        public float Skepticism;
        public List<RumourRecord> Rumours = new List<RumourRecord>();
        public List<DemandHangover> Hangovers = new List<DemandHangover>();
    }

    /// <summary>
    /// One planted fake rumour. The NewsItem itself lives in NewsManager.PublishedNews (linked by NewsId).
    /// Kept after the debunk for fatigue, trace and cooldown checks, then pruned.
    /// </summary>
    public class RumourRecord
    {
        public string NewsId;
        public string PlanterId;
        public string PlanterName;
        /// <summary>What the rumour claimed. Replaces the event template the first version used.</summary>
        public RumourClaimState Claim;

        public string Title;

        public string Scope;

        public int PlantWeek;
        public int SpreadWeek;
        public int ClaimWeek;

        public float Spend;
        public float CostScale;
        public float Credibility;
        public bool Bullish;

        /// <summary>"AntiqueType:Clock", "Country:France", … — for same-scope fatigue.</summary>
        public List<string> ScopeKeys = new List<string>();

        public bool Debunked;
        public bool Traced;

        public RumourRecord Copy()
        {
            var copy = (RumourRecord)MemberwiseClone();
            copy.ScopeKeys = ScopeKeys != null ? new List<string>(ScopeKeys) : new List<string>();
            copy.Claim = Claim != null ? Claim.Restore().Capture() : null;
            return copy;
        }
    }

    /// <summary>Temporary demand change put on one antique definition at a debunk, reverted at EndWeek.</summary>
    public class DemandHangover
    {
        public string DefinitionId;
        public float Amount;
        public int EndWeek;
    }
}
