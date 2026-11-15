using System.Collections.Generic;
using System.Linq;
using AntiqueTradingSimulator.Agents;
using AntiqueTradingSimulator.Events;
using AntiqueTradingSimulator.Market;
using UnityEngine;

namespace AntiqueTradingSimulator.Rumours
{
    /// <summary>
    /// Works out whether a fake rumour is worth planting for one NPC, and what it should claim.
    /// Decides nothing and changes nothing: NPCTrader.TierPlantRumour asks for a plan on its own
    /// action slot and RumourMill.PlantFor carries it out.
    ///
    /// A rumour pays off as a pump and dump, so the claim is built out of what the NPC already holds:
    /// the attribute values covering the most of its free stock, talked up. The expected rise on those
    /// holdings must beat the cost by NpcMinReturnOnCost.
    /// </summary>
    public static class NpcRumourPlanner
    {
        /// <summary>One attribute value the NPC could name, and the holdings it would cover.</summary>
        private struct Option
        {
            public System.Action<RumourClaim> Apply;
            public List<Antique> Covered;
        }

        /// <summary>Best claim and spend for this NPC, or false when nothing is worth its cost.</summary>
        public static bool TryFindPlan(RumourMill mill, NPCTrader npc, int week, out RumourQuote best)
        {
            best = null;
            if (mill == null || npc == null) return false;

            var s = mill.Settings;

            var holdings = npc.Inventory.Holdings.Values
                .Where(h => h != null && !h.IsReservedForContract && !h.IsInTransit && !h.IsInRestoration)
                .ToList();

            if (holdings.Count < s.NpcMinHoldings) return false;

            var options = RankedOptions(holdings, s.NpcMinHoldings);
            if (options.Count == 0) return false;

            float cashLimit = npc.Inventory.Cash * s.NpcMaxCashShare;
            float bestNet = 0f;

            // Narrow claims first: one value, then the two best together, and so on. A wider claim
            // covers more of its stock but costs more, so both are tried and the best net wins.
            int maxValues = Mathf.Min(s.MaxTotalValues, options.Count);

            for (int count = 1; count <= maxValues; count++)
            {
                var claim = new RumourClaim(RumourDirection.Rise);
                var covered = new HashSet<string>();

                for (int i = 0; i < count; i++)
                {
                    options[i].Apply(claim);
                    foreach (var antique in options[i].Covered) covered.Add(antique.Id);
                }

                float heldValue = holdings.Where(h => covered.Contains(h.Id)).Sum(h => h.CurrentPrice);
                if (covered.Count < s.NpcMinHoldings) continue;

                foreach (float multiple in s.NpcSpendOptionsInK ?? new float[0])
                {
                    var quote = Quote(mill, claim, npc.Id, week, multiple);
                    if (quote == null || quote.Spend > cashLimit) continue;

                    float gain = heldValue * s.NpcPriceImpactPerReactor * Mathf.Min(quote.ExpectedReactors, covered.Count);
                    float net = gain - quote.Spend;
                    if (net < quote.Spend * s.NpcMinReturnOnCost) continue;

                    if (best == null || net > bestNet)
                    {
                        best = quote;
                        bestNet = net;
                    }
                }
            }

            if (best != null)
                Debug.Log($"NpcRumourPlanner: {npc.TraderName} plants '{best.Claim.Summary()}' " +
                          $"(spend {best.Spend:F0}, credibility {best.Credibility:P0}, expected net {bestNet:F0}).");

            return best != null;
        }

        /// <summary>
        /// Every attribute value covering at least <paramref name="minHoldings"/> of the stock, the one
        /// covering the most first. An NPC never claims something it holds nothing of.
        /// </summary>
        private static List<Option> RankedOptions(List<Antique> holdings, int minHoldings)
        {
            var options = new List<Option>();

            foreach (var group in holdings.GroupBy(h => h.Type))
            {
                var value = group.Key;
                options.Add(new Option { Apply = c => c.Toggle(value), Covered = group.ToList() });
            }

            foreach (var group in holdings.GroupBy(h => h.Country))
            {
                var value = group.Key;
                options.Add(new Option { Apply = c => c.Toggle(value), Covered = group.ToList() });
            }

            foreach (var group in holdings.GroupBy(h => h.Century))
            {
                var value = group.Key;
                options.Add(new Option { Apply = c => c.Toggle(value), Covered = group.ToList() });
            }

            return options
                .Where(o => o.Covered.Count >= Mathf.Min(minHoldings, 1))
                .OrderByDescending(o => o.Covered.Sum(a => a.CurrentPrice))
                .ToList();
        }

        private static RumourQuote Quote(RumourMill mill, RumourClaim claim, string planterId, int week, float spendInK)
        {
            var s = mill.Settings;

            var quote = new RumourQuote
            {
                Claim = claim.Copy(),
                PlantWeek = week,
                SpreadWeek = week + s.NpcSpreadDelayWeeks
            };
            quote.ClaimWeek = quote.SpreadWeek + s.NpcClaimLeadWeeks;

            if (mill.Clock != null && quote.ClaimWeek > mill.Clock.CampaignLength) return null;

            // NPCs work at a fixed level: they have no Information Network upgrade.
            const InfoAccessLevel level = InfoAccessLevel.InformantNetwork;

            // Filled once to learn K, then again with the spend this option asks for.
            mill.Fill(quote, 0f, s.NpcCredibilityCap, planterId, level);
            if (quote.CostScale <= 0f) return null;

            mill.Fill(quote, spendInK * quote.CostScale, s.NpcCredibilityCap, planterId, level);
            return quote;
        }
    }
}
