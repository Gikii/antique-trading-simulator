using System;
using System.Collections.Generic;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Core;
using UnityEngine;

namespace AntiqueTradingSimulator.Company
{
    public enum CampaignPhase
    {
        Running,
        Finished
    }

    /// <summary>One trader's place in the final ranking, frozen when the campaign ended.</summary>
    public class FinalStanding
    {
        public int Place;
        public string Id;
        public string Name;
        public bool IsPlayer;
        public CompanySnapshot Snapshot;
        public float Score;
    }

    public class CampaignState
    {
        public CampaignPhase Phase;
        public int FinishedOnDay;
        public List<FinalStanding> Ranking = new List<FinalStanding>();
    }

    /// <summary>
    /// Ends the campaign when its last day is over: the ranking of every trader by campaign score
    /// (CompanyManager.GetRanking — same rules for the player and the NPCs) is frozen, the clock
    /// stops and <see cref="OnCampaignFinished"/> tells the end screen. Play may go on afterwards
    /// (sandbox); the frozen result stays, is saved, and the campaign never ends twice.
    /// </summary>
    public class CampaignManager : MonoBehaviour
    {
        [Header("References (found automatically if empty)")]
        [SerializeField] private TimeManager timeManager;
        [SerializeField] private CompanyManager company;

        private readonly List<FinalStanding> _ranking = new List<FinalStanding>();

        public CampaignPhase Phase { get; private set; } = CampaignPhase.Running;

        public bool IsFinished => Phase == CampaignPhase.Finished;

        /// <summary>The last day of the campaign (0 while it runs).</summary>
        public int FinishedOnDay { get; private set; }

        /// <summary>Best first. Empty while the campaign runs.</summary>
        public IReadOnlyList<FinalStanding> FinalRanking => _ranking;

        public FinalStanding Winner => _ranking.Count > 0 ? _ranking[0] : null;

        public FinalStanding PlayerStanding => _ranking.Find(s => s.IsPlayer);

        public bool PlayerWon => PlayerStanding != null && PlayerStanding.Place == 1;

        /// <summary>
        /// The player's company. Looked up when first needed, not in Awake: PlayerTrader adds the
        /// CompanyManager in its own Awake, which may run after this one.
        /// </summary>
        public CompanyManager Company
        {
            get
            {
                if (company != null) return company;

                company = FindFirstObjectByType<CompanyManager>();
                if (company == null)
                {
                    var player = FindFirstObjectByType<PlayerTrader>();
                    if (player != null) company = player.Company;
                }
                return company;
            }
        }

        public int CampaignLength => timeManager != null ? timeManager.CampaignLength : 0;

        /// <summary>Raised once, when the campaign ends (not when a finished game is loaded).</summary>
        public event Action OnCampaignFinished;

        private void Awake()
        {
            if (timeManager == null) timeManager = FindFirstObjectByType<TimeManager>();
            // The company is resolved later (see Company): it may not exist yet.
        }

        private void OnEnable()
        {
            if (timeManager != null) timeManager.OnDayChanged += HandleDayChanged;
        }

        private void OnDisable()
        {
            if (timeManager != null) timeManager.OnDayChanged -= HandleDayChanged;
        }

        // Day CampaignLength + 1 starting means the last campaign day is over.
        private void HandleDayChanged(int newDay)
        {
            if (!IsFinished && timeManager != null && newDay > timeManager.CampaignLength)
                Finish();
        }

        /// <summary>Freezes the ranking, stops the clock and reports the end. Does nothing if already finished.</summary>
        public void Finish()
        {
            if (IsFinished) return;

            var playerCompany = Company;
            if (playerCompany == null)
            {
                Debug.LogError("CampaignManager: no CompanyManager found (it lives on the player, next to PlayerTrader) — the campaign can't be ranked.", this);
                return;
            }

            _ranking.Clear();
            var ranking = playerCompany.GetRanking();
            for (int i = 0; i < ranking.Count; i++)
            {
                var entry = ranking[i];
                _ranking.Add(new FinalStanding
                {
                    Place = i + 1,
                    Id = entry.Id,
                    Name = entry.Name,
                    IsPlayer = entry.IsPlayer,
                    Snapshot = entry.Snapshot,
                    Score = entry.Score
                });
            }

            Phase = CampaignPhase.Finished;
            FinishedOnDay = timeManager != null ? Mathf.Min(timeManager.CurrentDay, timeManager.CampaignLength) : 0;
            if (timeManager != null && timeManager.IsRunning) timeManager.Pause();

            var player = PlayerStanding;
            Debug.Log($"CampaignManager: campaign over on day {FinishedOnDay}. Winner: {Winner?.Name ?? "nobody"}" +
                      (player != null ? $"; the player is {player.Place} of {_ranking.Count} with {player.Score:0}." : "."));

            OnCampaignFinished?.Invoke();
        }

        // ---------------------------------------------------------------- save / load

        public CampaignState CaptureState() => new CampaignState
        {
            Phase = Phase,
            FinishedOnDay = FinishedOnDay,
            Ranking = new List<FinalStanding>(_ranking)
        };

        /// <summary>A null state (a save from before campaigns ended) means the campaign is still running.</summary>
        public void RestoreState(CampaignState state)
        {
            _ranking.Clear();
            Phase = state?.Phase ?? CampaignPhase.Running;
            FinishedOnDay = state?.FinishedOnDay ?? 0;
            if (state?.Ranking != null) _ranking.AddRange(state.Ranking);
        }

#if UNITY_EDITOR
        // Inspector → CampaignManager → ⋮ menu, in Play mode: test the end without playing 90 days.
        [ContextMenu("Debug/Finish campaign now")]
        private void DebugFinish() => Finish();
#endif
    }
}
