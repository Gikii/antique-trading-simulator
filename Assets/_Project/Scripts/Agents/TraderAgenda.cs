using System.Collections.Generic;
using UnityEngine;

namespace AntiqueTradingSimulator.Agents
{
    /// <summary>What a trader means to do with one of its action slots.</summary>
    public enum Intent
    {
        /// <summary>A contract is short of pieces and the deadline is close.</summary>
        FillContract,
        /// <summary>A holding has reached its target, its stop or its fade.</summary>
        ExitPosition,
        /// <summary>A workshop stay or a planned contract step is due.</summary>
        ExecutePlan,
        /// <summary>The outlook says a facet is cheap; buy into it.</summary>
        OpenPosition,
        /// <summary>This listing is underpriced on its own merits.</summary>
        TakeBargain,
        /// <summary>Restoration pays on a piece already held.</summary>
        SendToWorkshop,
        /// <summary>A contract on the board is worth taking.</summary>
        TakeContract,
        /// <summary>Cash is short of a committed cost.</summary>
        RaiseCash,
        /// <summary>The warehouse is at its ceiling.</summary>
        FreeSpace,
        /// <summary>More capacity is worth buying.</summary>
        Invest,
        PlantRumour
    }

    /// <summary>One intention from the weekly review, with what justified it and what it may spend.</summary>
    public class AgendaItem
    {
        public Intent Intent;

        /// <summary>Antique, contract or definition the intention is about. May be null.</summary>
        public string TargetId;

        /// <summary>Most this item may spend, or 0 when it spends nothing.</summary>
        public float BudgetCeiling;

        /// <summary>Last week the intention still makes sense.</summary>
        public int ValidUntilWeek;

        /// <summary>The expectation behind it, for sizing and for the record.</summary>
        public float Conviction;

        /// <summary>Why the trader decided this, in one line, written where the decision was made.</summary>
        public string Rationale;

        /// <summary>Why it was not carried out, when a slot came to it and passed.</summary>
        public string SkippedReason;

        public bool Executed;

        /// <summary>Tick the slot that carried it out ran on; -1 until one does.</summary>
        public int ExecutedTick = -1;

        /// <summary>Set when a disposal item names the reason the holding is being sold.</summary>
        public DisposalReason Reason;
    }

    /// <summary>
    /// The week's intentions in the order they should be attempted. Built once a week with every
    /// candidate in view, then spent by the action slots.
    /// </summary>
    public class TraderAgenda
    {
        private readonly List<AgendaItem> _items = new List<AgendaItem>();

        public IReadOnlyList<AgendaItem> Items => _items;
        public int Count => _items.Count;

        public void Clear() => _items.Clear();

        public AgendaItem Add(Intent intent, string targetId, float budgetCeiling, int validUntilWeek,
            float conviction, string rationale, DisposalReason reason = DisposalReason.None)
        {
            var item = new AgendaItem
            {
                Intent = intent,
                TargetId = targetId,
                BudgetCeiling = budgetCeiling,
                ValidUntilWeek = validUntilWeek,
                Conviction = conviction,
                Rationale = rationale,
                Reason = reason
            };

            _items.Add(item);
            return item;
        }

        /// <summary>
        /// Puts obligations first, then the strongest conviction, then the target id, so two items
        /// of equal weight never swap order between runs.
        /// </summary>
        public void Sort()
        {
            _items.Sort((a, b) =>
            {
                int byIntent = RankOf(a.Intent).CompareTo(RankOf(b.Intent));
                if (byIntent != 0) return byIntent;

                int byConviction = b.Conviction.CompareTo(a.Conviction);
                if (byConviction != 0) return byConviction;

                return string.CompareOrdinal(a.TargetId ?? string.Empty, b.TargetId ?? string.Empty);
            });
        }

        /// <summary>The next item still worth attempting this week, or null.</summary>
        public AgendaItem NextFor(int week)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                var item = _items[i];
                if (item.Executed) continue;
                if (item.ValidUntilWeek > 0 && week > item.ValidUntilWeek) continue;
                return item;
            }

            return null;
        }

        /// <summary>Drops an item once it has been carried out or has turned out impossible.</summary>
        public void Retire(AgendaItem item, bool executed, string skippedReason = null, int tick = -1)
        {
            if (item == null) return;

            item.Executed = true;
            if (executed) item.ExecutedTick = tick;
            else item.SkippedReason = skippedReason;
        }

        /// <summary>Removes every item about one antique, once it has left the inventory.</summary>
        public void DropTarget(string targetId)
        {
            if (string.IsNullOrEmpty(targetId)) return;
            _items.RemoveAll(i => i.TargetId == targetId);
        }

        private static int RankOf(Intent intent) => intent switch
        {
            Intent.FillContract => 0,
            Intent.ExecutePlan => 1,
            Intent.RaiseCash => 2,
            Intent.FreeSpace => 3,
            Intent.ExitPosition => 4,
            Intent.TakeContract => 5,
            Intent.OpenPosition => 6,
            Intent.TakeBargain => 7,
            Intent.SendToWorkshop => 8,
            Intent.Invest => 9,
            _ => 10
        };
    }
}
