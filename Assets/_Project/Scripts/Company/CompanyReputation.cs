using System;
using System.Collections.Generic;
using UnityEngine;

namespace AntiqueTradingSimulator.Company
{
    public enum ReputationKind
    {
        Reputation,
        Credibility
    }

    /// <summary>One recorded change of reputation (points) or credibility (0..1 ratio).</summary>
    public readonly struct ReputationChange
    {
        public readonly int Day;
        public readonly ReputationKind Kind;
        public readonly float Delta;
        public readonly string Reason;

        public ReputationChange(int day, ReputationKind kind, float delta, string reason)
        {
            Day = day;
            Kind = kind;
            Delta = delta;
            Reason = reason ?? "";
        }
    }

    /// <summary>
    /// The player's reputation (prestige points, never below 0) and credibility (0..1),
    /// with a history of changes and tier lookups. Holds no game logic about WHEN values
    /// change — CompanyManager decides that and calls AddReputation/AddCredibility.
    /// </summary>
    public class CompanyReputation
    {
        private readonly ReputationSettings _settings;
        private readonly List<ReputationChange> _history = new List<ReputationChange>();

        public int Reputation { get; private set; }
        public float Credibility { get; private set; }

        /// <summary>Oldest first.</summary>
        public IReadOnlyList<ReputationChange> History => _history;

        public ReputationSettings Settings => _settings;

        public event Action<ReputationChange> OnChanged;

        public CompanyReputation(ReputationSettings settings)
        {
            _settings = settings;
            Reputation = Mathf.Max(0, settings.StartingReputation);
            Credibility = Mathf.Clamp01(settings.StartingCredibility);
        }

        public void AddReputation(int delta, string reason, int day)
        {
            int before = Reputation;
            Reputation = Mathf.Max(0, Reputation + delta);
            Record(new ReputationChange(day, ReputationKind.Reputation, Reputation - before, reason));
        }

        public void AddCredibility(float delta, string reason, int day)
        {
            float before = Credibility;
            Credibility = Mathf.Clamp01(Credibility + delta);
            Record(new ReputationChange(day, ReputationKind.Credibility, Credibility - before, reason));
        }

        private void Record(ReputationChange change)
        {
            if (Mathf.Approximately(change.Delta, 0f)) return;

            _history.Add(change);
            if (_history.Count > _settings.MaxHistoryEntries)
                _history.RemoveAt(0);

            OnChanged?.Invoke(change);
        }

        /// <summary>Newest first, at most <paramref name="count"/> changes of one kind.</summary>
        public List<ReputationChange> LatestChanges(ReputationKind kind, int count)
        {
            var result = new List<ReputationChange>();
            for (int i = _history.Count - 1; i >= 0 && result.Count < count; i--)
                if (_history[i].Kind == kind)
                    result.Add(_history[i]);
            return result;
        }

        // ---------------------------------------------------------------- tiers

        public IReadOnlyList<ReputationTier> Tiers(ReputationKind kind) =>
            kind == ReputationKind.Reputation ? _settings.ReputationTiers : _settings.CredibilityTiers;

        public float Value(ReputationKind kind) =>
            kind == ReputationKind.Reputation ? Reputation : Credibility;

        /// <summary>Index of the tier the current value falls in (0 if there are no tiers).</summary>
        public int TierIndex(ReputationKind kind) => TierIndexFor(kind, Value(kind));

        public int TierIndexFor(ReputationKind kind, float value)
        {
            var tiers = Tiers(kind);
            int index = 0;
            for (int i = 0; i < tiers.Count; i++)
                if (value >= tiers[i].MinValue)
                    index = i;
            return index;
        }

        public ReputationTier CurrentTier(ReputationKind kind)
        {
            var tiers = Tiers(kind);
            return tiers.Count > 0 ? tiers[TierIndex(kind)] : null;
        }

        /// <summary>The next tier up, or null at the top.</summary>
        public ReputationTier NextTier(ReputationKind kind)
        {
            var tiers = Tiers(kind);
            int next = TierIndex(kind) + 1;
            return next < tiers.Count ? tiers[next] : null;
        }

        /// <summary>0..1 progress from the current tier's minimum to the next tier's (1 at the top).</summary>
        public float ProgressToNextTier(ReputationKind kind)
        {
            var current = CurrentTier(kind);
            var next = NextTier(kind);
            if (current == null || next == null) return 1f;

            float span = next.MinValue - current.MinValue;
            return span > 0f ? Mathf.Clamp01((Value(kind) - current.MinValue) / span) : 1f;
        }

        /// <summary>Company title — the name of the current reputation tier.</summary>
        public string Title => CurrentTier(ReputationKind.Reputation)?.Name ?? "";

        // ---------------------------------------------------------------- save / load

        public ReputationState CaptureState() => new ReputationState
        {
            Reputation = Reputation,
            Credibility = Credibility,
            History = new List<ReputationChange>(_history)
        };

        public void RestoreState(ReputationState state)
        {
            if (state == null) return;

            Reputation = Mathf.Max(0, state.Reputation);
            Credibility = Mathf.Clamp01(state.Credibility);

            _history.Clear();
            if (state.History != null)
                _history.AddRange(state.History);
        }

    }
}
