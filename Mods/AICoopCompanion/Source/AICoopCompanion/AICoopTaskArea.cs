using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;

namespace AICoopCompanion
{
    // Only owned by the input window, never registered with AreaManager or saved.
    internal sealed class Area_AICoopTask : Area
    {
        public Area_AICoopTask(AreaManager manager) : base(manager) { }
        public override string Label { get { return "AI任务区"; } }
        public override Color Color { get { return Color.white; } }
        public override int ListPriority { get { return 251; } }
        public override bool AssignableAsAllowed() { return false; }
        public override string GetUniqueLoadID() { return "AICoopTaskTemporary_" + ID; }
    }

    public sealed class Designator_AICoopTask : Designator_Cells
    {
        private readonly HashSet<IntVec3> cells = new HashSet<IntVec3>();
        public Designator_AICoopTask()
        {
            defaultLabel = "AI任务区";
            defaultDesc = "拖动选择区域，输入希望AI在这里完成的任务。发送后区域自动删除；取消不发送。请先连接Agent。";
            icon = ContentFinder<Texture2D>.Get("AITaskArea");
            soundDragSustain = SoundDefOf.Designate_DragStandard;
            soundDragChanged = SoundDefOf.Designate_DragStandard_Changed;
            soundSucceeded = SoundDefOf.Designate_ZoneAdd;
            useMouseIcon = true;
        }
        public override bool DragDrawMeasurements { get { return true; } }
        public override DrawStyleCategoryDef DrawStyleCategory { get { return DrawStyleCategoryDefOf.Areas; } }
        public override AcceptanceReport CanDesignateCell(IntVec3 cell)
        {
            if (!AICoopAgentBridge.IsConnected) return "请先在Harness连接游戏。";
            return cell.InBounds(Map);
        }
        public override void DesignateSingleCell(IntVec3 cell)
        {
            if (cell.InBounds(Map)) cells.Add(cell);
        }
        protected override void FinalizeDesignationSucceeded()
        {
            base.FinalizeDesignationSucceeded();
            var selected = cells.ToList();
            cells.Clear();
            if (selected.Count > 0 && AICoopGameComponent.Current != null)
                Find.WindowStack.Add(new Dialog_AICoopTaskArea(Map, AICoopGameComponent.Current, selected));
            Find.DesignatorManager.Deselect();
        }
        protected override void FinalizeDesignationFailed()
        {
            cells.Clear();
            base.FinalizeDesignationFailed();
        }
    }

    internal sealed class Dialog_AICoopTaskArea : Window
    {
        private readonly Map map;
        private readonly AICoopGameComponent component;
        private readonly Area_AICoopTask area;
        private readonly string location;
        private string input = "";
        private bool focus = true;
        public Dialog_AICoopTaskArea(Map map, AICoopGameComponent component, List<IntVec3> cells)
        {
            this.map = map;
            this.component = component;
            area = new Area_AICoopTask(map.areaManager);
            foreach (IntVec3 cell in cells) area[cell] = true;
            location = "map=" + map.uniqueID + " x1=" + cells.Min(c => c.x) + " z1=" + cells.Min(c => c.z) +
                " x2=" + cells.Max(c => c.x) + " z2=" + cells.Max(c => c.z) + " cells=" + cells.Count +
                " rows=" + AICoopMapObstacles.RowSpans(cells);
            closeOnAccept = false;
            doCloseX = true;
            forcePause = true;
            absorbInputAroundWindow = true;
        }
        public override Vector2 InitialSize { get { return new Vector2(580f, 340f); } }
        public override void WindowUpdate()
        {
            base.WindowUpdate();
            if (AICoopGameComponent.Current != component || !Find.Maps.Contains(map)) { Close(); return; }
            area.MarkForDraw();
            area.AreaUpdate();
        }
        public override void PostClose()
        {
            area.Clear();
            base.PostClose();
        }
        public override void OnAcceptKeyPressed()
        {
            if (Event.current != null && Event.current.shift) return;
            Send();
            Event.current?.Use();
        }
        private void Send()
        {
            if (string.IsNullOrWhiteSpace(input))
            { Messages.Message("请输入希望AI完成的任务。", MessageTypeDefOf.RejectInput, false); return; }
            if (AICoopGameComponent.Current != component || !Find.Maps.Contains(map))
            { Messages.Message("地图或存档已变化，请重新划定任务区。", MessageTypeDefOf.RejectInput, false); Close(); return; }
            if (component.SendPlayerChat("请在以下AI任务区执行任务（区域只是本次任务范围，不是战斗区）：\n" + location +
                "\n坐标格式 rows=z:x起点-x终点；只有列出的格子属于任务区。\n任务：" + input.Trim())) Close();
        }
        public override void DoWindowContents(Rect rect)
        {
            if (Event.current.type == EventType.KeyDown &&
                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter)) OnAcceptKeyPressed();
            Widgets.Label(new Rect(0, 0, rect.width, 30), "AI任务区：希望AI在这里做什么？");
            Widgets.Label(new Rect(0, 34, rect.width, 28), "地图 " + map.uniqueID + " · 已选 " + area.TrueCount + " 格 · 白色区域仅本次有效");
            GUI.SetNextControlName("AICoopTaskInput");
            input = Widgets.TextArea(new Rect(0, 68, rect.width, rect.height - 124), input);
            if (focus) { GUI.FocusControl("AICoopTaskInput"); focus = false; }
            Widgets.Label(new Rect(0, rect.height - 48, 300, 24), "Enter发送 · Shift+Enter换行");
            if (Widgets.ButtonText(new Rect(rect.width - 176, rect.height - 42, 80, 32), "取消")) Close();
            if (Widgets.ButtonText(new Rect(rect.width - 88, rect.height - 42, 88, 32), "发送任务")) Send();
        }
    }
}
