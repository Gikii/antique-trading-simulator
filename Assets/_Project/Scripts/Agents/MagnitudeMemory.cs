using System;
using System.Collections.Generic;
using UnityEngine;

namespace AntiqueTradingSimulator.Agents
{
    /// <summary>
    /// What a trader believes events of a given name do to prices, learned by watching them.
    /// An unseen event gets a wide guess coloured by temperament; each one lived through narrows it.
    /// </summary>
    public class MagnitudeMemory
    {
        /// <summary>A believed move and how sure the trader is of it.</summary>
        public readonly struct Estimate
        {
            /// <summary>Believed size of the move, as a fraction of price. Always positive.</summary>
            public readonly float Mean;

            /// <summary>Uncertainty around Mean, as a fraction of it. 0 = certain.</summary>
            public readonly float Spread;

            public readonly int Observations;

            public Estimate(float mean, float spread, int observations)
            {
                Mean = mean;
                Spread = spread;
                Observations = observations;
            }

            public bool Exists => Observations > 0;
        }

        /// <summary>Believed size of an event nobody has seen before.</summary>
        public const float ColdMean = 0.12f;

        /// <summary>Uncertainty about an event nobody has seen before.</summary>
        public const float ColdSpread = 0.8f;

        /// <summary>However many times an event has been seen, this much doubt remains.</summary>
        public const float SpreadFloor = 0.12f;

        /// <summary>Weeks an announced event is assumed to run until one is seen running.</summary>
        public const int AssumedEventWeeks = 6;

        private readonly Dictionary<string, Estimate> _seen = new Dictionary<string, Estimate>(StringComparer.Ordinal);

        /// <summary>
        /// This trader's current view of an event by that name. A bold trader reads an unfamiliar
        /// event as larger than a cautious one does.
        /// </summary>
        public Estimate For(string officialName, NpcBehaviorProfile profile)
        {
            if (!string.IsNullOrEmpty(officialName) && _seen.TryGetValue(officialName, out var known) && known.Exists)
                return known;

            float temperament = profile != null ? 0.6f + 0.8f * Mathf.Clamp01(profile.RiskTolerance) : 1f;
            return new Estimate(ColdMean * temperament, ColdSpread, 0);
        }

        /// <summary>Records what an event of that name actually did, narrowing the next estimate.</summary>
        public void Observe(string officialName, float realisedMove)
        {
            if (string.IsNullOrEmpty(officialName)) return;

            float size = Mathf.Abs(realisedMove);
            _seen.TryGetValue(officialName, out var old);

            int observations = old.Observations + 1;
            float mean = old.Exists ? old.Mean + (size - old.Mean) / observations : size;
            float spread = Mathf.Max(SpreadFloor, ColdSpread / Mathf.Sqrt(observations));

            _seen[officialName] = new Estimate(Mathf.Max(0f, mean), spread, observations);
        }

        public IReadOnlyDictionary<string, Estimate> Entries => _seen;

        public void Clear() => _seen.Clear();

        public void Set(string officialName, float mean, float spread, int observations)
        {
            if (string.IsNullOrEmpty(officialName) || observations <= 0) return;
            _seen[officialName] = new Estimate(mean, spread, observations);
        }
    }
}
