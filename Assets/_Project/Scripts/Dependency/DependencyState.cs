using System.Collections.Generic;

namespace AntiqueTradingSimulator.Dependency
{
    /// <summary>
    /// Save data: the effects still in play and the signal collected since the last day change.
    /// The links themselves live in the settings asset.
    /// </summary>
    public class DependencyState
    {
        public List<InfluenceState> Influences = new List<InfluenceState>();

        public List<PendingSignalState> Pending = new List<PendingSignalState>();

        public static DependencyState Capture(IReadOnlyList<DependencyInfluence> influences,
                                              IReadOnlyDictionary<MarketFacet, float> pending)
        {
            var state = new DependencyState();

            if (influences != null)
            {
                foreach (var influence in influences)
                {
                    if (influence == null) continue;
                    state.Influences.Add(InfluenceState.Capture(influence));
                }
            }

            if (pending != null)
            {
                foreach (var pair in pending)
                    state.Pending.Add(new PendingSignalState
                    {
                        Kind = pair.Key.Kind,
                        Value = pair.Key.Value,
                        Signal = pair.Value
                    });
            }

            return state;
        }

        public List<DependencyInfluence> RestoreInfluences()
        {
            var result = new List<DependencyInfluence>();
            if (Influences == null) return result;

            foreach (var influenceState in Influences)
            {
                var influence = influenceState?.Restore();
                if (influence != null) result.Add(influence);
            }

            return result;
        }

        public List<KeyValuePair<MarketFacet, float>> RestorePending()
        {
            var result = new List<KeyValuePair<MarketFacet, float>>();
            if (Pending == null) return result;

            foreach (var pendingState in Pending)
            {
                if (pendingState == null) continue;
                result.Add(new KeyValuePair<MarketFacet, float>(
                    new MarketFacet(pendingState.Kind, pendingState.Value), pendingState.Signal));
            }

            return result;
        }
    }

    /// <summary>
    /// A saved influence. Categories are written as kind + value, which keeps a save readable and
    /// lets it survive enum members renamed by hand.
    /// </summary>
    public class InfluenceState
    {
        public FacetKind FacetKind;
        public int FacetValue;

        public FacetKind OriginKind;
        public int OriginValue;

        public DependencyKind Kind;
        public float Amount;
        public float DecayPerWeek;
        public int Depth;
        public int CreatedOnWeek;
        public int LastRefreshedOnWeek;

        public static InfluenceState Capture(DependencyInfluence influence)
        {
            if (influence == null) return null;

            return new InfluenceState
            {
                FacetKind = influence.Facet.Kind,
                FacetValue = influence.Facet.Value,
                OriginKind = influence.Origin.Kind,
                OriginValue = influence.Origin.Value,
                Kind = influence.Kind,
                Amount = influence.Amount,
                DecayPerWeek = influence.DecayPerWeek,
                Depth = influence.Depth,
                CreatedOnWeek = influence.CreatedOnWeek,
                LastRefreshedOnWeek = influence.LastRefreshedOnWeek
            };
        }

        public DependencyInfluence Restore() => new DependencyInfluence
        {
            Facet = new MarketFacet(FacetKind, FacetValue),
            Origin = new MarketFacet(OriginKind, OriginValue),
            Kind = Kind,
            Amount = Amount,
            DecayPerWeek = DecayPerWeek,
            Depth = Depth,
            CreatedOnWeek = CreatedOnWeek,
            LastRefreshedOnWeek = LastRefreshedOnWeek
        };
    }

    public class PendingSignalState
    {
        public FacetKind Kind;
        public int Value;
        public float Signal;
    }
}
