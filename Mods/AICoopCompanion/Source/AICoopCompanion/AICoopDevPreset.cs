using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace AICoopCompanion
{
    public sealed class Designator_AICoopDevPreset : Designator
    {
        public Designator_AICoopDevPreset()
        {
            defaultLabel = "创建预设";
            defaultDesc = "开发者工具：点击位置后选择预设，直接生成成品，无需连接Harness。点击已有房间右下角可向右共墙拼接；先对齐房间，再检查门位，旋转或强制拼接前询问。";
            icon = ContentFinder<Texture2D>.Get("AIIcon");
            useMouseIcon = true;
        }
        public override bool Visible { get { return Prefs.DevMode; } }
        public override AcceptanceReport CanDesignateCell(IntVec3 cell)
        {
            return Prefs.DevMode && cell.InBounds(Map);
        }
        public override void DesignateSingleCell(IntVec3 cell)
        {
            if (!Prefs.DevMode || !cell.InBounds(Map)) return;
            Map map = Map;
            var game = AICoopGameComponent.Current;
            var options = AICoopPresetManager.DevPresetOptions().Select(option => new FloatMenuOption(option.Item2, () =>
            {
                if (!Prefs.DevMode || game != AICoopGameComponent.Current || !Find.Maps.Contains(map)) return;
                AICoopActionExecutor.CreateDevPreset(map, cell, option.Item1);
            })).ToList();
            if (options.Count > 0) Find.WindowStack.Add(new FloatMenu(options));
            else Messages.Message("没有找到可用房间预设。", MessageTypeDefOf.RejectInput, false);
        }
    }

    public static partial class AICoopActionExecutor
    {
        private static void CompleteDevPreset(Map map, Pawn actor, HashSet<Blueprint_Build> existing)
        {
            int completed = 0, failed = 0;
            foreach (Blueprint_Build blueprint in map.listerThings.AllThings.OfType<Blueprint_Build>()
                .Where(b => !existing.Contains(b)).OrderBy(b => b.EntityToBuild() is TerrainDef ? 0 : 1).ToList())
            {
                IntVec3 cell = blueprint.Position;
                try
                {
                    // Use the vanilla frame completion path for both floors and buildings.
                    Frame frame = (Frame)ThingMaker.MakeThing(blueprint.EntityToBuild().frameDef, blueprint.stuffToUse);
                    frame.SetFactionDirect(Faction.OfPlayer);
                    frame.StyleSourcePrecept = blueprint.StyleSourcePrecept;
                    frame.StyleDef = blueprint.StyleDef;
                    frame.glowerColorOverride = blueprint.glowerColorOverride;
                    Rot4 rotation = blueprint.Rotation;
                    blueprint.DeSpawn();
                    try { GenSpawn.Spawn(frame, cell, map, rotation, WipeMode.Vanish); }
                    catch { GenSpawn.Spawn(blueprint, cell, map, rotation, WipeMode.Vanish); throw; }
                    blueprint.Destroy();
                    frame.CompleteConstruction(actor);
                    AICoopPresetManager.ApplyPendingState(map, cell);
                    completed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Log.Error("[AI队友] 预设成品生成失败 " + cell + "：" + ex);
                }
            }
            Messages.Message("预设成品生成：" + completed + " 项，失败 " + failed + " 项。", MessageTypeDefOf.TaskCompletion, false);
        }

        internal static void CreateDevPreset(Map map, IntVec3 cell, string name)
        {
            var component = AICoopGameComponent.Current;
            if (!Prefs.DevMode || component == null || map == null || !cell.InBounds(map)) return;
            Pawn actor = map.mapPawns.FreeColonistsSpawned.FirstOrDefault();
            if (actor == null)
            { Messages.Message("此地图需要至少一名殖民者才能运行预设生成逻辑。", MessageTypeDefOf.RejectInput, false); return; }
            string previous = component.ActivePresetName;
            int room = component.ActivePresetRoomIndex;
            try
            {
                component.SetPresetProgress(name, 0);
                BuildStructuredPresetRoom(component, actor, name,
                    new[] { "PRESET", name, map.uniqueID.ToString(), cell.x.ToString(), cell.z.ToString(), "0" },
                    "DEV PRESET " + name, devExactPosition: true);
            }
            catch (Exception ex)
            {
                Log.Error("[AI队友] 开发者预设生成失败：" + ex);
                Messages.Message("预设生成失败，请查看日志。", MessageTypeDefOf.RejectInput, false);
            }
            finally { component.SetPresetProgress(previous, room); }
        }
    }
}
