using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RimWorld;
using Verse;

namespace AICoopCompanion
{
    public enum AICoopOwner
    {
        Player,
        AI
    }

    public sealed class AICoopGameComponent : GameComponent
    {
        private Dictionary<int, AICoopOwner> owners = new Dictionary<int, AICoopOwner>();
        private List<string> logLines = new List<string>();
        private List<string> commandResults = new List<string>();
        private List<string> constructionFailureCounts = new List<string>();
        private List<string> constructionAssignedBuilders = new List<string>();
        private List<string> constructionsWaitingForPlayer = new List<string>();
        private List<string> roomPurposeRecords = new List<string>();
        private List<string> workMessages = new List<string>();
        private List<string> chatMessages = new List<string>();
        private List<string> playerRequests = new List<string>();
        private List<string> presetFailures = new List<string>();
        private List<string> aiPlannedConstructions = new List<string>();
        private List<int> aiCapturedPrisoners = new List<int>();
        private List<string> organSurgeryStages = new List<string>();
        // AI-issued wear jobs are marked playerForced by RimWorld's job tracker.
        // Clear that flag after the apparel is actually worn.
        private List<string> pendingNonForcedApparel = new List<string>();
        // Quest notifications are save-scoped: an unaccepted quest is still only
        // announced once after a reload, so the agent can decide without repeats.
        private List<int> reportedQuestIds = new List<int>();
        // Long-running plans are stored with the save so a reload can resume them.
        private List<string> agentTaskNotes = new List<string>();
        private int nextAgentTaskNoteId;
        private string lastPublicPlan = string.Empty;
        private int guideFileIndex;
        private int guideTaskIndex;
        private bool guideFileRead;
        private bool guideCompleted;
        private string activePresetName = string.Empty;
        private int activePresetRoomIndex;
        private List<string> completedDefensePresetRecords = new List<string>();
        private List<string> defensePresetPlacements = new List<string>();
        private int lastKnownPawnCount;
        private int lastAIPollTick;
        private int lastNeedsReviewTick;
        private bool mentalBreakWasActive;
        private bool needsReviewPending;
        private string lastAgentRecord = string.Empty;
        private int lastAgentRecordTick;
        private int lastSavedGameTick;
        private bool loadedFromSave;

        public AICoopGameComponent(Game game)
        {
        }

        public static AICoopGameComponent Current
        {
            get
            {
                if (Verse.Current.Game == null) return null;
                return (AICoopGameComponent)Verse.Current.Game.GetComponent(typeof(AICoopGameComponent));
            }
        }

        public IReadOnlyList<string> LogLines { get { return logLines; } }
        public IReadOnlyList<string> CommandResults { get { return commandResults; } }
        public IReadOnlyList<string> WorkMessages { get { return workMessages; } }
        public IReadOnlyList<string> ChatMessages { get { return chatMessages; } }
        public IReadOnlyList<string> PlayerRequests { get { return playerRequests; } }
        public IReadOnlyList<string> AgentTaskNotes { get { return agentTaskNotes; } }
        public IReadOnlyList<int> ReportedQuestIds { get { return reportedQuestIds; } }
        public IReadOnlyList<string> PresetFailures { get { return presetFailures; } }
        public IReadOnlyList<int> AICapturedPrisoners { get { return aiCapturedPrisoners; } }
        public string LastPublicPlan { get { return lastPublicPlan; } }
        public int GuideFileIndex { get { return guideFileIndex; } }
        public int GuideTaskIndex { get { return guideTaskIndex; } }
        public bool GuideFileRead { get { return guideFileRead; } }
        public bool GuideCompleted { get { return guideCompleted; } }
        public string ActivePresetName { get { return activePresetName; } }
        public int ActivePresetRoomIndex { get { return activePresetRoomIndex; } }
        public string LastAgentRecord { get { return lastAgentRecord; } }
        public int LastAgentRecordTick { get { return lastAgentRecordTick; } }
        public int LastSavedGameTick { get { return lastSavedGameTick; } }

        public override void FinalizeInit()
        {
            AICoopKitingManager.Reset();
            AICoopAgentRuntime.NotifyGameReady();
            AICoopAgentBridge.NotifyGameReady();
            AICoopActionExecutor.ClearPendingPermissions();
            EnsureOwnership();
            if (Find.TickManager != null)
            {
                int currentTick = Find.TickManager.TicksGame;
                lastAIPollTick = currentTick;
                lastNeedsReviewTick = currentTick;
            }
            needsReviewPending = false;
            mentalBreakWasActive = false;
            if (logLines.Count == 0)
            {
                AddLog("[系统] Agent 桥接已就绪；请在 DeepSeek Harness 中手动连接游戏。");
            }
            string reason = loadedFromSave ? "save_loaded" : "game_ready";
            string detail = loadedFromSave
                ? "玩家刚刚读档。存档时间=" + FormatGameTime(lastSavedGameTick) +
                    "；读档后时间=" + FormatGameTime(Find.TickManager == null ? 0 : Find.TickManager.TicksGame) +
                    "；AI最新记录=" + (lastAgentRecord.NullOrEmpty() ? "无" : lastAgentRecord)
                : "游戏存档已进入；下一条事件会附带当前殖民地状态，可连续调用 CLI 完成本轮动作后输出一次队友回复。";
            AICoopAgentBridge.QueueAgentTrigger(reason, detail);
            loadedFromSave = false;
        }

        public override void GameComponentTick()
        {
            AICoopAgentRuntime.Update();
            if (AICoopAgentRuntime.IsGameLoading) return;
            AICoopKitingManager.Tick();
            ProcessPendingNonForcedApparel();
            if (Find.TickManager.TicksGame % 60 == 0)
            {
                EnsureOwnership();
                EnsureInitialDumpingStockpiles();
            }

            int currentTick = Find.TickManager.TicksGame;
            bool mentalBreakActive = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists
                .Any(pawn => pawn != null && pawn.InMentalState);
            bool mentalBreakStarted = mentalBreakActive && !mentalBreakWasActive;
            mentalBreakWasActive = mentalBreakActive;
            bool halfDayDue = currentTick - lastNeedsReviewTick >= 30000;
            if (halfDayDue || mentalBreakStarted) needsReviewPending = true;

            AICoopSettings settings = AICoopMod.Settings;
            if (settings == null) return;
            if (needsReviewPending)
            {
                lastNeedsReviewTick = currentTick;
                needsReviewPending = false;
                string reviewReason = mentalBreakStarted ? "mental_break" : (halfDayDue ? "half_day_needs" : "needs_review");
                string reviewDetails = mentalBreakStarted
                    ? "检测到殖民者发生精神崩溃，请立即检查安全和需求。"
                    : (halfDayDue ? "已到游戏内半日需求检查，请检查殖民者需求和殖民地稳定性。" : "游戏检测到床位、俘虏或其他需要复查的殖民地变化。");
                AICoopAgentBridge.QueueAgentTrigger(reviewReason, reviewDetails);
            }
            // Zero means no inter-round wait, not a new event on every game tick.
            int externalIntervalTicks = Math.Max(60, settings.decisionIntervalSeconds * 60);
            int externalRequiredDelay = lastAIPollTick == 0 ? 300 : externalIntervalTicks;
            if (currentTick - lastAIPollTick >= externalRequiredDelay)
            {
                lastAIPollTick = currentTick;
                AICoopAgentBridge.QueueAgentTrigger("scheduled_decision", "定时自主决策已到；下一条事件会附带变化后的殖民地状态，可连续调用 CLI 完成本轮动作后输出一次队友回复。");
            }
        }

        public override void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                AICoopAgentBridge.NotifyGameLoading();
                AICoopAgentRuntime.NotifyGameLoading();
                loadedFromSave = true;
            }
            Scribe_Collections.Look(ref owners, "owners", LookMode.Value, LookMode.Value);
            List<string> savedLogSnapshot = Scribe.mode == LoadSaveMode.Saving
                ? new List<string>(logLines ?? new List<string>())
                : null;
            Scribe_Collections.Look(ref savedLogSnapshot, "logLines", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                logLines = savedLogSnapshot ?? new List<string>();
            }
            Scribe_Collections.Look(ref commandResults, "commandResults", LookMode.Value);
            Scribe_Collections.Look(ref constructionFailureCounts, "constructionFailureCounts", LookMode.Value);
            Scribe_Collections.Look(ref constructionAssignedBuilders, "constructionAssignedBuilders", LookMode.Value);
            Scribe_Collections.Look(ref constructionsWaitingForPlayer, "constructionsWaitingForPlayer", LookMode.Value);
            Scribe_Collections.Look(ref roomPurposeRecords, "roomPurposeRecords", LookMode.Value);
            Scribe_Collections.Look(ref workMessages, "workMessages", LookMode.Value);
            Scribe_Collections.Look(ref chatMessages, "chatMessages", LookMode.Value);
            if (chatMessages == null) chatMessages = new List<string>();
            Scribe_Collections.Look(ref playerRequests, "playerRequests", LookMode.Value);
            Scribe_Collections.Look(ref presetFailures, "presetFailures", LookMode.Value);
            Scribe_Collections.Look(ref aiPlannedConstructions, "aiPlannedConstructions", LookMode.Value);
            Scribe_Collections.Look(ref aiCapturedPrisoners, "aiCapturedPrisoners", LookMode.Value);
            Scribe_Collections.Look(ref organSurgeryStages, "organSurgeryStages", LookMode.Value);
            Scribe_Collections.Look(ref pendingNonForcedApparel, "pendingNonForcedApparel", LookMode.Value);
            Scribe_Collections.Look(ref reportedQuestIds, "reportedQuestIds", LookMode.Value);
            Scribe_Collections.Look(ref agentTaskNotes, "agentTaskNotes", LookMode.Value);
            Scribe_Values.Look(ref nextAgentTaskNoteId, "nextAgentTaskNoteId", 0);
            Scribe_Values.Look(ref lastPublicPlan, "lastPublicPlan", string.Empty);
            Scribe_Values.Look(ref guideFileIndex, "guideFileIndex", 0);
            Scribe_Values.Look(ref guideTaskIndex, "guideTaskIndex", 0);
            Scribe_Values.Look(ref guideFileRead, "guideFileRead", false);
            Scribe_Values.Look(ref guideCompleted, "guideCompleted", false);
            Scribe_Values.Look(ref activePresetName, "activePresetName", string.Empty);
            Scribe_Values.Look(ref activePresetRoomIndex, "activePresetRoomIndex", 0);
            Scribe_Collections.Look(ref completedDefensePresetRecords, "completedDefensePresetRecords", LookMode.Value);
            Scribe_Collections.Look(ref defensePresetPlacements, "defensePresetPlacements", LookMode.Value);
            Scribe_Values.Look(ref lastKnownPawnCount, "lastKnownPawnCount", 0);
            Scribe_Values.Look(ref lastAIPollTick, "lastAIPollTick", 0);
            Scribe_Values.Look(ref lastNeedsReviewTick, "lastNeedsReviewTick", 0);
            if (Scribe.mode == LoadSaveMode.Saving && Find.TickManager != null) lastSavedGameTick = Find.TickManager.TicksGame;
            Scribe_Values.Look(ref lastAgentRecord, "lastAgentRecord", string.Empty);
            Scribe_Values.Look(ref lastAgentRecordTick, "lastAgentRecordTick", 0);
            Scribe_Values.Look(ref lastSavedGameTick, "lastSavedGameTick", 0);
            if (owners == null) owners = new Dictionary<int, AICoopOwner>();
            if (logLines == null) logLines = new List<string>();
            if (commandResults == null) commandResults = new List<string>();
            if (constructionFailureCounts == null) constructionFailureCounts = new List<string>();
            if (constructionAssignedBuilders == null) constructionAssignedBuilders = new List<string>();
            if (constructionsWaitingForPlayer == null) constructionsWaitingForPlayer = new List<string>();
            if (roomPurposeRecords == null) roomPurposeRecords = new List<string>();
            if (workMessages == null) workMessages = new List<string>();
            if (playerRequests == null) playerRequests = new List<string>();
            if (presetFailures == null) presetFailures = new List<string>();
            if (aiPlannedConstructions == null) aiPlannedConstructions = new List<string>();
            if (aiCapturedPrisoners == null) aiCapturedPrisoners = new List<int>();
            if (organSurgeryStages == null) organSurgeryStages = new List<string>();
            if (pendingNonForcedApparel == null) pendingNonForcedApparel = new List<string>();
            if (reportedQuestIds == null) reportedQuestIds = new List<int>();
            if (agentTaskNotes == null) agentTaskNotes = new List<string>();
            if (lastPublicPlan == null) lastPublicPlan = string.Empty;
            if (lastAgentRecord == null) lastAgentRecord = string.Empty;
            if (activePresetName == null) activePresetName = string.Empty;
            if (activePresetRoomIndex < 0) activePresetRoomIndex = 0;
            if (completedDefensePresetRecords == null) completedDefensePresetRecords = new List<string>();
            if (defensePresetPlacements == null) defensePresetPlacements = new List<string>();
            TrimLogsToLimit();
            TrimCommandResults();
            TrimPlayerRequests();
        }

        public void RecordAgentExecution(string record)
        {
            if (record.NullOrEmpty()) return;
            lastAgentRecord = record.Trim();
            lastAgentRecordTick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
        }

        public bool HasReportedQuest(int questId)
        {
            return questId > 0 && reportedQuestIds != null && reportedQuestIds.Contains(questId);
        }

        public bool MarkQuestReported(int questId)
        {
            if (questId <= 0) return false;
            if (reportedQuestIds == null) reportedQuestIds = new List<int>();
            if (reportedQuestIds.Contains(questId)) return false;
            reportedQuestIds.Add(questId);
            return true;
        }

        public static string FormatGameTime(int ticks)
        {
            int safeTicks = Math.Max(0, ticks);
            int day = safeTicks / 60000 + 1;
            int hour = (safeTicks % 60000) / 2500;
            return "第" + day + "天 " + hour.ToString("00") + ":00 (tick=" + safeTicks + ")";
        }

        public AICoopOwner GetOwner(Pawn pawn)
        {
            if (pawn == null) return AICoopOwner.Player;
            AICoopOwner owner;
            return owners.TryGetValue(pawn.thingIDNumber, out owner) ? owner : AICoopOwner.Player;
        }

        public bool IsAI(Pawn pawn)
        {
            return GetOwner(pawn) == AICoopOwner.AI;
        }

        public void AssignNewRecruit(Pawn pawn, AICoopOwner owner)
        {
            if (pawn == null) return;
            owners[pawn.thingIDNumber] = owner;
            if (Find.ColonistBar != null) Find.ColonistBar.MarkColonistsDirty();
            CheckBedCapacityForPawn(pawn, "招募新成员");
        }

        public void CheckBedCapacityForPawn(Pawn pawn, string reason)
        {
            if (pawn == null) return;
            if (pawn.Map != null)
            {
                CheckBedCapacityOnMap(pawn.Map, reason);
                return;
            }

            foreach (Map map in Find.Maps) CheckBedCapacityOnMap(map, reason);
        }

        private void CheckBedCapacityOnMap(Map map, string reason)
        {
            if (map == null || map.mapPawns == null || map.listerThings == null) return;

            int colonists = map.mapPawns.FreeColonistsSpawned.Count;
            int prisoners = map.mapPawns.AllPawnsSpawned.Count(pawn => pawn != null && pawn.IsPrisonerOfColony);
            int colonistBeds = 0;
            int prisonerBeds = 0;
            foreach (Building_Bed bed in map.listerThings.GetThingsOfType<Building_Bed>())
            {
                if (bed == null || bed.Medical) continue;
                int freeSlots = Math.Max(0, bed.SleepingSlotsCount - (bed.OwnersForReading == null ? 0 : bed.OwnersForReading.Count));
                if (bed.ForPrisoners) prisonerBeds += freeSlots;
                else if (bed.ForColonists) colonistBeds += freeSlots;
            }

            int colonistDeficit = Math.Max(0, colonists - colonistBeds);
            int prisonerDeficit = Math.Max(0, prisoners - prisonerBeds);
            if (colonistDeficit == 0 && prisonerDeficit == 0) return;

            string details = "map=" + map.uniqueID + " reason=" + (reason ?? "capacity_check") +
                " colonists=" + colonists + " colonist_beds=" + colonistBeds + " colonist_deficit=" + colonistDeficit +
                " prisoners=" + prisoners + " prisoner_beds=" + prisonerBeds + " prisoner_deficit=" + prisonerDeficit;
            AddCommandResult("BED_CAPACITY " + details);
            AddWorkMessage("系统", "床位不足：" + details + "。请工作 AI 按需求建设新的宿舍或囚犯房间。" );
            AddLog("[床位] " + details + "，已通知工作 AI。" );
            needsReviewPending = true;
        }

        public void AddLog(string line)
        {
            if (line.NullOrEmpty() || logLines == null) return;
            logLines.Add("[tick " + Find.TickManager.TicksGame + "] " + line);
            if (line.StartsWith("[拒绝]", StringComparison.Ordinal))
            {
                AddCommandResult("FAIL command_not_executed reason=" + line.Substring(4).Trim());
            }
            TrimLogsToLimit();
        }

        public void AddCommandResult(string result)
        {
            if (result.NullOrEmpty()) return;
            if (commandResults == null) commandResults = new List<string>();
            commandResults.Add("tick=" + Find.TickManager.TicksGame + " " + result);
            TrimCommandResults();
        }

        public void RecordConstructionFailure(Frame frame, Pawn worker)
        {
            if (frame == null || worker == null || !IsAI(worker)) return;
            string key = ConstructionKey(frame);
            if (key.NullOrEmpty()) return;

            int failures = Math.Min(3, GetConstructionFailureCount(key, worker.thingIDNumber) + 1);
            SetConstructionFailureCount(key, worker.thingIDNumber, failures);
            string buildName = frame.def.entityDefToBuild == null ? frame.def.defName : frame.def.entityDefToBuild.defName;
            Pawn nextBuilder = FindNextBuilder(frame.Map, frame.def.entityDefToBuild, key);
            if (nextBuilder == null)
            {
                SetAssignedBuilder(key, -1);
                if (!constructionsWaitingForPlayer.Contains(key)) constructionsWaitingForPlayer.Add(key);
                AddCommandResult("FAIL build=" + buildName + " map=" + frame.Map.uniqueID + " cell=" + frame.Position.x + ":" + frame.Position.z +
                    " worker=" + worker.thingIDNumber + " attempt=" + failures + "/3 reason=all_ai_builders_failed_or_incapable player_help_required=1");
                AddLog("[失败] AI 殖民者建造 " + buildName + " 均已失败 3 次或不具备施工条件，现等待玩家殖民者帮忙。");
                return;
            }

            SetAssignedBuilder(key, nextBuilder.thingIDNumber);
            AddCommandResult("FAIL build=" + buildName + " map=" + frame.Map.uniqueID + " cell=" + frame.Position.x + ":" + frame.Position.z +
                " worker=" + worker.thingIDNumber + " attempt=" + failures + "/3 next_worker=" + nextBuilder.thingIDNumber);
            AddLog("[失败] " + worker.LabelShort + " 第 " + failures + "/3 次建造 " + buildName + " 失败；改由 " + nextBuilder.LabelShort + " 尝试。");
        }

        public bool CanAIConstruct(Pawn pawn, Thing thing)
        {
            string key = ConstructionKey(thing);
            if (key.NullOrEmpty()) return true;
            if (constructionsWaitingForPlayer.Contains(key)) return false;
            int assignedBuilder = GetAssignedBuilder(key);
            if (assignedBuilder >= 0)
            {
                return pawn != null && pawn.thingIDNumber == assignedBuilder && GetConstructionFailureCount(key, assignedBuilder) < 3;
            }
            Pawn best = FindNextBuilder(thing.Map, thing.def.entityDefToBuild, key);
            return best != null && pawn == best;
        }

        public bool IsConstructionWaitingForPlayer(Thing thing)
        {
            string key = ConstructionKey(thing);
            return !key.NullOrEmpty() && constructionsWaitingForPlayer.Contains(key);
        }

        public bool HasAssignedConstruction(Pawn pawn, Map map)
        {
            if (pawn == null || map == null) return false;
            string mapPrefix = map.uniqueID + ":";
            string pawnSuffix = "|" + pawn.thingIDNumber;
            return constructionAssignedBuilders.Any(record => record.StartsWith(mapPrefix, StringComparison.Ordinal) && record.EndsWith(pawnSuffix, StringComparison.Ordinal));
        }

        public void RecordConstructionSuccess(Frame frame)
        {
            string key = ConstructionKey(frame);
            if (key.NullOrEmpty()) return;
            constructionFailureCounts.RemoveAll(record => record.StartsWith(key + "|", StringComparison.Ordinal));
            constructionAssignedBuilders.RemoveAll(record => record.StartsWith(key + "|", StringComparison.Ordinal));
            constructionsWaitingForPlayer.Remove(key);
        }

        public void SetPresetPlacement(Map map, string presetName, int minX, int minZ, int rotation)
        {
            if (!presetName.NullOrEmpty()) activePresetName = presetName;
            if (map == null || presetName.NullOrEmpty() || !IsDefensePresetName(presetName)) return;
            rotation = Math.Max(0, Math.Min(3, rotation));
            string prefix = map.uniqueID + "|" + Path.GetFileNameWithoutExtension(presetName).ToLowerInvariant() + "|" + activePresetRoomIndex + "|";
            defensePresetPlacements.RemoveAll(record => record != null && record.StartsWith(prefix, StringComparison.Ordinal));
            defensePresetPlacements.Add(prefix + minX + "|" + minZ + "|" + rotation);
        }

        public bool TryRecordDefensePresetCompletion()
        {
            if (defensePresetPlacements == null) return false;
            bool completed = false;
            foreach (string placement in defensePresetPlacements.ToList())
            {
                string[] parts = placement == null ? new string[0] : placement.Split('|');
                int mapId, roomIndex, minX, minZ, rotation;
                if (parts.Length != 6 || !Int32.TryParse(parts[0], out mapId) || !Int32.TryParse(parts[2], out roomIndex) ||
                    !Int32.TryParse(parts[3], out minX) || !Int32.TryParse(parts[4], out minZ) || !Int32.TryParse(parts[5], out rotation)) continue;
                if (!IsDefensePresetName(parts[1])) continue;
                Map map = Find.Maps == null ? null : Find.Maps.FirstOrDefault(candidate => candidate != null && candidate.uniqueID == mapId);
                if (map == null || !AICoopPresetManager.AreAllBuildsComplete(parts[1], roomIndex, map, minX, minZ, rotation)) continue;
                string key = map.uniqueID + "|" + parts[1];
                if (completedDefensePresetRecords == null) completedDefensePresetRecords = new List<string>();
                if (completedDefensePresetRecords.Contains(key)) { completed = true; continue; }
                completedDefensePresetRecords.Add(key);
                completed = true;
                AddCommandResult("OK defense_preset_completed=1 file=" + parts[1] + " map=" + map.uniqueID);
                AddWorkMessage("系统", "防御预设 " + parts[1] + " 已全部建成；AI 征兆溜怪将限制在居住区内。" );
            }
            return completed;
        }

        public bool HasCompletedDefensePreset(Map map)
        {
            if (map == null || completedDefensePresetRecords == null) return false;
            if (!HasCompletedDefensePresetRecord(map)) TryDiscoverCompletedDefensePresets(map);
            string prefix = map.uniqueID + "|";
            return completedDefensePresetRecords.Any(record => record != null && record.StartsWith(prefix, StringComparison.Ordinal));
        }

        private bool HasCompletedDefensePresetRecord(Map map)
        {
            string prefix = map.uniqueID + "|";
            return completedDefensePresetRecords.Any(record => record != null && record.StartsWith(prefix, StringComparison.Ordinal));
        }

        private void TryDiscoverCompletedDefensePresets(Map map)
        {
            if (map == null || roomPurposeRecords == null || completedDefensePresetRecords == null) return;
            string[] defenseNames = { "room_defense_gate_battery", "room_defense_active_turrets" };
            foreach (string record in roomPurposeRecords)
            {
                string[] parts = record == null ? new string[0] : record.Split(new[] { '|' }, 6);
                int recordMap, minX, minZ, maxX, maxZ;
                if (parts.Length != 6 || !Int32.TryParse(parts[0], out recordMap) || recordMap != map.uniqueID ||
                    !Int32.TryParse(parts[1], out minX) || !Int32.TryParse(parts[2], out minZ) ||
                    !Int32.TryParse(parts[3], out maxX) || !Int32.TryParse(parts[4], out maxZ)) continue;
                foreach (string defenseName in defenseNames)
                {
                    if (parts[5].IndexOf(defenseName, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    int originalWidth = AICoopPresetManager.GetRoomWidth(defenseName);
                    int originalHeight = AICoopPresetManager.GetRoomHeight(defenseName);
                    int recordWidth = maxX - minX + 1;
                    int recordHeight = maxZ - minZ + 1;
                    for (int rotation = 0; rotation < 4; rotation++)
                    {
                        int width = rotation % 2 == 0 ? originalWidth : originalHeight;
                        int height = rotation % 2 == 0 ? originalHeight : originalWidth;
                        if (width != recordWidth || height != recordHeight) continue;
                        if (!AICoopPresetManager.AreAllBuildsComplete(defenseName, 0, map, minX, minZ, rotation)) continue;
                        string key = map.uniqueID + "|" + defenseName;
                        if (!completedDefensePresetRecords.Contains(key))
                        {
                            completedDefensePresetRecords.Add(key);
                            AddCommandResult("OK defense_preset_completed=1 file=" + defenseName + " map=" + map.uniqueID);
                            AddWorkMessage("系统", "防御预设 " + defenseName + " 已全部建成；AI 征兆溜怪将限制在居住区内。" );
                        }
                        break;
                    }
                }
            }
        }

        private static bool IsDefensePresetName(string name)
        {
            string file = Path.GetFileNameWithoutExtension(name ?? string.Empty);
            if (file.StartsWith("room_", StringComparison.OrdinalIgnoreCase)) file = file.Substring(5);
            return file.Equals("defense_gate_battery", StringComparison.OrdinalIgnoreCase) ||
                file.Equals("defense_active_turrets", StringComparison.OrdinalIgnoreCase);
        }

        public void AddWorkMessage(string speaker, string message)
        {
            if (speaker.NullOrEmpty() || message.NullOrEmpty()) return;
            workMessages.Add(speaker + "：" + message.Trim());
        }

        public void AddChatMessage(string speaker, string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            chatMessages.Add(speaker + "：" + message.Trim());
        }

        public bool SendPlayerChat(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return false;
            if (!AICoopAgentBridge.IsConnected)
            {
                Messages.Message("请先在 DeepSeek Harness 中连接游戏，再发送聊天。", MessageTypeDefOf.RejectInput, false);
                return false;
            }
            AddChatMessage("玩家", message);
            AICoopAgentRuntime.BeginExternalAgentThinking();
            AICoopAgentBridge.QueueAgentTrigger("player_message", "玩家说：" + message.Trim());
            return true;
        }

        public void AddPlayerRequest(string request)
        {
            if (request.NullOrEmpty()) return;
            string text = request.Trim();
            string entry = "tick=" + (Find.TickManager == null ? 0 : Find.TickManager.TicksGame) + " " + text;
            if (playerRequests == null) playerRequests = new List<string>();
            playerRequests.Add(entry);
            AddWorkMessage("AI请求玩家", text);
            AddChatMessage("AI", text);
            TrimPlayerRequests();
            if (Find.LetterStack != null)
            {
                Find.LetterStack.ReceiveLetter("AI 请求玩家", text, DefDatabase<LetterDef>.GetNamed("AICoop_PlayerRequest"));
            }
        }

        public string AddAgentTaskNote(string text)
        {
            if (text.NullOrEmpty()) return string.Empty;
            if (agentTaskNotes == null) agentTaskNotes = new List<string>();
            int tick = Find.TickManager == null ? 0 : Find.TickManager.TicksGame;
            string clean = text.Trim().Replace("\r", " ").Replace("\n", " ").Replace("|", "/");
            int id = ++nextAgentTaskNoteId;
            string record = id + "|" + tick + "|" + clean;
            agentTaskNotes.Add(record);
            while (agentTaskNotes.Count > 200) agentTaskNotes.RemoveAt(0);
            AddCommandResult("OK note_added id=" + id + " tick=" + tick);
            AddWorkMessage("AI待办", "已记录待办 #" + id + "（时间刻 " + tick + "）：" + clean);
            return record;
        }

        public void ReadAgentTaskNotes()
        {
            if (agentTaskNotes == null) agentTaskNotes = new List<string>();
            List<string> entries = agentTaskNotes.Where(record => !record.NullOrEmpty())
                .Select(record => "[" + record.Replace('|', ':') + "]").ToList();
            AddCommandResult("OK note_read count=" + entries.Count + " entries=" +
                (entries.Count == 0 ? "-" : string.Join(" ", entries.ToArray())));
        }

        public bool CompleteAgentTaskNote(string id)
        {
            if (agentTaskNotes == null || id.NullOrEmpty()) return false;
            string prefix = id.Trim() + "|";
            string record = agentTaskNotes.FirstOrDefault(item => item != null && item.StartsWith(prefix, StringComparison.Ordinal));
            if (record == null) return false;
            agentTaskNotes.Remove(record);
            AddCommandResult("OK note_done id=" + id.Trim());
            AddWorkMessage("AI待办", "已完成待办 #" + id.Trim());
            return true;
        }

        public void RegisterNonForcedApparel(Pawn pawn, Apparel apparel)
        {
            if (pawn == null || apparel == null) return;
            if (pendingNonForcedApparel == null) pendingNonForcedApparel = new List<string>();
            string key = pawn.thingIDNumber + "|" + apparel.thingIDNumber;
            if (!pendingNonForcedApparel.Contains(key)) pendingNonForcedApparel.Add(key);
        }

        private void ProcessPendingNonForcedApparel()
        {
            if (pendingNonForcedApparel == null || pendingNonForcedApparel.Count == 0) return;
            List<Pawn> pawns = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists;
            for (int i = pendingNonForcedApparel.Count - 1; i >= 0; i--)
            {
                string[] parts = pendingNonForcedApparel[i].Split('|');
                int pawnId, apparelId;
                if (parts.Length != 2 || !Int32.TryParse(parts[0], out pawnId) || !Int32.TryParse(parts[1], out apparelId))
                {
                    pendingNonForcedApparel.RemoveAt(i);
                    continue;
                }

                Pawn pawn = pawns == null ? null : pawns.FirstOrDefault(candidate => candidate != null && candidate.thingIDNumber == pawnId);
                if (pawn == null || pawn.apparel == null)
                {
                    pendingNonForcedApparel.RemoveAt(i);
                    continue;
                }

                Apparel worn = pawn.apparel.WornApparel.FirstOrDefault(candidate => candidate != null && candidate.thingIDNumber == apparelId);
                if (worn != null)
                {
                    if (pawn.outfits != null && pawn.outfits.forcedHandler != null)
                    {
                        pawn.outfits.forcedHandler.SetForced(worn, false);
                    }
                    pendingNonForcedApparel.RemoveAt(i);
                    continue;
                }

                bool stillQueued = pawn.jobs != null && pawn.jobs.AllJobs().Any(job =>
                    job != null && job.def == JobDefOf.Wear && job.targetA.Thing != null &&
                    job.targetA.Thing.thingIDNumber == apparelId);
                if (!stillQueued) pendingNonForcedApparel.RemoveAt(i);
            }
        }

        private void TrimPlayerRequests()
        {
            int limit = AICoopMod.Settings == null ? 200 : AICoopMod.Settings.maxLogEntries;
            while (playerRequests != null && playerRequests.Count > limit) playerRequests.RemoveAt(0);
        }

        public void SetGuideProgress(int fileIndex, int taskIndex, bool fileRead, bool completed)
        {
            guideFileIndex = Math.Max(0, fileIndex);
            guideTaskIndex = Math.Max(0, taskIndex);
            guideFileRead = fileRead;
            guideCompleted = completed;
        }

        public void SetPresetProgress(string presetName, int roomIndex)
        {
            activePresetName = presetName ?? string.Empty;
            activePresetRoomIndex = Math.Max(0, roomIndex);
        }

        public void RecordPresetFailure(string presetName, Map map, IntVec3 cell, string defName, string reason, int width = 0, int height = 0, int rotation = 0)
        {
            if (map == null || !cell.IsValid || defName.NullOrEmpty()) return;
            string prefix = (presetName ?? string.Empty) + "|" + map.uniqueID + "|" + cell.x + "|" + cell.z + "|" + defName + "|";
            presetFailures.RemoveAll(record => record != null && record.StartsWith(prefix, StringComparison.Ordinal));
            presetFailures.Add(prefix + (reason ?? "unknown").Replace('|', '/') + "|" + width + "|" + height + "|" + Math.Max(0, Math.Min(3, rotation)));
            AddCommandResult("FAIL preset_facility_pending=1 file=" + (presetName ?? "-") + " map=" + map.uniqueID +
                " cell=" + cell.x + ":" + cell.z + " def=" + defName + " reason=" + (reason ?? "unknown"));
        }

        public void RemovePresetFailure(string record)
        {
            if (record != null) presetFailures.Remove(record);
        }

        public void MarkAIPlannedConstruction(Map map, IntVec3 cell, BuildableDef buildDef)
        {
            if (map == null || buildDef == null || !cell.IsValid) return;
            string key = map.uniqueID + "|" + cell.x + "|" + cell.z + "|" + buildDef.defName;
            if (!aiPlannedConstructions.Contains(key)) aiPlannedConstructions.Add(key);
        }

        public bool ConsumeAIPlannedConstruction(Map map, IntVec3 cell, BuildableDef buildDef)
        {
            if (map == null || buildDef == null || !cell.IsValid) return false;
            string key = map.uniqueID + "|" + cell.x + "|" + cell.z + "|" + buildDef.defName;
            return aiPlannedConstructions.Remove(key);
        }

        public void RecordPrisonerCapture(Pawn prisoner, Pawn captor)
        {
            if (prisoner == null || captor == null || !IsAI(captor)) return;
            if (aiCapturedPrisoners.Contains(prisoner.thingIDNumber)) return;
            aiCapturedPrisoners.Add(prisoner.thingIDNumber);
            AddCommandResult("PRISONER_CAPTURED_BY_AI pawn=" + prisoner.thingIDNumber + " action=organ_harvest_required");
            AddWorkMessage("系统", "AI 已俘虏 " + prisoner.LabelShort + "；若不是任务保留目标，必须按肺、肾、心脏（不可摘时改肝）的顺序处理。" );
            needsReviewPending = true;
        }

        public bool WasCapturedByAI(Pawn prisoner)
        {
            return prisoner != null && aiCapturedPrisoners.Contains(prisoner.thingIDNumber);
        }

        public int GetOrganSurgeryStage(Pawn prisoner)
        {
            if (prisoner == null) return 0;
            string prefix = prisoner.thingIDNumber + "|";
            string record = organSurgeryStages.FirstOrDefault(item => item != null && item.StartsWith(prefix, StringComparison.Ordinal));
            int stage;
            return record != null && Int32.TryParse(record.Substring(prefix.Length), out stage) ? stage : 0;
        }

        public void SetOrganSurgeryStage(Pawn prisoner, int stage)
        {
            if (prisoner == null) return;
            string prefix = prisoner.thingIDNumber + "|";
            organSurgeryStages.RemoveAll(item => item != null && item.StartsWith(prefix, StringComparison.Ordinal));
            organSurgeryStages.Add(prefix + Math.Max(0, Math.Min(3, stage)));
        }

        public bool TryRegisterRoomPurpose(Map map, int minX, int minZ, int maxX, int maxZ, string purpose)
        {
            if (map == null || purpose.NullOrEmpty()) return true;
            foreach (string record in roomPurposeRecords)
            {
                string[] parts = record.Split(new[] { '|' }, 6);
                int recordMap, recordMinX, recordMinZ, recordMaxX, recordMaxZ;
                if (parts.Length != 6 || !Int32.TryParse(parts[0], out recordMap) || !Int32.TryParse(parts[1], out recordMinX) ||
                    !Int32.TryParse(parts[2], out recordMinZ) || !Int32.TryParse(parts[3], out recordMaxX) || !Int32.TryParse(parts[4], out recordMaxZ)) continue;
                bool overlaps = recordMap == map.uniqueID && minX <= recordMaxX && maxX >= recordMinX && minZ <= recordMaxZ && maxZ >= recordMinZ;
                if (overlaps && !string.Equals(parts[5], purpose, StringComparison.OrdinalIgnoreCase)) return false;
                if (overlaps) return true;
            }
            roomPurposeRecords.Add(map.uniqueID + "|" + minX + "|" + minZ + "|" + maxX + "|" + maxZ + "|" + purpose);
            return true;
        }

        public bool CanPlaceRoomWithCorridor(Map map, int minX, int minZ, int maxX, int maxZ)
        {
            if (map == null || roomPurposeRecords == null) return true;
            foreach (string record in roomPurposeRecords)
            {
                string[] parts = record.Split(new[] { '|' }, 6);
                int recordMap, recordMinX, recordMinZ, recordMaxX, recordMaxZ;
                if (parts.Length != 6 || !Int32.TryParse(parts[0], out recordMap) || recordMap != map.uniqueID ||
                    !Int32.TryParse(parts[1], out recordMinX) || !Int32.TryParse(parts[2], out recordMinZ) ||
                    !Int32.TryParse(parts[3], out recordMaxX) || !Int32.TryParse(parts[4], out recordMaxZ)) continue;
                if (minX <= recordMaxX + 1 && maxX >= recordMinX - 1 && minZ <= recordMaxZ + 1 && maxZ >= recordMinZ - 1) return false;
            }
            return true;
        }

        public bool HasRoomOnMap(Map map)
        {
            if (map == null || roomPurposeRecords == null) return false;
            string prefix = map.uniqueID + "|";
            return roomPurposeRecords.Any(record => record != null && record.StartsWith(prefix, StringComparison.Ordinal));
        }

        public bool TryGetRoomBounds(Map map, out int minX, out int minZ, out int maxX, out int maxZ)
        {
            minX = minZ = Int32.MaxValue;
            maxX = maxZ = Int32.MinValue;
            if (map == null || roomPurposeRecords == null) return false;
            foreach (string record in roomPurposeRecords)
            {
                string[] parts = record.Split(new[] { '|' }, 6);
                int recordMap, recordMinX, recordMinZ, recordMaxX, recordMaxZ;
                if (parts.Length != 6 || !Int32.TryParse(parts[0], out recordMap) || recordMap != map.uniqueID ||
                    !Int32.TryParse(parts[1], out recordMinX) || !Int32.TryParse(parts[2], out recordMinZ) ||
                    !Int32.TryParse(parts[3], out recordMaxX) || !Int32.TryParse(parts[4], out recordMaxZ)) continue;
                minX = Math.Min(minX, recordMinX);
                minZ = Math.Min(minZ, recordMinZ);
                maxX = Math.Max(maxX, recordMaxX);
                maxZ = Math.Max(maxZ, recordMaxZ);
            }
            return minX != Int32.MaxValue;
        }

        public bool TryGetResidenceBounds(Map map, out int minX, out int minZ, out int maxX, out int maxZ)
        {
            minX = minZ = Int32.MaxValue;
            maxX = maxZ = Int32.MinValue;
            if (map == null || roomPurposeRecords == null) return false;
            foreach (string record in roomPurposeRecords)
            {
                string[] parts = record.Split(new[] { '|' }, 6);
                int recordMap, recordMinX, recordMinZ, recordMaxX, recordMaxZ;
                if (parts.Length != 6 || !Int32.TryParse(parts[0], out recordMap) || recordMap != map.uniqueID ||
                    !Int32.TryParse(parts[1], out recordMinX) || !Int32.TryParse(parts[2], out recordMinZ) ||
                    !Int32.TryParse(parts[3], out recordMaxX) || !Int32.TryParse(parts[4], out recordMaxZ)) continue;
                if (parts[5].IndexOf("defense_", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                minX = Math.Min(minX, recordMinX);
                minZ = Math.Min(minZ, recordMinZ);
                maxX = Math.Max(maxX, recordMaxX);
                maxZ = Math.Max(maxZ, recordMaxZ);
            }
            return minX != Int32.MaxValue;
        }

        public void GetSharedRoomBoundaries(Map map, int minX, int minZ, int maxX, int maxZ,
            List<IntVec3> sharedCells, List<IntVec3> doorCells, List<IntVec3> wallOpenings)
        {
            if (map == null || roomPurposeRecords == null || sharedCells == null || doorCells == null || wallOpenings == null) return;
            foreach (string record in roomPurposeRecords)
            {
                string[] parts = record.Split(new[] { '|' }, 6);
                int recordMap, recordMinX, recordMinZ, recordMaxX, recordMaxZ;
                if (parts.Length != 6 || !Int32.TryParse(parts[0], out recordMap) || recordMap != map.uniqueID ||
                    !Int32.TryParse(parts[1], out recordMinX) || !Int32.TryParse(parts[2], out recordMinZ) ||
                    !Int32.TryParse(parts[3], out recordMaxX) || !Int32.TryParse(parts[4], out recordMaxZ)) continue;

                if (minX == recordMaxX + 1 || maxX == recordMinX - 1)
                {
                    int low = Math.Max(minZ, recordMinZ);
                    int high = Math.Min(maxZ, recordMaxZ);
                    if (low <= high)
                    {
                        int x = minX == recordMaxX + 1 ? minX : maxX;
                        for (int z = low; z <= high; z++) AddUnique(sharedCells, new IntVec3(x, 0, z));
                        IntVec3 doorCell = new IntVec3(x, 0, low + (high - low) / 2);
                        if (!doorCells.Contains(doorCell))
                        {
                            doorCells.Add(doorCell);
                            wallOpenings.Add(new IntVec3(minX == recordMaxX + 1 ? recordMaxX : recordMinX, 0, doorCell.z));
                        }
                    }
                }
                if (minZ == recordMaxZ + 1 || maxZ == recordMinZ - 1)
                {
                    int low = Math.Max(minX, recordMinX);
                    int high = Math.Min(maxX, recordMaxX);
                    if (low <= high)
                    {
                        int z = minZ == recordMaxZ + 1 ? minZ : maxZ;
                        for (int x = low; x <= high; x++) AddUnique(sharedCells, new IntVec3(x, 0, z));
                        IntVec3 doorCell = new IntVec3(low + (high - low) / 2, 0, z);
                        if (!doorCells.Contains(doorCell))
                        {
                            doorCells.Add(doorCell);
                            wallOpenings.Add(new IntVec3(doorCell.x, 0, minZ == recordMaxZ + 1 ? recordMaxZ : recordMinZ));
                        }
                    }
                }
            }
        }

        private static void AddUnique(List<IntVec3> cells, IntVec3 cell)
        {
            if (!cells.Contains(cell)) cells.Add(cell);
        }

        public string RoomPurposeSummary(Map map)
        {
            if (map == null) return "-";
            string prefix = map.uniqueID + "|";
            List<string> summaries = roomPurposeRecords.Where(record => record.StartsWith(prefix, StringComparison.Ordinal))
                .Select(record => record.Replace('|', ':')).ToList();
            return summaries.Count == 0 ? "-" : string.Join(",", summaries.ToArray());
        }

        public void TrimLogsToLimit()
        {
            int limit = AICoopMod.Settings == null ? 200 : AICoopMod.Settings.maxLogEntries;
            while (logLines != null && logLines.Count > limit) logLines.RemoveAt(0);
        }

        public void ClearLog()
        {
            logLines.Clear();
        }

        public void SetPublicPlan(string plan)
        {
            lastPublicPlan = plan ?? string.Empty;
        }

        private void TrimCommandResults()
        {
            while (commandResults != null && commandResults.Count > 30) commandResults.RemoveAt(0);
        }

        private int GetConstructionFailureCount(string key, int pawnId)
        {
            string prefix = key + "|" + pawnId + "|";
            string record = constructionFailureCounts.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));
            int count;
            return record != null && Int32.TryParse(record.Substring(prefix.Length), out count) ? count : 0;
        }

        private void SetConstructionFailureCount(string key, int pawnId, int count)
        {
            string prefix = key + "|" + pawnId + "|";
            constructionFailureCounts.RemoveAll(value => value.StartsWith(prefix, StringComparison.Ordinal));
            constructionFailureCounts.Add(prefix + count);
        }

        private int GetAssignedBuilder(string key)
        {
            string prefix = key + "|";
            string record = constructionAssignedBuilders.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.Ordinal));
            int pawnId;
            return record != null && Int32.TryParse(record.Substring(prefix.Length), out pawnId) ? pawnId : -1;
        }

        private void SetAssignedBuilder(string key, int pawnId)
        {
            string prefix = key + "|";
            constructionAssignedBuilders.RemoveAll(value => value.StartsWith(prefix, StringComparison.Ordinal));
            if (pawnId >= 0) constructionAssignedBuilders.Add(prefix + pawnId);
        }

        private Pawn FindNextBuilder(Map map, BuildableDef buildDef, string key)
        {
            if (map == null) return null;
            ThingDef thingDef = buildDef as ThingDef;
            List<Pawn> builders = map.mapPawns.FreeColonistsSpawned
                .Where(pawn => IsAI(pawn) && pawn.workSettings != null && pawn.skills != null &&
                    !pawn.WorkTypeIsDisabled(WorkTypeDefOf.Construction) &&
                    (thingDef == null || (pawn.skills.GetSkill(SkillDefOf.Construction).Level >= thingDef.constructionSkillPrerequisite &&
                        pawn.skills.GetSkill(SkillDefOf.Artistic).Level >= thingDef.artisticSkillPrerequisite)) &&
                    GetConstructionFailureCount(key, pawn.thingIDNumber) < 3)
                .OrderBy(pawn => GetConstructionFailureCount(key, pawn.thingIDNumber))
                .ThenByDescending(pawn => pawn.skills.GetSkill(SkillDefOf.Construction).Level)
                .ThenBy(pawn => pawn.thingIDNumber)
                .ToList();
            return builders.FirstOrDefault(pawn => !pawn.Downed && !pawn.InMentalState && !pawn.Drafted) ?? builders.FirstOrDefault();
        }

        private static string ConstructionKey(Thing thing)
        {
            if (thing == null || !thing.Spawned || thing.Map == null || thing.def == null || thing.def.entityDefToBuild == null) return null;
            return thing.Map.uniqueID + ":" + thing.Position.x + ":" + thing.Position.z + ":" + thing.def.entityDefToBuild.defName;
        }

        private void EnsureOwnership()
        {
            List<Pawn> pawns = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists;
            if (pawns == null) return;
            bool ownershipChanged = false;

            if (lastKnownPawnCount == 0 && pawns.Count >= 4)
            {
                for (int i = 0; i < pawns.Count; i++)
                {
                    owners[pawns[i].thingIDNumber] = i < 2 ? AICoopOwner.Player : AICoopOwner.AI;
                }
                ownershipChanged = true;
            }

            foreach (Pawn pawn in pawns)
            {
                if (!owners.ContainsKey(pawn.thingIDNumber))
                {
                    owners[pawn.thingIDNumber] = AICoopOwner.Player;
                    AddLog(pawn.LabelShort + " 加入了殖民地，归属玩家。");
                    ownershipChanged = true;
                }
            }

            lastKnownPawnCount = pawns.Count;
            if (ownershipChanged && Find.ColonistBar != null) Find.ColonistBar.MarkColonistsDirty();
        }

        private void EnsureInitialDumpingStockpiles()
        {
            foreach (Map map in Find.Maps)
            {
                if (map == null || map.mapPawns == null || !map.mapPawns.FreeColonistsSpawned.Any(IsAI)) continue;
                AICoopActionExecutor.EnsureInitialDumpingStockpile(map);
            }
        }
    }
}
