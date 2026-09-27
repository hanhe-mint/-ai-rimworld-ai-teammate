using System.Linq;
using RimWorld;
using Verse;

namespace AICoopCompanion
{
    // This part belongs only to the explicitly selected preset test scenario.
    public sealed class ScenPart_AICoopPresetTest : ScenPart
    {
        private bool prepared;
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref prepared, "aicoopTestMapPrepared", false);
        }
        public override void PostMapGenerate(Map map)
        {
            if (prepared || Find.GameInitData == null || map == null) return;
            TerrainDef soil = DefDatabase<TerrainDef>.GetNamed("Soil");
            // Remove overhead mountain before removing rock, so it cannot collapse onto the test field.
            foreach (IntVec3 cell in map.AllCells) map.roofGrid.SetRoof(cell, null);
            foreach (Thing thing in map.listerThings.AllThings.ToList())
            {
                if (thing is Pawn && thing.Faction == Faction.OfPlayer) continue;
                if (!thing.Destroyed) thing.Destroy(DestroyMode.Vanish);
            }
            foreach (Zone zone in map.zoneManager.AllZones.ToList()) zone.Delete();
            foreach (Area area in map.areaManager.AllAreas) area.Clear();
            foreach (IntVec3 cell in map.AllCells)
            {
                map.terrainGrid.SetTerrain(cell, soil);
                map.snowGrid.SetDepth(cell, 0f);
            }
            map.fogGrid.ClearAllFog();
            prepared = true;
        }
        public override void PostGameStart()
        {
            foreach (ResearchProjectDef research in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
                if (!research.IsFinished) Find.ResearchManager.FinishProject(research, false, null, false);
            Messages.Message("预设测试场已准备：地图清空、统一泥土地面、研究全部完成。开启开发者模式后，在区域菜单使用“创建预设”。", MessageTypeDefOf.TaskCompletion, false);
        }
    }
}
