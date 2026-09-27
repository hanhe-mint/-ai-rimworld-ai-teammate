using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace AICoopCompanion
{
    internal static class AICoopPresetExport
    {
        internal static string Token(string text) { return (text ?? "-").Replace(' ', '_').Replace('\n', '_').Replace('\r', '_'); }
        private static string Number(float n) { return n.ToString(CultureInfo.InvariantCulture); }
        internal static string Filter(ThingFilter filter)
        {
            return " allow=" + string.Join(",", filter.AllowedThingDefs.Select(d => d.defName).OrderBy(s => s).ToArray()) +
                " exclude=" + string.Join(",", ((typeof(ThingFilter).GetField("disallowedSpecialFilters", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.GetValue(filter) as List<SpecialThingFilterDef>) ?? new List<SpecialThingFilterDef>()).Select(d => d.defName).ToArray()) +
                " hpmin=" + Number(filter.AllowedHitPointsPercents.min) + " hpmax=" + Number(filter.AllowedHitPointsPercents.max) +
                " qmin=" + (int)filter.AllowedQualityLevels.min + " qmax=" + (int)filter.AllowedQualityLevels.max;
        }
        internal static string Capture(Map map, Area_AICoopPreset area)
        {
            var cells = new HashSet<IntVec3>(area.ActiveCells);
            if (cells.Count == 0) return "# EXPORT_ERROR 空范围";
            int x = cells.Min(c => c.x), z = cells.Min(c => c.z);
            int width = cells.Max(c => c.x) - x + 1, height = cells.Max(c => c.z) - z + 1;
            Func<IntVec3, string> pos = c => (c.x - x) + " " + (c.z - z);
            var text = new StringBuilder();
            text.AppendLine("# 从命名范围导出；相对坐标单位为游戏格。物品清单仅记录，不凭空生成库存或生物。");
            text.AppendLine("ROOM id=" + Token(area.Label) + " purpose=" + Token(area.Label) + " priority=5");
            text.AppendLine("FOOTPRINT " + width + "x" + height);
            foreach (IntVec3 cell in cells.OrderBy(c => c.z).ThenBy(c => c.x))
            {
                TerrainDef terrain = cell.GetTerrain(map);
                text.AppendLine((terrain.BuildableByPlayer ? "FLOOR " : "GROUND ") + terrain.defName + " " + pos(cell));
                if (cell.GetRoof(map) != null && !map.areaManager.NoRoof[cell]) text.AppendLine("ZONE build_roof " + pos(cell));
            }
            foreach (Thing thing in map.listerThings.AllThings.Where(t => cells.Contains(t.Position)).OrderBy(t => t.Position.z).ThenBy(t => t.Position.x))
            {
                ThingDef def = thing.def.entityDefToBuild as ThingDef ?? thing.def;
                if (!(thing is Building) && !(thing is Blueprint) && !(thing is Frame))
                {
                    text.AppendLine("# OBJECT " + def.defName + " " + pos(thing.Position) + " count=" + thing.stackCount + " class=" + thing.GetType().FullName);
                    continue;
                }
                if (thing.def.entityDefToBuild is TerrainDef) continue;
                if (def.building == null || !def.BuildableByPlayer)
                { text.AppendLine("# WARNING 不可建造对象已记录但不重建：" + def.defName + " " + pos(thing.Position)); continue; }
                if (!GenAdj.OccupiedRect(thing.Position, thing.Rotation, def.Size).Cells.All(cells.Contains))
                { text.AppendLine("# WARNING 建筑跨出范围，未导出：" + def.defName + " " + pos(thing.Position)); continue; }
                ThingDef stuff = thing is Blueprint_Build ? ((Blueprint_Build)thing).stuffToUse : thing.Stuff;
                text.AppendLine("BUILD " + def.defName + " " + pos(thing.Position) + " " + thing.Rotation.AsInt + " " + (stuff?.defName ?? "-"));
                string at = pos(thing.Position);
                var bed = thing as Building_Bed;
                if (bed != null) text.AppendLine("STATE bed " + at + " owner=" + bed.ForOwnerType.ToString().ToLowerInvariant() + " medical=" + (bed.Medical ? 1 : 0));
                var door = thing as Building_Door;
                if (door != null) text.AppendLine("STATE door " + at + " hold_open=" + (door.HoldOpen ? 1 : 0));
                var temp = thing.TryGetComp<CompTempControl>();
                if (temp != null) text.AppendLine("STATE temperature " + at + " temperature=" + Number(temp.TargetTemperature));
                var storage = thing as IStoreSettingsParent;
                if (storage?.GetStoreSettings() != null)
                    text.AppendLine("STATE storage " + at + " priority=" + (int)storage.GetStoreSettings().Priority + Filter(storage.GetStoreSettings().filter));
                var bills = thing as IBillGiver;
                if (bills != null)
                {
                    int index = 0;
                    foreach (Bill_Production bill in bills.BillStack.Bills.OfType<Bill_Production>())
                        text.AppendLine("STATE bill_" + index++ + " " + at + " recipe=" + bill.recipe.defName + " repeat=" + bill.repeatMode.defName +
                            " count=" + bill.repeatCount + " target=" + bill.targetCount + " suspended=" + (bill.suspended ? 1 : 0) + Filter(bill.ingredientFilter));
                }
            }
            foreach (Zone zone in map.zoneManager.AllZones)
            {
                var selected = zone.Cells.Where(cells.Contains).ToList();
                if (selected.Count == 0) continue;
                var grow = zone as Zone_Growing;
                var store = zone as Zone_Stockpile;
                if (grow == null && store == null)
                { text.AppendLine("# WARNING 不支持自动恢复的区域类型：" + zone.GetType().FullName); continue; }
                string label = Token(zone.label) + "_" + zone.ID;
                foreach (IntVec3 cell in selected)
                    text.AppendLine("ZONE " + (grow != null ? "growing" : "stockpile") + " " + pos(cell) + " " + (grow?.GetPlantDefToGrow()?.defName ?? "-") + " " + label);
                if (store != null) text.AppendLine("STATE zone_storage " + pos(selected[0]) + " priority=" + (int)store.GetStoreSettings().Priority + Filter(store.GetStoreSettings().filter));
                if (grow != null) text.AppendLine("STATE growing " + pos(selected[0]) + " sow=" + (grow.allowSow ? 1 : 0) + " cut=" + (grow.allowCut ? 1 : 0));
            }
            foreach (Area marked in map.areaManager.AllAreas)
            {
                if (marked is Area_AICoopPreset || marked is Area_AICoopCombat) continue;
                string kind = marked == map.areaManager.Home ? "home" : marked == map.areaManager.NoRoof ? "no_roof" :
                    marked == map.areaManager.BuildRoof ? "build_roof" : marked.GetType().Name == "Area_SnowClear" ? "snow_clear" :
                    marked.GetType().Name == "Area_PollutionClear" ? "pollution_clear" : marked is Area_Allowed ? "allowed" : null;
                if (kind == null)
                {
                    if (marked.ActiveCells.Any(cells.Contains)) text.AppendLine("# WARNING 不支持自动恢复的标记区域：" + marked.GetType().FullName);
                    continue;
                }
                foreach (IntVec3 cell in marked.ActiveCells.Where(cells.Contains))
                    text.AppendLine("ZONE " + kind + " " + pos(cell) + " - " + Token(marked.Label));
            }
            text.AppendLine("END_ROOM");
            return text.ToString();
        }
    }
}
