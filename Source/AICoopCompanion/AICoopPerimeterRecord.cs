using System;
using System.Collections.Generic;
using System.Linq;
using Verse;

namespace AICoopCompanion
{
    public sealed class AICoopPerimeterRecord : IExposable
    {
        public int mapId;
        public List<IntVec3> ring = new List<IntVec3>();
        private string lastNotification = "";
        private string checkedBounds;
        public void ExposeData()
        {
            Scribe_Values.Look(ref mapId, "mapId");
            Scribe_Collections.Look(ref ring, "ring", LookMode.Value);
            Scribe_Values.Look(ref lastNotification, "lastNotification", "");
        }

        public string Check()
        {
            Map map = Find.Maps.FirstOrDefault(m => m.uniqueID == mapId);
            int x1, z1, x2, z2;
            if (map == null || ring == null || ring.Count == 0 ||
                !AICoopActionExecutor.TryGetHomeBounds(map, out x1, out z1, out x2, out z2)) return "";
            string bounds = x1 + ":" + z1 + "-" + x2 + ":" + z2;
            if (bounds == checkedBounds) return "";
            checkedBounds = bounds;
            var blocked = new HashSet<IntVec3>(ring);
            var outside = OutsideCells(map);
            bool expanded = false;
            for (int x = x1; x <= x2 && !expanded; x++)
                for (int z = z1; z <= z2; z++)
                    if (outside.Contains(new IntVec3(x, 0, z)) || blocked.Contains(new IntVec3(x, 0, z))) { expanded = true; break; }
            if (!expanded) { lastNotification = ""; return ""; }
            if (lastNotification == bounds) return "";
            lastNotification = bounds;
            return "PERIMETER_EXPANDED map=" + mapId + " residential_bounds=" + bounds +
                " 居住区已超出上次围墙，请按需使用F包围所有居住区（包括不相连的部分），只建一层矩形围墙，地形受阻才绕行。";
        }

        public HashSet<IntVec3> OutsideCells(Map map)
        {
            // Treat intentional entrances as closed for enclosure and old-wall tests.
            var blocked = new HashSet<IntVec3>(ring);
            var outside = new HashSet<IntVec3>();
            var queue = new Queue<IntVec3>();
            foreach (IntVec3 cell in map.AllCells)
                if ((cell.x == 0 || cell.z == 0 || cell.x == map.Size.x - 1 || cell.z == map.Size.z - 1) && !blocked.Contains(cell) && outside.Add(cell)) queue.Enqueue(cell);
            while (queue.Count > 0)
            {
                IntVec3 cell = queue.Dequeue();
                foreach (IntVec3 direction in GenAdj.CardinalDirections)
                {
                    IntVec3 next = cell + direction;
                    if (next.InBounds(map) && !blocked.Contains(next) && outside.Add(next)) queue.Enqueue(next);
                }
            }
            return outside;
        }
    }
}
