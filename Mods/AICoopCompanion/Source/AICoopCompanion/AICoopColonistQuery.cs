using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace AICoopCompanion
{
    internal static class AICoopColonistQuery
    {
        internal static string Build(int? pawnId = null)
        {
            var pawns = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists
                .Where(p => p != null && (!pawnId.HasValue || p.thingIDNumber == pawnId.Value))
                .Distinct().OrderBy(p => p.thingIDNumber).ToList();
            if (pawnId.HasValue && pawns.Count == 0) throw new ArgumentException("找不到殖民者ID=" + pawnId.Value);
            var result = new StringBuilder("COLONIST_STATUS readonly=1 tick=" + (Find.TickManager == null ? 0 : Find.TickManager.TicksGame) +
                " count=" + pawns.Count + " percentages=0..100（food越低越饥饿）");
            foreach (Pawn pawn in pawns)
            {
                var caravan = pawn.GetCaravan();
                result.Append("\nPAWN id=").Append(pawn.thingIDNumber).Append(" name=").Append(Clean(pawn.LabelShort))
                    .Append(" owner=").Append(AICoopGameComponent.Current.GetOwner(pawn))
                    .Append(" location=").Append(pawn.Map != null ? "map:" + pawn.Map.uniqueID : caravan != null ? "caravan:" + caravan.ID : "world/transport")
                    .Append(" pos=").Append(pawn.Spawned ? pawn.Position.x + "," + pawn.Position.z : "-")
                    .Append(" downed=").Append(pawn.Downed ? 1 : 0)
                    .Append(" mental=").Append(pawn.InMentalState ? Clean(pawn.MentalStateDef.LabelCap) : "正常")
                    .Append(" job=").Append(pawn.CurJobDef == null ? "-" : Clean(pawn.CurJobDef.defName))
                    .Append(" health=").Append(pawn.health == null ? "-" : Percent(pawn.health.summaryHealth.SummaryHealthPercent))
                    .Append(" mood=").Append(NeedValue(pawn.needs?.mood))
                    .Append(" food=").Append(NeedValue(pawn.needs?.food))
                    .Append(" hunger=").Append(pawn.needs?.food == null ? "-" : pawn.needs.food.CurCategory.ToString())
                    .Append(" rest=").Append(NeedValue(pawn.needs?.rest))
                    .Append(" joy=").Append(NeedValue(pawn.needs?.joy));
                var thoughts = new List<Thought>();
                if (pawn.needs?.mood?.thoughts != null) pawn.needs.mood.thoughts.GetDistinctMoodThoughtGroups(thoughts);
                var negative = thoughts.Where(t => t != null && t.MoodOffset() < 0).OrderBy(t => t.MoodOffset()).ToList();
                result.Append("\nMOOD_CAUSES ").Append(pawn.thingIDNumber).Append(" ")
                    .Append(string.Join(" | ", negative.Take(5).Select(t => Clean(t.LabelCap) + ":" + t.MoodOffset().ToString("0.#", CultureInfo.InvariantCulture)).ToArray()));
                if (negative.Count > 5) result.Append(" omitted=").Append(negative.Count - 5);
                result.Append("\nHEALTH ").Append(pawn.thingIDNumber).Append(" ");
                if (pawn.health?.hediffSet == null || pawn.health.hediffSet.hediffs.Count == 0) result.Append("无伤病");
                else foreach (var h in pawn.health.hediffSet.hediffs)
                    result.Append("[").Append(Clean(h.LabelCap)).Append(" part=").Append(h.Part == null ? "全身" : Clean(h.Part.Label))
                        .Append(" severity=").Append(h.Severity.ToString("0.###", CultureInfo.InvariantCulture))
                        .Append(" tendable=").Append(h.TendableNow() ? 1 : 0).Append("]");
            }
            return result.ToString();
        }

        private static string Percent(float value) { return (Math.Round(value * 100, 1)).ToString(CultureInfo.InvariantCulture); }
        private static string NeedValue(Need need) { return need == null ? "-" : Percent(need.CurLevelPercentage); }
        private static string Clean(string value) { return (value ?? "-").Replace('\n', ' ').Replace('\r', ' '); }
    }
}
