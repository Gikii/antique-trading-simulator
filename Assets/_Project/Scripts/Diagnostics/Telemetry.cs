using System;
using System.Diagnostics;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.News;

namespace AntiqueTradingSimulator.Diagnostics
{
    /// <summary>
    /// The only part of the recorder that game code calls.
    ///
    /// Three layers keep recording from being able to change the game:
    ///
    /// 1. Every method carries [Conditional("ATS_DIAGNOSTICS")]. Without that define the compiler
    ///    removes the call sites outright — no branch, no static read, no argument evaluation.
    /// 2. With the define but recording off, the recorder never installs, so each call returns on
    ///    one null check.
    /// 3. Every body is wrapped. Nothing thrown inside the recorder can reach the caller, so a
    ///    recorder bug cannot break the simulation even while recording.
    ///
    /// Because arguments are not evaluated under rule 1, never put a call the game needs inside
    /// one of these calls. Compute it into a local and pass the local.
    /// </summary>
    public static class Telemetry
    {
        /// <summary>
        /// False disables recording for the whole process: the recorder never installs and never
        /// subscribes, so nothing is observed. Set before the game scene loads.
        /// </summary>
        public static bool Enabled = true;

        /// <summary>
        /// False skips fact_browse_funnel and fact_contract_eligibility_week. Those two replay the
        /// NPC decision gates over every listing, which is the recorder's only real per-tick cost;
        /// the rest is reads and diffs. Turn it off with -noDiagnosticsMirrors.
        /// </summary>
        public static bool Mirrors = true;

        /// <summary>Set when a hook threw, so the fault is visible even if the error table was lost.</summary>
        public static int FacadeFaults { get; private set; }

        [Conditional("ATS_DIAGNOSTICS")]
        public static void NpcActionBegin(NPCTrader npc)
        {
            try
            {
                var recorder = TelemetryRecorder.Instance;
                if (recorder == null) return;
                recorder.Traders.NpcActionBegin(npc);
            }
            catch (Exception e)
            {
                Fault(nameof(NpcActionBegin), e);
            }
        }

        /// <param name="tier">Name of the ladder rung that acted, or null when none did.</param>
        [Conditional("ATS_DIAGNOSTICS")]
        public static void NpcActionEnd(NPCTrader npc, int week, AgendaItem item)
        {
            try
            {
                var recorder = TelemetryRecorder.Instance;
                if (recorder == null) return;
                recorder.Traders.NpcActionEnd(npc, week, item);
            }
            catch (Exception e)
            {
                Fault(nameof(NpcActionEnd), e);
            }
        }

        /// <summary>Why a trader is parting with a holding, noted before the sale that carries it out.</summary>
        [Conditional("ATS_DIAGNOSTICS")]
        public static void Disposal(string antiqueId, DisposalReason reason)
        {
            try
            {
                var recorder = TelemetryRecorder.Instance;
                if (recorder == null) return;
                recorder.MarketLog.NoteDisposal(antiqueId, reason);
            }
            catch (Exception e)
            {
                Fault(nameof(Disposal), e);
            }
        }

        /// <summary>
        /// An agenda item a slot came to and could not carry out, counted for the per-slot record.
        /// The item's own SkippedReason carries why.
        /// </summary>
        [Conditional("ATS_DIAGNOSTICS")]
        public static void TierDeclined(NPCTrader npc, string tier)
        {
            try
            {
                var recorder = TelemetryRecorder.Instance;
                if (recorder == null) return;
                recorder.Traders.TierDeclined(npc, tier);
            }
            catch (Exception e)
            {
                Fault(nameof(TierDeclined), e);
            }
        }

        /// <param name="outcome">
        /// Queued, DetectedFake, DismissedByTrust, DismissedTooLate or NoTradableData.
        /// </param>
        [Conditional("ATS_DIAGNOSTICS")]
        public static void NewsReception(IInformationReceiver receiver, NewsItem news, int deliveryWeek,
            string outcome, float baseTrust, float effectiveTrust, float actionChance, float trustRoll,
            float expertChance, int delayWeeks, int actsFromWeek, float estimatedMove = 0f,
            float estimateSpread = 0f, float outlookBefore = 0f, float outlookAfter = 0f)
        {
            try
            {
                var recorder = TelemetryRecorder.Instance;
                if (recorder == null) return;
                recorder.WorldLog.NewsReception(receiver, news, deliveryWeek, outcome, baseTrust,
                    effectiveTrust, actionChance, trustRoll, expertChance, delayWeeks, actsFromWeek,
                    estimatedMove, estimateSpread, outlookBefore, outlookAfter);
            }
            catch (Exception e)
            {
                Fault(nameof(NewsReception), e);
            }
        }

        private static void Fault(string hook, Exception e)
        {
            FacadeFaults++;
            if (FacadeFaults == 1)
                UnityEngine.Debug.LogWarning(
                    $"Telemetry: {hook} threw and was swallowed — {e.GetType().Name}: {e.Message}. " +
                    "The game is unaffected; recording may be incomplete.");
        }
    }
}
