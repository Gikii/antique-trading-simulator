using System;
using UnityEngine;

namespace AntiqueTradingSimulator.Dependency
{
    /// <summary>
    /// A knock-on effect in play: one category's interest raised or lowered because another one
    /// moved. Fades a little every day.
    /// </summary>
    [Serializable]
    public class DependencyInfluence
    {
        /// <summary>The category that is being affected.</summary>
        public MarketFacet Facet;

        /// <summary>The category the change started in, so the UI can say "because of X".</summary>
        public MarketFacet Origin;

        public DependencyKind Kind;

        /// <summary>Current size of the effect, in units of demand.</summary>
        public float Amount;

        /// <summary>Share of Amount that fades each day.</summary>
        public float DecayPerWeek;

        /// <summary>Hops from the origin; 0 when a category reacts to itself.</summary>
        public int Depth;

        public int CreatedOnWeek;
        public int LastRefreshedOnWeek;

        /// <summary>Fades the influence by one day. Returns the amount left.</summary>
        public float Decay()
        {
            Amount *= 1f - Mathf.Clamp01(DecayPerWeek);
            return Amount;
        }

        public override string ToString() =>
            $"{MarketFacets.DisplayName(Facet)} {Amount:+0.00;-0.00} (from {MarketFacets.DisplayName(Origin)}, {Kind}, depth {Depth})";
    }
}
