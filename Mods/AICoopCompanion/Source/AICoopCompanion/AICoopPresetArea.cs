using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace AICoopCompanion
{
    public sealed class Area_AICoopPreset : Area
    {
        private string presetName = "殖民地预设范围";
        private string exportText;
        public Area_AICoopPreset() { }
        public Area_AICoopPreset(AreaManager manager, string name) : base(manager) { presetName = name; }
        public override string Label { get { return presetName; } }
        public override Color Color { get { return new Color(0.2f, 0.8f, 0.85f); } }
        public override int ListPriority { get { return 252; } }
        public override bool AssignableAsAllowed() { return false; }
        public override string GetUniqueLoadID() { return "AICoopPresetArea_" + ID; }
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref presetName, "presetName", "殖民地预设范围");
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                try { exportText = AICoopPresetExport.Capture(Map, this); }
                catch (Exception ex) { exportText = "# EXPORT_ERROR " + ex.Message; Log.Warning("[AI队友] 范围导出失败：" + ex.Message); }
            }
            Scribe_Values.Look(ref exportText, "presetExport", null);
        }
    }

    public sealed class Designator_AICoopPresetArea : Designator_Cells
    {
        private readonly HashSet<IntVec3> cells = new HashSet<IntVec3>();
        public Designator_AICoopPresetArea()
        {
            defaultLabel = "殖民地预设范围";
            defaultDesc = "拖动标记房间导出范围，松开后命名。仅标记，不限制AI或小人行动；保存游戏后用模组内脚本导出。再次点击已有范围可删除标记。";
            icon = ContentFinder<Texture2D>.Get("AIIcon");
            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            soundSucceeded = SoundDefOf.Designate_ZoneAdd;
        }
        public override bool DragDrawMeasurements { get { return true; } }
        public override DrawStyleCategoryDef DrawStyleCategory { get { return DrawStyleCategoryDefOf.Areas; } }
        public override AcceptanceReport CanDesignateCell(IntVec3 c) { return c.InBounds(Map); }
        public override void DesignateSingleCell(IntVec3 c) { if (c.InBounds(Map)) cells.Add(c); }
        public override void SelectedUpdate()
        {
            foreach (var area in Map.areaManager.AllAreas.OfType<Area_AICoopPreset>()) area.MarkForDraw();
        }
        protected override void FinalizeDesignationSucceeded()
        {
            base.FinalizeDesignationSucceeded();
            Map map = Map;
            var selected = cells.ToList(); cells.Clear();
            if (selected.Count == 1)
            {
                var existing = map.areaManager.AllAreas.OfType<Area_AICoopPreset>().FirstOrDefault(a => a[selected[0]]);
                if (existing != null)
                {
                    Find.WindowStack.Add(new Dialog_MessageBox("删除“" + existing.Label + "”的范围标记？不会删除建筑。", "删除标记",
                        () => { existing.Clear(); map.areaManager.AllAreas.Remove(existing); }, "取消"));
                    return;
                }
            }
            if (selected.Count > 0) Find.WindowStack.Add(new Dialog_AICoopPresetName(map, selected));
        }
    }

    internal sealed class Dialog_AICoopPresetName : Window
    {
        private readonly Map map;
        private readonly List<IntVec3> cells;
        private readonly Game game;
        private string name = "新房间";
        public Dialog_AICoopPresetName(Map map, List<IntVec3> cells)
        { this.map = map; this.cells = cells; game = Current.Game; doCloseX = true; forcePause = true; closeOnAccept = false; }
        public override Vector2 InitialSize { get { return new Vector2(460, 180); } }
        public override void DoWindowContents(Rect rect)
        {
            Widgets.Label(new Rect(0, 0, rect.width, 30), "命名殖民地预设范围（例如：卫生间）");
            name = Widgets.TextField(new Rect(0, 40, rect.width, 32), name);
            if (!Widgets.ButtonText(new Rect(rect.width - 120, 90, 120, 32), "保存标记")) return;
            string clean = name.Trim();
            if (clean.Length == 0 || clean.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) >= 0 || clean.Any(char.IsWhiteSpace) || clean.EndsWith("."))
            { Messages.Message("名称不能为空，不能包含空格或文件名非法字符。", MessageTypeDefOf.RejectInput, false); return; }
            if (Current.Game != game || !Find.Maps.Contains(map)) { Close(); return; }
            if (map.areaManager.AllAreas.OfType<Area_AICoopPreset>().Any(a => a.Label == clean))
            { Messages.Message("此地图已有同名范围，请使用不同名称。", MessageTypeDefOf.RejectInput, false); return; }
            var area = new Area_AICoopPreset(map.areaManager, clean);
            map.areaManager.AllAreas.Add(area);
            foreach (var cell in cells) area[cell] = true;
            Close();
        }
    }
}
