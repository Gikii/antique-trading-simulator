using System.Collections.Generic;
using UnityEngine;

namespace AntiqueTradingSimulator.Dependency
{
    /// <summary>
    /// The dependency network and the numbers controlling how changes travel through it. Create one
    /// asset in Data/Resources/ named "DependencyGraph"; adding a link is an edit to this asset.
    /// </summary>
    [CreateAssetMenu(fileName = "DependencyGraph", menuName = "AntiqueTradingSimulator/Economy/Dependency Graph")]
    public class DependencyGraphSettings : ScriptableObject
    {
        public const string DefaultResourcesPath = "DependencyGraph";

        [Header("Reading the market")]
        [Tooltip("How much a change in a category's Demand counts as 'interest' in it.")]
        [Min(0f)] public float DemandSignalWeight = 1f;

        [Tooltip("How much a change in Supply counts, with the sign flipped: stock leaving the market " +
                 "(a purchase) reads as interest, stock arriving reads as the opposite.")]
        [Min(0f)] public float SupplySignalWeight = 0.25f;

        [Header("Propagation")]
        [Tooltip("How many hops a change may travel: 1 = direct neighbours only, 2 = neighbours of neighbours.")]
        [Min(1)] public int MaxPropagationDepth = 2;

        [Tooltip("Multiplies the influence at each extra hop, so the ripple dies out.")]
        [Range(0f, 1f)] public float DepthDamping = 0.5f;

        [Tooltip("Influences smaller than this are never created.")]
        [Min(0f)] public float MinimumPropagatedChange = 0.05f;

        [Header("Limits")]
        [Tooltip("Hard cap on how much demand this system may add to (or take from) a single antique " +
                 "type at once, however many links point at it.")]
        [Min(0f)] public float MaxInfluencePerDefinition = 3f;

        [Tooltip("A fading influence is dropped once it shrinks below this.")]
        [Min(0f)] public float InfluenceCutoff = 0.02f;

        [Header("Links")]
        [Tooltip("Hand-written links between two categories. Each entry produces BOTH directions " +
                 "unless its Symmetry says otherwise.")]
        public List<DependencyRule> Rules = new List<DependencyRule>();

        [Tooltip("Link families generated from a whole dimension — the background cohesion of a " +
                 "century or the competition between types — so the matrix does not have to be clicked out.")]
        public List<AutoDependencyRule> AutoRules = new List<AutoDependencyRule>();

        public DependencyGraph BuildGraph() => DependencyGraph.Build(this);

        private void OnValidate()
        {
            if (MaxPropagationDepth < 1) MaxPropagationDepth = 1;
            if (MinimumPropagatedChange < 0f) MinimumPropagatedChange = 0f;
            if (InfluenceCutoff < 0f) InfluenceCutoff = 0f;
        }
    }
}
