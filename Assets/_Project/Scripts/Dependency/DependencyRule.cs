using System;
using UnityEngine;

namespace AntiqueTradingSimulator.Dependency
{
    /// <summary>
    /// What kind of link this is. Words the UI and groups links together.
    /// </summary>
    public enum DependencyKind
    {
        Complementary,       // collected together: desks and chairs, paintings and mirrors
        CapitalCompetition,  // money spent here is money not spent there
        SharedEra,           // same period reacts to the same news
        SharedOrigin,        // same country/workshop
        Fashion,             // a style becoming (un)popular
        Other
    }

    /// <summary>How the link works in the other direction, so a pair is written once.</summary>
    public enum Symmetry
    {
        /// <summary>A → B and B → A, same strength. The default: "paintings ↔ mirrors".</summary>
        Mutual,

        /// <summary>A → B at Strength, B → A at Strength * ReverseFactor.</summary>
        Mirrored,

        /// <summary>A → B only.</summary>
        OneWay
    }

    /// <summary>
    /// One link between two categories, written by hand in a DependencyGraphSettings asset. Both
    /// directions come from this single entry.
    /// </summary>
    [Serializable]
    public class DependencyRule
    {
        public bool Enabled = true;

        [Tooltip("The category the change starts in.")]
        public MarketFacet From;

        [Tooltip("The category that reacts.")]
        public MarketFacet To;

        [Tooltip("How strongly a change in From carries over to To. Positive = they rise together " +
                 "(complementary), negative = one grows at the other's expense (competition).")]
        [Range(-1f, 1f)] public float Strength = 0.5f;

        [Tooltip("Mutual = the same link both ways (write the pair once). Mirrored = the reverse " +
                 "direction uses Strength * ReverseFactor. OneWay = no reverse edge at all.")]
        public Symmetry Symmetry = Symmetry.Mutual;

        [Tooltip("Only used with Symmetry = Mirrored.")]
        [Range(0f, 1f)] public float ReverseFactor = 0.5f;

        [Tooltip("The incoming signal must reach this size before the link fires at all — this is " +
                 "what stops every single transaction from rippling through the whole market.")]
        [Min(0f)] public float ActivationThreshold = 0.5f;

        [Tooltip("Share of the resulting influence that fades away each day. 0.2 = roughly gone in a week.")]
        [Range(0f, 1f)] public float DecayPerDay = 0.2f;

        /// <summary>
        /// The kind carried by this rule's influences, read off the sign of Strength: positive
        /// means the categories rise together, negative means one grows at the other's expense.
        /// </summary>
        public DependencyKind DerivedKind =>
            Strength < 0f ? DependencyKind.CapitalCompetition : DependencyKind.Complementary;

        /// <summary>The strength of the reverse link, or 0 when the rule is one-way.</summary>
        public float ReverseStrength => Symmetry switch
        {
            Symmetry.Mutual => Strength,
            Symmetry.Mirrored => Strength * ReverseFactor,
            _ => 0f
        };

        public override string ToString()
        {
            string arrow = Symmetry == Symmetry.OneWay ? "→" : "↔";
            return $"{MarketFacets.DisplayName(From)} {arrow} {MarketFacets.DisplayName(To)} ({Strength:+0.00;-0.00})";
        }
    }

    /// <summary>
    /// A whole family of links generated from one dimension, covering new enum members as they
    /// are added.
    /// </summary>
    [Serializable]
    public class AutoDependencyRule
    {
        public bool Enabled = true;

        [Tooltip("Which dimension to expand.")]
        public FacetKind Dimension = FacetKind.Century;

        [Tooltip("True: cohesion inside one value — interest in XIX-century items lifts the rest of " +
                 "the XIX century (one self-link per value). " +
                 "False: links every pair of DIFFERENT values (types competing for the same wallet).")]
        public bool SameValueOnly = true;

        public DependencyKind Kind = DependencyKind.SharedEra;

        [Tooltip("Strength given to every generated link. Keep it small — these are background links " +
                 "and they add up with the hand-written rules.")]
        [Range(-1f, 1f)] public float Strength = 0.15f;

        [Min(0f)] public float ActivationThreshold = 1f;

        [Range(0f, 1f)] public float DecayPerDay = 0.25f;

        // Links cover the named enum members that at least one antique uses. That set is resolved
        // when the graph is built, so an antique added in a previously unused country needs a
        // RebuildGraph.

        public override string ToString() =>
            $"auto[{Dimension}] {(SameValueOnly ? "within value" : "across values")} {Strength:+0.00;-0.00}";
    }
}
