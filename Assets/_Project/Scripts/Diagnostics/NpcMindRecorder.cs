using System.Collections.Generic;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Market;
using UnityEngine;

namespace AntiqueTradingSimulator.Diagnostics
{
    /// <summary>
    /// The three weekly tables an NPC's reasoning is read from: fact_outlook_week for what it
    /// believes, fact_position for what it holds on that, fact_belief_outcome for how it turned out.
    /// </summary>
    public sealed class NpcMindRecorder
    {
        /// <summary>Horizon the weekly outlook snapshot is taken over, in weeks.</summary>
        private const int SnapshotHorizonWeeks = 8;

        private readonly TelemetryRecorder _host;
        private readonly TelemetryRun _run;

        /// <summary>Keys of outcomes already written, so each is recorded once per trader per item.</summary>
        private readonly HashSet<string> _reported = new HashSet<string>(System.StringComparer.Ordinal);

        public NpcMindRecorder(TelemetryRecorder host)
        {
            _host = host;
            _run = host.Run;
        }

        // ---------------------------------------------------------------- fact_outlook_week

        /// <summary>
        /// One row per trader per antique type a belief of theirs names, so the table holds only
        /// the types a trader actually has a view on.
        /// </summary>
        public void OutlookWeek()
        {
            var table = _run.Table(T.FactOutlookWeekName, T.FactOutlookWeekHeader);
            var scratch = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (string traderId in _host.Traders.TraderIds)
            {
                var npc = _host.Traders.NpcOf(traderId);
                var view = npc != null ? npc.WorldView : null;
                var profile = npc != null ? npc.Profile : null;
                if (view == null || profile == null) continue;

                scratch.Clear();
                var beliefs = view.Beliefs;

                for (int i = 0; i < beliefs.Count; i++)
                {
                    var definitions = TraderWorldView.DefinitionsInScope(beliefs[i].Facet);
                    if (definitions == null) continue;

                    for (int k = 0; k < definitions.Count; k++)
                        scratch.Add(definitions[k].Id);
                }

                foreach (string definitionId in scratch)
                {
                    var outlook = view.OutlookFor(definitionId, SnapshotHorizonWeeks, profile);
                    var definition = AntiqueDatabase.GetById(definitionId);

                    _run.StampWeek(table)
                        .S(traderId)
                        .S(definitionId)
                        .E(definition != null ? (System.Enum)definition.Type : null)
                        .F(outlook.Expected)
                        .F(outlook.Confidence)
                        .F(outlook.Conviction)
                        .I(outlook.Supporting)
                        .I(outlook.Opposing)
                        .I(outlook.PeakWeek)
                        .I(outlook.FadeWeek)
                        .I(beliefs.Count)
                        .End();
                }
            }
        }

        // ---------------------------------------------------------------- fact_position

        /// <summary>One row per held antique that carries a cost basis, thesis or not.</summary>
        public void Positions()
        {
            var table = _run.Table(T.FactPositionName, T.FactPositionHeader);

            foreach (string traderId in _host.Traders.TraderIds)
            {
                var npc = _host.Traders.NpcOf(traderId);
                var inventory = _host.Traders.InventoryOf(traderId);
                var view = npc != null ? npc.WorldView : null;
                if (npc == null || inventory == null) continue;

                foreach (var pair in inventory.Holdings)
                {
                    var holding = pair.Value;
                    var position = npc.PositionFor(pair.Key);
                    if (holding == null || position == null) continue;

                    _run.StampWeek(table)
                        .S(traderId)
                        .S(pair.Key)
                        .S(holding.DefinitionId)
                        .E(position.Purpose)
                        .F(position.CostBasis)
                        .I(position.AcquiredWeek)
                        .F(position.EntryOutlook)
                        .F(position.TargetPrice > 0f ? position.TargetPrice : (float?)null)
                        .F(position.StopPrice > 0f ? position.StopPrice : (float?)null)
                        .I(position.ExpectedPeakWeek > 0 ? position.ExpectedPeakWeek : (int?)null)
                        .I(position.ExpectedFadeWeek > 0 ? position.ExpectedFadeWeek : (int?)null)
                        .S(Join(position.BasedOnNewsIds))
                        .S(SourceKinds(view, position.BasedOnNewsIds))
                        .I(position.DistressSince >= 0 ? position.DistressSince : (int?)null)
                        .F(holding.CurrentPrice)
                        .F(holding.CurrentPrice - position.CostBasis)
                        .B(holding.IsListedForSale)
                        .F(holding.IsListedForSale ? holding.AskingPrice : (float?)null)
                        .End();
                }
            }
        }

        // ---------------------------------------------------------------- fact_belief_outcome

        /// <summary>
        /// One row per belief whose event has run its course, the week the trader learns from it.
        /// A reported set keys on trader and item, so each belief is written exactly once.
        /// </summary>
        public void BeliefOutcomes()
        {
            var table = _run.Table(T.FactBeliefOutcomeName, T.FactBeliefOutcomeHeader);

            foreach (string traderId in _host.Traders.TraderIds)
            {
                var npc = _host.Traders.NpcOf(traderId);
                var view = npc != null ? npc.WorldView : null;
                if (view == null) continue;

                var beliefs = view.Beliefs;
                for (int i = 0; i < beliefs.Count; i++)
                {
                    var belief = beliefs[i];
                    if (!belief.Learned || belief.NewsId == null) continue;

                    string key = traderId + "/" + belief.NewsId + "/" + ScopeKey(belief);
                    if (!_reported.Add(key)) continue;

                    var estimate = view.Memory.For(belief.OfficialName, npc.Profile);
                    float realised = belief.RealisedMove;

                    _run.StampWeek(table)
                        .S(traderId)
                        .S(belief.NewsId)
                        .S(belief.OfficialName)
                        .E(belief.Source)
                        .I(belief.Direction)
                        .F(belief.EstimatedMove)
                        .F(belief.Spread)
                        .F(realised)
                        .B(realised * belief.Direction >= 0f)
                        .I(belief.StartWeek)
                        .I(belief.EndWeek)
                        .I(belief.HeardWeek)
                        .I(Mathf.Max(0, estimate.Observations - 1))
                        .F(belief.Trust)
                        .End();
                }
            }
        }

        // ---------------------------------------------------------------- helpers

        private static string ScopeKey(Belief belief)
        {
            var facet = belief.Facet;
            if (facet == null) return "";
            return facet.targetScope + ":" + facet.AntiqueType + ":" + facet.Country + ":" + facet.Century;
        }

        private static string Join(string[] ids) =>
            ids != null && ids.Length > 0 ? string.Join(" ", ids) : null;

        /// <summary>
        /// The kinds of source a thesis rests on, so corroborated leaks and lone rumours can be
        /// compared without a join onto the belief table.
        /// </summary>
        private static string SourceKinds(TraderWorldView view, string[] newsIds)
        {
            if (view == null || newsIds == null || newsIds.Length == 0) return null;

            var kinds = new SortedSet<string>(System.StringComparer.Ordinal);
            var beliefs = view.Beliefs;

            for (int i = 0; i < beliefs.Count; i++)
            {
                for (int k = 0; k < newsIds.Length; k++)
                    if (beliefs[i].NewsId == newsIds[k]) kinds.Add(beliefs[i].Source.ToString());
            }

            return kinds.Count > 0 ? string.Join(" ", kinds) : null;
        }
    }
}
