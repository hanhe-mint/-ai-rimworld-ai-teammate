using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace AICoopCompanion
{
    public static partial class AICoopActionExecutor
    {
        private static AICoopGameComponent joinConsentGame;
        private static readonly HashSet<string> pendingJoinConsents = new HashSet<string>();
        private sealed class PresetJoinChoice
        {
            public int X, Z, Rotation;
            public bool Force;
            public HashSet<IntVec3> Shared = new HashSet<IntVec3>();
        }

        private static bool OnRoomEdge(CellRect r, IntVec3 c)
        {
            return r.Contains(c) && (c.x == r.minX || c.x == r.maxX || c.z == r.minZ || c.z == r.maxZ);
        }

        private static ThingDef ShellAt(Map map, IntVec3 cell)
        {
            if (!cell.InBounds(map)) return null;
            foreach (Thing thing in cell.GetThingList(map))
            {
                ThingDef def = (thing.def.entityDefToBuild as ThingDef) ?? thing.def;
                if (def.defName == "Wall" || def.IsDoor) return def;
            }
            return null;
        }

        private static Dictionary<IntVec3, ThingDef> PresetShell(string name, int room, int rotation)
        {
            int width, height;
            AICoopPresetManager.GetRotatedRoomSize(name, room, rotation, out width, out height);
            var bounds = new CellRect(0, 0, width, height);
            var shell = new Dictionary<IntVec3, ThingDef>();
            foreach (var item in AICoopPresetManager.GetBuildInstructions(name, room))
            {
                if (item.IsFloor) continue;
                var def = DefDatabase<ThingDef>.GetNamedSilentFail(item.DefName);
                var cell = AICoopPresetManager.TransformRoomCell(name, room, item.X, item.Z, 0, 0, rotation);
                if (def != null && (def.defName == "Wall" || def.IsDoor) && OnRoomEdge(bounds, cell)) shell[cell] = def;
            }
            return shell;
        }

        private static bool ValidateJoinedPreset(Map map, string name, int room, PresetJoinChoice choice,
            List<CellRect> rooms, bool requireDoors, out string reason)
        {
            reason = "preset_door_alignment_unavailable";
            choice.Shared.Clear();
            int width, height;
            AICoopPresetManager.GetRotatedRoomSize(name, room, choice.Rotation, out width, out height);
            var bounds = new CellRect(choice.X, choice.Z, width, height);
            var shell = PresetShell(name, room, choice.Rotation);
            int matchedDoors = 0;
            bool mismatch = false;
            foreach (CellRect old in rooms)
            {
                // A shared edge is allowed, an intersection of room interiors is not.
                if (bounds.minX < old.maxX && bounds.maxX > old.minX && bounds.minZ < old.maxZ && bounds.maxZ > old.minZ)
                { reason = "room_interior_overlap"; return false; }
                foreach (IntVec3 cell in bounds.Cells.Where(c => OnRoomEdge(old, c)))
                {
                    choice.Shared.Add(cell);
                    ThingDef existing = ShellAt(map, cell), incoming;
                    shell.TryGetValue(new IntVec3(cell.x - choice.X, 0, cell.z - choice.Z), out incoming);
                    if (existing == null || incoming == null || existing.IsDoor != incoming.IsDoor) mismatch = true;
                    else if (existing.IsDoor) matchedDoors++;
                }
            }
            if (rooms.Count > 0 && (choice.Shared.Count < 2 || (requireDoors && (matchedDoors == 0 || mismatch)))) return false;
            foreach (IntVec3 cell in bounds.Cells)
            {
                if (!cell.InBounds(map)) { reason = "out_of_bounds"; return false; }
                if (choice.Shared.Contains(cell)) continue;
                if (cell.GetThingList(map).Any(t => t.Faction == Faction.OfPlayer &&
                    (t is Building || t is Blueprint || t is Frame) &&
                    (((t.def.entityDefToBuild as ThingDef) ?? t.def).building != null)))
                { reason = "existing_player_building_in_new_room"; return false; }
            }
            return CanPlaceStructuredSite(map, name, room, choice.X, choice.Z, choice.Rotation, out reason, choice.Shared);
        }

        private static bool ChooseJoinedPreset(AICoopGameComponent component, Pawn actor, string name, int room,
            string[] parts, string line, int rotation, PresetJoinChoice approved, out PresetJoinChoice choice, bool devInstant = false)
        {
            Map map = actor.Map;
            if (joinConsentGame != component) { pendingJoinConsents.Clear(); joinConsentGame = component; }
            string consentKey = map.uniqueID + "|" + name + "|" + room;
            var rooms = component.PresetRoomBounds(map).Where(r => r.Cells.Any(c => OnRoomEdge(r, c) && ShellAt(map, c) != null)).ToList();
            choice = null;
            string reason;
            if (approved == null && pendingJoinConsents.Contains(consentKey))
            { component.AddCommandResult("WAIT preset_join_player_consent=1 file=" + name); return false; }
            if (approved != null)
            {
                if (!ValidateJoinedPreset(map, name, room, approved, rooms, !approved.Force, out reason))
                { component.AddCommandResult("FAIL preset_join_recheck=" + reason); return false; }
                choice = approved;
                return true;
            }
            int x = 0, z = 0;
            bool requested = parts.Length >= 5 && Int32.TryParse(parts[3], out x) && Int32.TryParse(parts[4], out z);
            if (rooms.Count == 0)
            {
                int w, h;
                AICoopPresetManager.GetRotatedRoomSize(name, room, rotation, out w, out h);
                if (!requested && !FindStructuredSite(map, name, room, w, h, rotation, actor.Position, out x, out z))
                { component.AddCommandResult("FAIL preset_build_no_site=1"); return false; }
                choice = new PresetJoinChoice { X = x, Z = z, Rotation = rotation };
                if (!ValidateJoinedPreset(map, name, room, choice, rooms, false, out reason))
                { component.AddCommandResult("FAIL preset_build_invalid_site=" + reason); return false; }
            }
            else
            {
                IntVec3 preferred = requested ? new IntVec3(x, 0, z) : map.Center;
                // Align room footprints first. Door positions must never shift the room.
                int w, h;
                AICoopPresetManager.GetRotatedRoomSize(name, room, rotation, out w, out h);
                var origins = new HashSet<IntVec3>();
                foreach (var old in rooms)
                {
                    origins.Add(new IntVec3(old.maxX, 0, old.minZ));
                    origins.Add(new IntVec3(old.minX - w + 1, 0, old.minZ));
                    origins.Add(new IntVec3(old.minX, 0, old.maxZ));
                    origins.Add(new IntVec3(old.minX, 0, old.minZ - h + 1));
                }
                foreach (var origin in origins.OrderBy(c => (c - preferred).LengthHorizontalSquared).ThenBy(c => c.x).ThenBy(c => c.z))
                {
                    var candidate = new PresetJoinChoice { X = origin.x, Z = origin.z, Rotation = rotation };
                    if (!ValidateJoinedPreset(map, name, room, candidate, rooms, false, out reason)) continue;
                    choice = candidate;
                    break;
                }
                if (choice != null && !ValidateJoinedPreset(map, name, room, choice, rooms, true, out reason))
                {
                    choice.Force = true;
                    foreach (int candidateRotation in new[] { 0, 1, 2, 3 }.Where(r => r != rotation))
                    {
                        int rotatedWidth, rotatedHeight;
                        AICoopPresetManager.GetRotatedRoomSize(name, room, candidateRotation, out rotatedWidth, out rotatedHeight);
                        if (rotatedWidth != w || rotatedHeight != h) continue;
                        var candidate = new PresetJoinChoice { X = choice.X, Z = choice.Z, Rotation = candidateRotation };
                        if (!ValidateJoinedPreset(map, name, room, candidate, rooms, true, out reason)) continue;
                        choice = candidate;
                        break;
                    }
                }
                if (choice == null)
                { component.AddCommandResult("FAIL preset_join_no_safe_site=1"); component.AddPlayerRequest("预设 " + name + " 未找到可安全共墙的位置，请帮助选择或清理建设位置。"); return false; }
            }
            if (choice.Rotation != 0 || choice.Force)
            {
                PresetJoinChoice pending = choice;
                string question = "预设 " + name + "，地图 " + map.uniqueID + "，位置（" + choice.X + "，" + choice.Z + "）。\n";
                if (choice.Rotation != 0) question += "需要旋转 " + choice.Rotation * 90 + " 度，是否允许？\n";
                if (choice.Force) question += "尝试旋转后仍无法完全门对门。是否继续？将只保留原有房间的共享墙门，请您随后调整连接处。\n";
                question += "取消则不会放置蓝图或拆墙。";
                pendingJoinConsents.Add(consentKey);
                Action decline = () =>
                {
                    if (joinConsentGame == component) pendingJoinConsents.Remove(consentKey);
                    if (AICoopGameComponent.Current == component) component.AddCommandResult("FAIL preset_join_player_declined=1 file=" + name);
                };
                component.AddCommandResult("WAIT preset_join_player_consent=1 file=" + name);
                Find.WindowStack.Add(new Dialog_MessageBox(question, "同意建设", () =>
                {
                    if (joinConsentGame == component) pendingJoinConsents.Remove(consentKey);
                    if (AICoopGameComponent.Current != component || !Find.Maps.Contains(map) || !actor.Spawned || actor.Map != map ||
                        (devInstant ? !Prefs.DevMode : (component.ActivePresetName != name || component.ActivePresetRoomIndex != room || !AICoopAgentBridge.IsConnected)))
                    { AICoopGameComponent.Current?.AddCommandResult("FAIL preset_join_consent_expired=1"); return; }
                    string previous = component.ActivePresetName;
                    int previousRoom = component.ActivePresetRoomIndex;
                    try
                    {
                        if (devInstant) component.SetPresetProgress(name, room);
                        BuildStructuredPresetRoom(component, actor, name, parts, line, pending, devInstant);
                    }
                    finally { if (devInstant) component.SetPresetProgress(previous, previousRoom); }
                }, "取消", decline, title: "预设拼接确认", cancelAction: decline));
                return false;
            }
            return true;
        }
    }
}
