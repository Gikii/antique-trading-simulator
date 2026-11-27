using AntiqueTradingSimulator.Auctions;
using AntiqueTradingSimulator.Company;
using AntiqueTradingSimulator.Contracts;
using AntiqueTradingSimulator.Economy;
using AntiqueTradingSimulator.Events;
using AntiqueTradingSimulator.Logistics;
using AntiqueTradingSimulator.Market;
using AntiqueTradingSimulator.News;
using AntiqueTradingSimulator.Restoration;
using AntiqueTradingSimulator.Rumours;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;
using static AntiqueTradingSimulator.Events.EventEffect;
using static AntiqueTradingSimulator.Market.AntiqueEnums;

namespace AntiqueTradingSimulator.Agents
{
    /// <summary>
    /// Taking news in: what this trader believes, and how sure it is of the source.
    /// One part of NPCTrader, which is a partial class.
    /// </summary>
    public partial class NPCTrader
    {
        // ---------------------------------------------------------------- receiving nes

        public void ReceiveNews(NewsItem news, int deliveryWeek)
        {
            var profile = Profile;
            if (profile == null || _economyManager == null || _economyManager.TimeManager == null) return;

            if (news?.NewsData == null || news.NewsData.Count == 0) return;

            float baseTrust = news.Type switch
            {
                NewsType.Official => 1f,
                NewsType.Leak => profile.LeakTrust,
                NewsType.Rumor or NewsType.Fake => profile.RumorTrust,
            };

            float trust = news.Type is NewsType.Rumor or NewsType.Fake
                ? baseTrust * (1f - RumourMill.CurrentSkepticism)
                : baseTrust;

            var rng = ReceptionRng(news.Id, deliveryWeek);

            float expertChance = float.NaN;
            if (news.Type == NewsType.Fake && AccessLevel >= InfoAccessLevel.Expert)
            {
                expertChance = FakeRumourSettings.Active.ExpertDetection * (1f - news.Credibility);
                if (rng.NextDouble() < expertChance)
                {
                    Debug.Log($"Trader {TraderName} spotted the fake rumour '{news.OfficialName}'.");
                    Diagnostics.Telemetry.NewsReception(this, news, deliveryWeek, "DetectedFake",
                        baseTrust, trust, float.NaN, float.NaN, expertChance, -1, -1);
                    return;
                }
            }

            // Belief is a probability; how hard the trader acts on it is decided by the outlook.
            float actionChance = Mathf.Clamp01(trust * news.Credibility);
            float trustRoll = (float)rng.NextDouble();

            if (trustRoll > actionChance)
            {
                Debug.Log($"Trader {TraderName} dismissed the {news.Type} news from Week {news.WeekPublished} based on actionChance");
                Diagnostics.Telemetry.NewsReception(this, news, deliveryWeek, "DismissedByTrust",
                    baseTrust, trust, actionChance, trustRoll, expertChance, -1, -1);
                return;
            }

            // The reaction delay is how long this trader takes to make up its mind: the belief is
            // recorded at once and carries no weight in the outlook until that week.
            int delay = rng.Next(profile.MinReactionDelayWeeks, profile.MaxReactionDelayWeeks + 1);
            int actsFromWeek = Mathf.Max(deliveryWeek, _economyManager.TimeManager.CurrentWeek) + delay;

            var outlookBefore = OutlookAcross(news, profile);

            // Believed, so it becomes part of what this trader thinks the market is about to do.
            WorldView?.Add(news, deliveryWeek, trust, profile, actsFromWeek);

            var outlookAfter = OutlookAcross(news, profile);

            Diagnostics.Telemetry.NewsReception(this, news, deliveryWeek, "Believed",
                baseTrust, trust, actionChance, trustRoll, expertChance, delay, actsFromWeek,
                WorldView != null ? WorldView.LastEstimatedMove : 0f,
                WorldView != null ? WorldView.LastSpread : 0f,
                outlookBefore, outlookAfter);
        }

        /// <summary>
        /// Mean conviction across the antique types one news item names, for the reception record:
        /// it says whether the item changed this trader's mind, and by how much.
        /// </summary>
        private float OutlookAcross(NewsItem news, NpcBehaviorProfile profile)
        {
            if (WorldView == null || news?.NewsData == null) return 0f;

            float total = 0f;
            int counted = 0;

            for (int i = 0; i < news.NewsData.Count; i++)
            {
                var definitions = TraderWorldView.DefinitionsInScope(news.NewsData[i]);
                if (definitions == null) continue;

                for (int k = 0; k < definitions.Count; k++)
                {
                    total += WorldView.OutlookFor(definitions[k].Id, HoldWeeks(profile), profile).Conviction;
                    counted++;
                }
            }

            return counted > 0 ? total / counted : 0f;
        }

        /// <summary>Drops the beliefs resting on a news item (a debunked rumour).</summary>
        public void ForgetNews(string newsId)
        {
            if (string.IsNullOrEmpty(newsId)) return;

            int removed = WorldView != null ? WorldView.Forget(newsId) : 0;
            if (removed > 0) Debug.Log($"Trader {TraderName} dropped {removed} belief(s) in debunked news.");
        }

        /// <summary>
        /// Writes the thesis behind a rumour this NPC planted onto its matching holdings: the move
        /// peaks at fromWeek and the fade is the debunk at claimWeek. Called by RumourMill.
        /// </summary>
        public void BeginRumourExit(IReadOnlyList<NewsEventData> scope, int fromWeek, int claimWeek, float pumpMultiple = 1f)
        {
            if (scope == null) return;

            var profile = Profile;
            if (profile == null) return;

            _scopeScratch.Clear();
            foreach (var data in scope)
                if (data != null && data.targetScope != TargetScope.Other)
                    _scopeScratch.Add(data);

            if (_scopeScratch.Count == 0) return;

            int week = CurrentWeek;
            int lead = Mathf.Max(1, profile.ExitDumpLeadWeeks);

            foreach (var pair in Inventory.Holdings)
            {
                var holding = pair.Value;
                if (holding == null || holding.IsReservedForContract) continue;
                if (!MatchesAny(holding, _scopeScratch)) continue;

                var plan = PlanForOrCreate(holding, week);
                if (plan.HasRestorationLeg) continue;

                plan.Purpose = PlanPurpose.NewsThesis;
                plan.ContractId = null;
                plan.EntryOutlook = Mathf.Max(0f, pumpMultiple - 1f) * Mathf.Clamp01(profile.ExitPumpConfidence);
                plan.ExpectedPeakWeek = Mathf.Max(week, fromWeek);
                plan.ExpectedFadeWeek = claimWeek;

                // DumpAfterWeek marks the fade as a hard deadline, so the exit sells to the market
                // from that week rather than listing and hoping.
                plan.DumpAfterWeek = Mathf.Max(fromWeek + 1, claimWeek - lead);

                plan.TargetPrice = ExitAskingPrice(holding, pumpMultiple, profile);

                // The planter holds no belief in its own rumour, so there is nothing to invalidate
                // the thesis and no stop to be wrong against.
                plan.BasedOnNewsIds = null;
                plan.StopPrice = 0f;
                plan.DistressSince = -1;
            }

            _scopeScratch.Clear();
        }

        private float ExitAskingPrice(Antique antique, float pumpMultiple, NpcBehaviorProfile profile)
        {
            float believed = 1f + (Mathf.Max(1f, pumpMultiple) - 1f) * Mathf.Clamp01(profile.ExitPumpConfidence);
            float asking = Core.Money.RoundToNice(antique.CurrentPrice * believed * Mathf.Max(1f, profile.ExitTargetRatio));

            float floor = antique.CurrentPrice / Mathf.Max(0.01f, 1f - Inventory.Modifiers.ListingFeeRate);

            return asking > floor ? asking : 0f;
        }

        private static bool MatchesAny(Antique antique, List<NewsEventData> scopes)
        {
            for (int i = 0; i < scopes.Count; i++)
                if (MatchesScope(antique, scopes[i])) return true;
            return false;
        }
    }
}
