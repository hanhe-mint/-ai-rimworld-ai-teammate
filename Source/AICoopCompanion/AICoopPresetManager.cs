using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using RimWorld;
using Verse;

namespace AICoopCompanion
{
    internal sealed class AICoopPresetBuildInstruction
    {
        public string DefName;
        public int X;
        public int Z;
        public int Rotation;
        public string Stuff;
        public bool IsFloor;
        public string FloorDef;
        public ushort FloorHash;
    }

    internal sealed class AICoopPresetZoneInstruction
    {
        public string Type;
        public int X;
        public int Z;
        public int X2;
        public int Z2;
        public bool HasRectangle;
        public string PlantDef;
        public string Label;
    }

    internal sealed class AICoopPresetStateInstruction
    {
        public string Type;
        public int X;
        public int Z;
        public string Owner;
        public bool Medical;
        public bool HoldOpen;
        public bool HasTemperature;
        public float Temperature;
    }

    internal static class AICoopPresetManager
    {
        private sealed class PresetRoom
        {
            public string Id;
            public string Purpose;
            public int Priority;
            public int Width;
            public int Height;
            public List<string> KnownDefs = new List<string>();
            public List<string> Steps = new List<string>();
            public List<AICoopPresetBuildInstruction> Builds = new List<AICoopPresetBuildInstruction>();
            public List<AICoopPresetZoneInstruction> Zones = new List<AICoopPresetZoneInstruction>();
            public List<AICoopPresetStateInstruction> States = new List<AICoopPresetStateInstruction>();
        }

        private sealed class PresetFile
        {
            public string Name;
            public List<PresetRoom> Rooms = new List<PresetRoom>();
        }

        private sealed class PendingState
        {
            public int MapId;
            public IntVec3 Cell;
            public AICoopPresetStateInstruction State;
        }

        private static readonly List<PresetFile> Presets = new List<PresetFile>();
        private static readonly List<PendingState> pendingStates = new List<PendingState>();
        private static readonly MethodInfo AreaSetMethod = typeof(Area).GetMethod("Set", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static bool initialized;
        private static string loadError = string.Empty;

        public static bool IsPresetActive(AICoopGameComponent component)
        {
            EnsureInitialized();
            return component != null && !component.ActivePresetName.NullOrEmpty() && FindPreset(component.ActivePresetName) != null;
        }

        public static bool TryGetAutoBuildFootprint(string name, out int width, out int height)
        {
            return TryGetAutoBuildFootprint(name, 0, out width, out height);
        }

        public static List<string> GetAutoBuildFacilities(string name)
        {
            return GetAutoBuildFacilities(name, 0);
        }

        internal static List<string> GetAutoBuildFacilities(string name, int roomIndex)
        {
            EnsureInitialized();
            PresetFile preset = FindPreset(name);
            return preset == null || roomIndex < 0 || roomIndex >= preset.Rooms.Count
                ? new List<string>()
                : new List<string>(preset.Rooms[roomIndex].KnownDefs);
        }

        public static List<AICoopPresetBuildInstruction> GetBuildInstructions(string name)
        {
            return GetBuildInstructions(name, 0);
        }

        public static List<AICoopPresetBuildInstruction> GetBuildInstructions(string name, int roomIndex)
        {
            EnsureInitialized();
            PresetFile preset = FindPreset(name);
            return preset == null || roomIndex < 0 || roomIndex >= preset.Rooms.Count
                ? new List<AICoopPresetBuildInstruction>()
                : new List<AICoopPresetBuildInstruction>(preset.Rooms[roomIndex].Builds);
        }

        public static List<AICoopPresetZoneInstruction> GetZoneInstructions(string name)
        {
            return GetZoneInstructions(name, 0);
        }

        public static List<AICoopPresetZoneInstruction> GetZoneInstructions(string name, int roomIndex)
        {
            EnsureInitialized();
            PresetFile preset = FindPreset(name);
            return preset == null || roomIndex < 0 || roomIndex >= preset.Rooms.Count
                ? new List<AICoopPresetZoneInstruction>()
                : new List<AICoopPresetZoneInstruction>(preset.Rooms[roomIndex].Zones);
        }

        public static List<AICoopPresetStateInstruction> GetStateInstructions(string name)
        {
            return GetStateInstructions(name, 0);
        }

        public static List<AICoopPresetStateInstruction> GetStateInstructions(string name, int roomIndex)
        {
            EnsureInitialized();
            PresetFile preset = FindPreset(name);
            return preset == null || roomIndex < 0 || roomIndex >= preset.Rooms.Count
                ? new List<AICoopPresetStateInstruction>()
                : new List<AICoopPresetStateInstruction>(preset.Rooms[roomIndex].States);
        }

        public static bool HasStructuredLayout(string name)
        {
            return HasStructuredLayout(name, 0);
        }

        public static bool HasStructuredLayout(string name, int roomIndex)
        {
            EnsureInitialized();
            PresetFile preset = FindPreset(name);
            if (preset == null || roomIndex < 0 || roomIndex >= preset.Rooms.Count) return false;
            PresetRoom room = preset.Rooms[roomIndex];
            return room.Builds.Count > 0 || room.Zones.Count > 0 || room.States.Count > 0;
        }

        public static int GetRoomWidth(string name)
        {
            int width, height;
            return TryGetAutoBuildFootprint(name, out width, out height) ? width : 0;
        }

        public static int GetRoomWidth(string name, int roomIndex)
        {
            int width, height;
            return TryGetAutoBuildFootprint(name, roomIndex, out width, out height) ? width : 0;
        }

        public static int GetRoomHeight(string name)
        {
            int width, height;
            return TryGetAutoBuildFootprint(name, out width, out height) ? height : 0;
        }

        public static int GetRoomHeight(string name, int roomIndex)
        {
            int width, height;
            return TryGetAutoBuildFootprint(name, roomIndex, out width, out height) ? height : 0;
        }

        internal static bool TryGetAutoBuildFootprint(string name, int roomIndex, out int width, out int height)
        {
            width = height = 0;
            EnsureInitialized();
            PresetFile preset = FindPreset(name);
            if (preset == null || roomIndex < 0 || roomIndex >= preset.Rooms.Count) return false;
            width = preset.Rooms[roomIndex].Width;
            height = preset.Rooms[roomIndex].Height;
            return width >= 2 && height >= 2;
        }

        public static void RegisterPendingState(Map map, IntVec3 cell, AICoopPresetStateInstruction state)
        {
            if (map == null || !cell.IsValid || state == null) return;
            pendingStates.RemoveAll(item => item.MapId == map.uniqueID && item.Cell == cell && item.State != null && item.State.Type == state.Type);
            pendingStates.Add(new PendingState { MapId = map.uniqueID, Cell = cell, State = state });
            ApplyPendingState(map, cell);
        }

        public static void ApplyPendingState(Map map, IntVec3 cell)
        {
            if (map == null || !cell.IsValid) return;
            foreach (PendingState pending in pendingStates.Where(item => item.MapId == map.uniqueID && item.Cell == cell).ToList())
            {
                if (ApplyState(map, pending.Cell, pending.State)) pendingStates.Remove(pending);
            }
        }

        public static int ApplyZoneInstructions(Map map, string name, int minX, int minZ)
        {
            return ApplyZoneInstructions(map, name, 0, minX, minZ, 0);
        }

        public static int ApplyZoneInstructions(Map map, string name, int roomIndex, int minX, int minZ)
        {
            return ApplyZoneInstructions(map, name, roomIndex, minX, minZ, 0);
        }

        public static int ApplyZoneInstructions(Map map, string name, int roomIndex, int minX, int minZ, int rotation)
        {
            if (map == null) return 0;
            int width, height;
            if (!TryGetAutoBuildFootprint(name, roomIndex, out width, out height)) return 0;

            int changed = 0;
            foreach (IGrouping<string, AICoopPresetZoneInstruction> group in GetZoneInstructions(name, roomIndex)
                .GroupBy(item => (item.Type ?? string.Empty).ToLowerInvariant() + "|" + (item.PlantDef ?? string.Empty)))
            {
                List<IntVec3> cells = group.SelectMany(item => item.HasRectangle
                        ? Enumerable.Range(Math.Min(item.X, item.X2), Math.Abs(item.X2 - item.X) + 1)
                            .SelectMany(x => Enumerable.Range(Math.Min(item.Z, item.Z2), Math.Abs(item.Z2 - item.Z) + 1)
                                .Select(z => TransformCell(minX, minZ, x, z, width, height, rotation)))
                        : new[] { TransformCell(minX, minZ, item.X, item.Z, width, height, rotation) })
                    .Where(cell => cell.InBounds(map) && (IsAreaPresetType(group.First().Type) || map.zoneManager.ZoneAt(cell) == null))
                    .Distinct().ToList();
                if (cells.Count == 0) continue;

                string type = group.First().Type ?? string.Empty;
                if (type.Equals("growing", StringComparison.OrdinalIgnoreCase))
                {
                    ThingDef plant = DefDatabase<ThingDef>.GetNamedSilentFail(group.First().PlantDef);
                    if (plant == null || plant.plant == null || !plant.plant.Sowable) continue;
                    Zone_Growing zone = new Zone_Growing(map.zoneManager);
                    map.zoneManager.RegisterZone(zone);
                    zone.SetPlantDefToGrow(plant);
                    foreach (IntVec3 cell in cells) { zone.AddCell(cell); changed++; }
                }
                else if (type.Equals("stockpile", StringComparison.OrdinalIgnoreCase) || type.Equals("dumping", StringComparison.OrdinalIgnoreCase))
                {
                    Zone_Stockpile zone = new Zone_Stockpile(type.Equals("dumping", StringComparison.OrdinalIgnoreCase)
                        ? StorageSettingsPreset.DumpingStockpile : StorageSettingsPreset.DefaultStockpile, map.zoneManager);
                    map.zoneManager.RegisterZone(zone);
                    if (!group.First().Label.NullOrEmpty()) zone.RenamableLabel = group.First().Label;
                    foreach (IntVec3 cell in cells) { zone.AddCell(cell); changed++; }
                }
                else if (IsAreaPresetType(type) && AreaSetMethod != null)
                {
                    Area area = FindPresetArea(map, type);
                    if (area == null) continue;
                    foreach (IntVec3 cell in cells)
                    {
                        try { AreaSetMethod.Invoke(area, new object[] { cell, true }); changed++; } catch { }
                    }
                }
            }

            PresetFile presetFile = FindPreset(name);
            if (presetFile != null && roomIndex >= 0 && roomIndex < presetFile.Rooms.Count)
                changed += EnsureOpenAirRoom(map, presetFile.Rooms[roomIndex], minX, minZ, rotation);
            return changed;
        }

        private static int EnsureOpenAirRoom(Map map, PresetRoom room, int minX, int minZ, int rotation)
        {
            if (map == null || room == null || (!room.Id.Equals("pasture_hut", StringComparison.OrdinalIgnoreCase) && !room.Id.Equals("power_standard", StringComparison.OrdinalIgnoreCase))) return 0;
            Area noRoof = FindPresetArea(map, "no_roof");
            Area buildRoof = FindPresetArea(map, "build_roof");
            if (noRoof == null || AreaSetMethod == null) return 0;
            int changed = 0;
            for (int x = 0; x < room.Width; x++) for (int z = 0; z < room.Height; z++)
            {
                IntVec3 cell = TransformCell(minX, minZ, x, z, room.Width, room.Height, rotation);
                if (!cell.InBounds(map)) continue;
                try
                {
                    AreaSetMethod.Invoke(noRoof, new object[] { cell, true });
                    if (buildRoof != null) AreaSetMethod.Invoke(buildRoof, new object[] { cell, false });
                    changed++;
                }
                catch { }
            }
            return changed;
        }

        public static TerrainDef ResolveTerrain(string defName, ushort shortHash)
        {
            TerrainDef terrain = defName.NullOrEmpty() ? null : DefDatabase<TerrainDef>.GetNamedSilentFail(defName);
            if (terrain != null || shortHash == 0) return terrain;
            return DefDatabase<TerrainDef>.AllDefsListForReading.FirstOrDefault(candidate => candidate != null && candidate.shortHash == shortHash);
        }

        private static bool IsAreaPresetType(string type)
        {
            return type.Equals("home", StringComparison.OrdinalIgnoreCase) || type.Equals("no_roof", StringComparison.OrdinalIgnoreCase) || type.Equals("build_roof", StringComparison.OrdinalIgnoreCase);
        }

        private static Area FindPresetArea(Map map, string type)
        {
            if (map == null || map.areaManager == null) return null;
            if (type.Equals("home", StringComparison.OrdinalIgnoreCase)) return map.areaManager.Home;
            if (type.Equals("no_roof", StringComparison.OrdinalIgnoreCase) && map.areaManager.NoRoof != null) return map.areaManager.NoRoof;
            if (type.Equals("build_roof", StringComparison.OrdinalIgnoreCase) && map.areaManager.BuildRoof != null) return map.areaManager.BuildRoof;
            string className = type.Equals("no_roof", StringComparison.OrdinalIgnoreCase) ? "Area_NoRoof" : "Area_BuildRoof";
            return map.areaManager.AllAreas.FirstOrDefault(area => area != null && area.GetType().Name.Equals(className, StringComparison.Ordinal));
        }

        public static IntVec3 TransformRoomCell(string name, int roomIndex, int x, int z, int minX, int minZ, int rotation)
        {
            int width, height;
            return TryGetAutoBuildFootprint(name, roomIndex, out width, out height)
                ? TransformCell(minX, minZ, x, z, width, height, rotation) : IntVec3.Invalid;
        }

        public static bool AreAllBuildsComplete(string name, int roomIndex, Map map, int minX, int minZ, int rotation)
        {
            if (map == null) return false;
            List<AICoopPresetBuildInstruction> instructions = GetBuildInstructions(name, roomIndex);
            if (instructions.Count == 0) return false;
            foreach (AICoopPresetBuildInstruction instruction in instructions)
            {
                if (instruction == null || instruction.IsFloor) continue;
                ThingDef buildDef = DefDatabase<ThingDef>.GetNamedSilentFail(instruction.DefName);
                if (buildDef == null) return false;
                IntVec3 cell = TransformRoomCell(name, roomIndex, instruction.X, instruction.Z, minX, minZ, rotation);
                if (!cell.IsValid || !cell.InBounds(map) || !cell.GetThingList(map).Any(thing => thing is Building && thing.def == buildDef)) return false;
            }
            return true;
        }

        public static void GetRotatedRoomSize(string name, int roomIndex, int rotation, out int width, out int height)
        {
            int originalWidth, originalHeight;
            if (!TryGetAutoBuildFootprint(name, roomIndex, out originalWidth, out originalHeight)) { width = height = 0; return; }
            if (((rotation % 4) + 4) % 2 == 1) { width = originalHeight; height = originalWidth; }
            else { width = originalWidth; height = originalHeight; }
        }

        public static bool TryGetRelativeBuildCell(string name, int roomIndex, string defName, out int x, out int z)
        {
            x = z = 0;
            if (defName.NullOrEmpty()) return false;
            AICoopPresetBuildInstruction instruction = GetBuildInstructions(name, roomIndex)
                .FirstOrDefault(item => item != null && !item.IsFloor && string.Equals(item.DefName, defName, StringComparison.OrdinalIgnoreCase));
            if (instruction == null) return false;
            x = instruction.X;
            z = instruction.Z;
            return true;
        }

        private static IntVec3 TransformCell(int minX, int minZ, int x, int z, int width, int height, int rotation)
        {
            int tx = x, tz = z;
            switch (((rotation % 4) + 4) % 4)
            {
                case 1: tx = height - 1 - z; tz = x; break;
                case 2: tx = width - 1 - x; tz = height - 1 - z; break;
                case 3: tx = z; tz = width - 1 - x; break;
            }
            return new IntVec3(minX + tx, 0, minZ + tz);
        }

        private static bool ApplyState(Map map, IntVec3 cell, AICoopPresetStateInstruction state)
        {
            if (map == null || state == null) return false;
            if (state.Type.Equals("bed", StringComparison.OrdinalIgnoreCase))
            {
                Building_Bed bed = cell.GetThingList(map).OfType<Building_Bed>().FirstOrDefault();
                if (bed == null) return false;
                if (state.Owner.Equals("prisoner", StringComparison.OrdinalIgnoreCase)) bed.ForOwnerType = BedOwnerType.Prisoner;
                else if (state.Owner.Equals("slave", StringComparison.OrdinalIgnoreCase)) bed.ForOwnerType = BedOwnerType.Slave;
                else if (state.Owner.Equals("colonist", StringComparison.OrdinalIgnoreCase)) bed.ForOwnerType = BedOwnerType.Colonist;
                bed.Medical = state.Medical;
                MethodInfo notify = typeof(Building_Bed).GetMethod("NotifyRoomBedTypeChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (notify != null) notify.Invoke(bed, null);
                return true;
            }
            if (state.Type.Equals("door", StringComparison.OrdinalIgnoreCase))
            {
                Building_Door door = cell.GetThingList(map).OfType<Building_Door>().FirstOrDefault();
                if (door == null) return false;
                bool changed = SetMember(door, "Open", state.HoldOpen) | SetMember(door, "open", state.HoldOpen) | SetMember(door, "openInt", state.HoldOpen);
                if (state.HoldOpen) changed = SetMember(door, "HoldOpen", true) | SetMember(door, "holdOpen", true) | SetMember(door, "holdOpenInt", true) | changed;
                return changed;
            }
            if (state.Type.Equals("cooler", StringComparison.OrdinalIgnoreCase) ||
                state.Type.Equals("temperature", StringComparison.OrdinalIgnoreCase))
            {
                if (!state.HasTemperature) return false;
                Building building = cell.GetThingList(map).OfType<Building>().FirstOrDefault();
                CompTempControl control = building == null ? null : building.TryGetComp<CompTempControl>();
                if (control == null) return false;
                control.TargetTemperature = state.Temperature;
                return true;
            }
            return false;
        }

        private static bool SetMember(object instance, string name, object value)
        {
            if (instance == null || value == null) return false;
            try
            {
                PropertyInfo property = instance.GetType().GetProperty(name, BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (property != null && property.CanWrite && property.PropertyType.IsAssignableFrom(value.GetType())) { property.SetValue(instance, value, null); return true; }
                FieldInfo field = instance.GetType().GetField(name, BindingFlags.IgnoreCase | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null && field.FieldType.IsAssignableFrom(value.GetType())) { field.SetValue(instance, value); return true; }
            }
            catch { }
            return false;
        }

        public static string ActiveRoomPurpose(AICoopGameComponent component)
        {
            EnsureInitialized();
            PresetFile preset = component == null ? null : FindPreset(component.ActivePresetName);
            if (preset == null || component.ActivePresetRoomIndex < 0 || component.ActivePresetRoomIndex >= preset.Rooms.Count) return "preset";
            return "preset:" + preset.Name + ":" + preset.Rooms[component.ActivePresetRoomIndex].Id;
        }

        public static string BuildPromptSection(AICoopGameComponent component)
        {
            EnsureInitialized();
            if (!loadError.NullOrEmpty()) return "PRESETS unavailable reason=" + loadError;
            if (Presets.Count == 0) return "PRESETS none directory=Presets";

            StringBuilder result = new StringBuilder();
            result.AppendLine("PRESETS available=" + string.Join(",", Presets.Select(preset => preset.Name).ToArray()) +
                " active=" + (component == null || component.ActivePresetName.NullOrEmpty() ? "-" : component.ActivePresetName));
            result.AppendLine("PRESET_CATALOG " + string.Join(",", Presets
                .Where(preset => preset.Rooms.Count > 0)
                .Select(preset => preset.Name + ":" + preset.Rooms[0].Id + ":" + preset.Rooms[0].Purpose + ":priority=" + preset.Rooms[0].Priority)
                .ToArray()));
            if (component == null || component.ActivePresetName.NullOrEmpty())
            {
                return result.ToString().TrimEnd();
            }

            PresetFile preset = FindPreset(component.ActivePresetName);
            if (preset == null)
            {
                result.AppendLine("PRESET_ERROR 当前预设文件不存在；用 REQUEST_PLAYER 告知玩家并停止本次预设建设。");
                return result.ToString().TrimEnd();
            }
            if (component.ActivePresetRoomIndex >= preset.Rooms.Count)
            {
                result.AppendLine("PRESET_COMPLETE file=" + preset.Name);
                return result.ToString().TrimEnd();
            }

            PresetRoom room = preset.Rooms[component.ActivePresetRoomIndex];
            result.AppendLine("PRESET_ROOM file=" + preset.Name + " index=" + component.ActivePresetRoomIndex + "/" + preset.Rooms.Count +
                " id=" + room.Id + " purpose=" + room.Purpose + " priority=" + room.Priority);
            foreach (string step in room.Steps) result.AppendLine("PRESET_STEP " + step);
            if (component.ActivePresetRoomIndex + 1 < preset.Rooms.Count)
            {
                result.AppendLine("PRESET_FUTURE_ROOMS " + string.Join(",", preset.Rooms.Skip(component.ActivePresetRoomIndex + 1)
                    .Select(next => next.Id + ":" + next.Purpose + ":priority=" + next.Priority).ToArray()));
            }
            result.AppendLine("PRESET_ACTIVE_RULE 先按 PRESET_STEP 恢复本房间的 BUILD/FLOOR/ZONE/STATE；MAP 替换为当前地图 ID，旋转值 0-3 必须统一应用于全部相对坐标。预设只固定房间用途/房间类型；只要最终房间类型不变，允许自由增加、删除或调整墙、门、地板、设施、温控和状态，但不得混入其他用途或让房间失去有效封闭性；待定设施见 PRESET_FAILURES；房间稳定可用后输出 PRESET_DONE " + room.Id + "。");
            if (string.Equals(room.Id, "pasture_hut", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(room.Id, "power_standard", StringComparison.OrdinalIgnoreCase))
                result.AppendLine("PRESET_OPEN_AIR 该房间整个 footprint 必须使用移除屋顶区 no_roof，不得使用或恢复 build_roof 区域。");
            return result.ToString().TrimEnd();
        }

        public static bool Activate(AICoopGameComponent component, string name)
        {
            EnsureInitialized();
            PresetFile preset;
            int requestedRoomIndex;
            bool aliasSelection;
            bool found = TryFindPresetSelection(name, out preset, out requestedRoomIndex, out aliasSelection);
            if (component == null || !found || preset == null)
            {
                if (component != null)
                {
                    component.AddWorkMessage("系统", "找不到殖民地预设：" + name);
                    component.AddCommandResult("FAIL preset_not_found=1 name=" + (name ?? string.Empty));
                }
                return false;
            }
            bool samePreset = string.Equals(component.ActivePresetName, preset.Name, StringComparison.OrdinalIgnoreCase);
            if (!samePreset || (aliasSelection && component.ActivePresetRoomIndex != requestedRoomIndex))
                component.SetPresetProgress(preset.Name, requestedRoomIndex);
            component.AddWorkMessage("AI", (samePreset
                ? "继续殖民地预设 " + preset.Name + "，当前房间序号 " + (component.ActivePresetRoomIndex + 1)
                : "已激活殖民地预设 " + preset.Name + "，当前房间序号 " + (component.ActivePresetRoomIndex + 1)) + "；每次只处理这一个房间，完成后不会自动推进其他房间。");
            component.AddCommandResult("OK preset_activated=" + preset.Name);
            return true;
        }

        public static bool CompleteRoom(AICoopGameComponent component, string roomId)
        {
            EnsureInitialized();
            PresetFile preset = component == null ? null : FindPreset(component.ActivePresetName);
            if (preset == null || component.ActivePresetRoomIndex >= preset.Rooms.Count)
            {
                if (component != null) component.AddCommandResult("FAIL preset_room_not_active=1");
                return false;
            }
            PresetRoom current = preset.Rooms[component.ActivePresetRoomIndex];
            if (!string.Equals(current.Id, roomId, StringComparison.OrdinalIgnoreCase))
            {
                component.AddCommandResult("FAIL preset_room_mismatch=1 expected=" + current.Id);
                return false;
            }
            component.TryRecordDefensePresetCompletion();
            // A PRESET command owns exactly one room.  Completing it must not
            // silently advance through the rest of a multi-room layout.
            component.SetPresetProgress(string.Empty, 0);
            component.AddCommandResult("OK preset_room_completed=" + roomId);
            component.AddWorkMessage("AI", "预设房间 " + roomId + " 已完成；当前预设已释放。需要其他房间时请按需求重新选择对应 PRESET。每次只建设一个房间。");
            return true;
        }

        private static PresetFile FindPreset(string name)
        {
            string requested = NormalizePresetName(name);
            return Presets.FirstOrDefault(preset => string.Equals(preset.Name, name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(NormalizePresetName(preset.Name), requested, StringComparison.OrdinalIgnoreCase));
        }

        private static string NormalizePresetName(string name)
        {
            string file = Path.GetFileNameWithoutExtension(name ?? string.Empty);
            return file.StartsWith("room_", StringComparison.OrdinalIgnoreCase) ? file.Substring(5) : file;
        }

        private static bool TryFindPresetSelection(string name, out PresetFile preset, out int roomIndex, out bool aliasSelection)
        {
            preset = FindPreset(name);
            roomIndex = 0;
            aliasSelection = false;
            if (preset != null) return true;
            string requestedId = Path.GetFileNameWithoutExtension(name ?? string.Empty);
            if (requestedId.StartsWith("room_", StringComparison.OrdinalIgnoreCase)) requestedId = requestedId.Substring(5);
            foreach (PresetFile candidate in Presets)
            {
                for (int index = 0; index < candidate.Rooms.Count; index++)
                {
                    if (!string.Equals(candidate.Rooms[index].Id, requestedId, StringComparison.OrdinalIgnoreCase)) continue;
                    preset = candidate;
                    roomIndex = index;
                    aliasSelection = true;
                    return true;
                }
            }
            return false;
        }

        private static void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;
            try
            {
                string directory = PresetDirectory();
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                // All preset files are data-only text files.  Do not restrict loading
                // to room_ so dedicated defense layouts are available as well.
                List<string> paths = Directory.GetFiles(directory, "*.txt")
                    .Where(path => !Path.GetFileName(path).Equals("README.txt", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase).ToList();
                foreach (string path in paths)
                {
                    PresetFile preset = ParseFile(path);
                    if (preset.Rooms.Count > 0) Presets.Add(preset);
                }
            }
            catch (Exception ex)
            {
                loadError = ex.GetType().Name;
            }
        }

        private static PresetFile ParseFile(string path)
        {
            PresetFile preset = new PresetFile { Name = Path.GetFileName(path) };
            PresetRoom current = null;
            foreach (string rawLine in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = rawLine.Trim();
                if (line.NullOrEmpty() || line.StartsWith("#", StringComparison.Ordinal)) continue;
                if (line.StartsWith("ROOM ", StringComparison.OrdinalIgnoreCase))
                {
                    string id = Attribute(line, "id", "ROOM" + (preset.Rooms.Count + 1));
                    current = new PresetRoom
                    {
                        Id = id,
                        Purpose = Attribute(line, "purpose", "未命名"),
                        Priority = ParsePriority(Attribute(line, "priority", "5"))
                    };
                    preset.Rooms.Add(current);
                    continue;
                }
                if (current != null && line.StartsWith("FOOTPRINT ", StringComparison.OrdinalIgnoreCase))
                {
                    System.Text.RegularExpressions.Match footprint = System.Text.RegularExpressions.Regex.Match(line, "(\\d+)x(\\d+)");
                    if (footprint.Success)
                    {
                        Int32.TryParse(footprint.Groups[1].Value, out current.Width);
                        Int32.TryParse(footprint.Groups[2].Value, out current.Height);
                    }
                    current.Steps.Add(line);
                    continue;
                }
                if (current != null && line.StartsWith("KNOWN_DEFS ", StringComparison.OrdinalIgnoreCase))
                {
                    current.KnownDefs.AddRange(line.Substring("KNOWN_DEFS ".Length)
                        .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(defName => defName.Trim())
                        .Where(defName => !defName.NullOrEmpty()));
                    current.Steps.Add(line);
                    continue;
                }
                if (current != null && line.StartsWith("BUILD ", StringComparison.OrdinalIgnoreCase))
                {
                    string[] tokens = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    int x, z, rotation = 0;
                    if (tokens.Length >= 4 && Int32.TryParse(tokens[2], out x) && Int32.TryParse(tokens[3], out z))
                    {
                        if (tokens.Length >= 5) Int32.TryParse(tokens[4], out rotation);
                        current.Builds.Add(new AICoopPresetBuildInstruction
                        {
                            DefName = tokens[1], X = x, Z = z, Rotation = Math.Max(0, Math.Min(3, rotation)),
                            Stuff = tokens.Length >= 6 && tokens[5] != "-" ? tokens[5] : null
                        });
                    }
                    current.Steps.Add(line);
                    continue;
                }
                if (current != null && line.StartsWith("FLOOR ", StringComparison.OrdinalIgnoreCase))
                {
                    string[] tokens = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    int x, z;
                    if (tokens.Length >= 4 && Int32.TryParse(tokens[2], out x) && Int32.TryParse(tokens[3], out z))
                    {
                        current.Builds.Add(new AICoopPresetBuildInstruction { IsFloor = true, FloorDef = tokens[1], X = x, Z = z, Rotation = 0 });
                    }
                    current.Steps.Add(line);
                    continue;
                }
                if (current != null && line.StartsWith("FLOOR_HASH ", StringComparison.OrdinalIgnoreCase))
                {
                    string[] tokens = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    int x, z;
                    ushort hash;
                    if (tokens.Length >= 4 && UInt16.TryParse(tokens[1], out hash) && Int32.TryParse(tokens[2], out x) && Int32.TryParse(tokens[3], out z))
                        current.Builds.Add(new AICoopPresetBuildInstruction { IsFloor = true, FloorHash = hash, X = x, Z = z, Rotation = 0 });
                    current.Steps.Add(line);
                    continue;
                }
                if (current != null && line.StartsWith("ZONE ", StringComparison.OrdinalIgnoreCase))
                {
                    string[] tokens = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    int x = 0, z = 0, x2 = 0, z2 = 0;
                    bool rectangle = tokens.Length >= 6 && Int32.TryParse(tokens[2], out x) && Int32.TryParse(tokens[3], out z) &&
                        Int32.TryParse(tokens[4], out x2) && Int32.TryParse(tokens[5], out z2);
                    if (rectangle || (tokens.Length >= 4 && Int32.TryParse(tokens[2], out x) && Int32.TryParse(tokens[3], out z)))
                    {
                        current.Zones.Add(new AICoopPresetZoneInstruction
                        {
                            Type = tokens[1], X = x, Z = z, X2 = rectangle ? x2 : x, Z2 = rectangle ? z2 : z, HasRectangle = rectangle,
                            PlantDef = rectangle ? (tokens.Length >= 7 && tokens[6] != "-" ? tokens[6] : null) : (tokens.Length >= 5 && tokens[4] != "-" ? tokens[4] : null),
                            Label = rectangle ? (tokens.Length >= 8 ? tokens[7].Replace('_', ' ') : null) : (tokens.Length >= 6 ? tokens[5].Replace('_', ' ') : null)
                        });
                    }
                    current.Steps.Add(line);
                    continue;
                }
                if (current != null && line.StartsWith("STATE ", StringComparison.OrdinalIgnoreCase))
                {
                    string[] tokens = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                    int x, z;
                    if (tokens.Length >= 4 && Int32.TryParse(tokens[2], out x) && Int32.TryParse(tokens[3], out z))
                    {
                        AICoopPresetStateInstruction state = new AICoopPresetStateInstruction { Type = tokens[1], X = x, Z = z };
                        for (int i = 4; i < tokens.Length; i++)
                        {
                            string[] pair = tokens[i].Split(new[] { '=' }, 2);
                            if (pair.Length != 2) continue;
                            if (pair[0].Equals("owner", StringComparison.OrdinalIgnoreCase)) state.Owner = pair[1];
                            else if (pair[0].Equals("medical", StringComparison.OrdinalIgnoreCase)) state.Medical = pair[1] == "1" || pair[1].Equals("true", StringComparison.OrdinalIgnoreCase);
                            else if (pair[0].Equals("hold_open", StringComparison.OrdinalIgnoreCase)) state.HoldOpen = pair[1] == "1" || pair[1].Equals("true", StringComparison.OrdinalIgnoreCase);
                            else if (pair[0].Equals("temperature", StringComparison.OrdinalIgnoreCase))
                            {
                                float temperature;
                                if (Single.TryParse(pair[1], System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out temperature))
                                {
                                    state.Temperature = temperature;
                                    state.HasTemperature = true;
                                }
                            }
                        }
                        current.States.Add(state);
                    }
                    current.Steps.Add(line);
                    continue;
                }
                if (line.Equals("END_ROOM", StringComparison.OrdinalIgnoreCase))
                {
                    current = null;
                    continue;
                }
                if (current != null) current.Steps.Add(line);
            }
            NormalizePreset(preset);
            return preset;
        }

        private static void NormalizePreset(PresetFile preset)
        {
            if (preset == null) return;
            foreach (PresetRoom room in preset.Rooms)
            {
                if (!string.Equals(room.Id, "pasture_hut", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(room.Id, "power_standard", StringComparison.OrdinalIgnoreCase)) continue;
                room.Zones.RemoveAll(zone => zone != null && string.Equals(zone.Type, "build_roof", StringComparison.OrdinalIgnoreCase));
                room.Steps.RemoveAll(step => step != null && step.StartsWith("ZONE build_roof ", StringComparison.OrdinalIgnoreCase));
            }
        }

        private static string Attribute(string line, string key, string fallback)
        {
            string prefix = key + "=";
            foreach (string token in line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return token.Substring(prefix.Length);
            }
            return fallback;
        }

        private static int ParsePriority(string value)
        {
            int priority;
            return Int32.TryParse(value, out priority) ? Math.Max(1, Math.Min(9, priority)) : 5;
        }

        private static string PresetDirectory()
        {
            return AICoopMod.Instance == null || AICoopMod.Instance.Content == null
                ? string.Empty
                : Path.Combine(AICoopMod.Instance.Content.RootDir, "Presets");
        }
    }
}
