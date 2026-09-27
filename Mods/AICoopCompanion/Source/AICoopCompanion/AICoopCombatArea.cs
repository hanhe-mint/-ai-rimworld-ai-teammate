using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace AICoopCompanion
{
    public sealed class Area_AICoopCombat : Area
    {
        public Area_AICoopCombat() { }
        public Area_AICoopCombat(AreaManager manager) : base(manager) { }
        public override string Label { get { return "AI战斗区"; } }
        public override Color Color { get { return new Color(0.55f, 0.04f, 0.08f); } }
        public override int ListPriority { get { return 250; } }
        public override bool AssignableAsAllowed() { return false; }
        public override string GetUniqueLoadID() { return "AICoopCombat_" + ID; }
    }

    public class Designator_AICoopCombat : Designator_Cells
    {
        protected bool erase;
        public Designator_AICoopCombat()
        {
            defaultLabel = "AI战斗区";
            defaultDesc = "划定AI殖民者征召后的战斗活动范围。未划定或全部删除时不限制；区外人员先返回，远程可从区内攻击区外目标。";
            icon = ContentFinder<Texture2D>.Get("AICombatArea");
            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            soundSucceeded = SoundDefOf.Designate_ZoneAdd;
            useMouseIcon = true;
        }
        public override bool DragDrawMeasurements { get { return true; } }
        public override DrawStyleCategoryDef DrawStyleCategory { get { return DrawStyleCategoryDefOf.Areas; } }
        public override AcceptanceReport CanDesignateCell(IntVec3 cell)
        {
            if (!cell.InBounds(Map)) return false;
            Area_AICoopCombat area = AICoopCombatArea.Get(Map);
            bool selected = area != null && area[cell];
            return erase ? selected : !selected;
        }
        public override void DesignateSingleCell(IntVec3 cell)
        {
            if (cell.InBounds(Map)) AICoopCombatArea.Get(Map, true)[cell] = !erase;
        }
        public override void SelectedUpdate()
        {
            GenUI.RenderMouseoverBracket();
            AICoopCombatArea.Get(Map)?.MarkForDraw();
        }
        protected override void FinalizeDesignationSucceeded()
        {
            base.FinalizeDesignationSucceeded();
            if (AICoopAgentBridge.IsConnected)
                AICoopGameComponent.Current?.AddCommandResult("AI_COMBAT_AREA_CHANGED map=" + Map.uniqueID +
                    " cells=" + (AICoopCombatArea.Get(Map)?.TrueCount ?? 0) + "; drafted_AI_stay_inside; zero=restriction_removed");
        }
    }

    public sealed class Designator_AICoopCombatClear : Designator_AICoopCombat
    {
        public Designator_AICoopCombatClear()
        {
            erase = true;
            defaultLabel = "删除AI战斗区";
            defaultDesc = "擦除选中的AI战斗区；全部擦除后恢复原有战斗活动范围。";
            icon = ContentFinder<Texture2D>.Get("AICombatAreaClear");
        }
    }

    internal static class AICoopCombatArea
    {
        private static readonly FieldInfo PathIndex = AccessTools.Field(typeof(PawnPath), "curNodeIndex");
        private static readonly FieldInfo PathCost = AccessTools.Field(typeof(PawnPath), "totalCostInt");
        private static readonly FieldInfo PathInUse = AccessTools.Field(typeof(PawnPath), "inUse");

        internal static Area_AICoopCombat Get(Map map, bool create = false)
        {
            if (map == null) return null;
            Area_AICoopCombat area = map.areaManager.AllAreas.OfType<Area_AICoopCombat>().FirstOrDefault();
            if (area == null && create)
            {
                area = new Area_AICoopCombat(map.areaManager);
                map.areaManager.AllAreas.Add(area);
            }
            return area;
        }

        internal static Area_AICoopCombat For(Pawn pawn)
        {
            if (pawn == null || !pawn.Spawned || !pawn.Drafted || pawn.Dead ||
                !AICoopAgentBridge.IsConnected || AICoopGameComponent.Current?.IsAI(pawn) != true) return null;
            Area_AICoopCombat area = Get(pawn.Map);
            return area != null && area.TrueCount > 0 ? area : null;
        }

        internal static bool Allows(Pawn pawn, IntVec3 cell)
        {
            Area_AICoopCombat area = For(pawn);
            return area == null || (cell.InBounds(pawn.Map) && area[cell]);
        }

        private static bool Walkable(Pawn pawn, IntVec3 cell)
        {
            if (!cell.InBounds(pawn.Map) || !cell.Walkable(pawn.Map)) return false;
            Building_Door door = cell.GetEdifice(pawn.Map) as Building_Door;
            return door == null || door.FreePassage || door.PawnCanOpen(pawn);
        }

        // Cardinal paths cannot cut across an unpainted corner. Once inside the
        // area, no route may leave it, even when two painted islands are nearby.
        internal static List<IntVec3> Route(Pawn pawn, Area_AICoopCombat area, LocalTargetInfo target, PathEndMode mode, bool returnOnly)
        {
            IntVec3 start = pawn.Position;
            if (!returnOnly && (!target.IsValid || !target.Cell.InBounds(pawn.Map) ||
                (mode == PathEndMode.OnCell && !area[target.Cell]))) return null;
            var queue = new Queue<IntVec3>();
            var previous = new Dictionary<IntVec3, IntVec3>();
            queue.Enqueue(start);
            previous[start] = start;
            while (queue.Count > 0)
            {
                IntVec3 cell = queue.Dequeue();
                if (area[cell] && (returnOnly || ReachabilityImmediate.CanReachImmediate(cell, target, pawn.Map, mode, pawn)))
                {
                    var path = new List<IntVec3>();
                    for (IntVec3 node = cell; ; node = previous[node])
                    {
                        path.Add(node);
                        if (node == start) return path;
                    }
                }
                foreach (IntVec3 direction in GenAdj.CardinalDirections)
                {
                    IntVec3 next = cell + direction;
                    if (!Walkable(pawn, next) || previous.ContainsKey(next) || (area[cell] && !area[next])) continue;
                    previous[next] = cell;
                    queue.Enqueue(next);
                }
            }
            return null;
        }

        internal static IntVec3 ReturnCell(Pawn pawn)
        {
            Area_AICoopCombat area = For(pawn);
            if (area == null) return IntVec3.Invalid;
            List<IntVec3> route = Route(pawn, area, LocalTargetInfo.Invalid, PathEndMode.OnCell, true);
            return route == null ? IntVec3.Invalid : route[0];
        }

        internal static PathRequest Request(Pawn pawn, LocalTargetInfo target, PathEndMode mode, Area_AICoopCombat area)
        {
            int tick = Find.TickManager.TicksGame;
            var request = new PathRequest(pawn.Map, pawn.Position, target, null,
                TraverseParms.For(pawn, Danger.Deadly), PathFinderCostTuning.For(pawn), mode, pawn, tick, tick, tick, null);
            List<IntVec3> route = Route(pawn, area, target, mode, false);
            if (route == null) { request.Resolve(null); return request; }
            PawnPath path = pawn.Map.pawnPathPool.GetPath();
            foreach (IntVec3 cell in route) path.AddNode(cell);
            PathIndex.SetValue(path, route.Count - 1);
            PathCost.SetValue(path, (float)(route.Count * 13));
            PathInUse.SetValue(path, true);
            request.Resolve(path);
            return request;
        }
    }

    public sealed class AICoopCombatAreaMapComponent : MapComponent
    {
        public AICoopCombatAreaMapComponent(Map map) : base(map) { }
        public override void MapComponentTick()
        {
            if (Find.TickManager.TicksGame % 60 != 0) return;
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned)
            {
                Area_AICoopCombat area = AICoopCombatArea.For(pawn);
                if (area == null || area[pawn.Position] || pawn.Downed || pawn.jobs == null) continue;
                if (pawn.CurJob?.def == JobDefOf.Goto && pawn.CurJob.targetA.Cell.InBounds(map) && area[pawn.CurJob.targetA.Cell]) continue;
                IntVec3 destination = AICoopCombatArea.ReturnCell(pawn);
                if (destination.IsValid) pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, destination));
                else if (pawn.CurJob != null) pawn.jobs.EndCurrentJob(JobCondition.InterruptForced);
            }
        }
    }

    [HarmonyPatch(typeof(Pawn_PathFollower), "GenerateNewPathRequest")]
    internal static class AICoopCombatPathPatch
    {
        private static bool Prefix(Pawn ___pawn, LocalTargetInfo ___destination, PathEndMode ___peMode, ref PathRequest __result)
        {
            Area_AICoopCombat area = AICoopCombatArea.For(___pawn);
            if (area == null) return true;
            __result = AICoopCombatArea.Request(___pawn, ___destination, ___peMode, area);
            return false;
        }
    }

    // Painted cells can change while an old path is in progress.
    [HarmonyPatch(typeof(Pawn_PathFollower), "TryEnterNextPathCell")]
    internal static class AICoopCombatBoundaryPatch
    {
        private static bool Prefix(Pawn_PathFollower __instance, Pawn ___pawn, IntVec3 ___nextCell)
        {
            Area_AICoopCombat area = AICoopCombatArea.For(___pawn);
            if (area == null) return true;
            bool returning = !area[___pawn.Position] && __instance.Destination.Cell.InBounds(___pawn.Map) && area[__instance.Destination.Cell];
            if (returning || (___nextCell.InBounds(___pawn.Map) && area[___nextCell])) return true;
            __instance.StopDead();
            ___pawn.jobs?.EndCurrentJob(JobCondition.InterruptForced);
            return false;
        }
    }

    [HarmonyPatch]
    internal static class AICoopCombatAttackPatch
    {
        private static IEnumerable<MethodBase> TargetMethods()
        {
            return AccessTools.GetDeclaredMethods(typeof(Verb)).Where(method => method.Name == "TryStartCastOn");
        }
        private static bool Prefix(Verb __instance, ref bool __result)
        {
            Pawn pawn = __instance.CasterPawn;
            if (pawn == null || AICoopCombatArea.Allows(pawn, pawn.Position)) return true;
            __result = false;
            return false;
        }
    }
}
