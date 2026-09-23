using System;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace AICoopCompanion
{
    internal static class AICoopMapQuery
    {
        public static void Execute(string[] args)
        {
            var component = AICoopGameComponent.Current;
            int mapId, x1, z1, x2, z2, offset = 0;
            if ((args.Length != 6 && args.Length != 7) || !int.TryParse(args[1], out mapId) ||
                !int.TryParse(args[2], out x1) || !int.TryParse(args[3], out z1) ||
                !int.TryParse(args[4], out x2) || !int.TryParse(args[5], out z2) ||
                (args.Length == 7 && !int.TryParse(args[6], out offset)))
            {
                component.AddCommandResult("FAIL MAP_SCAN mapID x1 z1 x2 z2 [offset]");
                return;
            }
            Map map = Find.Maps.FirstOrDefault(item => item.uniqueID == mapId);
            if (map == null || offset < 0 || !new IntVec3(x1, 0, z1).InBounds(map) || !new IntVec3(x2, 0, z2).InBounds(map))
            {
                component.AddCommandResult("FAIL MAP_SCAN invalid_map_or_bounds_or_offset");
                return;
            }
            int minX = Math.Min(x1, x2), minZ = Math.Min(z1, z2);
            int width = Math.Abs(x2 - x1) + 1, total = width * (Math.Abs(z2 - z1) + 1);
            if (offset >= total) { component.AddCommandResult("FAIL MAP_SCAN offset_out_of_range total=" + total); return; }
            int end = Math.Min(total, offset + 128);
            var text = new StringBuilder("MAP_SCAN map=" + mapId + " tick=" + Find.TickManager.TicksGame +
                " total=" + total + " offset=" + offset + " next=" + (end < total ? end.ToString() : "done") + "\n");
            var things = new HashSet<Thing>();
            for (int index = offset; index < end; index++)
            {
                var cell = new IntVec3(minX + index % width, 0, minZ + index / width);
                TerrainDef terrain = cell.GetTerrain(map);
                Zone zone = map.zoneManager.ZoneAt(cell);
                var growing = zone as Zone_Growing;
                text.AppendLine("CELL " + cell.x + ":" + cell.z + " terrain=" + terrain.defName +
                    " fertility=" + cell.GetFertility(map).ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) +
                    " walk=" + cell.Walkable(map) + " fog=" + cell.Fogged(map) + " roof=" + (cell.GetRoof(map)?.defName ?? "-") +
                    " affordances=" + string.Join(",", terrain.affordances.Select(a => a.defName).ToArray()) +
                    " zone=" + (zone == null ? "-" : zone.ID + ":" + zone.GetType().Name + ":" + zone.label) +
                    (growing == null ? "" : " crop=" + growing.GetPlantDefToGrow().defName) +
                    " areas=" + string.Join(",", map.areaManager.AllAreas.Where(area => area[cell]).Select(area => area.Label).ToArray()) +
                    " designations=" + string.Join(",", map.designationManager.AllDesignationsAt(cell).Select(d => d.def.defName).ToArray()));
                foreach (Thing thing in cell.GetThingList(map)) things.Add(thing);
            }
            foreach (Thing thing in things.OrderBy(t => t.thingIDNumber))
            {
                var buildable = thing is Blueprint ? ((Blueprint)thing).EntityToBuild() : thing.def.entityDefToBuild;
                text.Append("THING id=").Append(thing.thingIDNumber).Append(" def=").Append(thing.def.defName)
                    .Append(" pos=").Append(thing.Position.x).Append(':').Append(thing.Position.z)
                    .Append(" size=").Append(thing.def.size).Append(" rot=").Append(thing.Rotation.AsInt)
                    .Append(" count=").Append(thing.stackCount).Append(" hp=").Append(thing.HitPoints)
                    .Append(" faction=").Append(thing.Faction?.Name ?? "-").Append(" stuff=").Append(thing.Stuff?.defName ?? "-")
                    .Append(" forbidden=").Append(thing.TryGetComp<CompForbiddable>()?.Forbidden ?? false)
                    .Append(" builds=").Append(buildable?.defName ?? "-");
                var plant = thing as Plant;
                if (plant != null) text.Append(" growth=").Append(plant.Growth.ToString("0.##")).Append(" harvestable=").Append(plant.HarvestableNow);
                var pawn = thing as Pawn;
                if (pawn != null) text.Append(" pawn=").Append(pawn.LabelShort).Append(" AI=").Append(component.IsAI(pawn))
                    .Append(" downed=").Append(pawn.Downed).Append(" drafted=").Append(pawn.Drafted).Append(" mental=").Append(pawn.InMentalState)
                    .Append(" job=").Append(pawn.CurJobDef?.defName ?? "-").Append(" weapon=").Append(pawn.equipment?.Primary?.def.defName ?? "-")
                    .Append(" hediffs=").Append(string.Join(",", pawn.health.hediffSet.hediffs.Select(h => h.def.defName + ":" + h.Severity.ToString("0.##")).ToArray()));
                var bed = thing as Building_Bed;
                if (bed != null) text.Append(" prisoner=").Append(bed.ForPrisoners).Append(" medical=").Append(bed.Medical).Append(" slaves=").Append(bed.ForSlaves);
                var door = thing as Building_Door;
                if (door != null) text.Append(" holdOpen=").Append(door.HoldOpen).Append(" open=").Append(door.Open);
                var temp = thing.TryGetComp<CompTempControl>();
                if (temp != null) text.Append(" temperature=").Append(temp.TargetTemperature);
                var power = thing.TryGetComp<CompPowerTrader>();
                if (power != null) text.Append(" power=").Append(power.PowerOn);
                var bills = thing as IBillGiver;
                if (bills != null) text.Append(" bills=").Append(string.Join(",", bills.BillStack.Bills.Select(b => b.recipe.defName + ":suspended=" + b.suspended).ToArray()));
                text.AppendLine();
            }
            component.AddCommandResult(text.ToString());
        }
    }
}
