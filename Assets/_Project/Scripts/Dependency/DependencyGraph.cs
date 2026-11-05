using System.Collections.Generic;
using UnityEngine;

namespace AntiqueTradingSimulator.Dependency
{
    /// <summary>One resolved, one-directional link in the runtime graph.</summary>
    public readonly struct DependencyEdge
    {
        public readonly MarketFacet Source;
        public readonly MarketFacet Target;
        public readonly float Strength;
        public readonly DependencyKind Kind;
        public readonly float ActivationThreshold;
        public readonly float DecayPerDay;

        public DependencyEdge(MarketFacet source, MarketFacet target, float strength, DependencyKind kind,
                              float activationThreshold, float decayPerDay)
        {
            Source = source;
            Target = target;
            Strength = strength;
            Kind = kind;
            ActivationThreshold = activationThreshold;
            DecayPerDay = decayPerDay;
        }

        public bool IsSelfLink => Source == Target;

        public override string ToString() =>
            $"{MarketFacets.DisplayName(Source)} → {MarketFacets.DisplayName(Target)} " +
            $"({Strength:+0.00;-0.00}, {Kind}, thr {ActivationThreshold:0.##}, decay {DecayPerDay:0.##})";
    }

    /// <summary>One category's share of a market change, produced by walking the graph.</summary>
    public struct PropagatedInfluence
    {
        public MarketFacet Facet;
        public float Amount;
        public float DecayPerDay;
        public DependencyKind Kind;
        public int Depth;
    }

    /// <summary>
    /// The links the rules expand into, ready to walk. Built once from a settings asset and not
    /// changed afterwards; rebuild it when the rules change.
    /// </summary>
    public class DependencyGraph
    {
        private readonly Dictionary<MarketFacet, List<DependencyEdge>> _outgoing = new Dictionary<MarketFacet, List<DependencyEdge>>();
        private readonly Dictionary<MarketFacet, DependencyEdge> _selfLinks = new Dictionary<MarketFacet, DependencyEdge>();

        private static readonly List<DependencyEdge> NoEdges = new List<DependencyEdge>();

        public int EdgeCount { get; private set; }
        public int SelfLinkCount => _selfLinks.Count;
        public IEnumerable<MarketFacet> Sources => _outgoing.Keys;

        /// <summary>Links leading from a category to the other categories.</summary>
        public IReadOnlyList<DependencyEdge> EdgesFrom(MarketFacet facet) =>
            _outgoing.TryGetValue(facet, out var edges) ? edges : NoEdges;

        /// <summary>The link a category has with itself, if any.</summary>
        public bool TryGetSelfLink(MarketFacet facet, out DependencyEdge edge) => _selfLinks.TryGetValue(facet, out edge);

        /// <summary>Every edge, for validation and editor previews.</summary>
        public IEnumerable<DependencyEdge> AllEdges()
        {
            foreach (var pair in _selfLinks)
                yield return pair.Value;

            foreach (var list in _outgoing.Values)
                foreach (var edge in list)
                    yield return edge;
        }

        // ---------------------------------------------------------------- building

        private struct EdgeAccumulator
        {
            public float Strength;
            public float ActivationThreshold;
            public float DecayPerDay;
            public DependencyKind Kind;
            public float DominantMagnitude;
        }

        /// <summary>
        /// Expands the authored rules into directed edges. A two-way rule becomes two edges, and
        /// rules landing on the same pair are merged into one.
        /// </summary>
        public static DependencyGraph Build(DependencyGraphSettings settings)
        {
            var graph = new DependencyGraph();
            if (settings == null) return graph;

            var pairs = new Dictionary<(MarketFacet from, MarketFacet to), EdgeAccumulator>();

            if (settings.Rules != null)
            {
                foreach (var rule in settings.Rules)
                {
                    if (rule == null || !rule.Enabled) continue;

                    AddPair(pairs, rule.From, rule.To, rule.Strength, rule.DerivedKind,
                            rule.ActivationThreshold, rule.DecayPerDay);

                    // One authored rule, both directions.
                    float reverse = rule.ReverseStrength;
                    if (rule.Symmetry != Symmetry.OneWay && !Mathf.Approximately(reverse, 0f))
                        AddPair(pairs, rule.To, rule.From, reverse, rule.DerivedKind,
                                rule.ActivationThreshold, rule.DecayPerDay);
                }
            }

            if (settings.AutoRules != null)
            {
                foreach (var autoRule in settings.AutoRules)
                    ExpandAutoRule(pairs, autoRule);
            }

            foreach (var pair in pairs)
            {
                var accumulator = pair.Value;
                float strength = Mathf.Clamp(accumulator.Strength, -1f, 1f);
                if (Mathf.Approximately(strength, 0f)) continue;

                var edge = new DependencyEdge(pair.Key.from, pair.Key.to, strength, accumulator.Kind,
                                              accumulator.ActivationThreshold, accumulator.DecayPerDay);

                if (edge.IsSelfLink)
                {
                    graph._selfLinks[edge.Source] = edge;
                    continue;
                }

                if (!graph._outgoing.TryGetValue(edge.Source, out var list))
                {
                    list = new List<DependencyEdge>();
                    graph._outgoing[edge.Source] = list;
                }

                list.Add(edge);
                graph.EdgeCount++;
            }

            // Propagation reaches each category once, so edge order decides ties. Sorted here
            // because the merge runs through a Dictionary, which keeps its own order.
            foreach (var list in graph._outgoing.Values)
                list.Sort(CompareByTarget);

            return graph;
        }

        private static int CompareByTarget(DependencyEdge a, DependencyEdge b)
        {
            int byKind = ((int)a.Target.Kind).CompareTo((int)b.Target.Kind);
            return byKind != 0 ? byKind : a.Target.Value.CompareTo(b.Target.Value);
        }

        private static void AddPair(Dictionary<(MarketFacet, MarketFacet), EdgeAccumulator> pairs,
                                    MarketFacet from, MarketFacet to, float strength, DependencyKind kind,
                                    float threshold, float decay)
        {
            if (Mathf.Approximately(strength, 0f)) return;

            float magnitude = Mathf.Abs(strength);
            var key = (from, to);

            if (!pairs.TryGetValue(key, out var accumulator))
            {
                pairs[key] = new EdgeAccumulator
                {
                    Strength = strength,
                    ActivationThreshold = threshold,
                    DecayPerDay = decay,
                    Kind = kind,
                    DominantMagnitude = magnitude
                };
                return;
            }

            accumulator.Strength += strength;
            accumulator.ActivationThreshold = Mathf.Min(accumulator.ActivationThreshold, threshold);

            if (magnitude > accumulator.DominantMagnitude)
            {
                accumulator.DominantMagnitude = magnitude;
                accumulator.Kind = kind;
                accumulator.DecayPerDay = decay;
            }

            pairs[key] = accumulator;
        }

        private static void ExpandAutoRule(Dictionary<(MarketFacet, MarketFacet), EdgeAccumulator> pairs,
                                           AutoDependencyRule rule)
        {
            if (rule == null || !rule.Enabled || Mathf.Approximately(rule.Strength, 0f)) return;

            var facets = new List<MarketFacet>();
            foreach (int value in MarketFacets.ValuesOf(rule.Dimension))
            {
                var facet = new MarketFacet(rule.Dimension, value);
                // Links cover named enum members that at least one antique uses.
                if (facet.IsDefaultValue) continue;
                if (MarketFacets.MemberCount(facet) == 0) continue;
                facets.Add(facet);
            }

            if (rule.SameValueOnly)
            {
                // Interest in part of a century lifts the rest of it: one self-link per value.
                foreach (var facet in facets)
                    AddPair(pairs, facet, facet, rule.Strength, rule.Kind,
                            rule.ActivationThreshold, rule.DecayPerDay);
                return;
            }

            // Every pair of different values, both ways.
            for (int i = 0; i < facets.Count; i++)
            {
                for (int j = 0; j < facets.Count; j++)
                {
                    if (i == j) continue;
                    AddPair(pairs, facets[i], facets[j], rule.Strength, rule.Kind,
                            rule.ActivationThreshold, rule.DecayPerDay);
                }
            }
        }

        // ---------------------------------------------------------------- propagation

        /// <summary>
        /// Walks outwards from a category that has just moved and collects what every reachable
        /// category should feel, appending to the list it is given. Each category is reached once,
        /// by the shortest path, so a ripple passes through a cycle a single time.
        /// </summary>
        public void Propagate(MarketFacet source, float signal, int maxDepth, float damping,
                              float minimumChange, List<PropagatedInfluence> results)
        {
            if (results == null || Mathf.Approximately(signal, 0f)) return;

            var visited = new HashSet<MarketFacet> { source };
            var queue = new Queue<(MarketFacet facet, float amount, int depth)>();

            // The source's own reaction resolves first, undamped, and the walk continues past it.
            if (TryGetSelfLink(source, out var selfLink) && Mathf.Abs(signal) >= selfLink.ActivationThreshold)
            {
                float selfAmount = signal * selfLink.Strength;
                if (Mathf.Abs(selfAmount) >= minimumChange)
                    results.Add(new PropagatedInfluence
                    {
                        Facet = source,
                        Amount = selfAmount,
                        DecayPerDay = selfLink.DecayPerDay,
                        Kind = selfLink.Kind,
                        Depth = 0
                    });
            }

            queue.Enqueue((source, signal, 0));

            while (queue.Count > 0)
            {
                var (facet, amount, depth) = queue.Dequeue();
                if (depth >= maxDepth) continue;

                foreach (var edge in EdgesFrom(facet))
                {
                    if (visited.Contains(edge.Target)) continue;

                    // A link fires once the change reaches its threshold, which takes a few sales.
                    if (Mathf.Abs(amount) < edge.ActivationThreshold) continue;

                    float contribution = amount * edge.Strength * Mathf.Pow(damping, depth);
                    if (Mathf.Abs(contribution) < minimumChange) continue;

                    visited.Add(edge.Target);
                    results.Add(new PropagatedInfluence
                    {
                        Facet = edge.Target,
                        Amount = contribution,
                        DecayPerDay = edge.DecayPerDay,
                        Kind = edge.Kind,
                        Depth = depth + 1
                    });

                    queue.Enqueue((edge.Target, contribution, depth + 1));
                }
            }
        }
    }
}
