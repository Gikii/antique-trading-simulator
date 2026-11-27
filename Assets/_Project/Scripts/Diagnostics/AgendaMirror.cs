using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Market;
using UnityEngine;

namespace AntiqueTradingSimulator.Diagnostics
{
    /// <summary>
    /// Writes fact_agenda_item from each trader's agenda, on the week change: the slots are spent
    /// by then and the next Review has not cleared it, so executed and skipped are both known.
    /// </summary>
    public sealed class AgendaMirror
    {
        private readonly TelemetryRecorder _host;
        private readonly TelemetryRun _run;

        public AgendaMirror(TelemetryRecorder host)
        {
            _host = host;
            _run = host.Run;
        }

        /// <summary>One row per agenda item per trader, for the week just ended.</summary>
        public void Emit(int week)
        {
            var table = _run.Table(T.FactAgendaItemName, T.FactAgendaItemHeader);

            foreach (string traderId in _host.Traders.TraderIds)
            {
                var npc = _host.Traders.NpcOf(traderId);
                var agenda = npc != null ? npc.Agenda : null;
                if (agenda == null) continue;

                for (int rank = 0; rank < agenda.Count; rank++)
                {
                    var item = agenda[rank];
                    if (item == null) continue;

                    _run.StampWeek(table)
                        .S(traderId)
                        .I(rank)
                        .E(item.Intent)
                        .I(TraderRecorder.TierRank(item.Intent.ToString()))
                        .S(item.TargetId)
                        .S(KindOf(npc, item))
                        .F(item.Conviction)
                        .F(item.BudgetCeiling)
                        .I(item.ValidUntilWeek)
                        .S(item.Rationale)
                        .B(item.Executed && item.SkippedReason == null)
                        .I(item.ExecutedTick >= 0 ? item.ExecutedTick : (int?)null)
                        .S(item.SkippedReason)
                        .E(item.Reason != DisposalReason.None ? (System.Enum)item.Reason : null)
                        .End();
                }
            }
        }

        /// <summary>
        /// What the target id names: an owned antique, an antique type, a contract, or nothing.
        /// </summary>
        private static string KindOf(NPCTrader npc, AgendaItem item)
        {
            if (string.IsNullOrEmpty(item.TargetId)) return "None";
            if (npc != null && npc.Inventory != null && npc.Inventory.Owns(item.TargetId)) return "Antique";
            return AntiqueDatabase.GetById(item.TargetId) != null ? "Definition" : "Contract";
        }
    }
}
