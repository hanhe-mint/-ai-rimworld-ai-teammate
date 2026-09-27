using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace AICoopCompanion
{
    internal static class AICoopMapObstacles
    {
        internal static bool IsOre(Thing thing)
        {
            ThingDef product = thing?.def?.building?.mineableThing;
            return thing != null && thing.Spawned && product != null && thing.def.building.isNaturalRock &&
                !product.defName.StartsWith("Chunk", StringComparison.OrdinalIgnoreCase) &&
                !(product.thingCategories?.Any(c => c.defName == "StoneChunks" || c.defName == "Chunks") ?? false);
        }

        internal static string TerrainRestriction(TerrainDef terrain)
        {
            if (terrain == null) return "unknown";
            if (terrain.IsWater || terrain.IsOcean) return "water";
            if (terrain.passability == Traversability.Impassable) return "impassable";
            if (terrain.affordances == null || !terrain.affordances.Contains(TerrainAffordanceDefOf.Heavy)) return "limited_support";
            return "none";
        }

        // Exact row spans, not bounding boxes: gaps are never mistaken for ore or marsh.
        internal static string RowSpans(IEnumerable<IntVec3> cells)
        {
            var spans = new List<string>();
            foreach (var row in cells.Distinct().GroupBy(c => c.z).OrderBy(g => g.Key))
            {
                int[] xs = row.Select(c => c.x).OrderBy(x => x).ToArray();
                int first = xs[0], last = first;
                for (int i = 1; i <= xs.Length; i++)
                {
                    if (i < xs.Length && xs[i] == last + 1) { last = xs[i]; continue; }
                    spans.Add(row.Key + ":" + first + (last == first ? "" : "-" + last));
                    if (i < xs.Length) first = last = xs[i];
                }
            }
            return string.Join(";", spans.ToArray());
        }

        internal static void Append(StringBuilder state, Map map)
        {
            state.AppendLine("MAP_OBSTACLE_FORMAT map=" + map.uniqueID + " rows=z:xStart-xEnd; single=z:x; exact_cells_not_bbox; surface_ore_only; limited_support_requires_checking_building_affordance");
            foreach (var group in map.listerThings.AllThings.Where(IsOre).GroupBy(t => t.def.defName).OrderBy(g => g.Key))
            {
                Thing sample = group.First();
                state.AppendLine("SURFACE_ORE map=" + map.uniqueID + " rock=" + group.Key + " product=" + sample.def.building.mineableThing.defName +
                    " count=" + group.Count() + " rows=" + RowSpans(group.SelectMany(t => t.OccupiedRect().Cells)));
            }
            var terrains = new Dictionary<TerrainDef, List<IntVec3>>();
            foreach (IntVec3 cell in map.AllCells)
            {
                TerrainDef terrain = cell.GetTerrain(map);
                if (terrain == null || TerrainRestriction(terrain) == "none") continue;
                List<IntVec3> cells;
                if (!terrains.TryGetValue(terrain, out cells)) terrains[terrain] = cells = new List<IntVec3>();
                cells.Add(cell);
            }
            foreach (var pair in terrains.OrderBy(p => p.Key.defName))
                state.AppendLine("SPECIAL_TERRAIN map=" + map.uniqueID + " terrain=" + pair.Key.defName + " restriction=" + TerrainRestriction(pair.Key) +
                    " supports=" + string.Join(",", (pair.Key.affordances ?? new List<TerrainAffordanceDef>()).Select(a => a.defName).ToArray()) +
                    " count=" + pair.Value.Count + " rows=" + RowSpans(pair.Value));
        }
    }
}
