using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Linq;
using RimWorld;
using RimWorld.Planet;
using Verse.AI;
using Verse;

namespace AICoopCompanion
{
    internal static class AICoopCommandDisplay
    {
        public static void Log(string protocol)
        {
            AICoopGameComponent component = AICoopGameComponent.Current;
            if (component == null || protocol.NullOrEmpty()) return;
            foreach (string rawLine in protocol.Replace("\r", string.Empty).Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.NullOrEmpty()) continue;
                if (line.StartsWith("PLAN ", StringComparison.OrdinalIgnoreCase))
                {
                    component.AddLog("[AI 计划] " + line.Substring(5).Trim());
                    continue;
                }
                if (line.StartsWith("GUIDE_DONE ", StringComparison.OrdinalIgnoreCase)) continue;
                if (line.StartsWith("SAY ", StringComparison.OrdinalIgnoreCase) || line.StartsWith("CHAT ", StringComparison.OrdinalIgnoreCase) || line.StartsWith("REQUEST_PLAYER ", StringComparison.OrdinalIgnoreCase)) continue;
                component.AddLog("[AI 指令] " + Describe(line));
            }
        }

        internal static string Describe(string line)
        {
            string[] parts = System.Text.RegularExpressions.Regex.Split(line.Trim(), "\\s+");
            string command = Part(parts, 0).ToUpperInvariant();
            string pawn = PawnLabel(Part(parts, 1));
            switch (command)
            {
                case "M":
                    return "让地图 " + Part(parts, 1) + " 上的 AI 殖民者移动到坐标（" + Part(parts, 2) + "，" + Part(parts, 3) + "）。";
                case "D":
                    return Part(parts, 2) == "1" ? "征召 " + pawn + "。" : "解除 " + pawn + " 的征召。";
                case "A":
                    return "征召地图 " + Part(parts, 1) + " 上的 AI 殖民者攻击敌人或只能攻击的障碍 " + ThingLabel(Part(parts, 2)) + "。";
                case "Q":
                    return "在地图 " + Part(parts, 1) + " 的（" + Part(parts, 2) + "，" + Part(parts, 3) + "）至（" + Part(parts, 4) + "，" + Part(parts, 5) + "）规划矩形房间" + OptionalStuff(parts, 6) + "。";
                case "B":
                    return "在地图 " + Part(parts, 1) + " 的坐标（" + Part(parts, 3) + "，" + Part(parts, 4) + "）放置“" + DefLabel<ThingDef>(Part(parts, 2)) + "”建筑蓝图，朝向 " + Part(parts, 5, "0") + OptionalStuff(parts, 6) + "。";
                case "F":
                    return "环绕地图 " + Part(parts, 1) + " 上所有居住区建立单层矩形石墙，墙在居住区外，只留左右两个开口；不可建地形处绕行。";
                case "F2":
                    return "兼容旧指令：环绕居住区建造单层围墙，不再追加第二层。";
                case "G":
                    if (parts.Length == 6 && parts[5].Equals("fertile_adjacent", StringComparison.OrdinalIgnoreCase))
                        return "在地图 " + Part(parts, 1) + " 从（" + Part(parts, 3) + "，" + Part(parts, 4) + "）开始，建立上下左右连续的最高肥力种植区，最多 15×全体殖民者人数格：“" + DefLabel<ThingDef>(Part(parts, 2)) + "”。";
                    return "在地图 " + Part(parts, 1) + " 的（" + Part(parts, 3) + "，" + Part(parts, 4) + "）至（" + Part(parts, 5) + "，" + Part(parts, 6) + "）建立“" + DefLabel<ThingDef>(Part(parts, 2)) + "”种植区。";
                case "S":
                    return "在地图 " + Part(parts, 1) + " 的（" + Part(parts, 2) + "，" + Part(parts, 3) + "）至（" + Part(parts, 4) + "，" + Part(parts, 5) + "）建立" +
                        (parts.Length == 7 && parts[6].Equals("dump", StringComparison.OrdinalIgnoreCase) ? "至少 7x7 的临时垃圾储存区。" : "仓储区。");
                case "P":
                    return "在地图 " + Part(parts, 1) + " 的 " + ThingLabel(Part(parts, 2)) + " 添加“" + DefLabel<RecipeDef>(Part(parts, 3)) + "”生产账单，共 " + Part(parts, 4) + " 次。";
                case "R":
                    return "在地图 " + Part(parts, 1) + " 选择“" + DefLabel<ResearchProjectDef>(Part(parts, 2)) + "”研究项目。";
                case "C":
                    return DescribeAdvanced(parts);
                case "X":
                    return "让 " + pawn + " 对 " + ThingLabel(Part(parts, 2)) + " 执行“" + DesignationLabel(Part(parts, 3)) + "”指定。";
                case "T":
                    return "对地图 " + Part(parts, 1) + " 的范围（" + Part(parts, 2) + "，" + Part(parts, 3) + "）至（" + Part(parts, 4) + "，" + Part(parts, 5) + "）内全部目标添加“" + DesignationLabel(Part(parts, 6)) + "”指定。";
                case "ORE":
                    return "将地图 " + Part(parts, 1) + " 上全部“" + DefLabel<ThingDef>(Part(parts, 2)) + "”天然矿物标记为待开采。";
                case "J":
                    return "让 " + pawn + " 对 " + ThingLabel(Part(parts, 2)) + " 执行“" + JobLabel(Part(parts, 3)) + "”任务。";
                case "WORLD":
                    return DescribeWorld(parts);
                case "N":
                    return "本轮不执行操作。";
                case "NOTE_ADD":
                    return "记录待办：" + (line.Length <= 8 ? "" : line.Substring(8).Trim());
                case "NOTE_READ":
                    return "读取存档内 AI 待办文件。";
                case "NOTE_DONE":
                    return "标记待办 #" + Part(parts, 1) + " 已完成。";
                case "PRESET":
                    if (Part(parts, 1).IndexOf("defense_gate_battery", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "激活大门防御预设，并把中间墙/门自动对齐到左右两个外围墙开口之一；必要时扩建外围墙。";
                    if (Part(parts, 1).IndexOf("defense_active_turrets", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "在墙内放置主动炮台远程防御预设，与其他建筑保持至少两格间距。";
                    return "激活并自动建设殖民地预设“" + Part(parts, 1) + "”。";
                case "PRESET_DONE":
                    return "标记预设房间“" + Part(parts, 1) + "”已完成。";
                case "PRESET_RETRY":
                    return "重试所有暂未放置的预设设施。";
                default:
                    return "无法识别的指令。";
            }
        }

        private static string Part(string[] parts, int index, string fallback = "？")
        {
            return index < parts.Length && !parts[index].NullOrEmpty() ? parts[index] : fallback;
        }

        private static string PawnLabel(string id)
        {
            int pawnId;
            if (Verse.Current.Game != null && Int32.TryParse(id, out pawnId))
            {
                Pawn pawn = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists.FirstOrDefault(item => item.thingIDNumber == pawnId);
                if (pawn != null) return pawn.LabelShort + "（" + id + "）";
            }
            return "AI 殖民者 " + id;
        }

        private static string ThingLabel(string id)
        {
            int thingId;
            if (Verse.Current.Game != null && Int32.TryParse(id, out thingId))
            {
                foreach (Map map in Find.Maps)
                {
                    Thing thing = map.listerThings.AllThings.FirstOrDefault(item => item.thingIDNumber == thingId);
                    if (thing != null) return thing.LabelShort + "（" + id + "）";
                }
            }
            return "目标 " + id;
        }

        private static string DefLabel<T>(string defName) where T : Def
        {
            T def = DefDatabase<T>.GetNamedSilentFail(defName);
            return def == null ? defName : def.LabelCap.ToString();
        }

        private static string OptionalStuff(string[] parts, int index)
        {
            return index < parts.Length ? "，材料为“" + DefLabel<ThingDef>(parts[index]) + "”" : string.Empty;
        }

        private static string DescribeWorld(string[] parts)
        {
            string action = Part(parts, 1).ToLowerInvariant();
            if (action == "caravan") return "从地图 " + Part(parts, 2) + " 组建只含 AI 殖民者的商队，前往 tile " + Part(parts, 3) + "。";
            if (action == "move") return "将 AI 商队 " + Part(parts, 2) + " 改道前往 tile " + Part(parts, 3) + "。";
            if (action == "load") return "将目标 " + Part(parts, 3) + " 装入运输舱 " + Part(parts, 2) + "。";
            if (action == "launch") return "发射运输舱 " + Part(parts, 2) + " 前往 tile " + Part(parts, 3) + "。";
            if (action == "trade")
            {
                if (parts.Length > 4 && parts[4].Equals("ui", StringComparison.OrdinalIgnoreCase))
                    return "打开世界对象 " + Part(parts, 2) + " 的贸易界面。";
                bool buy = parts.Length > 4 && parts[4].Equals("buy", StringComparison.OrdinalIgnoreCase);
                return "让 AI 谈判者 " + Part(parts, 3) + (buy ? "购买 " : "出售 ") + Part(parts, 5) + " x" + Part(parts, 6) + "。";
            }
            if (action == "quest") return "让 AI 殖民者 " + Part(parts, 4) + " 接受任务 " + Part(parts, 2) + "。";
            return "执行世界地图操作“" + action + "”。";
        }

        private static string DesignationLabel(string kind)
        {
            switch (kind.ToLowerInvariant())
            {
                case "cancel": return "取消指定";
                case "chop": return "伐木";
                case "cut": return "削除普通植物";
                case "harvest": return "收获";
                case "mine": return "采矿";
                case "deconstruct": return "拆除";
                case "hunt": return "狩猎";
                case "slaughter": return "宰杀";
                case "tame": return "驯服";
                case "haul": return "搬运";
                case "haul_chunks": return "仅搬运石块";
                case "unforbid": return "解禁";
                case "forbid": return "禁用";
                case "claim": return "认领";
                case "smooth": return "打磨表面";
                case "paint_building": return "粉饰建筑";
                case "paint_floor": return "粉饰地板";
                case "remove_building_paint": return "移除建筑涂料";
                case "remove_floor_paint": return "移除地板涂料";
                case "remove_plan": return "移除计划";
                case "strip": return "剥取衣物";
                default: return kind;
            }
        }

        private static string JobLabel(string kind)
        {
            switch (kind.ToLowerInvariant())
            {
                case "rescue": return "救援";
                case "tend": return "治疗";
                case "capture": return "抓捕";
                case "arrest": return "逮捕";
                case "clean": return "清洁";
                case "repair": return "修理";
                case "equip": return "装备武器";
                case "wear": return "穿戴服装";
                default: return kind;
            }
        }

        private static string DescribeAdvanced(string[] parts)
        {
            string kind = Part(parts, 2).ToLowerInvariant();
            string label;
            switch (kind)
            {
                case "work": label = "一次设置殖民者全部工作优先级"; break;
                case "storedelete": label = "删除完成搬运后的 AI 临时垃圾储存区"; break;
                case "autowork": label = "按技能和热情自动生成工作分工"; break;
                case "timetablehour": label = "设置逐小时作息"; break;
                case "selftend": label = "设置自我治疗"; break;
                case "hostility": label = "设置敌对反应模式"; break;
                case "area": label = "分配允许活动区"; break;
                case "area_new": label = "创建允许活动区"; break;
                case "policycreate": label = "创建服装、药物或食物方案"; break;
                case "policyedit": label = "编辑方案物品许可"; break;
                case "storecategory": label = "按类别筛选仓储"; break;
                case "storededicated": label = "设置专属仓储"; break;
                case "haulrules": label = "设置自动搬运规则"; break;
                case "temp": label = "设置温控目标温度"; break;
                case "rebuild": label = "设置自动重建"; break;
                case "homeauto": label = "自动更新居住区"; break;
                case "floor": label = "批量铺设地板"; break;
                case "conduit": label = "批量铺设电缆"; break;
                case "hiddenconduit": label = "沿路径铺设隐藏电缆"; break;
                case "fortify": label = "建设防御工事（兼容命令）"; break;
                case "defense": label = "在外围缺口外建造钢铁陷阱防御工事"; break;
                case "surgery": label = "添加手术账单"; break;
                case "organharvest": label = "按肺肾心脏顺序处理 AI 俘虏"; break;
                case "animal": label = "管理动物训练和放归"; break;
                case "deepauto": label = "按资源自动放置深钻井"; break;
                case "bill":
                case "billconfig": label = "配置高级生产账单"; break;
                case "billorder": label = "调整生产账单顺序"; break;
                default: label = "调整高级管理设置“" + Part(parts, 2) + "”"; break;
            }
            return "在地图 " + Part(parts, 1) + " 上" + label + "。";
        }
    }

    internal static class AICoopAgentRuntime
    {
        private static bool gameLoading;
        private static Game activeGame;
        private static bool workPauseChanged;
        private static bool workPausePending;
        private static bool externalAgentThinking;
        private static DateTime workPauseEligibleAtUtc = DateTime.MinValue;
        private static DateTime lastWorkPauseEndedAtUtc = DateTime.MinValue;

        public static bool IsGameLoading { get { return gameLoading; } }

        public static void NotifyGameLoading()
        {
            if (gameLoading) return;
            gameLoading = true;
            // Loading must not toggle the old save's TickManager.
            workPausePending = false;
            workPauseChanged = false;
            externalAgentThinking = false;
            workPauseEligibleAtUtc = DateTime.MinValue;
            AICoopStateSerializer.ClearCaches();
            Verse.Log.Message("[AI 协作队友] 游戏正在载入，已清空旧 Agent 状态。");
        }

        public static void NotifyGameReady()
        {
            activeGame = Verse.Current.Game;
            gameLoading = false;
            workPausePending = false;
            workPauseChanged = false;
            externalAgentThinking = false;
            lastWorkPauseEndedAtUtc = DateTime.MinValue;
            AICoopStateSerializer.ClearCaches();
            Verse.Log.Message("[AI 协作队友] 游戏载入完成，Agent 桥接已就绪。");
        }

        public static void Update()
        {
            if (activeGame != null && !object.ReferenceEquals(activeGame, Verse.Current.Game))
                NotifyGameLoading();
            if (IsGameLoading) return;
            if (!AICoopAgentBridge.IsConnected)
            {
                if (externalAgentThinking) CompleteExternalAgentThinking();
                return;
            }
            TryStartPendingWorkPause();
        }

        private static void BeginWorkPause()
        {
            if (IsGameLoading || workPauseChanged) return;
            AICoopSettings settings = AICoopMod.Settings;
            if (settings == null || !settings.pauseDuringWorkAI || Find.TickManager == null || Find.TickManager.Paused) return;
            int minimumSeconds = Math.Max(1, settings.minWorkSecondsBetweenPauses);
            DateTime now = DateTime.UtcNow;
            if (lastWorkPauseEndedAtUtc == DateTime.MinValue || (now - lastWorkPauseEndedAtUtc).TotalSeconds >= minimumSeconds)
            {
                Find.TickManager.TogglePaused();
                workPauseChanged = true;
                workPausePending = false;
                return;
            }
            workPausePending = true;
            workPauseEligibleAtUtc = lastWorkPauseEndedAtUtc.AddSeconds(minimumSeconds);
        }

        internal static void BeginExternalAgentThinking()
        {
            externalAgentThinking = true;
            BeginWorkPause();
        }

        internal static void CompleteExternalAgentThinking()
        {
            externalAgentThinking = false;
            RestoreWorkPause();
        }

        private static void RestoreWorkPause()
        {
            workPausePending = false;
            if (!workPauseChanged) return;
            if (Find.TickManager != null && Find.TickManager.Paused) Find.TickManager.TogglePaused();
            workPauseChanged = false;
            lastWorkPauseEndedAtUtc = DateTime.UtcNow;
        }

        private static void TryStartPendingWorkPause()
        {
            if (!workPausePending) return;
            if (AICoopMod.Settings == null || !AICoopMod.Settings.pauseDuringWorkAI ||
                !externalAgentThinking || Find.TickManager == null || Find.TickManager.Paused)
            {
                workPausePending = false;
                return;
            }
            if (DateTime.UtcNow < workPauseEligibleAtUtc) return;
            Find.TickManager.TogglePaused();
            workPauseChanged = true;
            workPausePending = false;
        }

    }

    internal static class AICoopStateSerializer
    {
        private sealed class MaterialAvailabilitySnapshot
        {
            public int Tick;
            public readonly Dictionary<ThingDef, int> Amounts = new Dictionary<ThingDef, int>();
        }

        private static readonly Dictionary<int, MaterialAvailabilitySnapshot> MaterialSnapshots =
            new Dictionary<int, MaterialAvailabilitySnapshot>();
        private static object dynamicSnapshotGame;
        private static bool dynamicSnapshotInitialized;
        private static Dictionary<string, string> lastDynamicLines = new Dictionary<string, string>(StringComparer.Ordinal);
        private static HashSet<int> sentQuestIds = new HashSet<int>();
        private sealed class HarnessPawnSnapshot
        {
            public int Id;
            public string Owner;
            public string Role;
            public string Name;
            public int MapId;
            public IntVec3 Position;
            public bool Spawned;
            public bool Downed;
            public bool Mental;
            public string MentalState;
            public bool RecoverableInjury;
            public Pawn PawnRef;
        }

        private sealed class HarnessEnemySnapshot
        {
            public int Id;
            public string Name;
            public int MapId;
            public IntVec3 Position;
            public bool Downed;
        }

        private static object harnessSnapshotGame;
        private static Dictionary<int, HarnessPawnSnapshot> harnessPawns = new Dictionary<int, HarnessPawnSnapshot>();
        private static Dictionary<int, HarnessEnemySnapshot> harnessEnemies = new Dictionary<int, HarnessEnemySnapshot>();
        private static HashSet<int> harnessMaturePlants = new HashSet<int>();
        private static HashSet<int> harnessQuestIds = new HashSet<int>();
        private static HashSet<Letter> harnessLetters = new HashSet<Letter>();

        internal static void ClearCaches()
        {
            MaterialSnapshots.Clear();
            dynamicSnapshotGame = null;
            dynamicSnapshotInitialized = false;
            lastDynamicLines = new Dictionary<string, string>(StringComparer.Ordinal);
            sentQuestIds = new HashSet<int>();
            harnessSnapshotGame = null;
            harnessPawns = new Dictionary<int, HarnessPawnSnapshot>();
            harnessEnemies = new Dictionary<int, HarnessEnemySnapshot>();
            harnessMaturePlants = new HashSet<int>();
            harnessQuestIds = new HashSet<int>();
            harnessLetters = new HashSet<Letter>();
        }

        public static string BuildPrompt(bool forceFullDynamicState = true)
        {
            StringBuilder state = new StringBuilder();
            AICoopGameComponent component = AICoopGameComponent.Current;
            state.AppendLine("STATE tick=" + Find.TickManager.TicksGame + " maps=" + Find.Maps.Count);
            List<WorkTypeDef> priorityOrder = DefDatabase<WorkTypeDef>.AllDefsListForReading
                .OrderByDescending(work => work.naturalPriority).ThenBy(work => work.defName, StringComparer.Ordinal).ToList();
            state.AppendLine("WORK_PRIORITY_ORDER " + string.Join(",", priorityOrder.Select(work => work.defName).ToArray()));
            state.AppendLine("WORK_PRIORITY_RULE C mapID work pawnID comma_separated_priorities; all_entries_required; 0=disabled,1=highest,4=lowest; disabled_work_must_be_0; priorities_1_or_2_require_adequate_skill");
            state.AppendLine(forceFullDynamicState
                ? "DYNAMIC_RULE external_status=full_each_request; DYNAMIC_INDEX=compact_current_ids_and_positions."
                : "DYNAMIC_RULE first_request=full; later_requests=delta_only; DYNAMIC_INDEX=compact_current_ids_and_positions; DYNAMIC_CHANGED=old_to_new; DYNAMIC_REMOVED=object_removed.");
            state.AppendLine(AICoopGuideManager.BuildPromptSection());
            state.AppendLine(AICoopPresetManager.BuildPromptSection(component));
            state.AppendLine("TOOL_PERMISSIONS " + (AICoopMod.Settings == null ? string.Empty : AICoopMod.Settings.ToolPermissionsSummary()));
            state.AppendLine("PLAYER_REQUESTS " + (component.PlayerRequests.Count == 0 ? "-" : string.Join("|", component.PlayerRequests.ToArray())));
            state.AppendLine("PRESET_FAILURES " + (component.PresetFailures.Count == 0 ? "-" : string.Join("|", component.PresetFailures.ToArray())));
            state.AppendLine("AI_CAPTURED_PRISONERS " + (component.AICapturedPrisoners.Count == 0 ? "-" : string.Join(",", component.AICapturedPrisoners.Select(id => id + ":stage=" + FindOrganStage(component, id)).ToArray())));
            state.AppendLine(AICoopAdvancedActions.BuildIdeologyPromptSection());
            state.AppendLine(AICoopWorldActions.BuildPromptSection(component));
            state.AppendLine("BUILD_DEFS " + string.Join(",", DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => def.building != null && def.BuildableByPlayer && def.canGenerateDefaultDesignator && !def.building.neverBuildable && def.IsResearchFinished)
                .Select(def => def.defName).OrderBy(name => name).ToArray()));
            state.AppendLine("PLANT_DEFS " + string.Join(",", DefDatabase<ThingDef>.AllDefsListForReading
                .Where(def => def.plant != null && def.plant.Sowable &&
                    (def.plant.sowResearchPrerequisites == null || def.plant.sowResearchPrerequisites.All(project => project.IsFinished)))
                .Select(def => def.defName).OrderBy(name => name).ToArray()));
            state.AppendLine("RESEARCH_AVAILABLE " + string.Join(",", DefDatabase<ResearchProjectDef>.AllDefsListForReading
                .Where(project => project.CanStartNow).Select(project => project.defName).OrderBy(name => name).ToArray()));
            state.AppendLine("POLICY_INDEX outfit=" + string.Join(",", Current.Game.outfitDatabase.AllOutfits.Select((policy, index) => index + ":" + Clean(policy.label)).ToArray()) +
                " drug=" + string.Join(",", Current.Game.drugPolicyDatabase.AllPolicies.Select((policy, index) => index + ":" + Clean(policy.label)).ToArray()) +
                " food=" + string.Join(",", Current.Game.foodRestrictionDatabase.AllFoodRestrictions.Select((policy, index) => index + ":" + Clean(policy.label)).ToArray()));
            ResearchProjectDef currentResearch = Find.ResearchManager.GetProject();
            state.AppendLine("RESEARCH_CURRENT " + (currentResearch == null ? "-" : currentResearch.defName + ":" + Percent(currentResearch.ProgressPercent)));
            state.AppendLine("COLONISTS owner,id,name,map,pos,health,food,rest,mood,job,idle,drafted,downed,mental,background,traits,skills,disabled");
            foreach (Pawn pawn in PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists)
            {
                string owner = component.IsAI(pawn) ? "AI" : "PLAYER";
                string map = pawn.Map == null ? "world" : pawn.Map.uniqueID.ToString();
                string position = pawn.Spawned ? pawn.Position.x + ":" + pawn.Position.z : "-";
                string job = pawn.CurJobDef == null ? "-" : pawn.CurJobDef.defName;
                string mental = pawn.InMentalState && pawn.MentalStateDef != null ? pawn.MentalStateDef.defName : "-";
                string background = PawnBackground(pawn);
                string traits = PawnTraits(pawn);
                string skills = pawn.skills == null ? "-" : string.Join("|", pawn.skills.skills.Select(skill => skill.def.defName + ":" + skill.Level).ToArray());
                string disabled = string.Join("|", pawn.GetDisabledWorkTypes().Select(work => work.defName).ToArray());
                state.AppendLine("C " + owner + "," + pawn.thingIDNumber + "," + Clean(pawn.LabelShort) + "," + map + "," + position +
                    "," + Percent(pawn.health == null ? 0f : pawn.health.summaryHealth.SummaryHealthPercent) +
                    "," + NeedPercent(pawn.needs == null ? null : pawn.needs.food) +
                    "," + NeedPercent(pawn.needs == null ? null : pawn.needs.rest) +
                    "," + NeedPercent(pawn.needs == null ? null : pawn.needs.mood) +
                    "," + job + "," + Bool(pawn.mindState != null && pawn.mindState.IsIdle) + "," + Bool(pawn.Drafted) + "," + Bool(pawn.Downed) + "," + mental +
                    ",background=" + background + ",traits=" + traits + ",skills=" + skills + ",disabled=" + (disabled.NullOrEmpty() ? "-" : disabled) +
                    ",priorities=" + string.Join("|", priorityOrder.Select(work => pawn.workSettings == null || pawn.WorkTypeIsDisabled(work) ? "0" : pawn.workSettings.GetPriority(work).ToString()).ToArray()));
                AppendMoodCauses(state, pawn);
            }
            AppendTrackedPawnStates(state, component);

            foreach (Map map in Find.Maps)
            {
                List<Pawn> enemies = map.mapPawns.AllPawnsSpawned.Where(pawn => !pawn.Dead && pawn.HostileTo(Faction.OfPlayer)).ToList();
                string weather = map.weatherManager == null || map.weatherManager.curWeather == null ? "-" : map.weatherManager.curWeather.defName;
                string danger = map.dangerWatcher == null ? "-" : map.dangerWatcher.DangerRating.ToString();
                state.AppendLine("MAP id=" + map.uniqueID + " size=" + map.Size.x + "x" + map.Size.z + " weather=" + weather + " danger=" + danger + " enemies=" + enemies.Count);
                float originalRaidPoints, adjustedRaidPoints;
                if (AICoopKitingManager.TryGetRaidPoints(map, out originalRaidPoints, out adjustedRaidPoints))
                    state.AppendLine("RAID_POINTS map=" + map.uniqueID + " original=" + originalRaidPoints.ToString("F0") + " compressed=" + adjustedRaidPoints.ToString("F0") +
                        " turret_priority=" + (enemies.Count >= 10 && (adjustedRaidPoints > 700f || originalRaidPoints > 2100f) ? "1" : "0"));
                state.AppendLine("ROOM_SITES " + FindRoomSites(map, component));
                state.AppendLine("ROOM_PURPOSES " + component.RoomPurposeSummary(map));
                int colonistCount = map.mapPawns.FreeColonistsSpawned.Count;
                state.AppendLine("RICE_FARM_TARGET map=" + map.uniqueID + " minimum_cells=" + (colonistCount * 13) + " colonists=" + colonistCount);
                AppendTerrainSummary(state, map);
                AppendPerimeterState(state, map);
                int prisonerCount = map.mapPawns.AllPawnsSpawned.Count(pawn => pawn != null && pawn.IsPrisonerOfColony);
                int colonistBedSlots = 0;
                int prisonerBedSlots = 0;
                foreach (Building_Bed bed in map.listerThings.GetThingsOfType<Building_Bed>())
                {
                    if (bed == null || bed.Medical) continue;
                    int freeSlots = Math.Max(0, bed.SleepingSlotsCount - (bed.OwnersForReading == null ? 0 : bed.OwnersForReading.Count));
                    if (bed.ForPrisoners) prisonerBedSlots += freeSlots;
                    else if (bed.ForColonists) colonistBedSlots += freeSlots;
                }
                state.AppendLine("BED_CAPACITY map=" + map.uniqueID + " colonists=" + colonistCount + " colonist_beds=" + colonistBedSlots +
                    " colonist_deficit=" + Math.Max(0, colonistCount - colonistBedSlots) + " prisoners=" + prisonerCount +
                    " prisoner_beds=" + prisonerBedSlots + " prisoner_deficit=" + Math.Max(0, prisonerCount - prisonerBedSlots));
                state.AppendLine("RESOURCES " + string.Join(",", map.resourceCounter.AllCountedAmounts
                    .Where(pair => pair.Value > 0).OrderByDescending(pair => pair.Value)
                    .Select(pair => pair.Key.defName + ":" + pair.Value).ToArray()));
                state.AppendLine("BUILD_MATERIALS " + string.Join(",", DefDatabase<ThingDef>.AllDefsListForReading
                    .Where(def => def.IsStuff)
                    .Select(def => new KeyValuePair<ThingDef, int>(def, EstimateMaterialAvailability(map, def)))
                    .Where(pair => pair.Value > 0)
                    .OrderBy(pair => IsStrategicMaterial(pair.Key) ? 1 : 0)
                    .ThenByDescending(pair => pair.Value)
                    .Select(pair => pair.Key.defName + ":" + pair.Value + (IsStrategicMaterial(pair.Key) ? ":strategic" : ":ordinary"))
                    .ToArray()));
                AppendStartingGear(state, map);
                foreach (IntVec3 cell in map.AllCells.Where(cell => map.deepResourceGrid.ThingDefAt(cell) != null).Take(40))
                {
                    ThingDef resource = map.deepResourceGrid.ThingDefAt(cell);
                    state.AppendLine("DEEP " + cell.x + ":" + cell.z + "," + resource.defName + ",count=" + map.deepResourceGrid.CountAt(cell));
                }

                foreach (Building building in map.listerBuildings.allBuildingsColonist.Where(IsRelevantBuildingForState))
                {
                    StringBuilder details = new StringBuilder();
                    CompPowerTrader power = building.TryGetComp<CompPowerTrader>();
                    if (power != null) details.Append(",power=").Append(power.PowerOn ? "on" : "off");
                    CompPowerBattery battery = building.TryGetComp<CompPowerBattery>();
                    if (battery != null) details.Append(",battery=").Append(Percent(battery.StoredEnergyPct));
                    CompTempControl temperatureControl = building.TryGetComp<CompTempControl>();
                    if (temperatureControl != null) details.Append(",targetTemp=").Append(temperatureControl.TargetTemperature.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
                    Building_Bed bed = building as Building_Bed;
                    if (bed != null)
                    {
                        int freeSlots = Math.Max(0, bed.SleepingSlotsCount - (bed.OwnersForReading == null ? 0 : bed.OwnersForReading.Count));
                        details.Append(",bedType=").Append(bed.ForOwnerType).Append(",medical=").Append(bed.Medical ? "1" : "0")
                            .Append(",freeSlots=").Append(freeSlots).Append(",slots=").Append(bed.SleepingSlotsCount);
                    }
                    Building_Door door = building as Building_Door;
                    if (door != null) details.Append(",doorOpen=").Append(door.Open ? "1" : "0").Append(",doorHoldOpen=").Append(door.HoldOpen ? "1" : "0");
                    state.AppendLine("BLD " + building.thingIDNumber + "," + building.def.defName + "," + building.Position.x + ":" + building.Position.z + details);

                    Building_WorkTable table = building as Building_WorkTable;
                    if (table != null)
                    {
                        string recipes = string.Join(",", table.def.AllRecipes
                            .Where(recipe => (recipe.researchPrerequisite == null || recipe.researchPrerequisite.IsFinished) &&
                                (recipe.researchPrerequisites == null || recipe.researchPrerequisites.All(project => project.IsFinished)))
                            .Select(recipe => recipe.defName).OrderBy(name => name).ToArray());
                        string bills = table.BillStack == null ? string.Empty : string.Join(",", table.BillStack.Bills.Select(bill => bill.recipe.defName).ToArray());
                        state.AppendLine("TABLE id=" + table.thingIDNumber + " recipes=" + recipes + " bills=" + (bills.NullOrEmpty() ? "-" : bills));
                    }
                }

                foreach (Zone zone in map.zoneManager.AllZones)
                {
                    if (zone.cells == null || zone.cells.Count == 0) continue;
                    int minX = zone.cells.Min(cell => cell.x);
                    int maxX = zone.cells.Max(cell => cell.x);
                    int minZ = zone.cells.Min(cell => cell.z);
                    int maxZ = zone.cells.Max(cell => cell.z);
                    Zone_Growing growing = zone as Zone_Growing;
                    state.AppendLine("ZONE " + zone.ID + "," + (growing == null ? "stockpile" : "growing:" + growing.PlantDefToGrow.defName) +
                        "," + minX + ":" + minZ + "-" + maxX + ":" + maxZ + ",cells=" + zone.CellCount + ",label=" + Clean(zone.RenamableLabel));
                    Zone_Stockpile stockpile = zone as Zone_Stockpile;
                    if (stockpile != null && AICoopActionExecutor.IsTemporaryDumpingStockpile(stockpile))
                    {
                        int pendingChunkHaul = AICoopActionExecutor.PendingStoneChunkHaulCount(map);
                        int storedChunks = stockpile.AllContainedThings.Count(AICoopActionExecutor.IsStoneChunk);
                        state.AppendLine("TEMP_DUMP_ZONE map=" + map.uniqueID + " id=" + stockpile.ID + " cells=" + stockpile.CellCount +
                            " stored_chunks=" + storedChunks + " pending_chunk_haul=" + pendingChunkHaul +
                            (pendingChunkHaul == 0 ? " action=delete_with_C_storeDelete" : " action=wait_for_hauling"));
                    }
                }

                var blueprints = map.listerThings.AllThings.OfType<Blueprint>().ToList();
                state.AppendLine("BLUEPRINT_SUMMARY map=" + map.uniqueID + " " + string.Join(",", blueprints.GroupBy(bp => bp.EntityToBuild()?.defName ?? bp.def.defName).Select(group => group.Key + ":" + group.Count()).ToArray()));
                var requiredMaterials = new Dictionary<ThingDef, int>();
                foreach (Blueprint bp in blueprints)
                    foreach (ThingDefCountClass cost in bp.TotalMaterialCost() ?? new List<ThingDefCountClass>())
                    {
                        if (cost?.thingDef == null) continue;
                        int previous;
                        requiredMaterials.TryGetValue(cost.thingDef, out previous);
                        requiredMaterials[cost.thingDef] = previous + bp.ThingCountNeeded(cost.thingDef);
                    }
                state.AppendLine("BLUEPRINT_MATERIAL_TOTAL map=" + map.uniqueID + " " + string.Join(",", requiredMaterials.Select(pair => pair.Key.defName + ":need=" + pair.Value + ":available=" + EstimateMaterialAvailability(map, pair.Key)).ToArray()));
                foreach (Blueprint blueprint in blueprints.Take(12))
                {
                    BuildableDef entity = blueprint.EntityToBuild();
                    state.AppendLine("BP " + blueprint.thingIDNumber + "," + (entity == null ? blueprint.def.defName : entity.defName) +
                        "," + blueprint.Position.x + ":" + blueprint.Position.z);
                    List<string> materialNeeds = new List<string>();
                    List<ThingDefCountClass> totalMaterialCost = blueprint.TotalMaterialCost();
                    foreach (ThingDefCountClass cost in totalMaterialCost ?? new List<ThingDefCountClass>())
                    {
                        if (cost == null || cost.thingDef == null) continue;
                        int remaining = blueprint.ThingCountNeeded(cost.thingDef);
                        if (remaining <= 0) continue;
                        int available = EstimateMaterialAvailability(map, cost.thingDef);
                        materialNeeds.Add(cost.thingDef.defName + ":remaining=" + remaining + ":available=" + available + ":shortage=" + Math.Max(0, remaining - available));
                    }
                    if (materialNeeds.Count > 0) state.AppendLine("BLUEPRINT_MATERIALS " + blueprint.thingIDNumber + " " + string.Join(",", materialNeeds.ToArray()));
                }
                foreach (Thing frame in map.listerThings.AllThings.Where(thing => thing.def.IsFrame))
                {
                    state.AppendLine("FRAME " + frame.thingIDNumber + "," + (frame.def.entityDefToBuild == null ? frame.def.defName : frame.def.entityDefToBuild.defName) +
                        "," + frame.Position.x + ":" + frame.Position.z);
                }

                foreach (Pawn enemy in enemies)
                {
                    state.AppendLine("E " + enemy.thingIDNumber + "," + Clean(enemy.LabelShort) + "," + map.uniqueID + "," + enemy.Position.x + ":" + enemy.Position.z +
                        ",health=" + Percent(enemy.health == null ? 0f : enemy.health.summaryHealth.SummaryHealthPercent) + ",downed=" + Bool(enemy.Downed));
                }
                AppendTargets(state, map, component);
            }
            string fullState = state.ToString();
            state.Clear();
            state.Append(BuildDynamicStateDelta(fullState, forceFullDynamicState));
            return ReorderForCache(state.ToString());
        }

        private static void AppendTrackedPawnStates(StringBuilder state, AICoopGameComponent component)
        {
            if (state == null) return;
            HashSet<int> written = new HashSet<int>();
            foreach (Pawn pawn in CurrentTrackedPawns())
            {
                if (pawn == null || pawn.IsColonist || pawn.Dead || !written.Add(pawn.thingIDNumber)) continue;
                string role = PawnRole(pawn, component);
                string owner = component != null && component.IsAI(pawn) ? "AI" : (pawn.IsColonist ? "PLAYER" : "NPC");
                string map = pawn.Map == null ? "world" : pawn.Map.uniqueID.ToString();
                string pos = pawn.Spawned ? pawn.Position.x + ":" + pawn.Position.z : "-";
                string faction = pawn.Faction == null ? "-" : Clean(pawn.Faction.Name);
                string job = pawn.CurJobDef == null ? "-" : pawn.CurJobDef.defName;
                string mental = pawn.InMentalState && pawn.MentalStateDef != null ? pawn.MentalStateDef.defName : "-";
                state.AppendLine("PAWN_STATUS id=" + pawn.thingIDNumber + " role=" + role + " owner=" + owner + " name=" + Clean(pawn.LabelShort) +
                    " faction=" + faction + " map=" + map + " pos=" + pos +
                    " health=" + Percent(pawn.health == null ? 0f : pawn.health.summaryHealth.SummaryHealthPercent) +
                    " food=" + NeedPercent(pawn.needs == null ? null : pawn.needs.food) +
                    " mood=" + NeedPercent(pawn.needs == null ? null : pawn.needs.mood) +
                    " job=" + job + " downed=" + Bool(pawn.Downed) + " mental=" + mental +
                    " kind=" + (pawn.kindDef == null ? "-" : pawn.kindDef.defName));
            }
        }

        private static IEnumerable<Pawn> CurrentTrackedPawns()
        {
            HashSet<int> seen = new HashSet<int>();
            foreach (Map map in Find.Maps)
            {
                if (map == null || map.mapPawns == null) continue;
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (IsTrackedHumanlikePawn(pawn) && seen.Add(pawn.thingIDNumber)) yield return pawn;
                }
            }
            if (Find.WorldObjects != null)
            {
                foreach (Caravan caravan in Find.WorldObjects.AllWorldObjects.OfType<Caravan>())
                {
                    if (caravan == null || caravan.PawnsListForReading == null) continue;
                    foreach (Pawn pawn in caravan.PawnsListForReading)
                    {
                        if (IsTrackedHumanlikePawn(pawn) && seen.Add(pawn.thingIDNumber)) yield return pawn;
                    }
                }
            }
        }

        private static bool IsTrackedHumanlikePawn(Pawn pawn)
        {
            if (pawn == null || pawn.Dead || pawn.RaceProps == null || !pawn.RaceProps.Humanlike) return false;
            if (pawn.IsColonist) return true;
            if (pawn.IsPrisonerOfColony || IsSlavePawn(pawn)) return true;
            return Faction.OfPlayer == null || !pawn.HostileTo(Faction.OfPlayer);
        }

        private static string PawnRole(Pawn pawn, AICoopGameComponent component)
        {
            if (pawn == null) return "unknown";
            if (component != null && component.IsAI(pawn)) return "AI_COLONIST";
            if (pawn.IsColonist) return "PLAYER_COLONIST";
            if (pawn.IsPrisonerOfColony) return "PRISONER";
            if (IsSlavePawn(pawn)) return "SLAVE";
            return "NPC";
        }

        private static bool IsSlavePawn(Pawn pawn)
        {
            if (pawn == null || pawn.guest == null) return false;
            Type type = pawn.guest.GetType();
            PropertyInfo property = type.GetProperty("IsSlave", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null && property.PropertyType == typeof(bool)) return (bool)property.GetValue(pawn.guest, null);
            FieldInfo field = type.GetField("isSlave", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return field != null && field.FieldType == typeof(bool) && (bool)field.GetValue(pawn.guest);
        }

        public static string BuildHarnessState(bool forceFullDynamicState)
        {
            string perimeterNotices = "CONTROL_RULE player=" + (AICoopMod.Settings != null && AICoopMod.Settings.PlayerCanControlAI ? "all" : "owned") +
                " ai=" + (AICoopMod.Settings != null && AICoopMod.Settings.AICanControlPlayer ? "all" : "owned") + "\n" +
                (AICoopGameComponent.Current?.PerimeterNotices() ?? "");
            if (!forceFullDynamicState && object.ReferenceEquals(harnessSnapshotGame, Verse.Current.Game))
            {
                return perimeterNotices + "\n" + BuildHarnessEventState();
            }

            bool firstHarnessSnapshot = !object.ReferenceEquals(harnessSnapshotGame, Verse.Current.Game);
            AICoopGameComponent harnessComponent = AICoopGameComponent.Current;
            HashSet<int> questsReportedBefore = harnessComponent == null || harnessComponent.ReportedQuestIds == null
                ? new HashSet<int>() : new HashSet<int>(harnessComponent.ReportedQuestIds);
            string prompt = BuildPrompt(forceFullDynamicState);
            StringBuilder state = new StringBuilder();
            if (!perimeterNotices.NullOrEmpty()) state.AppendLine(perimeterNotices);
            state.AppendLine("HARNESS_STATE mode=initial; fixed_baseline_and_full_colony_state; later_rounds=notifications_critical_changes_and_command_feedback_only");
            foreach (string rawLine in (prompt ?? string.Empty).Replace("\r", string.Empty).Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.StartsWith("WORK_PRIORITY_", StringComparison.Ordinal) ||
                    line.StartsWith("DYNAMIC_INDEX ", StringComparison.Ordinal) ||
                    line.StartsWith("DYNAMIC_STATE ", StringComparison.Ordinal) ||
                    line.StartsWith("DYNAMIC ", StringComparison.Ordinal) ||
                    line.StartsWith("DYNAMIC_ADDED ", StringComparison.Ordinal) ||
                    line.StartsWith("DYNAMIC_CHANGED ", StringComparison.Ordinal) ||
                    line.StartsWith("DYNAMIC_REMOVED ", StringComparison.Ordinal) ||
                    line.StartsWith("GUIDE_STATE ", StringComparison.Ordinal) ||
                    line.StartsWith("GUIDE_REFERENCE ", StringComparison.Ordinal) ||
                    line.StartsWith("GUIDE_MILESTONE ", StringComparison.Ordinal) ||
                    line.StartsWith("PRESETS available=", StringComparison.Ordinal) ||
                    line.StartsWith("PRESET_CATALOG ", StringComparison.Ordinal) ||
                    line.StartsWith("TOOL_PERMISSIONS ", StringComparison.Ordinal) ||
                    line.StartsWith("TRADE_MODE ", StringComparison.Ordinal) ||
                    line.StartsWith("RAID_POINTS ", StringComparison.Ordinal) ||
                    line.StartsWith("WORLD_SUMMARY ", StringComparison.Ordinal) ||
                    line.StartsWith("BLUEPRINT_SUMMARY ", StringComparison.Ordinal) ||
                    line.StartsWith("BLUEPRINT_MATERIAL_TOTAL ", StringComparison.Ordinal))
                {
                    state.AppendLine(line);
                }
            }
            foreach (RimWorld.Quest quest in AICoopWorldActions.CurrentAvailableQuests())
            {
                if (quest == null) continue;
                string marker = "WORLD_QUEST id=" + quest.id + " ";
                if (!firstHarnessSnapshot || questsReportedBefore.Contains(quest.id) || state.ToString().IndexOf(marker, StringComparison.Ordinal) >= 0) continue;
                state.AppendLine("QUEST_APPEARED " + AICoopWorldActions.DescribeQuest(quest));
                sentQuestIds.Add(quest.id);
            }
            AppendCurrentHarnessNotices(state);
            CaptureHarnessSnapshot();
            return state.ToString().TrimEnd();
        }

        private static string BuildHarnessEventState()
        {
            StringBuilder state = new StringBuilder();
            int changeCount = 0;
            Dictionary<int, HarnessPawnSnapshot> currentPawns = CurrentHarnessPawns();
            foreach (KeyValuePair<int, HarnessPawnSnapshot> pair in currentPawns)
            {
                HarnessPawnSnapshot current = pair.Value;
                HarnessPawnSnapshot previous;
                if (!harnessPawns.TryGetValue(pair.Key, out previous))
                {
                    state.AppendLine("PAWN_JOINED " + HarnessPawnLine(current));
                    changeCount++;
                    continue;
                }
                if (!previous.Mental && current.Mental)
                {
                    state.AppendLine("PAWN_MENTAL_BREAK " + HarnessPawnLine(current));
                    changeCount++;
                }
                if (!string.Equals(previous.Role, current.Role, StringComparison.Ordinal) ||
                    !string.Equals(previous.Owner, current.Owner, StringComparison.Ordinal))
                {
                    state.AppendLine("PAWN_ROLE_CHANGED " + HarnessPawnLine(current));
                    changeCount++;
                }
                if (!previous.Downed && current.Downed)
                {
                    state.AppendLine("PAWN_DOWNED " + HarnessPawnLine(current));
                    changeCount++;
                }
                if ((previous.Downed && !current.Downed || previous.Mental && !current.Mental) && !current.Downed && !current.Mental)
                {
                    state.AppendLine("PAWN_CAN_ACT_AGAIN " + HarnessPawnLine(current));
                    changeCount++;
                }
                if (previous.RecoverableInjury && !current.RecoverableInjury)
                {
                    state.AppendLine("PAWN_FULLY_RECOVERED " + HarnessPawnLine(current));
                    changeCount++;
                }
            }
            foreach (KeyValuePair<int, HarnessPawnSnapshot> pair in harnessPawns)
            {
                if (!currentPawns.ContainsKey(pair.Key))
                {
                    string goneEvent = pair.Value.PawnRef != null && pair.Value.PawnRef.Dead ? "PAWN_DIED" : "PAWN_LEFT";
                    state.AppendLine(goneEvent + " id=" + pair.Key + " role=" + Clean(pair.Value.Role) +
                        " owner=" + pair.Value.Owner + " name=" + Clean(pair.Value.Name));
                    changeCount++;
                }
            }

            Dictionary<int, HarnessEnemySnapshot> currentEnemies = CurrentHarnessEnemies();
            foreach (KeyValuePair<int, HarnessEnemySnapshot> pair in currentEnemies)
            {
                HarnessEnemySnapshot current = pair.Value;
                HarnessEnemySnapshot previous;
                if (!harnessEnemies.TryGetValue(pair.Key, out previous))
                {
                    state.AppendLine("ENEMY_APPEARED " + HarnessEnemyLine(current));
                    changeCount++;
                }
                else if (!previous.Downed && current.Downed)
                {
                    state.AppendLine("ENEMY_DOWNED " + HarnessEnemyLine(current));
                    changeCount++;
                }
            }
            foreach (KeyValuePair<int, HarnessEnemySnapshot> pair in harnessEnemies)
            {
                if (!currentEnemies.ContainsKey(pair.Key))
                {
                    state.AppendLine("ENEMY_GONE id=" + pair.Key + " map=" + pair.Value.MapId + " name=" + Clean(pair.Value.Name));
                    changeCount++;
                }
            }

            HashSet<int> currentQuestIds = new HashSet<int>();
            foreach (RimWorld.Quest quest in AICoopWorldActions.CurrentAvailableQuests())
            {
                if (quest == null) continue;
                currentQuestIds.Add(quest.id);
                if (harnessQuestIds.Contains(quest.id)) continue;
                state.AppendLine("QUEST_APPEARED " + AICoopWorldActions.DescribeQuest(quest));
                changeCount++;
            }

            HashSet<int> currentMaturePlants = new HashSet<int>();
            foreach (Map map in Find.Maps)
            {
                List<Plant> newlyMature = new List<Plant>();
                foreach (Plant plant in map.listerThings.AllThings.OfType<Plant>())
                {
                    if (!IsHarnessPlantFullyMature(plant)) continue;
                    currentMaturePlants.Add(plant.thingIDNumber);
                    if (!harnessMaturePlants.Contains(plant.thingIDNumber)) newlyMature.Add(plant);
                }
                foreach (IGrouping<ThingDef, Plant> group in newlyMature.GroupBy(plant => plant.def))
                {
                    int minX = group.Min(plant => plant.Position.x);
                    int maxX = group.Max(plant => plant.Position.x);
                    int minZ = group.Min(plant => plant.Position.z);
                    int maxZ = group.Max(plant => plant.Position.z);
                    state.AppendLine("PLANTS_100_PERCENT map=" + map.uniqueID + " def=" + group.Key.defName +
                        " count=" + group.Count() + " area=" + minX + ":" + minZ + "-" + maxX + ":" + maxZ);
                    changeCount++;
                }
            }

            HashSet<Letter> currentLetters = CurrentHarnessLetters();
            foreach (Letter letter in currentLetters)
            {
                if (harnessLetters.Contains(letter) || IsAgentPlayerRequestLetter(letter)) continue;
                state.AppendLine("GAME_NOTIFICATION label=" + Clean(letter.Label.ToString()) + " text=" + Truncate(Clean(HarnessLetterText(letter)), 600));
                changeCount++;
            }

            harnessPawns = currentPawns;
            harnessEnemies = currentEnemies;
            foreach (int questId in currentQuestIds) harnessQuestIds.Add(questId);
            harnessMaturePlants = currentMaturePlants;
            harnessLetters = currentLetters;
            state.Insert(0, "HARNESS_STATE mode=event critical_changes=" + changeCount +
                "; omitted=terrain_layout_normal_growth_static_prompt_paths_permissions_trade_mode\n");
            return state.ToString().TrimEnd();
        }

        private static void CaptureHarnessSnapshot()
        {
            harnessSnapshotGame = Verse.Current.Game;
            harnessPawns = CurrentHarnessPawns();
            harnessEnemies = CurrentHarnessEnemies();
            foreach (RimWorld.Quest quest in AICoopWorldActions.CurrentAvailableQuests())
            {
                if (quest != null) harnessQuestIds.Add(quest.id);
            }
            harnessMaturePlants = new HashSet<int>(Find.Maps.SelectMany(map => map.listerThings.AllThings.OfType<Plant>())
                .Where(IsHarnessPlantFullyMature)
                .Select(plant => plant.thingIDNumber));
            harnessLetters = CurrentHarnessLetters();
        }

        private static Dictionary<int, HarnessPawnSnapshot> CurrentHarnessPawns()
        {
            AICoopGameComponent component = AICoopGameComponent.Current;
            Dictionary<int, HarnessPawnSnapshot> result = new Dictionary<int, HarnessPawnSnapshot>();
            foreach (Pawn pawn in CurrentTrackedPawns())
            {
                if (pawn == null) continue;
                result[pawn.thingIDNumber] = new HarnessPawnSnapshot
                {
                    Id = pawn.thingIDNumber,
                    Owner = component != null && component.IsAI(pawn) ? "AI" : "PLAYER",
                    Role = PawnRole(pawn, component),
                    Name = pawn.LabelShort,
                    MapId = pawn.Map == null ? -1 : pawn.Map.uniqueID,
                    Position = pawn.Position,
                    Spawned = pawn.Spawned,
                    Downed = pawn.Downed,
                    Mental = pawn.InMentalState,
                    MentalState = pawn.InMentalState && pawn.MentalStateDef != null ? pawn.MentalStateDef.defName : "-",
                    RecoverableInjury = HasRecoverableInjury(pawn),
                    PawnRef = pawn
                };
            }
            return result;
        }

        private static Dictionary<int, HarnessEnemySnapshot> CurrentHarnessEnemies()
        {
            Dictionary<int, HarnessEnemySnapshot> result = new Dictionary<int, HarnessEnemySnapshot>();
            foreach (Map map in Find.Maps)
            {
                foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
                {
                    if (pawn == null || pawn.Dead || !pawn.HostileTo(Faction.OfPlayer)) continue;
                    result[pawn.thingIDNumber] = new HarnessEnemySnapshot
                    {
                        Id = pawn.thingIDNumber,
                        Name = pawn.LabelShort,
                        MapId = map.uniqueID,
                        Position = pawn.Position,
                        Downed = pawn.Downed
                    };
                }
            }
            return result;
        }

        private static bool HasRecoverableInjury(Pawn pawn)
        {
            return pawn != null && pawn.health != null && pawn.health.hediffSet != null &&
                pawn.health.hediffSet.hediffs.OfType<Hediff_Injury>().Any(injury => !injury.IsPermanent());
        }

        private static bool IsHarnessPlantFullyMature(Plant plant)
        {
            return plant != null && plant.Spawned && plant.HarvestableNow && plant.Growth >= 0.9999f;
        }

        private static string HarnessPawnLine(HarnessPawnSnapshot pawn)
        {
            string position = pawn.Spawned ? pawn.Position.x + ":" + pawn.Position.z : "-";
            return "id=" + pawn.Id + " role=" + Clean(pawn.Role) + " owner=" + pawn.Owner + " name=" + Clean(pawn.Name) + " map=" + pawn.MapId +
                " pos=" + position + " downed=" + Bool(pawn.Downed) + " mental=" + Clean(pawn.MentalState);
        }

        private static string HarnessEnemyLine(HarnessEnemySnapshot pawn)
        {
            return "id=" + pawn.Id + " name=" + Clean(pawn.Name) + " map=" + pawn.MapId + " pos=" +
                pawn.Position.x + ":" + pawn.Position.z + " downed=" + Bool(pawn.Downed);
        }

        private static HashSet<Letter> CurrentHarnessLetters()
        {
            return Find.LetterStack == null
                ? new HashSet<Letter>()
                : new HashSet<Letter>(Find.LetterStack.LettersListForReading);
        }

        private static void AppendCurrentHarnessNotices(StringBuilder state)
        {
            foreach (Letter letter in CurrentHarnessLetters())
            {
                if (IsAgentPlayerRequestLetter(letter)) continue;
                state.AppendLine("GAME_NOTIFICATION_CURRENT label=" + Clean(letter.Label.ToString()) +
                    " text=" + Truncate(Clean(HarnessLetterText(letter)), 600));
            }
        }

        private static string HarnessLetterText(Letter letter)
        {
            if (letter == null) return string.Empty;
            System.Reflection.PropertyInfo property = letter.GetType().GetProperty("Text",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            if (property != null)
            {
                object value = property.GetValue(letter, null);
                if (value != null) return value.ToString();
            }
            System.Reflection.FieldInfo field = letter.GetType().GetField("text",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
            object fieldValue = field == null ? null : field.GetValue(letter);
            return fieldValue == null ? string.Empty : fieldValue.ToString();
        }

        private static bool IsAgentPlayerRequestLetter(Letter letter)
        {
            return letter != null && letter.Label.ToString().IndexOf("AI 请求玩家", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string Truncate(string value, int maxLength)
        {
            if (value.NullOrEmpty() || value.Length <= maxLength) return value ?? string.Empty;
            return value.Substring(0, maxLength) + "...";
        }

        private static string BuildDynamicStateDelta(string fullState, bool forceFullState)
        {
            List<string> fixedLines = new List<string>();
            List<string> dynamicLines = new List<string>();
            foreach (string rawLine in (fullState ?? string.Empty).Replace("\r", string.Empty).Split('\n'))
            {
                string line = rawLine.Trim();
                if (line.NullOrEmpty()) continue;
                if (IsDynamicStateLine(line)) dynamicLines.Add(line);
                else fixedLines.Add(line);
            }

            Game game = Verse.Current.Game;
            if (!object.ReferenceEquals(dynamicSnapshotGame, game))
            {
                dynamicSnapshotGame = game;
                dynamicSnapshotInitialized = false;
                lastDynamicLines = new Dictionary<string, string>(StringComparer.Ordinal);
                sentQuestIds = new HashSet<int>();
            }

            List<string> questLines = dynamicLines.Where(IsOneTimeQuestLine).ToList();
            List<string> persistentDynamicLines = dynamicLines.Where(line => !IsOneTimeQuestLine(line)).ToList();
            Dictionary<string, string> current = new Dictionary<string, string>(StringComparer.Ordinal);
            Dictionary<string, int> duplicateCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string line in persistentDynamicLines)
            {
                string baseKey = DynamicStateKey(line);
                int count;
                duplicateCounts.TryGetValue(baseKey, out count);
                duplicateCounts[baseKey] = count + 1;
                string key = count == 0 ? baseKey : baseKey + "#" + count;
                current[key] = line;
            }

            StringBuilder result = new StringBuilder(fullState == null ? 256 : fullState.Length);
            foreach (string line in fixedLines) result.AppendLine(line);
            if (!forceFullState && dynamicSnapshotInitialized) AppendDynamicIndex(result, persistentDynamicLines);

            List<KeyValuePair<int, string>> newQuestLines = new List<KeyValuePair<int, string>>();
            AICoopGameComponent questComponent = AICoopGameComponent.Current;
            foreach (string line in questLines)
            {
                int questId;
                if (!TryGetQuestId(line, out questId) || !sentQuestIds.Add(questId)) continue;
                if (questComponent != null && !questComponent.MarkQuestReported(questId)) continue;
                newQuestLines.Add(new KeyValuePair<int, string>(questId, line));
            }

            if (forceFullState)
            {
                result.AppendLine("DYNAMIC_STATE version=1 mode=full changed=" + (current.Count + newQuestLines.Count));
                foreach (KeyValuePair<string, string> pair in current) result.AppendLine("DYNAMIC " + pair.Value);
                foreach (KeyValuePair<int, string> quest in newQuestLines)
                    result.AppendLine("DYNAMIC WORLD_QUEST:" + quest.Key + " " + quest.Value);
                return result.ToString().TrimEnd();
            }
            if (!dynamicSnapshotInitialized)
            {
                result.AppendLine("DYNAMIC_STATE version=1 mode=full changed=" + (current.Count + newQuestLines.Count));
                foreach (KeyValuePair<string, string> pair in current) result.AppendLine("DYNAMIC " + pair.Value);
                foreach (KeyValuePair<int, string> quest in newQuestLines)
                    result.AppendLine("DYNAMIC WORLD_QUEST:" + quest.Key + " " + quest.Value);
            }
            else
            {
                int changed = 0;
                foreach (KeyValuePair<string, string> pair in current)
                {
                    string previous;
                    if (!lastDynamicLines.TryGetValue(pair.Key, out previous))
                    {
                        result.AppendLine("DYNAMIC_ADDED key=" + pair.Key + " value=" + pair.Value);
                        changed++;
                    }
                    else if (!string.Equals(previous, pair.Value, StringComparison.Ordinal))
                    {
                        result.AppendLine("DYNAMIC_CHANGED key=" + pair.Key + " old=" + previous + " new=" + pair.Value);
                        changed++;
                    }
                }
                foreach (KeyValuePair<int, string> quest in newQuestLines)
                {
                    result.AppendLine("DYNAMIC_ADDED key=WORLD_QUEST:" + quest.Key + " value=" + quest.Value);
                    changed++;
                }
                foreach (KeyValuePair<string, string> pair in lastDynamicLines)
                {
                    if (!current.ContainsKey(pair.Key))
                    {
                        result.AppendLine("DYNAMIC_REMOVED key=" + pair.Key + " old=" + pair.Value);
                        changed++;
                    }
                }
                result.AppendLine("DYNAMIC_STATE version=1 mode=delta changed=" + changed);
            }
            lastDynamicLines = current;
            dynamicSnapshotInitialized = true;
            return result.ToString().TrimEnd();
        }

        private static bool IsOneTimeQuestLine(string line)
        {
            int questId;
            return TryGetQuestId(line, out questId);
        }

        private static bool TryGetQuestId(string line, out int questId)
        {
            questId = 0;
            if (line.NullOrEmpty() || !line.StartsWith("WORLD_QUEST ", StringComparison.Ordinal)) return false;
            System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(
                line, "^WORLD_QUEST\\s+id=(\\d+)\\s+state=NotYetAccepted(?:\\s|$)",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            return match.Success && Int32.TryParse(match.Groups[1].Value, out questId);
        }

        private static void AppendDynamicIndex(StringBuilder result, IEnumerable<string> dynamicLines)
        {
            if (result == null || dynamicLines == null) return;
            List<string> index = new List<string>();
            foreach (string line in dynamicLines)
            {
                if (line.StartsWith("C ", StringComparison.Ordinal) || line.StartsWith("MAP ", StringComparison.Ordinal) ||
                    line.StartsWith("BLD ", StringComparison.Ordinal) || line.StartsWith("BP ", StringComparison.Ordinal) ||
                    line.StartsWith("FRAME ", StringComparison.Ordinal) || line.StartsWith("E ", StringComparison.Ordinal) ||
                    line.StartsWith("T ", StringComparison.Ordinal))
                {
                    string compact = CompactDynamicIndexLine(line);
                    index.Add(compact);
                }
            }
            if (index.Count > 0) result.AppendLine("DYNAMIC_INDEX " + string.Join("|", index.Take(220).ToArray()));
        }

        private static string CompactDynamicIndexLine(string line)
        {
            if (line.StartsWith("C ", StringComparison.Ordinal))
            {
                string[] parts = line.Substring(2).Split(new[] { ',' }, 5);
                return parts.Length >= 5 ? "C " + parts[0] + "," + parts[1] + "," + parts[2] + "," + parts[3] + "," + parts[4] : line;
            }
            if (line.StartsWith("BLD ", StringComparison.Ordinal) || line.StartsWith("BP ", StringComparison.Ordinal) ||
                line.StartsWith("FRAME ", StringComparison.Ordinal) || line.StartsWith("E ", StringComparison.Ordinal) ||
                line.StartsWith("T ", StringComparison.Ordinal))
            {
                string[] parts = line.Split(new[] { ',' }, 4);
                return parts.Length >= 3 ? parts[0] + "," + parts[1] + "," + parts[2] : line;
            }
            if (line.StartsWith("MAP ", StringComparison.Ordinal))
            {
                System.Text.RegularExpressions.Match match = System.Text.RegularExpressions.Regex.Match(line, "^MAP id=([^ ]+).*?size=([^ ]+).*?enemies=([^ ]+)");
                return match.Success ? "MAP id=" + match.Groups[1].Value + " size=" + match.Groups[2].Value + " enemies=" + match.Groups[3].Value : line;
            }
            return line.Length > 160 ? line.Substring(0, 160) : line;
        }

        private static bool IsDynamicStateLine(string line)
        {
            string[] prefixes =
            {
                "STATE ", "PAWN_STATUS ", "GUIDE_STATUS ", "GUIDE_STATE ", "GUIDE_FILE_ONCE ", "GUIDE_MILESTONE ",
                "PRESETS available=", "PRESET_ROOM ", "PRESET_COMPLETE ", "PRESET_FUTURE_ROOMS ", "PRESET_STEP ",
                "PRESET_ACTIVE_RULE ", "PRESET_OPEN_AIR ", "TOOL_PERMISSIONS ", "PRESET_FAILURES ", "AI_CAPTURED_PRISONERS ",
                "POLICY_INDEX ", "RESEARCH_CURRENT ", "PLAYER_WORK_REQUEST ", "PLAYER_REQUESTS ", "COLONISTS ", "BUILD_DEFS ", "PLANT_DEFS ",
                "RESEARCH_AVAILABLE ", "TRADE_MODE ", "WORLD_STATUS ", "WORLD_OBJECT ", "WORLD_QUEST ",
                "IDEOLOGY_STATUS ", "IDEOLOGY_RITUAL ", "C ", "MOOD_CAUSES ",
                "MAP ", "ROOM_SITES ", "ROOM_PURPOSES ", "RICE_FARM_TARGET ", "TERRAIN_SUMMARY ", "BUILD_BLOCKED ",
                 "RICE_BLOCKED ", "RICE_FERTILITY_TOP ", "PERIMETER ", "RAID_POINTS ", "BED_CAPACITY ", "RESOURCES ", "BUILD_MATERIALS ",
                "STARTING_GEAR ", "DEEP ", "BLD ", "TABLE ", "ZONE ", "TEMP_DUMP_ZONE ", "BP ", "BLUEPRINT_MATERIALS ",
                "FRAME ", "E ", "T "
            };
            if (line.StartsWith("COLONISTS owner,", StringComparison.Ordinal)) return false;
            return prefixes.Any(prefix => line.StartsWith(prefix, StringComparison.Ordinal));
        }

        private static string DynamicStateKey(string line)
        {
            if (line.StartsWith("STATE ", StringComparison.Ordinal)) return "STATE";
            string prefix = line.Split(new[] { ' ' }, 2)[0];
            if (line.StartsWith("C ", StringComparison.Ordinal) || line.StartsWith("MOOD_CAUSES ", StringComparison.Ordinal))
            {
                string[] parts = line.Substring(prefix.Length).Trim().Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
                return prefix + ":" + (line.StartsWith("MOOD_CAUSES ", StringComparison.Ordinal)
                    ? (parts.Length > 0 ? parts[0] : line)
                    : (parts.Length > 1 ? parts[1] : (parts.Length == 1 ? parts[0] : line)));
            }
            System.Text.RegularExpressions.Match id = System.Text.RegularExpressions.Regex.Match(line, "\\bid=([^ ,]+)");
            if (id.Success) return prefix + ":" + id.Groups[1].Value;
            string[] commaParts = line.Substring(prefix.Length).Trim().Split(new[] { ',' }, 2);
            return prefix + ":" + (commaParts.Length > 0 ? commaParts[0] : line);
        }

        private static string ReorderForCache(string prompt)
        {
            if (prompt.NullOrEmpty()) return prompt;
            List<string> stable = new List<string>();
            List<string> context = new List<string>();
            List<string> dynamic = new List<string>();
            foreach (string line in prompt.Replace("\r", string.Empty).Split('\n'))
            {
                if (line.NullOrEmpty()) continue;
                if (line.StartsWith("LOG_CONTEXT ", StringComparison.Ordinal) ||
                    line.StartsWith("LOG_SUMMARY ", StringComparison.Ordinal) ||
                    line.StartsWith("LOG_ENTRY ", StringComparison.Ordinal)) context.Add(line);
                else if (IsCacheStableLine(line)) stable.Add(line);
                else dynamic.Add(line);
            }
            if (stable.Count == 0) return prompt;
            StringBuilder result = new StringBuilder(prompt.Length + 64);
            result.AppendLine("CACHE_STABLE_PREFIX v3");
            foreach (string line in stable) result.AppendLine(line);
            foreach (string line in context) result.AppendLine(line);
            foreach (string line in dynamic) result.AppendLine(line);
            return result.ToString().TrimEnd();
        }

        private static bool IsCacheStableLine(string line)
        {
            string[] prefixes =
            {
                "DYNAMIC_RULE ", "PRESET_CATALOG "
            };
            return prefixes.Any(prefix => line.StartsWith(prefix, StringComparison.Ordinal));
        }

        private static int FindOrganStage(AICoopGameComponent component, int pawnId)
        {
            if (component == null) return 0;
            Pawn pawn = Find.Maps.SelectMany(map => map.mapPawns == null ? Enumerable.Empty<Pawn>() : map.mapPawns.AllPawnsSpawned)
                .FirstOrDefault(candidate => candidate != null && candidate.thingIDNumber == pawnId);
            return component.GetOrganSurgeryStage(pawn);
        }

        private static void AppendTerrainSummary(StringBuilder state, Map map)
        {
            if (map == null) return;
            ThingDef rice = DefDatabase<ThingDef>.GetNamedSilentFail("Plant_Rice");
            float riceMinFertility = rice == null || rice.plant == null ? 0f : rice.plant.fertilityMin;
            int buildable = 0;
            int buildBlocked = 0;
            int water = 0;
            int ricePlantable = 0;
            int riceBlocked = 0;
            float maxFertility = -1f;
            List<IntVec3> topFertilityCells = new List<IntVec3>();
            Dictionary<string, int> buildBlockedByTerrain = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> riceBlockedByTerrain = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<IntVec3>> buildSamples = new Dictionary<string, List<IntVec3>>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, List<IntVec3>> riceSamples = new Dictionary<string, List<IntVec3>>(StringComparer.OrdinalIgnoreCase);
            HashSet<IntVec3> occupiedCells = new HashSet<IntVec3>();
            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (thing == null || !thing.Spawned || thing.def == null ||
                    !(thing.def.IsEdifice() || thing.def.IsBlueprint || thing.def.IsFrame)) continue;
                foreach (IntVec3 occupiedCell in thing.OccupiedRect())
                {
                    if (occupiedCell.InBounds(map)) occupiedCells.Add(occupiedCell);
                }
            }

            foreach (IntVec3 cell in map.AllCells)
            {
                TerrainDef terrain = map.terrainGrid.TerrainAt(cell);
                string terrainName = terrain == null ? "unknown" : terrain.defName;
                bool isWater = terrain != null && (terrain.IsWater || terrain.IsOcean);
                if (isWater) water++;
                bool occupied = occupiedCells.Contains(cell);
                bool canBuild = !isWater && cell.Walkable(map) && !occupied;
                if (canBuild) buildable++;
                else
                {
                    buildBlocked++;
                    string reason = isWater ? "water" : (occupied ? "occupied" : (!cell.Walkable(map) ? "impassable" : "terrain"));
                    AddTerrainSample(buildBlockedByTerrain, buildSamples, reason + "|" + terrainName, cell);
                }

                float fertility = map.fertilityGrid == null ? (terrain == null ? 0f : terrain.fertility) : map.fertilityGrid.FertilityAt(cell);
                bool canPlantRice = rice != null && rice.plant != null && !isWater && cell.Walkable(map) && !occupied && fertility >= riceMinFertility;
                if (canPlantRice)
                {
                    ricePlantable++;
                    if (fertility > maxFertility)
                    {
                        maxFertility = fertility;
                        topFertilityCells.Clear();
                    }
                    if (fertility >= maxFertility && topFertilityCells.Count < 24) topFertilityCells.Add(cell);
                }
                else
                {
                    riceBlocked++;
                    string reason = isWater ? "water" : (occupied ? "occupied" : (fertility < riceMinFertility ? "fertility" : (!cell.Walkable(map) ? "impassable" : "terrain")));
                    AddTerrainSample(riceBlockedByTerrain, riceSamples, reason + "|" + terrainName, cell);
                }
            }

            state.AppendLine("TERRAIN_SUMMARY map=" + map.uniqueID + " buildable_cells=" + buildable + " build_blocked_cells=" + buildBlocked +
                " water_cells=" + water + " rice_plantable_cells=" + ricePlantable + " rice_blocked_cells=" + riceBlocked +
                " rice_min_fertility=" + riceMinFertility.ToString("0.##") + " max_rice_fertility=" + (maxFertility < 0f ? "-" : maxFertility.ToString("0.##")));
            AppendTerrainGroups(state, "BUILD_BLOCKED", buildBlockedByTerrain, buildSamples);
            AppendTerrainGroups(state, "RICE_BLOCKED", riceBlockedByTerrain, riceSamples);
            if (topFertilityCells.Count > 0)
            {
                int minX = topFertilityCells.Min(cell => cell.x);
                int maxX = topFertilityCells.Max(cell => cell.x);
                int minZ = topFertilityCells.Min(cell => cell.z);
                int maxZ = topFertilityCells.Max(cell => cell.z);
                state.AppendLine("RICE_FERTILITY_TOP map=" + map.uniqueID + " max=" + maxFertility.ToString("0.##") +
                    " bbox=" + minX + ":" + minZ + "-" + maxX + ":" + maxZ +
                    " cells=" + string.Join("|", topFertilityCells.Select(cell => cell.x + ":" + cell.z).ToArray()));
            }
            else state.AppendLine("RICE_FERTILITY_TOP map=" + map.uniqueID + " max=- cells=-");
        }

        private static void AddTerrainSample(Dictionary<string, int> counts, Dictionary<string, List<IntVec3>> samples, string key, IntVec3 cell)
        {
            int count;
            counts.TryGetValue(key, out count);
            counts[key] = count + 1;
            List<IntVec3> cells;
            if (!samples.TryGetValue(key, out cells))
            {
                cells = new List<IntVec3>();
                samples[key] = cells;
            }
            if (cells.Count < 6) cells.Add(cell);
        }

        private static void AppendTerrainGroups(StringBuilder state, string prefix, Dictionary<string, int> counts, Dictionary<string, List<IntVec3>> samples)
        {
            foreach (KeyValuePair<string, int> pair in counts.OrderByDescending(value => value.Value).Take(5))
            {
                List<IntVec3> cells = samples[pair.Key];
                string[] keyParts = pair.Key.Split(new[] { '|' }, 2);
                string reason = keyParts.Length > 1 ? keyParts[0] : "terrain";
                string terrain = keyParts.Length > 1 ? keyParts[1] : keyParts[0];
                string bbox = cells.Count == 0 ? "-" : cells.Min(cell => cell.x) + ":" + cells.Min(cell => cell.z) + "-" +
                    cells.Max(cell => cell.x) + ":" + cells.Max(cell => cell.z);
                state.AppendLine(prefix + " reason=" + reason + " terrain=" + terrain + " count=" + pair.Value + " sample_bbox=" + bbox + " samples=" +
                    string.Join("|", cells.Select(cell => cell.x + ":" + cell.z).ToArray()));
            }
        }

        private static void AppendPerimeterState(StringBuilder state, Map map)
        {
            if (map == null || map.listerThings == null) return;
            int wallCount = map.listerThings.AllThings.Count(IsWallOrDoorForState);
            int minX, minZ, maxX, maxZ;
            bool hasBounds = AICoopActionExecutor.TryGetColonyBounds(map, out minX, out minZ, out maxX, out maxZ);
            int width = hasBounds ? maxX - minX + 1 : 0;
            int height = hasBounds ? maxZ - minZ + 1 : 0;
            string status = !hasBounds ? "needed" :
                (width < 13 || height < 13 ? "blocked_need_13x13" : (wallCount > 0 ? "in_progress" : "needed"));
            state.AppendLine("PERIMETER map=" + map.uniqueID + " status=" + status +
                " colony_size=" + (hasBounds ? width + "x" + height : "-") +
                " wall_or_door_count=" + wallCount + " boundary=all_home_cells openings=2 layers=1 action=F " + map.uniqueID);
        }

        private static bool IsWallOrDoorForState(Thing thing)
        {
            if (thing == null || thing.def == null) return false;
            Frame frame = thing as Frame;
            BuildableDef entity = thing is Blueprint ? (thing as Blueprint).EntityToBuild() :
                (frame == null ? thing.def : frame.def.entityDefToBuild);
            return entity != null && (entity.defName == "Wall" || IsDoorEntityForState(entity));
        }

        private static bool IsDoorEntityForState(BuildableDef entity)
        {
            if (entity == null) return false;
            if (entity.defName == "Door") return true;
            ThingDef thingDef = entity as ThingDef;
            return thingDef != null && thingDef.thingClass != null && typeof(Building_Door).IsAssignableFrom(thingDef.thingClass);
        }

        internal static int EstimateMaterialAvailability(Map map, ThingDef stuff)
        {
            if (map == null || stuff == null) return 0;
            int tick = Find.TickManager == null ? -1 : Find.TickManager.TicksGame;
            MaterialAvailabilitySnapshot snapshot;
            if (!MaterialSnapshots.TryGetValue(map.uniqueID, out snapshot) || snapshot.Tick != tick)
            {
                snapshot = BuildMaterialAvailabilitySnapshot(map, tick);
                MaterialSnapshots[map.uniqueID] = snapshot;
            }
            int count;
            return snapshot.Amounts.TryGetValue(stuff, out count) ? count : 0;
        }

        internal static void ReportBlueprintMaterialStatus(Map map, Blueprint_Build blueprint)
        {
            if (map == null || blueprint == null || !blueprint.Spawned) return;
            BuildableDef entity = blueprint.EntityToBuild();
            if (entity == null || !entity.IsResearchFinished) return;

            Dictionary<ThingDef, int> totalNeeds = new Dictionary<ThingDef, int>();
            foreach (Blueprint_Build placed in map.listerThings.AllThings.OfType<Blueprint_Build>())
            {
                if (placed == null || !placed.Spawned) continue;
                BuildableDef placedEntity = placed.EntityToBuild();
                if (placedEntity == null || !placedEntity.IsResearchFinished) continue;
                foreach (ThingDefCountClass cost in placed.TotalMaterialCost() ?? new List<ThingDefCountClass>())
                {
                    if (cost == null || cost.thingDef == null) continue;
                    int remaining = placed.ThingCountNeeded(cost.thingDef);
                    if (remaining > 0) AddMaterialAmount(totalNeeds, cost.thingDef, remaining);
                }
            }

            List<string> shortages = new List<string>();
            foreach (KeyValuePair<ThingDef, int> need in totalNeeds)
            {
                int available = 0;
                if (map.resourceCounter != null) available = map.resourceCounter.GetCount(need.Key);
                if (available < need.Value)
                {
                    shortages.Add(need.Key.defName + ":blueprint_need=" + need.Value + ":available=" + available +
                        ":shortage=" + (need.Value - available));
                }
            }
            if (shortages.Count == 0) return;

            string buildName = blueprint.EntityToBuild() == null ? blueprint.def.defName : blueprint.EntityToBuild().defName;
            string detail = string.Join(",", shortages.ToArray());
            AICoopGameComponent component = AICoopGameComponent.Current;
            if (component == null) return;
            component.AddCommandResult("FAIL blueprint_material_shortage=1 blueprint=" + blueprint.thingIDNumber +
                " build=" + buildName + " map=" + map.uniqueID + " cell=" + blueprint.Position.x + ":" + blueprint.Position.z +
                " " + detail);
            component.AddLog("[错误] 蓝图 " + buildName + "（" + blueprint.thingIDNumber + "）材料不足：" + detail + "。蓝图已保留，需先收集材料。");
        }

        private static MaterialAvailabilitySnapshot BuildMaterialAvailabilitySnapshot(Map map, int tick)
        {
            MaterialAvailabilitySnapshot snapshot = new MaterialAvailabilitySnapshot { Tick = tick };
            if (map.resourceCounter != null)
            {
                foreach (KeyValuePair<ThingDef, int> pair in map.resourceCounter.AllCountedAmounts)
                {
                    if (pair.Key != null && pair.Value > 0) snapshot.Amounts[pair.Key] = pair.Value;
                }
            }
            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (thing == null || !thing.Spawned || thing.def == null) continue;
                if (thing.def.building != null && thing.def.building.mineableThing != null)
                {
                    AddMaterialAmount(snapshot.Amounts, thing.def.building.mineableThing, thing.def.building.EffectiveMineableYield);
                    continue;
                }
                Plant plant = thing as Plant;
                if (plant != null && plant.def.plant != null && plant.def.plant.harvestedThingDef != null)
                {
                    AddMaterialAmount(snapshot.Amounts, plant.def.plant.harvestedThingDef,
                        Math.Max(1, (int)Math.Round(plant.def.plant.harvestYield * plant.Growth)));
                }
            }
            return snapshot;
        }

        private static void AddMaterialAmount(Dictionary<ThingDef, int> amounts, ThingDef def, int amount)
        {
            if (amounts == null || def == null || amount <= 0) return;
            int current;
            amounts.TryGetValue(def, out current);
            amounts[def] = current + amount;
        }

        internal static bool IsStrategicMaterial(ThingDef stuff)
        {
            if (stuff == null) return false;
            switch (stuff.defName)
            {
                case "Silver":
                case "Gold":
                case "Jade":
                case "Uranium":
                case "Plasteel":
                    return true;
                default:
                    return false;
            }
        }

        private static void AppendMoodCauses(StringBuilder state, Pawn pawn)
        {
            if (pawn == null || pawn.needs == null || pawn.needs.mood == null || pawn.needs.mood.thoughts == null) return;
            List<Thought> thoughts = new List<Thought>();
            pawn.needs.mood.thoughts.GetDistinctMoodThoughtGroups(thoughts);
            List<string> causes = thoughts
                .Where(thought => thought != null && thought.MoodOffset() < 0f)
                .OrderBy(thought => thought.MoodOffset())
                .Take(8)
                .Select(thought => Clean(thought.LabelCap) + ":" + ((int)Math.Round(thought.MoodOffset())).ToString())
                .ToList();
            if (causes.Count > 0) state.AppendLine("MOOD_CAUSES " + pawn.thingIDNumber + " " + string.Join("|", causes.ToArray()));
        }

        private static void AppendStartingGear(StringBuilder state, Map map)
        {
            List<string> gear = map.listerThings.AllThings
                .Where(thing => thing != null && thing.Spawned && thing.def != null && thing.def.category == ThingCategory.Item &&
                    (thing.def.IsWeapon || thing is Apparel))
                .OrderBy(thing => thing.thingIDNumber)
                .Take(40)
                .Select(thing =>
                {
                    ThingWithComps withComps = thing as ThingWithComps;
                    CompForbiddable forbiddable = withComps == null ? null : withComps.GetComp<CompForbiddable>();
                    return thing.thingIDNumber + "," + thing.def.defName + "," + (thing.def.IsWeapon ? "weapon" : "apparel") +
                        ",count=" + thing.stackCount + ",forbidden=" + (forbiddable != null && forbiddable.Forbidden ? "1" : "0");
                }).ToList();
            state.AppendLine("STARTING_GEAR map=" + map.uniqueID + " " + (gear.Count == 0 ? "-" : string.Join("|", gear.ToArray())));
        }

        private static string NeedPercent(Need need)
        {
            return need == null ? "-" : Percent(need.CurLevelPercentage);
        }

        private static string Percent(float value)
        {
            return ((int)Math.Round(value * 100f)).ToString();
        }

        private static string Bool(bool value)
        {
            return value ? "1" : "0";
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Replace(",", " ");
        }

        private static string PawnBackground(Pawn pawn)
        {
            if (pawn == null || pawn.story == null) return "-";
            string childhood = pawn.story.Childhood == null ? "-" : pawn.story.Childhood.TitleFor(pawn.gender);
            string adulthood = pawn.story.Adulthood == null ? "-" : pawn.story.Adulthood.TitleFor(pawn.gender);
            return Clean(childhood) + "|" + Clean(adulthood);
        }

        private static string PawnTraits(Pawn pawn)
        {
            if (pawn == null || pawn.story == null || pawn.story.traits == null) return "-";
            string traits = string.Join("|", pawn.story.traits.allTraits.Select(trait => Clean(trait.Label)).ToArray());
            return traits.NullOrEmpty() ? "-" : traits;
        }

        private static string FindRoomSites(Map map, AICoopGameComponent component)
        {
            Pawn originPawn = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(pawn => component.IsAI(pawn));
            ThingDef wall = DefDatabase<ThingDef>.GetNamedSilentFail("Wall");
            ThingDef door = DefDatabase<ThingDef>.GetNamedSilentFail("Door");
            if (originPawn == null || wall == null || door == null) return "-";
            ThingDef stuff = AICoopActionExecutor.ChooseStoneStuff(wall, door, map);
            if (stuff == null || stuff.stuffProps == null || !stuff.stuffProps.CanMake(door)) return "-";

            IntVec3 origin = map.Center;
            List<string> sites = new List<string>();
            for (int radius = 0; radius <= 32 && sites.Count < 5; radius += 4)
            {
                for (int dx = -radius; dx <= radius && sites.Count < 5; dx += 4)
                {
                    for (int dz = -radius; dz <= radius && sites.Count < 5; dz += 4)
                    {
                        if (Math.Abs(dx) != radius && Math.Abs(dz) != radius) continue;
                        int minX = origin.x + dx - 6;
                        int maxX = minX + 12;
                        int minZ = origin.z + dz - 6;
                        int maxZ = minZ + 12;
                        IntVec3 doorCell = new IntVec3(minX + 6, 0, minZ);
                        bool valid = true;
                        for (int x = minX; x <= maxX && valid; x++)
                        {
                            valid = RoomCellValid(map, new IntVec3(x, 0, minZ), new IntVec3(x, 0, minZ) == doorCell ? door : wall, stuff) &&
                                RoomCellValid(map, new IntVec3(x, 0, maxZ), wall, stuff);
                        }
                        for (int z = minZ + 1; z < maxZ && valid; z++)
                        {
                            valid = RoomCellValid(map, new IntVec3(minX, 0, z), wall, stuff) &&
                                RoomCellValid(map, new IntVec3(maxX, 0, z), wall, stuff);
                        }
                        if (valid) sites.Add(minX + ":" + minZ + "-" + maxX + ":" + maxZ);
                    }
                }
            }
            return sites.Count == 0 ? "-" : string.Join(",", sites.ToArray());
        }

        private static bool RoomCellValid(Map map, IntVec3 cell, ThingDef buildDef, ThingDef stuff)
        {
            return cell.InBounds(map) && GenConstruct.CanPlaceBlueprintAt(buildDef, cell, Rot4.North, map, false, null, null, stuff).Accepted;
        }

        private static void AppendTargets(StringBuilder state, Map map, AICoopGameComponent component)
        {
            IntVec3 origin = map.Center;
            IEnumerable<Thing> targets = map.listerThings.AllThings.Where(IsUsefulTarget)
                .OrderBy(thing => Math.Abs(thing.Position.x - origin.x) + Math.Abs(thing.Position.z - origin.z))
                .Take(80);
            foreach (Thing thing in targets)
            {
                state.AppendLine("T " + thing.thingIDNumber + "," + thing.def.defName + "," + thing.Position.x + ":" + thing.Position.z + "," + TargetKind(thing));
            }
        }

        private static bool IsUsefulTarget(Thing thing)
        {
            if (thing == null || !thing.Spawned || !thing.def.HasThingIDNumber || thing.def.IsBlueprint || thing.def.IsFrame) return false;
            Pawn pawn = thing as Pawn;
            if (pawn != null) return !pawn.IsFreeColonist;
            if (thing is Plant || thing is Filth) return true;
            if (thing.def.building != null && thing.def.building.isNaturalRock) return true;
            if (thing.def.IsNonDeconstructibleAttackableBuilding) return true;
            return thing.def.category == ThingCategory.Item;
        }

        private static bool IsRelevantBuildingForState(Building building)
        {
            if (building == null || building.def == null) return false;
            return building.def.defName != "Wall";
        }

        private static string TargetKind(Thing thing)
        {
            Pawn pawn = thing as Pawn;
            if (pawn != null)
            {
                if (pawn.HostileTo(Faction.OfPlayer)) return "enemy" + (pawn.Downed ? ":downed" : string.Empty);
                if (pawn.RaceProps.Animal) return "animal" + (pawn.Downed ? ":downed" : string.Empty);
                return pawn.Downed ? "pawn:downed" : "pawn";
            }
            Plant plant = thing as Plant;
            if (plant != null) return "plant:growth=" + Percent(plant.Growth);
            if (thing is Filth) return "filth";
            if (thing.def.building != null && thing.def.building.isNaturalRock) return "mineable";
            if (thing.def.IsNonDeconstructibleAttackableBuilding) return "attackable_obstacle";
            return "item:count=" + thing.stackCount;
        }
    }

    public static class AICoopActionExecutor
    {
        internal const string TemporaryDumpingStockpileLabel = "AI临时垃圾储存区";

        [ThreadStatic]
        private static AICoopOwner actorOwner;
        private sealed class PendingPermission
        {
            public string Line;
            public string Key;
        }

        private static readonly Queue<PendingPermission> PermissionQueue = new Queue<PendingPermission>();
        private static bool permissionDialogOpen;

        public static AICoopOwner CurrentActorOwner
        {
            get { return actorOwner; }
        }

        public static void ClearPendingPermissions()
        {
            PermissionQueue.Clear();
            permissionDialogOpen = false;
        }

        public static void Execute(string response)
        {
            ExecuteInternal(response, false);
        }

        internal static bool IsExecuting { get; private set; }

        private static void ExecuteInternal(string response, bool bypassPermission)
        {
            bool previous = IsExecuting;
            IsExecuting = true;
            try { ExecuteLines(response, bypassPermission); }
            finally { IsExecuting = previous; }
        }

        private static void ExecuteLines(string response, bool bypassPermission)
        {
            if (response.NullOrEmpty()) return;
            AICoopGameComponent component = AICoopGameComponent.Current;
            if (component == null) return;
            string[] lines = response.Replace("\r", string.Empty).Split('\n');
            bool roomHandled = false;
            foreach (string rawLine in lines)
            {
                string line = rawLine.Trim();
                if (line.NullOrEmpty() || line.StartsWith("```", StringComparison.Ordinal)) continue;
                if (line.StartsWith("PLAN ", StringComparison.OrdinalIgnoreCase))
                {
                    string plan = line.Substring(5).Trim();
                    AICoopGameComponent.Current.SetPublicPlan(plan);
                    AICoopGameComponent.Current.AddWorkMessage("AI 计划", plan);
                    continue;
                }

                string[] parts = System.Text.RegularExpressions.Regex.Split(line, "\\s+");
                string command = parts[0].ToUpperInvariant();
                if (command == "GUIDE_DONE")
                {
                    if (parts.Length != 2)
                    {
                        AICoopGameComponent.Current.AddLog("[拒绝] 无效攻略完成确认：" + line);
                    }
                    else
                    {
                        AICoopGuideManager.CompleteCurrentTask(parts[1]);
                    }
                    continue;
                }
                if (command == "N")
                {
                    if (parts.Length == 1) AICoopGameComponent.Current.AddLog("[执行] AI 本轮不行动。");
                    else AICoopGameComponent.Current.AddLog("[拒绝] 无效命令：" + line);
                    continue;
                }
                if (command == "SAY")
                {
                    ExecuteAssistantSpeech(line, "SAY", "AI 队友");
                    continue;
                }
                if (command == "REQUEST_PLAYER")
                {
                    string request = line.Length <= command.Length ? string.Empty : line.Substring(command.Length).Trim();
                    if (request.NullOrEmpty()) component.AddCommandResult("FAIL command_not_executed reason=request_player_empty");
                    else
                    {
                        component.AddPlayerRequest(request);
                        component.AddCommandResult("OK request_player=1");
                    }
                    continue;
                }
                if (command == "NOTE_ADD")
                {
                    string note = line.Length <= command.Length ? string.Empty : line.Substring(command.Length).Trim();
                    if (note.NullOrEmpty()) component.AddCommandResult("FAIL command_not_executed reason=note_add_empty");
                    else component.AddAgentTaskNote(note);
                    continue;
                }
                if (command == "NOTE_READ")
                {
                    if (parts.Length != 1) component.AddCommandResult("FAIL command_not_executed reason=note_read_args");
                    else component.ReadAgentTaskNotes();
                    continue;
                }
                if (command == "NOTE_DONE")
                {
                    if (parts.Length != 2 || !component.CompleteAgentTaskNote(parts[1]))
                        component.AddCommandResult("FAIL command_not_executed reason=note_not_found id=" + (parts.Length > 1 ? parts[1] : "-"));
                    continue;
                }
                if (command != "M" && command != "D" && command != "A" &&
                    command != "MAP_SCAN" && command != "Q" && command != "B" && command != "F" && command != "F2" && command != "G" && command != "S" && command != "P" && command != "C" &&
                    command != "R" && command != "X" && command != "J" && command != "T" && command != "ORE" && command != "WORLD" && command != "PRESET" && command != "PRESET_DONE" && command != "PRESET_RETRY" && command != "NOTE_ADD" && command != "NOTE_READ" && command != "NOTE_DONE")
                {
                    AICoopGameComponent.Current.AddLog("[拒绝] 无法识别的输出：" + line);
                    continue;
                }

                if (!bypassPermission)
                {
                    string permissionKey = PermissionKey(command, parts);
                    AICoopPermissionMode permission = AICoopMod.Settings == null
                        ? AICoopPermissionMode.Allow
                        : AICoopMod.Settings.GetToolPermission(permissionKey);
                    if (permission == AICoopPermissionMode.Deny)
                    {
                        component.AddLog("[权限] 已禁止工具 " + permissionKey + "，命令未执行：" + line);
                        component.AddCommandResult("FAIL permission_denied=1 tool=" + permissionKey + " line=" + line);
                        continue;
                    }
                    if (permission == AICoopPermissionMode.Ask)
                    {
                        EnqueuePermission(line, permissionKey);
                        continue;
                    }
                }

                if (command == "MAP_SCAN")
                {
                    try { AICoopMapQuery.Execute(parts); }
                    catch (Exception ex) { component.AddCommandResult("FAIL MAP_SCAN " + ex.Message); }
                    continue;
                }
                if (command == "ORE")
                {
                    try
                    {
                        ExecuteOreDesignation(parts, line);
                    }
                    catch (Exception ex)
                    {
                        component.AddLog("[拒绝] 矿物开采指令执行失败 " + line + "：" + ex.Message);
                        component.AddCommandResult("FAIL ore_command_error=1 line=" + line);
                    }
                    continue;
                }
                if (command == "WORLD")
                {
                    try
                    {
                        AICoopWorldActions.Execute(parts, line);
                    }
                    catch (Exception ex)
                    {
                        component.AddLog("[拒绝] 世界地图命令执行失败 " + line + "：" + ex.Message);
                        component.AddCommandResult("FAIL world_command_error=1 line=" + line);
                    }
                    continue;
                }

                if (command == "PRESET")
                {
                    if (roomHandled)
                    {
                        component.AddLog("[拒绝] 每轮最多处理一个房间；请在下一轮按需选择下一个房间。");
                        continue;
                    }
                    roomHandled = true;
                    if (parts.Length != 2 && parts.Length != 5 && parts.Length != 6) component.AddLog("[拒绝] PRESET 格式：PRESET 预设文件名 [mapID x z [旋转0-3]]。");
                    else if (AICoopPresetManager.Activate(component, parts[1]))
                    {
                        TryBuildPresetRoom(component, parts, line);
                    }
                    continue;
                }
                if (command == "PRESET_DONE")
                {
                    roomHandled = true;
                    if (parts.Length != 2) component.AddLog("[拒绝] PRESET_DONE 格式：PRESET_DONE 当前预设房间编号。");
                    else AICoopPresetManager.CompleteRoom(component, parts[1]);
                    continue;
                }
                if (command == "PRESET_RETRY")
                {
                    if (parts.Length != 1) component.AddLog("[拒绝] PRESET_RETRY 不接受参数。");
                    else TryRetryPresetFailures(component);
                    continue;
                }

                if (command == "Q")
                {
                    if (roomHandled)
                    {
                        component.AddLog("[拒绝] 每轮最多处理一个房间；请在下一轮再建设其他房间。");
                        component.AddCommandResult("FAIL command_not_executed reason=one_room_per_response line=" + line);
                        continue;
                    }
                    roomHandled = true;
                }

                if (IsBatchCommand(command))
                {
                    try
                    {
                        ExecuteBatchCommand(command, parts, line);
                    }
                    catch (Exception ex)
                    {
                        component.AddLog("[拒绝] 批量命令执行失败 " + line + "：" + ex.Message);
                    }
                    continue;
                }

                if (parts.Length < 2)
                {
                    AICoopGameComponent.Current.AddLog("[拒绝] 命令缺少殖民者 ID 或名称：" + line);
                    continue;
                }
                bool ambiguousPawnName;
                Pawn pawn = FindPawn(parts[1], out ambiguousPawnName);
                if (ambiguousPawnName)
                {
                    AICoopGameComponent.Current.AddLog("[拒绝] 殖民者名称不唯一，请使用数字 ID：" + parts[1]);
                    continue;
                }
                if (pawn == null)
                {
                    AICoopGameComponent.Current.AddLog("[拒绝] 找不到殖民者：" + parts[1]);
                    continue;
                }
                if (!AICoopGameComponent.Current.CanAIControl(pawn))
                {
                    AICoopGameComponent.Current.AddLog("[拒绝] 只能命令 AI 所属殖民者：" + parts[1]);
                    continue;
                }
                actorOwner = AICoopOwner.AI;
                try
                {
                    if (command == "M") ExecuteMove(pawn, parts, line);
                    else if (command == "D") ExecuteDraft(pawn, parts, line);
                    else if (command == "A") ExecuteAttack(pawn, parts, line);
                    else if (command == "Q") ExecuteRoom(pawn, parts, line);
                    else if (command == "B") ExecuteBuild(pawn, parts, line);
                    else if (command == "G") ExecuteGrowingZone(pawn, parts, line);
                    else if (command == "S") ExecuteStockpile(pawn, parts, line);
                    else if (command == "P") ExecuteProduction(pawn, parts, line);
                    else if (command == "R") ExecuteResearch(pawn, parts, line);
                    else if (command == "X") ExecuteDesignation(pawn, parts, line);
                    else if (command == "J") ExecuteTargetJob(pawn, parts, line);
                }
                catch (Exception ex)
                {
                    AICoopGameComponent.Current.AddLog("[拒绝] 命令执行失败 " + line + "：" + ex.Message);
                }
                finally
                {
                    actorOwner = AICoopOwner.Player;
                }
            }
        }

        private static string PermissionKey(string command, string[] parts)
        {
            if (command == "C" && parts.Length > 2) return "C." + parts[2].ToLowerInvariant();
            if (command == "T" && parts.Length > 6) return "T." + parts[6].ToLowerInvariant();
            if ((command == "X" || command == "J") && parts.Length > 3) return command + "." + parts[3].ToLowerInvariant();
            if (command == "WORLD" && parts.Length > 1) return "WORLD." + parts[1].ToLowerInvariant();
            if (command == "F2") return "F";
            if (command == "ORE") return "ORE";
            return command;
        }

        private static void EnqueuePermission(string line, string key)
        {
            PermissionQueue.Enqueue(new PendingPermission { Line = line, Key = key });
            AICoopGameComponent component = AICoopGameComponent.Current;
            if (component != null) component.AddWorkMessage("AI", "请求玩家确认工具 " + key + "：" + line);
            ShowNextPermissionDialog();
        }

        private static void ShowNextPermissionDialog()
        {
            if (permissionDialogOpen || PermissionQueue.Count == 0 || Find.WindowStack == null) return;
            PendingPermission request = PermissionQueue.Dequeue();
            permissionDialogOpen = true;
            string message = "AI 请求执行工具：" + request.Key + "\n" + request.Line;
            Find.WindowStack.Add(new Dialog_MessageBox(
                message,
                "允许",
                delegate
                {
                    permissionDialogOpen = false;
                    ExecuteInternal(request.Line, true);
                    ShowNextPermissionDialog();
                },
                "拒绝",
                delegate
                {
                    permissionDialogOpen = false;
                    LogPermissionRefusal(request.Line, request.Key);
                    ShowNextPermissionDialog();
                },
                "AI 操作确认",
                false,
                null,
                null,
                WindowLayer.Dialog));
        }

        private static void LogPermissionRefusal(string line, string key)
        {
            AICoopGameComponent component = AICoopGameComponent.Current;
            if (component != null)
            {
                component.AddLog("[权限] 玩家拒绝工具 " + key + "，命令未执行：" + line);
                component.AddCommandResult("FAIL permission_denied=1 tool=" + key + " line=" + line);
            }
        }

        private static bool IsBatchCommand(string command)
        {
            return command == "M" || command == "A" || command == "Q" || command == "B" || command == "F" || command == "F2" || command == "G" || command == "C" || command == "ORE" ||
                command == "S" || command == "P" || command == "R" || command == "T";
        }

        private static void TryRetryPresetFailures(AICoopGameComponent component)
        {
            if (component == null || component.PresetFailures.Count == 0)
            {
                component?.AddCommandResult("OK preset_retry=0 reason=no_pending_failures");
                component?.AddWorkMessage("系统", "没有待重试的预设设施。");
                return;
            }
            int retried = 0;
            foreach (string record in component.PresetFailures.ToList())
            {
                string[] fields = record.Split('|');
                if (fields.Length < 5) continue;
                int mapId, minX, minZ, width = 0, height = 0, rotation = 0;
                if (!Int32.TryParse(fields[1], out mapId) || !Int32.TryParse(fields[2], out minX) || !Int32.TryParse(fields[3], out minZ)) continue;
                if (fields.Length >= 8)
                {
                    Int32.TryParse(fields[6], out width);
                    Int32.TryParse(fields[7], out height);
                }
                if (fields.Length >= 9) Int32.TryParse(fields[8], out rotation);
                Map map = Find.Maps.FirstOrDefault(candidate => candidate != null && candidate.uniqueID == mapId);
                Pawn actor = FindAIActor(map);
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(fields[4]);
                if (map == null || actor == null || def == null || !def.BuildableByPlayer || !def.IsResearchFinished) continue;
                if (map.listerThings.AllThings.OfType<Building>().Any(building => building.def == def &&
                    (width < 2 || (building.Position.x >= minX && building.Position.x < minX + width && building.Position.z >= minZ && building.Position.z < minZ + height))))
                {
                    component.RemovePresetFailure(record);
                    continue;
                }
                IntVec3 cell = width >= 2 && height >= 2 ? FindFacilityCell(map, def, minX, minZ, width, height) : new IntVec3(minX, 0, minZ);
                if (!cell.IsValid) continue;
                string[] build = { "B", actor.thingIDNumber.ToString(), def.defName, cell.x.ToString(), cell.z.ToString(), rotation.ToString() };
                int before = map.listerThings.AllThings.Count(thing => thing is Blueprint_Build && thing.Position == cell);
                ExecuteBuild(actor, build, "PRESET_RETRY " + def.defName);
                int after = map.listerThings.AllThings.Count(thing => thing is Blueprint_Build && thing.Position == cell);
                if (after > before)
                {
                    component.RemovePresetFailure(record);
                    retried++;
                }
            }
            component.AddCommandResult("OK preset_retry=" + retried + " remaining=" + component.PresetFailures.Count);
            component.AddWorkMessage("AI", "已重试 " + retried + " 个预设失败设施，剩余 " + component.PresetFailures.Count + " 个仍需处理。");
        }

        private static void TryBuildPresetRoom(AICoopGameComponent component, string[] parts, string line)
        {
            string presetName = component == null || component.ActivePresetName.NullOrEmpty() ? parts[1] : component.ActivePresetName;
            int presetRoomIndex = component == null ? 0 : component.ActivePresetRoomIndex;
            int width, height;
            if (!AICoopPresetManager.TryGetAutoBuildFootprint(presetName, presetRoomIndex, out width, out height))
            {
                component.AddCommandResult("FAIL preset_no_footprint=1 file=" + presetName);
                component.AddWorkMessage("系统", "预设文件缺少可解析的房间尺寸，无法自动放置房间外壳。");
                return;
            }

            Map map = parts.Length >= 5 ? FindMap(parts[2]) : Find.Maps.FirstOrDefault(candidate => FindAIActor(candidate) != null);
            if (map == null)
            {
                component.AddCommandResult("FAIL preset_build_no_ai_map=1");
                component.AddWorkMessage("系统", "预设已激活，但没有找到可用的 AI 殖民者地图，暂未放置开局小屋。");
                return;
            }
            Pawn actor = FindAIActor(map);
            ThingDef wall = DefDatabase<ThingDef>.GetNamedSilentFail("Wall");
            ThingDef door = DefDatabase<ThingDef>.GetNamedSilentFail("Door");
            ThingDef stuff = wall == null || door == null ? null : ChooseStoneStuff(wall, door, map);
            if (actor == null || wall == null || door == null || stuff == null)
            {
                component.AddCommandResult("FAIL preset_build_defs_unavailable=1");
                component.AddWorkMessage("系统", "预设已激活，但当前地图缺少可用的墙、门或建筑材料。");
                return;
            }

            if (AICoopPresetManager.HasStructuredLayout(presetName, presetRoomIndex))
            {
                BuildStructuredPresetRoom(component, actor, presetName, parts, line);
                return;
            }

            int minX = 0, minZ = 0;
            bool hasRequestedAnchor = parts.Length >= 5 && Int32.TryParse(parts[3], out minX) && Int32.TryParse(parts[4], out minZ);
            if (hasRequestedAnchor)
            {
                RemoveOverlappingRoomWalls(map, minX, minZ, minX + width - 1, minZ + height - 1);
                if (!CanPlacePresetSite(map, minX, minZ, width, height, wall, door, stuff))
                {
                    component.AddCommandResult("FAIL preset_build_invalid_site=1");
                    component.AddWorkMessage("系统", "指定的预设左下角坐标无法放置完整开局小屋。");
                    return;
                }
            }
            else if (!FindPresetSite(map, width, height, wall, door, stuff, actor.Position, out minX, out minZ))
            {
                component.AddCommandResult("FAIL preset_build_no_site=1");
                component.AddWorkMessage("系统", "预设已激活，但当前地图没有可以放置完整开局小屋的连续区域。");
                return;
            }

            string[] roomCommand = { "Q", actor.thingIDNumber.ToString(), minX.ToString(), minZ.ToString(),
                (minX + width - 1).ToString(), (minZ + height - 1).ToString(), stuff.defName };
            try
            {
                ExecuteRoom(actor, roomCommand, line);
            }
            catch (Exception ex)
            {
                string reason = CompactException(ex);
                component.AddLog("[拒绝] 预设房间外壳放置失败：" + reason);
                component.AddCommandResult("FAIL preset_room_error=1 reason=" + reason + " file=" + presetName);
                component.AddWorkMessage("系统", "开局小屋外壳放置时出现异常，蓝图可能未完整生成；请查看工作日志中的具体原因。" );
                return;
            }
            int placed;
            try
            {
                placed = BuildPresetFacilities(actor, presetName, presetRoomIndex, minX, minZ, width, height);
            }
            catch (Exception ex)
            {
                string reason = CompactException(ex);
                component.AddLog("[拒绝] 预设设施放置失败：" + reason);
                component.AddCommandResult("FAIL preset_facility_error=1 reason=" + reason + " file=" + presetName);
                component.AddWorkMessage("系统", "开局小屋外壳已处理，但部分设施蓝图放置时出现异常；请先完成外壳再重试缺失设施。" );
                return;
            }
            component.AddCommandResult("OK preset_blueprints=" + placed + " file=" + presetName);
            component.AddWorkMessage("AI", "已根据预设 " + presetName + " 自动放置开局小屋外壳和 " + placed + " 个可用设施蓝图；建成后再确认房间完成。");
        }

        private static void BuildStructuredPresetRoom(AICoopGameComponent component, Pawn actor, string presetName, string[] parts, string line)
        {
            Map map = actor == null ? null : actor.Map;
            int roomIndex = component == null ? 0 : component.ActivePresetRoomIndex;
            int rotation = 0;
            if (parts.Length == 6 && (!Int32.TryParse(parts[5], out rotation) || rotation < 0 || rotation > 3))
            {
                component.AddCommandResult("FAIL preset_rotation_invalid=1 file=" + presetName);
                component.AddWorkMessage("系统", "预设旋转参数必须是 0、1、2 或 3。" );
                return;
            }
            int width, height;
            AICoopPresetManager.GetRotatedRoomSize(presetName, roomIndex, rotation, out width, out height);
            if (map == null || width < 2 || height < 2)
            {
                component.AddCommandResult("FAIL preset_structured_dimensions=1 file=" + presetName);
                component.AddWorkMessage("系统", "预设布局缺少有效尺寸，未放置任何蓝图。");
                return;
            }
            int minX = 0, minZ = 0;
            bool requested = !IsDefenseGatePreset(presetName) && parts.Length >= 5 && Int32.TryParse(parts[3], out minX) && Int32.TryParse(parts[4], out minZ);
            if (requested)
            {
                RemoveOverlappingRoomWalls(map, minX, minZ, minX + width - 1, minZ + height - 1);
                string requestedReason;
                if (!CanPlaceStructuredSite(map, presetName, roomIndex, minX, minZ, rotation, out requestedReason))
                {
                    component.AddCommandResult("FAIL preset_build_invalid_site=1 file=" + presetName + " reason=" + requestedReason + " cell=" + minX + ":" + minZ);
                    component.AddWorkMessage("系统", "指定坐标无法放置该预设，原因：" + requestedReason + "（只有不可放置地形、矿石岩体或越界会阻止整间房）。");
                    return;
                }
            }
            else if (IsDefenseGatePreset(presetName) && !FindDefenseGateSite(map, presetName, roomIndex, width, height, rotation, out minX, out minZ))
            {
                component.AddCommandResult("FAIL preset_build_no_site=1 file=" + presetName);
                    component.AddWorkMessage("系统", "大门防御预设无法与左右外围开口之一对齐，已保留现有殖民地布局。请先确认外围墙至少有一侧能容纳预设长度。" );
                return;
            }
            else if (!IsDefenseGatePreset(presetName) && !FindStructuredSite(map, presetName, roomIndex, width, height, rotation, actor.Position, out minX, out minZ))
            {
                component.AddCommandResult("FAIL preset_build_no_site=1 file=" + presetName);
                component.AddWorkMessage("系统", "当前地图没有避开不可放置地形和矿石岩体后仍可用的预设区域，已保留现有殖民地布局。");
                return;
            }

            RemoveOverlappingRoomWalls(map, minX, minZ, minX + width - 1, minZ + height - 1);
            component.SetPresetPlacement(map, presetName, minX, minZ, rotation);

            int placed = 0;
            ThingDef wallDef = DefDatabase<ThingDef>.GetNamedSilentFail("Wall");
            ThingDef doorDef = DefDatabase<ThingDef>.GetNamedSilentFail("Door");
            ThingDef roomStone = wallDef == null || doorDef == null ? null : ChooseStoneStuff(wallDef, doorDef, map);
            foreach (AICoopPresetBuildInstruction instruction in AICoopPresetManager.GetBuildInstructions(presetName, roomIndex))
            {
                IntVec3 cell = AICoopPresetManager.TransformRoomCell(presetName, roomIndex, instruction.X, instruction.Z, minX, minZ, rotation);
                if (!cell.IsValid) continue;
                Rot4 buildRotation = new Rot4((instruction.Rotation + rotation) % 4);
                if (instruction.IsFloor)
                {
                    TerrainDef terrain = AICoopPresetManager.ResolveTerrain(instruction.FloorDef, instruction.FloorHash);
                    if (terrain == null || !cell.InBounds(map) ||
                        (map.terrainGrid.TerrainAt(cell) != null && (map.terrainGrid.TerrainAt(cell).IsWater || map.terrainGrid.TerrainAt(cell).IsOcean)) ||
                        cell.GetThingList(map).Any(IsPresetBlockingThing) || map.terrainGrid.TerrainAt(cell) == terrain) continue;
                    if (map.terrainGrid.TerrainAt(cell).defName == terrain.defName) continue;
                    if (map.listerThings.AllThings.OfType<Blueprint_Build>().Any(existing => existing.Position == cell && existing.EntityToBuild() == terrain)) continue;
                    ThingDef floorStuff = terrain.MadeFromStuff ? ChooseStuff(terrain, map, null) : null;
                    if (terrain.MadeFromStuff && floorStuff == null)
                    {
                        component.AddCommandResult("FAIL preset_floor_pending=1 file=" + presetName + " cell=" + cell.x + ":" + cell.z + " reason=floor_material_unavailable");
                        continue;
                    }
                    AcceptanceReport floorReport = GenConstruct.CanPlaceBlueprintAt(terrain, cell, Rot4.North, map, false, null, null, floorStuff);
                    if (!floorReport.Accepted)
                    {
                        component.AddCommandResult("FAIL preset_floor_pending=1 file=" + presetName + " cell=" + cell.x + ":" + cell.z + " reason=floor_placement_rejected");
                        continue;
                    }
                    Blueprint_Build floorBlueprint = GenConstruct.PlaceBlueprintForBuild(terrain, cell, map, Rot4.North, Faction.OfPlayer, floorStuff);
                    if (floorBlueprint != null)
                    {
                        AICoopStateSerializer.ReportBlueprintMaterialStatus(map, floorBlueprint);
                        placed++;
                    }
                    else
                    {
                        component.AddCommandResult("FAIL preset_floor_pending=1 file=" + presetName + " cell=" + cell.x + ":" + cell.z + " reason=floor_blueprint_rejected");
                    }
                    continue;
                }
                ThingDef buildDef = DefDatabase<ThingDef>.GetNamedSilentFail(instruction.DefName);
                if (buildDef == null || !buildDef.BuildableByPlayer || buildDef.building == null || !buildDef.IsResearchFinished)
                {
                    component.RecordPresetFailure(presetName, map, cell, instruction.DefName,
                        buildDef == null ? "def_missing" : (!buildDef.IsResearchFinished ? "research_or_unlock_missing" : "not_buildable"), 1, 1, buildRotation.AsInt);
                    continue;
                }
                if (HasPresetBuildAt(map, cell, buildDef)) continue;
                bool isRoomWall = buildDef.defName.Equals("Wall", StringComparison.OrdinalIgnoreCase) ||
                    buildDef.defName.Equals("Door", StringComparison.OrdinalIgnoreCase);
                ThingDef material = isRoomWall ? roomStone : (buildDef.MadeFromStuff ? DefDatabase<ThingDef>.GetNamedSilentFail(instruction.Stuff) : null);
                if (buildDef.MadeFromStuff && (material == null || material.stuffProps == null || !material.stuffProps.CanMake(buildDef)))
                    material = isRoomWall ? roomStone : ChooseStuff(buildDef, map, null);
                if (buildDef.MadeFromStuff && material == null)
                {
                    component.RecordPresetFailure(presetName, map, cell, buildDef.defName, "material_unavailable", 1, 1, buildRotation.AsInt);
                    continue;
                }
                if (!EnsureConstructionAreaClear(map, GenAdj.OccupiedRect(cell, buildRotation, buildDef.Size), line, true))
                {
                    component.RecordPresetFailure(presetName, map, cell, buildDef.defName, "area_not_clear", 1, 1, buildRotation.AsInt);
                    continue;
                }
                if (!CanPlacePresetBlueprintAt(map, cell, buildDef, buildRotation, material))
                {
                    component.RecordPresetFailure(presetName, map, cell, buildDef.defName, "placement_or_material_rejected", 1, 1, buildRotation.AsInt);
                    continue;
                }
                Blueprint_Build blueprint = GenConstruct.PlaceBlueprintForBuild(buildDef, cell, map, buildRotation, Faction.OfPlayer, material);
                if (blueprint == null)
                {
                    component.RecordPresetFailure(presetName, map, cell, buildDef.defName, "placement_returned_null", 1, 1, buildRotation.AsInt);
                    continue;
                }
                AICoopStateSerializer.ReportBlueprintMaterialStatus(map, blueprint);
                component.MarkAIPlannedConstruction(map, blueprint.Position, buildDef);
                placed++;
            }
            int zoneCells = AICoopPresetManager.ApplyZoneInstructions(map, presetName, roomIndex, minX, minZ, rotation);
            foreach (AICoopPresetStateInstruction state in AICoopPresetManager.GetStateInstructions(presetName, roomIndex))
            {
                IntVec3 stateCell = AICoopPresetManager.TransformRoomCell(presetName, roomIndex, state.X, state.Z, minX, minZ, rotation);
                if (stateCell.IsValid) AICoopPresetManager.RegisterPendingState(map, stateCell, state);
            }
            string purpose = AICoopPresetManager.ActiveRoomPurpose(component);
            component.TryRegisterRoomPurpose(map, minX, minZ, minX + width - 1, minZ + height - 1, purpose);
            component.AddCommandResult("OK preset_structured_blueprints=" + placed + " zones=" + zoneCells + " file=" + presetName);
            component.AddWorkMessage("AI", "已按存档相对坐标放置预设 " + presetName + " 的墙、门、设施和区域蓝图；床位/门状态将在建筑完成后恢复。已放置 " + placed + " 项。");
        }

        private static bool HasPresetBuildAt(Map map, IntVec3 cell, ThingDef buildDef)
        {
            if (map == null || buildDef == null || !cell.IsValid) return false;
            foreach (Thing thing in cell.GetThingList(map))
            {
                Blueprint blueprint = thing as Blueprint;
                if (blueprint != null && blueprint.EntityToBuild() == buildDef) return true;
                if (thing != null && thing.def == buildDef) return true;
            }
            return false;
        }

        private static bool FindStructuredSite(Map map, string presetName, int roomIndex, int width, int height, int rotation, IntVec3 origin, out int minX, out int minZ)
        {
            minX = minZ = 0;
            Dictionary<string, int> failures = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (IntVec3 anchor in SiteAnchorsFromCenter(map, width, height, 1))
            {
                string reason;
                if (!CanPlaceStructuredSite(map, presetName, roomIndex, anchor.x, anchor.z, rotation, out reason))
                {
                    int count;
                    failures.TryGetValue(reason, out count);
                    failures[reason] = count + 1;
                    continue;
                }
                minX = anchor.x;
                minZ = anchor.z;
                return true;
            }
            if (failures.Count > 0 && AICoopGameComponent.Current != null)
            {
                string diagnostic = string.Join(",", failures.OrderByDescending(pair => pair.Value).Take(5)
                    .Select(pair => pair.Key + "=" + pair.Value).ToArray());
                AICoopGameComponent.Current.AddCommandResult("FAIL preset_site_diagnostics file=" + presetName + " reasons=" + diagnostic);
                AICoopGameComponent.Current.AddWorkMessage("系统", "预设选址失败原因统计：" + diagnostic + "。" );
            }
            return false;
        }

        private static bool IsDefenseGatePreset(string presetName)
        {
            string file = Path.GetFileNameWithoutExtension(presetName ?? string.Empty);
            if (file.StartsWith("room_", StringComparison.OrdinalIgnoreCase)) file = file.Substring(5);
            return file.Equals("defense_gate_battery", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsDefenseBatteryPreset(string presetName)
        {
            string file = Path.GetFileNameWithoutExtension(presetName ?? string.Empty);
            if (file.StartsWith("room_", StringComparison.OrdinalIgnoreCase)) file = file.Substring(5);
            return file.Equals("defense_active_turrets", StringComparison.OrdinalIgnoreCase);
        }

        private static bool FindDefenseGateSite(Map map, string presetName, int roomIndex, int width, int height, int rotation,
            out int minX, out int minZ)
        {
            minX = minZ = 0;
            int colonyMinX, colonyMinZ, colonyMaxX, colonyMaxZ;
            if (!TryGetHomeBounds(map, out colonyMinX, out colonyMinZ, out colonyMaxX, out colonyMaxZ)) return false;
            const int margin = 1;
            ThingDef wall = DefDatabase<ThingDef>.GetNamedSilentFail("Wall");
            ThingDef stuff = wall == null ? null : ChooseStoneStuff(wall, null, map);
            if (wall == null || stuff == null) return false;
            int requiredLength = Math.Max(width, height);
            int sideLength = Math.Max(colonyMaxX - colonyMinX + 1, colonyMaxZ - colonyMinZ + 1) + margin * 2;
            if (sideLength < requiredLength)
            {
                // The captured gate battery is 30 cells long. Extend one available
                // perimeter side before aligning its center wall with an opening.
                ExtendPerimeterSideForGate(map, colonyMinX, colonyMinZ, colonyMaxX, colonyMaxZ, margin, requiredLength, wall, stuff);
            }
            int relativeX, relativeZ;
            if (!AICoopPresetManager.TryGetRelativeBuildCell(presetName, roomIndex, "Door", out relativeX, out relativeZ)) return false;
            IntVec3[] openings =
            {
                new IntVec3(colonyMinX - margin, 0, (colonyMinZ + colonyMaxZ) / 2),
                new IntVec3(colonyMaxX + margin, 0, (colonyMinZ + colonyMaxZ) / 2)
            };
            IntVec3[] directions =
            {
                new IntVec3(-1, 0, 0), new IntVec3(1, 0, 0)
            };
            for (int side = 0; side < openings.Length; side++)
            {
                for (int candidateRotation = 0; candidateRotation < 4; candidateRotation++)
                {
                    IntVec3 transformedDoor = AICoopPresetManager.TransformRoomCell(presetName, roomIndex, relativeX, relativeZ, 0, 0, candidateRotation);
                    for (int offset = 1; offset <= 3; offset++)
                    {
                        IntVec3 origin = openings[side] + directions[side] * offset - transformedDoor;
                        string reason;
                        if (!CanPlaceStructuredSite(map, presetName, roomIndex, origin.x, origin.z, candidateRotation, out reason)) continue;
                        minX = origin.x;
                        minZ = origin.z;
                        return true;
                    }
                }
            }
            return false;
        }

        private static void ExtendPerimeterSideForGate(Map map, int minX, int minZ, int maxX, int maxZ, int margin,
            int requiredLength, ThingDef wall, ThingDef stuff)
        {
            if (map == null || wall == null || requiredLength < 1) return;
            bool horizontal = maxX - minX >= maxZ - minZ;
            int center = horizontal ? (minX + maxX) / 2 : (minZ + maxZ) / 2;
            int half = requiredLength / 2;
            int low = center - half;
            int high = low + requiredLength - 1;
            if (horizontal)
            {
                int z = minZ - margin;
                for (int x = low; x <= high; x++) if (x >= 0 && x < map.Size.x && z >= 0 && z < map.Size.z && !HasExistingWallOrDoor(map, new IntVec3(x, 0, z)))
                    PlacePerimeterExtension(map, new IntVec3(x, 0, z), wall, stuff);
            }
            else
            {
                int x = minX - margin;
                for (int z = low; z <= high; z++) if (z >= 0 && z < map.Size.z && x >= 0 && x < map.Size.x && !HasExistingWallOrDoor(map, new IntVec3(x, 0, z)))
                    PlacePerimeterExtension(map, new IntVec3(x, 0, z), wall, stuff);
            }
        }

        private static void PlacePerimeterExtension(Map map, IntVec3 cell, ThingDef wall, ThingDef stuff)
        {
            if (map == null || wall == null || !cell.InBounds(map) || HasExistingWallOrDoor(map, cell)) return;
            if (!GenConstruct.CanPlaceBlueprintAt(wall, cell, Rot4.North, map, false, null, null, stuff).Accepted) return;
            Blueprint_Build blueprint = GenConstruct.PlaceBlueprintForBuild(wall, cell, map, Rot4.North, Faction.OfPlayer, stuff);
            if (blueprint != null) AICoopStateSerializer.ReportBlueprintMaterialStatus(map, blueprint);
        }

        private static IEnumerable<IntVec3> SiteAnchorsFromCenter(Map map, int width, int height, int margin)
        {
            if (map == null || width < 1 || height < 1) yield break;
            int maxX = map.Size.x - width - margin;
            int maxZ = map.Size.z - height - margin;
            if (maxX < margin || maxZ < margin) yield break;
            int centerX = map.Center.x - width / 2;
            int centerZ = map.Center.z - height / 2;
            int maxRadius = map.Size.x + map.Size.z;
            for (int radius = 0; radius <= maxRadius; radius++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int dz = radius - Math.Abs(dx);
                    int x = centerX + dx;
                    int z = centerZ + dz;
                    if (x >= margin && x <= maxX && z >= margin && z <= maxZ) yield return new IntVec3(x, 0, z);
                    if (dz > 0)
                    {
                        z = centerZ - dz;
                        if (x >= margin && x <= maxX && z >= margin && z <= maxZ) yield return new IntVec3(x, 0, z);
                    }
                }
            }
        }

        private static bool CanPlaceStructuredSite(Map map, string presetName, int roomIndex, int minX, int minZ, int rotation = 0)
        {
            string ignoredReason;
            return CanPlaceStructuredSite(map, presetName, roomIndex, minX, minZ, rotation, out ignoredReason);
        }

        private static bool CanPlaceStructuredSite(Map map, string presetName, int roomIndex, int minX, int minZ,
            int rotation, out string failureReason)
        {
            failureReason = "unknown";
            int width, height;
            AICoopPresetManager.GetRotatedRoomSize(presetName, roomIndex, rotation, out width, out height);
            AICoopGameComponent component = AICoopGameComponent.Current;
            if (map == null || component == null)
            {
                failureReason = "map_or_component_unavailable";
                return false;
            }
            if (!component.CanPlaceRoomWithCorridor(map, minX, minZ, minX + width - 1, minZ + height - 1))
            {
                failureReason = "corridor_or_room_overlap";
                return false;
            }
            if (IsDefenseBatteryPreset(presetName) && !HasDefenseBatterySpacing(map, presetName, roomIndex, minX, minZ, rotation))
            {
                failureReason = "defense_battery_needs_2_cell_clearance";
                return false;
            }
            foreach (AICoopPresetBuildInstruction instruction in AICoopPresetManager.GetBuildInstructions(presetName, roomIndex))
            {
                if (instruction.IsFloor) continue;
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(instruction.DefName);
                if (def == null || !def.BuildableByPlayer || !def.IsResearchFinished) continue;
                IntVec3 cell = AICoopPresetManager.TransformRoomCell(presetName, roomIndex, instruction.X, instruction.Z, minX, minZ, rotation);
                ThingDef stuff = def.MadeFromStuff
                    ? ((def.defName.Equals("Wall", StringComparison.OrdinalIgnoreCase) || def.defName.Equals("Door", StringComparison.OrdinalIgnoreCase))
                        ? ChooseStoneStuff(def, null, map) : ChooseStuff(def, map, null))
                    : null;
                string placementReason = PresetPlacementFailureReason(map, cell, def,
                    new Rot4((instruction.Rotation + rotation) % 4), stuff);
                // Site selection only rejects hard blockers. Trees, chunks,
                // ordinary rock and existing buildings are handled during
                // the per-facility placement pass.
                if (placementReason == "ore_block" || placementReason == "unplaceable_terrain" ||
                    placementReason == "out_of_bounds" || placementReason == "invalid")
                {
                    failureReason = placementReason;
                    return false;
                }
            }
            return true;
        }

        private static bool HasDefenseBatterySpacing(Map map, string presetName, int roomIndex, int minX, int minZ, int rotation)
        {
            if (map == null) return false;
            int width, height;
            AICoopPresetManager.GetRotatedRoomSize(presetName, roomIndex, rotation, out width, out height);
            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (thing == null || !(thing is Building) || thing.def == null ||
                    (thing.def.building != null && thing.def.building.isNaturalRock)) continue;
                for (int x = 0; x < width; x++) for (int z = 0; z < height; z++)
                {
                    IntVec3 cell = new IntVec3(minX + x, 0, minZ + z);
                    int dx = Math.Abs(thing.Position.x - cell.x);
                    int dz = Math.Abs(thing.Position.z - cell.z);
                    if (Math.Max(dx, dz) < 3) return false;
                }
            }
            return true;
        }

        private static bool FindPresetSite(Map map, int width, int height, ThingDef wall, ThingDef door, ThingDef stuff,
            IntVec3 origin, out int minX, out int minZ)
        {
            minX = minZ = 0;
            if (map == null || width < 2 || height < 2 || width > map.Size.x || height > map.Size.z) return false;
            foreach (IntVec3 anchor in SiteAnchorsFromCenter(map, width, height, 0))
            {
                if (!CanPlacePresetSite(map, anchor.x, anchor.z, width, height, wall, door, stuff)) continue;
                minX = anchor.x;
                minZ = anchor.z;
                return true;
            }
            return false;
        }

        private static bool CanPlacePresetSite(Map map, int minX, int minZ, int width, int height,
            ThingDef wall, ThingDef door, ThingDef stuff)
        {
            int maxX = minX + width - 1;
            int maxZ = minZ + height - 1;
            IntVec3 doorCell = new IntVec3(minX + width / 2, 0, minZ);
            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    IntVec3 cell = new IntVec3(x, 0, z);
                    if (!cell.InBounds(map) || cell.GetThingList(map).Any(IsPresetBlockingThing)) return false;
                }
            }
            for (int x = minX; x <= maxX; x++)
            {
                if (!CanPlacePresetCell(map, new IntVec3(x, 0, minZ), x == doorCell.x ? door : wall, stuff) ||
                    !CanPlacePresetCell(map, new IntVec3(x, 0, maxZ), wall, stuff)) return false;
            }
            for (int z = minZ + 1; z < maxZ; z++)
            {
                if (!CanPlacePresetCell(map, new IntVec3(minX, 0, z), wall, stuff) ||
                    !CanPlacePresetCell(map, new IntVec3(maxX, 0, z), wall, stuff)) return false;
            }
            return true;
        }

        private static bool CanPlacePresetCell(Map map, IntVec3 cell, ThingDef buildDef, ThingDef stuff)
        {
            if (!cell.InBounds(map)) return false;
            foreach (Thing thing in cell.GetThingList(map))
            {
                if (IsPresetBlockingThing(thing)) return false;
            }
            return CanPlacePresetBlueprintAt(map, cell, buildDef, Rot4.North, stuff);
        }

        private static bool CanPlacePresetBlueprintAt(Map map, IntVec3 cell, ThingDef buildDef, Rot4 rotation, ThingDef stuff)
        {
            return PresetPlacementFailureReason(map, cell, buildDef, rotation, stuff).NullOrEmpty();
        }

        private static string PresetPlacementFailureReason(Map map, IntVec3 cell, ThingDef buildDef, Rot4 rotation, ThingDef stuff)
        {
            if (map == null || buildDef == null || !cell.InBounds(map)) return "invalid";
            CellRect occupiedRect = GenAdj.OccupiedRect(cell, rotation, buildDef.Size);
            bool removableObstacle = false;
            foreach (IntVec3 occupiedCell in occupiedRect.Cells)
            {
                if (!occupiedCell.InBounds(map)) return "out_of_bounds";
                if (occupiedCell.GetThingList(map).Any(IsPresetBlockingThing)) return "ore_block";
                TerrainDef terrain = map.terrainGrid.TerrainAt(occupiedCell);
                if (terrain != null && (terrain.IsWater || terrain.IsOcean)) return "unplaceable_terrain";
                removableObstacle |= occupiedCell.GetThingList(map).Any(IsPresetRemovableObstacle);
            }
            AcceptanceReport report = GenConstruct.CanPlaceBlueprintAt(buildDef, cell, rotation, map, false, null, null, stuff);
            if (report.Accepted || removableObstacle) return string.Empty;
            return "vanilla_rejected_" + (report.Reason == null ? "unknown" : report.Reason.ToString()).Replace(' ', '_').Replace('|', '_');
        }

        private static bool IsPresetBlockingThing(Thing thing)
        {
            if (thing == null || thing.Destroyed || thing is Blueprint || thing is Frame) return false;
            return IsPresetOreRock(thing);
        }

        private static bool IsPresetOreRock(Thing thing)
        {
            if (thing == null || thing.def == null || thing.def.building == null || !thing.def.building.isNaturalRock) return false;
            ThingDef minedThing = thing.def.building.mineableThing;
            if (minedThing == null) return false;
            if (minedThing.defName.StartsWith("Chunk", StringComparison.OrdinalIgnoreCase)) return false;
            return minedThing.thingCategories == null || !minedThing.thingCategories.Any(category =>
                category != null && (category.defName == "StoneChunks" || category.defName == "Chunks"));
        }

        private static bool IsPresetRemovableObstacle(Thing thing)
        {
            if (thing == null || thing.Destroyed || thing is Blueprint || thing is Frame) return false;
            Plant plant = thing as Plant;
            if (plant != null && plant.def.plant != null && plant.def.plant.IsTree) return true;
            if (thing.def != null && thing.def.building != null && thing.def.building.isNaturalRock) return !IsPresetOreRock(thing);
            return IsStoneChunk(thing);
        }

        private static int BuildPresetFacilities(Pawn actor, string presetName, int roomIndex, int minX, int minZ, int width, int height)
        {
            List<string> facilities = new List<string>();
            string fileName = Path.GetFileNameWithoutExtension(presetName ?? string.Empty);
            bool startingCore = fileName.IndexOf("starting_core", StringComparison.OrdinalIgnoreCase) >= 0;
            if (startingCore)
            {
                AddFacilityCopies(facilities, "Bed", 4);
                AddFacilityCopies(facilities, "EndTable", 4);
                AddFacilityCopies(facilities, "StandingLamp", 4);
                AddFacilityCopies(facilities, "NutrientPasteDispenser", 1);
                AddFacilityCopies(facilities, "ElectricStove", 1);
                AddFacilityCopies(facilities, "Shelf", 1);
                AddFacilityCopies(facilities, "Table2x2c", 1);
                AddFacilityCopies(facilities, "DiningChair", 2);
                AddFacilityCopies(facilities, "SimpleResearchBench", 1);
            }
            else facilities.AddRange(AICoopPresetManager.GetAutoBuildFacilities(presetName, roomIndex));

            int placed = 0;
            int facilityIndex = 0;
            foreach (string defName in facilities)
            {
                ThingDef buildDef = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
                if (buildDef == null || !buildDef.BuildableByPlayer || buildDef.building == null || !buildDef.IsResearchFinished)
                {
                    IntVec3 pendingCell = new IntVec3(minX + 1 + (facilityIndex % Math.Max(1, width - 2)), 0, minZ + 1 + ((facilityIndex / Math.Max(1, width - 2)) % Math.Max(1, height - 2)));
                    AICoopGameComponent.Current?.RecordPresetFailure(presetName, actor.Map, pendingCell, defName,
                        buildDef == null ? "def_missing" : (!buildDef.IsResearchFinished ? "research_or_unlock_missing" : "not_buildable"), width, height);
                    facilityIndex++;
                    continue;
                }
                IntVec3 cell = FindFacilityCell(actor.Map, buildDef, minX, minZ, width, height);
                if (!cell.IsValid)
                {
                    IntVec3 pendingCell = new IntVec3(minX + 1 + (facilityIndex % Math.Max(1, width - 2)), 0, minZ + 1 + ((facilityIndex / Math.Max(1, width - 2)) % Math.Max(1, height - 2)));
                    AICoopGameComponent.Current?.RecordPresetFailure(presetName, actor.Map, pendingCell, defName, "no_valid_placement", width, height);
                    facilityIndex++;
                    continue;
                }
                string[] command = { "B", actor.thingIDNumber.ToString(), buildDef.defName, cell.x.ToString(), cell.z.ToString() };
                int blueprintsBefore = actor.Map.listerThings.AllThings.Count(thing => thing is Blueprint_Build);
                ExecuteBuild(actor, command, "PRESET " + presetName + " " + buildDef.defName);
                if (actor.Map.listerThings.AllThings.Count(thing => thing is Blueprint_Build) > blueprintsBefore) placed++;
                else AICoopGameComponent.Current?.RecordPresetFailure(presetName, actor.Map, cell, buildDef.defName, "placement_or_material_rejected", width, height);
                facilityIndex++;
            }
            return placed;
        }

        private static void AddFacilityCopies(List<string> facilities, string defName, int count)
        {
            for (int i = 0; i < count; i++) facilities.Add(defName);
        }

        private static IntVec3 FindFacilityCell(Map map, ThingDef buildDef, int minX, int minZ, int width, int height)
        {
            int maxX = minX + width - 1;
            int maxZ = minZ + height - 1;
            for (int x = minX + 1; x < maxX; x++)
            {
                for (int z = minZ + 1; z < maxZ; z++)
                {
                    IntVec3 cell = new IntVec3(x, 0, z);
                    if (!GenAdj.OccupiedRect(cell, Rot4.North, buildDef.Size).Cells.All(candidate =>
                        candidate.InBounds(map) && candidate.x > minX && candidate.x < maxX && candidate.z > minZ && candidate.z < maxZ)) continue;
                    if (GenConstruct.CanPlaceBlueprintAt(buildDef, cell, Rot4.North, map, false, null, null, buildDef.MadeFromStuff ? ChooseStuff(buildDef, map, null) : null).Accepted)
                        return cell;
                }
            }
            return IntVec3.Invalid;
        }

        private static void ExecuteBatchCommand(string command, string[] parts, string line)
        {
            Map map = FindMap(parts[1]);
            if (map == null)
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 找不到地图：" + parts[1]);
                return;
            }
            Pawn actor = FindAIActor(map);
            if (actor == null && command != "T")
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 该地图没有可用的 AI 殖民者：" + parts[1]);
                return;
            }

            switch (command)
            {
                case "M":
                    ExecuteBatchMove(map, parts, line);
                    break;
                case "A":
                    ExecuteBatchAttack(map, parts, line);
                    break;
                case "Q":
                    ExecuteRoom(actor, ReplaceFirstArgument(parts, actor), line);
                    break;
                case "B":
                    ExecuteBuild(actor, ReplaceFirstArgument(parts, actor), line);
                    break;
                case "F":
                    ExecutePerimeter(actor, parts, line, false);
                    break;
                case "F2":
                    ExecutePerimeter(actor, parts, line, true);
                    break;
                case "G":
                    ExecuteGrowingZone(actor, ReplaceFirstArgument(parts, actor), line);
                    break;
                case "S":
                    ExecuteStockpile(actor, ReplaceFirstArgument(parts, actor), line);
                    break;
                case "P":
                    ExecuteProduction(actor, ReplaceFirstArgument(parts, actor), line);
                    break;
                case "R":
                    ExecuteResearch(actor, ReplaceFirstArgument(parts, actor), line);
                    break;
                case "C":
                    AICoopAdvancedActions.Execute(map, parts, line);
                    break;
                case "T":
                    ExecuteBatchDesignation(map, parts, line);
                    break;
                case "ORE":
                    ExecuteOreDesignation(parts, line);
                    break;
            }
        }

        private static string[] ReplaceFirstArgument(string[] parts, Pawn actor)
        {
            string[] converted = (string[])parts.Clone();
            converted[1] = actor.thingIDNumber.ToString();
            return converted;
        }

        private static Map FindMap(string identifier)
        {
            int mapId;
            if (!Int32.TryParse(identifier, out mapId)) return null;
            return Find.Maps.FirstOrDefault(map => map.uniqueID == mapId);
        }

        private static Pawn FindAIActor(Map map)
        {
            AICoopGameComponent component = AICoopGameComponent.Current;
            return component == null || map == null ? null : map.mapPawns.FreeColonistsSpawned.FirstOrDefault(component.CanAIControl);
        }

        private static void ExecuteBatchMove(Map map, string[] parts, string line)
        {
            int x, z;
            if (parts.Length != 4 || !Int32.TryParse(parts[2], out x) || !Int32.TryParse(parts[3], out z))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 无效批量移动命令：" + line);
                return;
            }
            IntVec3 cell = new IntVec3(x, 0, z);
            if (!cell.InBounds(map) || !cell.Walkable(map))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 批量移动目标不可用：" + cell);
                return;
            }
            int count = 0;
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned.Where(pawn => AICoopGameComponent.Current.CanAIControl(pawn) && !pawn.Downed))
            {
                if (pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, new LocalTargetInfo(cell)))) count++;
            }
            AICoopGameComponent.Current.AddLog("[执行] 让当前地图 " + count + " 名 AI 殖民者移动到 " + cell + "。");
        }

        internal static bool IsApproachingOrAttacking(Pawn pawn, Thing target)
        {
            if (pawn == null || target == null || pawn.jobs == null) return false;
            if (IsAttackJobForTarget(pawn.CurJob, target)) return true;
            return pawn.jobs.jobQueue.Any(queued => IsAttackJobForTarget(queued.job, target));
        }

        internal static bool OrderAttackWithApproach(Pawn pawn, Thing target)
        {
            if (pawn == null || target == null || pawn.Map == null || pawn.Map != target.Map || !target.Spawned) return false;
            if (IsApproachingOrAttacking(pawn, target)) return true;

            LocalTargetInfo targetInfo = new LocalTargetInfo(target);
            Verb verb = pawn.TryGetAttackVerb(target, false);
            if (verb == null) return false;
            Job attack = JobMaker.MakeJob(JobDefOf.AttackStatic, targetInfo);
            if (verb.CanHitTargetFrom(pawn.Position, targetInfo)) return pawn.jobs.TryTakeOrderedJob(attack);

            IntVec3 approachCell = FindApproachCell(pawn, target, verb);
            if (!approachCell.IsValid) return false;
            if (!pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.Goto, new LocalTargetInfo(approachCell)))) return false;
            pawn.jobs.jobQueue.EnqueueLast(attack);
            return true;
        }

        private static bool IsAttackJobForTarget(Job job, Thing target)
        {
            return job != null && job.def == JobDefOf.AttackStatic && job.targetA.Thing == target;
        }

        private static IntVec3 FindApproachCell(Pawn pawn, Thing target, Verb verb)
        {
            float radius = Math.Min(Math.Max(verb.EffectiveRange, 1f), 35f);
            LocalTargetInfo targetInfo = new LocalTargetInfo(target);
            return GenRadial.RadialCellsAround(target.Position, radius, true)
                .Where(cell => cell.InBounds(pawn.Map) && cell.Walkable(pawn.Map) &&
                    pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly) && verb.CanHitTargetFrom(cell, targetInfo))
                .OrderByDescending(cell => CoverUtility.CalculateCoverGiverSet(targetInfo, cell, pawn.Map).Sum(info => info.BlockChance))
                .ThenBy(cell => pawn.Position.DistanceToSquared(cell))
                .FirstOrDefault();
        }

        private static void ExecuteBatchAttack(Map map, string[] parts, string line)
        {
            int targetId;
            if (parts.Length != 3 || !Int32.TryParse(parts[2], out targetId))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 无效批量攻击命令：" + line);
                return;
            }
            Thing target = map.listerThings.AllThings.FirstOrDefault(thing => thing.thingIDNumber == targetId);
            if (!IsValidAttackTarget(target))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 批量攻击目标不是当前地图上的敌人或只能攻击的障碍：" + targetId);
                return;
            }
            int count = 0;
            foreach (Pawn pawn in map.mapPawns.FreeColonistsSpawned.Where(candidate =>
                AICoopGameComponent.Current.CanAIControl(candidate) && !candidate.Downed && !candidate.InMentalState &&
                candidate.drafter != null && candidate.TryGetAttackVerb(target, false) != null))
            {
                AICoopKitingManager.MarkAIDraftIntent(pawn, true);
                pawn.drafter.Drafted = true;
                if (OrderAttackWithApproach(pawn, target)) count++;
            }
            AICoopGameComponent.Current.AddLog("[执行] 征召并让 " + count + " 名 AI 殖民者机动后攻击 " + target.LabelShort + "。");
        }

        private static void ExecuteBatchDesignation(Map map, string[] parts, string line)
        {
            int x1, z1, x2, z2;
            if ((parts.Length != 7 && parts.Length != 8) || !Int32.TryParse(parts[2], out x1) || !Int32.TryParse(parts[3], out z1) ||
                !Int32.TryParse(parts[4], out x2) || !Int32.TryParse(parts[5], out z2))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 无效范围指定命令：" + line);
                return;
            }
            int minX = Math.Min(x1, x2), maxX = Math.Max(x1, x2), minZ = Math.Min(z1, z2), maxZ = Math.Max(z1, z2);
            string kind = parts[6].ToLowerInvariant();
            if (kind == "haul_chunks" && !map.zoneManager.AllZones.OfType<Zone_Stockpile>().Any(IsValidTemporaryDumpingStockpile))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 搬运石块前必须先建立一个长宽至少 7 格的 S dump 临时垃圾储存区。");
                return;
            }
            ColorDef color = parts.Length == 8 ? DefDatabase<ColorDef>.GetNamedSilentFail(parts[7]) : null;
            if ((kind == "paint_building" || kind == "paint_floor") && color == null)
            {
                color = DefDatabase<ColorDef>.AllDefsListForReading.FirstOrDefault(def => def.colorType == ColorType.Structure);
            }

            if (kind == "cancel")
            {
                int removed = 0;
                foreach (Designation designation in map.designationManager.AllDesignations
                    .Where(item => CellInRectangle(item.target.Cell, minX, minZ, maxX, maxZ)).ToList())
                {
                    map.designationManager.RemoveDesignation(designation);
                    removed++;
                }
                foreach (Thing thing in map.listerThings.AllThings
                    .Where(item => (item is Blueprint || item is Frame) && CellInRectangle(item.Position, minX, minZ, maxX, maxZ)).ToList())
                {
                    thing.Destroy(DestroyMode.Cancel);
                    removed++;
                }
                AICoopGameComponent.Current.AddLog("[执行] 取消范围内 " + removed + " 个指定、蓝图或施工框架。");
                return;
            }

            int count = 0;
            foreach (Thing target in map.listerThings.AllThings.Where(thing => thing.Spawned && thing.Position.x >= minX && thing.Position.x <= maxX &&
                thing.Position.z >= minZ && thing.Position.z <= maxZ))
            {
                if (TryApplyThingOperation(map, target, kind, color)) count++;
            }
            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    IntVec3 cell = new IntVec3(x, 0, z);
                    if (cell.InBounds(map) && TryApplyCellOperation(map, cell, kind, color)) count++;
                }
            }
            AICoopGameComponent.Current.AddLog("[执行] 对范围 " + minX + ":" + minZ + "-" + maxX + ":" + maxZ + " 内的 " + count + " 个目标执行“" + kind + "”。");
        }

        private static void ExecuteOreDesignation(string[] parts, string line)
        {
            AICoopGameComponent component = AICoopGameComponent.Current;
            if (component == null || parts == null || parts.Length != 3)
            {
                component?.AddLog("[拒绝] ORE 格式：ORE mapID 矿物defName。");
                return;
            }
            Map map = FindMap(parts[1]);
            ThingDef mineral = DefDatabase<ThingDef>.GetNamedSilentFail(parts[2]);
            if (map == null)
            {
                component.AddLog("[拒绝] 找不到地图：" + parts[1]);
                component.AddCommandResult("FAIL command_not_executed reason=ore_map_not_found map=" + parts[1]);
                return;
            }
            if (!IsValidMineableResourceDef(mineral))
            {
                component.AddLog("[拒绝] ORE 目标必须是存在的矿物 ThingDef，不能是石块类资源：" + parts[2]);
                component.AddCommandResult("FAIL command_not_executed reason=ore_def_invalid def=" + parts[2]);
                return;
            }

            int marked = 0;
            int alreadyMarked = 0;
            foreach (Building rock in map.listerThings.AllThings.OfType<Building>())
            {
                if (rock == null || rock.Destroyed || !rock.Spawned || rock.def == null || rock.def.building == null ||
                    !rock.def.building.isNaturalRock || rock.def.building.mineableThing != mineral) continue;
                Designation existing = map.designationManager.DesignationAt(rock.Position, DesignationDefOf.Mine);
                if (existing != null)
                {
                    alreadyMarked++;
                    continue;
                }
                map.designationManager.AddDesignation(new Designation(rock.Position, DesignationDefOf.Mine));
                marked++;
            }
            component.AddCommandResult("OK ore_designations=" + marked + " already=" + alreadyMarked + " mineral=" + mineral.defName + " map=" + map.uniqueID);
            component.AddLog("[执行] 已将地图 " + map.uniqueID + " 上的 " + mineral.LabelCap + " 天然矿物标记为待开采，共 " + marked + " 个，已有指定 " + alreadyMarked + " 个。");
        }

        private static bool IsValidMineableResourceDef(ThingDef def)
        {
            if (def == null || def.defName.NullOrEmpty()) return false;
            if (def.defName.StartsWith("Chunk", StringComparison.OrdinalIgnoreCase)) return false;
            if (def.thingCategories != null && def.thingCategories.Any(category => category != null &&
                (string.Equals(category.defName, "StoneChunks", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(category.defName, "Chunks", StringComparison.OrdinalIgnoreCase)))) return false;
            return DefDatabase<ThingDef>.AllDefsListForReading.Any(candidate => candidate != null && candidate.building != null &&
                candidate.building.isNaturalRock && candidate.building.mineableThing == def);
        }

        private static bool CellInRectangle(IntVec3 cell, int minX, int minZ, int maxX, int maxZ)
        {
            return cell.IsValid && cell.x >= minX && cell.x <= maxX && cell.z >= minZ && cell.z <= maxZ;
        }

        private static bool TryApplyThingOperation(Map map, Thing target, string kind, ColorDef color)
        {
            ThingWithComps thingWithComps = target as ThingWithComps;
            CompForbiddable forbiddable = thingWithComps == null ? null : thingWithComps.GetComp<CompForbiddable>();
            if (kind == "forbid" || kind == "unforbid")
            {
                bool forbidden = kind == "forbid";
                if (forbiddable == null || forbiddable.Forbidden == forbidden) return false;
                forbiddable.Forbidden = forbidden;
                return true;
            }
            if (kind == "claim")
            {
                Building building = target as Building;
                if (building == null || !building.ClaimableBy(Faction.OfPlayer).Accepted) return false;
                building.SetFaction(Faction.OfPlayer);
                return true;
            }

            DesignationDef designation;
            WorkTypeDef workType;
            if (!TryGetDesignation(target, kind, out designation, out workType))
            {
                Building building = target as Building;
                if (building == null) return false;
                if (kind == "paint_building" && building.def.building.paintable && color != null) designation = DesignationDefOf.PaintBuilding;
                else if (kind == "remove_building_paint" && building.PaintColorDef != null) designation = DesignationDefOf.RemovePaintBuilding;
                else return false;
            }
            if (DesignationOnTarget(map, target, designation) != null) return false;
            AddDesignationForTarget(map, target, designation, color);
            return true;
        }

        private static Designation DesignationOnTarget(Map map, Thing target, DesignationDef designation)
        {
            return designation == DesignationDefOf.Mine
                ? map.designationManager.DesignationAt(target.Position, designation)
                : map.designationManager.DesignationOn(target, designation);
        }

        private static void AddDesignationForTarget(Map map, Thing target, DesignationDef designation, ColorDef color = null)
        {
            Designation newDesignation = designation == DesignationDefOf.Mine
                ? new Designation(target.Position, designation, color)
                : new Designation(target, designation, color);
            map.designationManager.AddDesignation(newDesignation);
        }

        private static bool TryApplyCellOperation(Map map, IntVec3 cell, string kind, ColorDef color)
        {
            DesignationDef designation = null;
            TerrainDef terrain = map.terrainGrid.TerrainAt(cell);
            if (kind == "smooth" && terrain != null && terrain.smoothedTerrain != null) designation = DesignationDefOf.SmoothFloor;
            else if (kind == "paint_floor" && terrain != null && terrain.isPaintable && color != null) designation = DesignationDefOf.PaintFloor;
            else if (kind == "remove_floor_paint" && terrain != null && terrain.colorDef != null) designation = DesignationDefOf.RemovePaintFloor;
            else if (kind == "remove_plan")
            {
                Designation plan = map.designationManager.DesignationAt(cell, DesignationDefOf.Plan);
                if (plan == null) return false;
                map.designationManager.RemoveDesignation(plan);
                return true;
            }
            if (designation == null || map.designationManager.DesignationAt(cell, designation) != null) return false;
            map.designationManager.AddDesignation(new Designation(cell, designation, color));
            return true;
        }

        private static void ExecuteAssistantSpeech(string line, string prefix, string speaker)
        {
            string message = line.Length <= prefix.Length ? string.Empty : line.Substring(prefix.Length).Trim();
            if (message.NullOrEmpty())
            {
                AICoopGameComponent.Current.AddWorkMessage("系统", "Agent 返回了空消息。");
                return;
            }
            AICoopGameComponent.Current.AddWorkMessage(speaker, message);
        }

        private static void ExecuteMove(Pawn pawn, string[] parts, string line)
        {
            int x, z;
            if (parts.Length != 4 || !Int32.TryParse(parts[2], out x) || !Int32.TryParse(parts[3], out z))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 无效移动命令：" + line);
                return;
            }
            IntVec3 cell = new IntVec3(x, 0, z);
            if (!cell.IsValid || pawn.Map == null || !cell.InBounds(pawn.Map) || !cell.Walkable(pawn.Map))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] " + pawn.LabelShort + " 的移动目标不可用：" + cell);
                return;
            }
            Job job = JobMaker.MakeJob(JobDefOf.Goto, new LocalTargetInfo(cell));
            if (pawn.jobs.TryTakeOrderedJob(job)) AICoopGameComponent.Current.AddLog("[执行] " + pawn.LabelShort + " 移动到 " + cell + "。");
            else AICoopGameComponent.Current.AddLog("[拒绝] " + pawn.LabelShort + " 未接受移动命令。");
        }

        private static void ExecuteWork(Pawn pawn, string[] parts, string line)
        {
            int priority;
            if (parts.Length != 4 || !Int32.TryParse(parts[3], out priority))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 无效工作命令：" + line);
                return;
            }
            string workName = parts[2];
            WorkTypeDef work = DefDatabase<WorkTypeDef>.GetNamedSilentFail(workName);
            if (work == null || priority < 0 || priority > 4)
            {
                AICoopGameComponent.Current.AddLog("[拒绝] " + pawn.LabelShort + " 的工作或优先级无效：" + workName);
                return;
            }
            if (work == WorkTypeDefOf.Construction && priority > 0)
            {
                AssignBestAIBuilder(pawn.Map, priority);
                return;
            }
            if (pawn.WorkTypeIsDisabled(work))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] " + pawn.LabelShort + " 无法执行 " + workName + " 工作。");
                return;
            }
            string skillReason;
            if (!AICoopSkillRules.CheckPriority(pawn, work, priority, out skillReason))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] " + skillReason);
                return;
            }
            pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
            pawn.workSettings.SetPriority(work, priority);
            AICoopGameComponent.Current.AddLog("[执行] " + pawn.LabelShort + " 的 " + work.defName + " 优先级设为 " + priority + "。");
        }

        private static void ExecuteDraft(Pawn pawn, string[] parts, string line)
        {
            if (parts.Length != 3 || (parts[2] != "0" && parts[2] != "1"))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 无效征召命令：" + line);
                return;
            }
            bool drafted = parts[2] == "1";
            AICoopKitingManager.MarkAIDraftIntent(pawn, drafted);
            pawn.drafter.Drafted = drafted;
            AICoopGameComponent.Current.AddLog(drafted ? "[执行] 征召 " + pawn.LabelShort + "。" : "[执行] 解除 " + pawn.LabelShort + " 的征召。");
        }

        private static void ExecuteAttack(Pawn pawn, string[] parts, string line)
        {
            int targetId;
            if (parts.Length != 3 || !Int32.TryParse(parts[2], out targetId) || pawn.Map == null)
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 无效攻击命令：" + line);
                return;
            }
            Thing target = pawn.Map.listerThings.AllThings.FirstOrDefault(thing => thing.thingIDNumber == targetId);
            if (!IsValidAttackTarget(target))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] " + pawn.LabelShort + " 的攻击目标不是当前地图上的敌人或只能攻击的障碍：" + targetId);
                return;
            }
            if (pawn.drafter == null || pawn.InMentalState || pawn.Downed || pawn.TryGetAttackVerb(target, false) == null)
            {
                AICoopGameComponent.Current.AddLog("[拒绝] " + pawn.LabelShort + " 当前没有可用的攻击能力，未征召。");
                return;
            }
            AICoopKitingManager.MarkAIDraftIntent(pawn, true);
            pawn.drafter.Drafted = true;
            if (OrderAttackWithApproach(pawn, target)) AICoopGameComponent.Current.AddLog("[执行] " + pawn.LabelShort + " 机动后攻击 " + target.LabelShort + "。");
            else AICoopGameComponent.Current.AddLog("[拒绝] " + pawn.LabelShort + " 未找到可攻击的安全站位。");
        }

        private static bool IsValidAttackTarget(Thing target)
        {
            if (target == null || !target.Spawned) return false;
            Pawn pawn = target as Pawn;
            if (pawn != null) return !pawn.Dead && pawn.HostileTo(Faction.OfPlayer);
            Building building = target as Building;
            return building != null && target.def.IsNonDeconstructibleAttackableBuilding &&
                !building.DeconstructibleBy(Faction.OfPlayer).Accepted;
        }

        private static void ExecuteBuild(Pawn pawn, string[] parts, string line)
        {
            if (pawn.Map == null || !pawn.Spawned || parts.Length < 5 || parts.Length > 7)
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 无效建造命令：" + line);
                return;
            }
            int x, z, rotationValue = 0;
            if (!Int32.TryParse(parts[3], out x) || !Int32.TryParse(parts[4], out z) ||
                (parts.Length >= 6 && !Int32.TryParse(parts[5], out rotationValue)) || rotationValue < 0 || rotationValue > 3)
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 建造坐标或朝向无效：" + line);
                return;
            }
            ThingDef buildDef = DefDatabase<ThingDef>.GetNamedSilentFail(parts[2]);
            if (buildDef == null || buildDef.building == null ||
                !buildDef.BuildableByPlayer || !buildDef.IsResearchFinished)
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 建筑不存在、不可建造或研究前置未完成：" + parts[2]);
                return;
            }
            AICoopGameComponent component = AICoopGameComponent.Current;
            Pawn builder = AssignBestAIBuilder(pawn.Map, 1, buildDef);
            if (builder == null) return;
            IntVec3 cell = new IntVec3(x, 0, z);
            Rot4 rotation = new Rot4(rotationValue);
            ThingDef stuff = null;
            if (buildDef.MadeFromStuff)
            {
                if (parts.Length == 7) stuff = DefDatabase<ThingDef>.GetNamedSilentFail(parts[6]);
                if (stuff != null && (!stuff.IsStuff || stuff.stuffProps == null || !stuff.stuffProps.CanMake(buildDef)))
                {
                    AICoopGameComponent.Current.AddLog("[拒绝] " + stuff.defName + " 不能用于建造 " + buildDef.defName + "。");
                    return;
                }
                if (stuff == null) stuff = ChooseStuff(buildDef, pawn.Map, null);
                if (stuff == null)
                {
                    AICoopGameComponent.Current.AddLog("[拒绝] 找不到适合 " + buildDef.defName + " 的建筑材料。");
                    return;
                }
            }
            string roomRoleReason;
            if (!CanModifyExistingRoom(pawn.Map, cell, buildDef, out roomRoleReason))
            {
                AICoopGameComponent.Current.AddCommandResult("FAIL preset_room_role_change=1 reason=" + roomRoleReason + " def=" + buildDef.defName);
                AICoopGameComponent.Current.AddLog("[拒绝] 该建筑会改变现有房间类型，未放置蓝图：" + buildDef.defName + "，原因=" + roomRoleReason);
                return;
            }
            if (!EnsureConstructionAreaClear(pawn.Map, GenAdj.OccupiedRect(cell, rotation, buildDef.Size), line)) return;
            AcceptanceReport report = GenConstruct.CanPlaceBlueprintAt(buildDef, cell, rotation, pawn.Map, false, null, null, stuff);
            if (!report.Accepted)
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 无法在 " + cell + " 放置 " + buildDef.defName + "：" + report.Reason);
                return;
            }
            Blueprint_Build blueprint = GenConstruct.PlaceBlueprintForBuild(buildDef, cell, pawn.Map, rotation, Faction.OfPlayer, stuff);
            if (blueprint == null)
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 游戏未接受建筑放置：" + buildDef.defName);
                return;
            }
            AICoopStateSerializer.ReportBlueprintMaterialStatus(pawn.Map, blueprint);
            component.MarkAIPlannedConstruction(pawn.Map, blueprint.Position, buildDef);
            if (buildDef.comps != null && buildDef.comps.OfType<CompProperties_Power>().Any(comp => comp != null && comp.PowerConsumption > 0f))
            {
                EnsurePowerConnection(pawn.Map, blueprint);
            }
            bool deliveryStarted = StartMaterialDelivery(builder, blueprint);
            AICoopGameComponent.Current.AddLog("[执行] 放置 " + buildDef.defName + " 蓝图于 " + cell + (deliveryStarted ? "，已安排 AI 立即交付材料。" : "，当前无法立即交付材料，已交由施工优先级处理。"));
        }

        private static bool CanModifyExistingRoom(Map map, IntVec3 cell, ThingDef buildDef, out string reason)
        {
            reason = string.Empty;
            if (map == null || buildDef == null) return true;
            AICoopGameComponent component = AICoopGameComponent.Current;
            if (component == null || !AICoopPresetManager.IsPresetActive(component)) return true;
            Room room = cell.GetRoom(map);
            if (room == null || !room.ProperRoom || room.Role == null || room.Role == RoomRoleDefOf.None) return true;
            RoomRoleDef predicted = room.GetRoomRoleIfBuildingPlaced(buildDef);
            if (predicted == null || predicted == RoomRoleDefOf.None || predicted == room.Role) return true;
            reason = room.Role.defName + "->" + predicted.defName;
            return false;
        }

        private static void EnsurePowerConnection(Map map, Blueprint_Build blueprint)
        {
            if (map == null || blueprint == null || blueprint.EntityToBuild() == null) return;
            bool hasGenerator = map.listerThings.AllThings.Any(IsPowerGeneratorThing);
            if (!hasGenerator)
            {
                string[] generatorNames = { "WoodFiredGenerator", "ChemfuelPoweredGenerator", "WindTurbine", "SolarGenerator", "GeothermalGenerator", "WatermillGenerator" };
                ThingDef generator = generatorNames.Select(name => DefDatabase<ThingDef>.GetNamedSilentFail(name))
                    .FirstOrDefault(def => def != null && def.BuildableByPlayer && def.IsResearchFinished);
                IntVec3 generatorCell = IntVec3.Invalid;
                if (generator != null)
                {
                    foreach (IntVec3 candidate in GenRadial.RadialCellsAround(blueprint.Position, 10f, true))
                    {
                        if (!candidate.InBounds(map) || !GenConstruct.CanPlaceBlueprintAt(generator, candidate, Rot4.North, map, false, null, null,
                            generator.MadeFromStuff ? ChooseStuff(generator, map, null) : null).Accepted) continue;
                        generatorCell = candidate;
                        break;
                    }
                }
                if (!generatorCell.IsValid)
                {
                    AICoopGameComponent.Current.AddCommandResult("FAIL power_no_generator=1 building=" + blueprint.EntityToBuild().defName);
                    AICoopGameComponent.Current.AddWorkMessage("AI", "建筑 " + blueprint.EntityToBuild().defName + " 需要供电，但没有可放置且已研究的发电设备；请玩家处理。");
                    return;
                }
                ThingDef generatorStuff = generator.MadeFromStuff ? ChooseStuff(generator, map, null) : null;
                Blueprint_Build generatorBlueprint = GenConstruct.PlaceBlueprintForBuild(generator, generatorCell, map, Rot4.North, Faction.OfPlayer, generatorStuff);
                if (generatorBlueprint == null)
                {
                    AICoopGameComponent.Current.AddCommandResult("FAIL power_generator_blueprint=1 building=" + blueprint.EntityToBuild().defName);
                    AICoopGameComponent.Current.AddWorkMessage("AI", "无法为 " + blueprint.EntityToBuild().defName + " 放置发电设备蓝图，请玩家处理。");
                    return;
                }
                AICoopStateSerializer.ReportBlueprintMaterialStatus(map, generatorBlueprint);
                AICoopGameComponent.Current.MarkAIPlannedConstruction(map, generatorBlueprint.Position, generator);
                Pawn generatorBuilder = AssignBestAIBuilder(map, 1, generator);
                if (generatorBuilder != null) StartMaterialDelivery(generatorBuilder, generatorBlueprint);
                AICoopGameComponent.Current.AddLog("[执行] 未找到发电设备，已先放置 " + generator.defName + " 发电设备蓝图。");
                hasGenerator = true;
            }
            ThingDef hidden = DefDatabase<ThingDef>.GetNamedSilentFail("HiddenConduit");
            Thing source = map.listerThings.AllThings.Where(IsPowerGeneratorThing)
                .OrderBy(thing => thing.PositionHeld.DistanceToSquared(blueprint.PositionHeld)).FirstOrDefault();
            if (hidden == null || source == null) return;
            int placed = PlaceHiddenConduitPath(map, source.Position, blueprint.Position, hidden);
            AICoopGameComponent.Current.AddLog("[执行] 为 " + blueprint.EntityToBuild().defName + " 自动铺设 " + placed + " 格隐藏电缆。");
        }

        internal static int PlaceHiddenConduitPath(Map map, IntVec3 start, IntVec3 end, ThingDef hidden)
        {
            if (map == null || hidden == null) return 0;
            List<IntVec3> cells = new List<IntVec3>();
            int x = start.x, z = start.z;
            while (x != end.x) { x += x < end.x ? 1 : -1; cells.Add(new IntVec3(x, 0, z)); }
            while (z != end.z) { z += z < end.z ? 1 : -1; cells.Add(new IntVec3(x, 0, z)); }
            int placed = 0;
            Blueprint_Build firstBlueprint = null;
            foreach (IntVec3 cell in cells.Distinct())
            {
                if (!cell.InBounds(map)) continue;
                bool existing = cell.GetThingList(map).Any(thing =>
                {
                    BuildableDef entity = thing is Blueprint ? (thing as Blueprint).EntityToBuild() : thing.def;
                    return entity != null && (entity.defName == "PowerConduit" || entity.defName == "HiddenConduit");
                });
                if (existing || !GenConstruct.CanPlaceBlueprintAt(hidden, cell, Rot4.North, map, false, null, null, null).Accepted) continue;
                Blueprint_Build blueprint = GenConstruct.PlaceBlueprintForBuild(hidden, cell, map, Rot4.North, Faction.OfPlayer, null);
                if (blueprint != null)
                {
                    AICoopStateSerializer.ReportBlueprintMaterialStatus(map, blueprint);
                    if (firstBlueprint == null) firstBlueprint = blueprint;
                    placed++;
                }
            }
            if (firstBlueprint != null)
            {
                Pawn builder = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(candidate => AICoopGameComponent.Current != null &&
                    AICoopGameComponent.Current.CanAIControl(candidate) && candidate.workSettings != null && !candidate.Downed);
                if (builder != null) StartMaterialDelivery(builder, firstBlueprint);
            }
            return placed;
        }

        internal static bool IsPowerGeneratorThing(Thing thing)
        {
            if (thing == null || thing.def == null) return false;
            ThingDef def = thing.def;
            Blueprint blueprint = thing as Blueprint;
            Frame frame = thing as Frame;
            if (blueprint != null) def = blueprint.EntityToBuild() as ThingDef;
            else if (frame != null) def = frame.def.entityDefToBuild as ThingDef;
            return def != null && def.comps != null && def.comps.OfType<CompProperties_Power>()
                .Any(comp => comp != null && comp.PowerConsumption < 0f);
        }

        private static void ExecuteRoom(Pawn pawn, string[] parts, string line)
        {
            if (pawn.Map == null || !pawn.Spawned || parts.Length < 6 || parts.Length > 8)
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 无效房间命令：" + line);
                return;
            }
            int x1, z1, x2, z2;
            if (!Int32.TryParse(parts[2], out x1) || !Int32.TryParse(parts[3], out z1) ||
                !Int32.TryParse(parts[4], out x2) || !Int32.TryParse(parts[5], out z2))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 房间坐标无效：" + line);
                return;
            }
            int minX = Math.Min(x1, x2);
            int maxX = Math.Max(x1, x2);
            int minZ = Math.Min(z1, z2);
            int maxZ = Math.Max(z1, z2);
            int width = maxX - minX + 1;
            int height = maxZ - minZ + 1;
            AICoopGameComponent component = AICoopGameComponent.Current;
            bool presetRoom = component != null && AICoopPresetManager.IsPresetActive(component);
            if (width < 2 || height < 2 || (!presetRoom && (width > 20 || height > 20)))
            {
                AICoopGameComponent.Current.AddLog(presetRoom
                    ? "[拒绝] 预设房间至少需要 2x2 格。"
                    : "[拒绝] 普通房间长宽必须在 4 到 20 格之间。");
                return;
            }
            if (!presetRoom && (width != 13 || height != 13) && !IsSpecialRoomTerrain(pawn.Map, minX, minZ, maxX, maxZ))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 普通地形房间必须为 13x13；只有紧邻水域或奥德赛飞船等特殊地形才能缩小。");
                return;
            }
            string purpose = parts.Length == 8 ? parts[7] : string.Empty;
            if (!presetRoom && purpose.NullOrEmpty())
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 普通房间必须在 Q 命令末尾提供用途标签，例如 bedroom、storage 或 workshop。");
                return;
            }
            ThingDef wall = DefDatabase<ThingDef>.GetNamedSilentFail("Wall");
            ThingDef door = DefDatabase<ThingDef>.GetNamedSilentFail("Door");
            if (wall == null || door == null)
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 房间墙或门定义不可用。");
                return;
            }
            Pawn builder = AssignBestAIBuilder(pawn.Map, 1, wall, door);
            if (builder == null) return;
            ThingDef stuff = parts.Length >= 7 ? DefDatabase<ThingDef>.GetNamedSilentFail(parts[6]) : ChooseStuff(wall, pawn.Map, door);
            if (stuff == null || stuff.stuffProps == null ||
                !stuff.stuffProps.CanMake(wall) || !stuff.stuffProps.CanMake(door))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 房间墙、门或材料定义不可用。");
                return;
            }
            RemoveOverlappingRoomWalls(pawn.Map, minX, minZ, maxX, maxZ);
            List<IntVec3> sharedCells = new List<IntVec3>();
            List<IntVec3> sharedDoorCells = new List<IntVec3>();
            List<IntVec3> sharedWallOpenings = new List<IntVec3>();
            if (component != null) component.GetSharedRoomBoundaries(pawn.Map, minX, minZ, maxX, maxZ, sharedCells, sharedDoorCells, sharedWallOpenings);
            bool hasSharedEntrance = sharedDoorCells.Count > 0;
            for (int i = sharedDoorCells.Count - 1; i >= 0; i--)
            {
                if (!HasExistingDoor(pawn.Map, sharedWallOpenings[i])) continue;
                sharedDoorCells.RemoveAt(i);
                sharedWallOpenings.RemoveAt(i);
            }
            if (!hasSharedEntrance) sharedDoorCells.Add(new IntVec3(minX + width / 2, 0, minZ));
            List<KeyValuePair<IntVec3, ThingDef>> placements = new List<KeyValuePair<IntVec3, ThingDef>>();
            for (int x = minX; x <= maxX; x++)
            {
                AddRoomPlacement(placements, new IntVec3(x, 0, minZ), wall, door, sharedCells, sharedDoorCells);
                AddRoomPlacement(placements, new IntVec3(x, 0, maxZ), wall, door, sharedCells, sharedDoorCells);
            }
            for (int z = minZ + 1; z < maxZ; z++)
            {
                AddRoomPlacement(placements, new IntVec3(minX, 0, z), wall, door, sharedCells, sharedDoorCells);
                AddRoomPlacement(placements, new IntVec3(maxX, 0, z), wall, door, sharedCells, sharedDoorCells);
            }
            if (!EnsureConstructionAreaClear(pawn.Map,
                CellRect.FromLimits(minX, minZ, maxX, maxZ).Cells.Where(cell => !sharedCells.Contains(cell)), line)) return;
            foreach (KeyValuePair<IntVec3, ThingDef> placement in placements)
            {
                if (!EnsureConstructionAreaClear(pawn.Map, new[] { placement.Key }, line)) return;
                AcceptanceReport report = GenConstruct.CanPlaceBlueprintAt(placement.Value, placement.Key, Rot4.North, pawn.Map, false, null, null, stuff);
                if (!report.Accepted)
                {
                    string reason = report.Reason == null ? "unknown" : report.Reason.ToString().Replace(' ', '_').Replace('|', '_');
                    AICoopGameComponent.Current.AddCommandResult("FAIL room_site_cell=" + placement.Key.x + ":" + placement.Key.z + " reason=vanilla_rejected_" + reason);
                    AICoopGameComponent.Current.AddLog("[拒绝] 房间场地不可用，未放置任何蓝图：" + placement.Key + " 原因=" + report.Reason);
                    return;
                }
            }
            string roomPurpose = presetRoom ? AICoopPresetManager.ActiveRoomPurpose(component) : purpose;
            if (!component.TryRegisterRoomPurpose(pawn.Map, minX, minZ, maxX, maxZ, roomPurpose))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 房间范围已被登记为其他用途；每个房间只能承担一个用途。");
                return;
            }
            OpenSharedWallCells(pawn.Map, sharedWallOpenings);
            List<Blueprint_Build> blueprints = new List<Blueprint_Build>();
            foreach (KeyValuePair<IntVec3, ThingDef> placement in placements)
            {
                Blueprint_Build blueprint;
                blueprint = GenConstruct.PlaceBlueprintForBuild(placement.Value, placement.Key, pawn.Map, Rot4.North, Faction.OfPlayer, stuff);
                if (blueprint == null) continue;
                AICoopStateSerializer.ReportBlueprintMaterialStatus(pawn.Map, blueprint);
                blueprints.Add(blueprint);
            }
            bool deliveryStarted = blueprints.Count > 0 && StartMaterialDelivery(builder, blueprints[0]);
            AICoopGameComponent.Current.AddLog("[执行] 放置 " + width + "x" + height + " 房间墙和门蓝图，材料为 " + stuff.defName + (deliveryStarted ? "，已安排 AI 立即交付第一批材料。" : "，当前无法立即交付材料，已交由施工优先级处理。"));
        }

        private static bool EnsureConstructionAreaClear(Map map, IEnumerable<IntVec3> cells, string line, bool allowPresetRemovableObstacles = false)
        {
            if (map == null || cells == null) return false;
            foreach (IntVec3 cell in cells)
            {
                if (!cell.InBounds(map)) continue;
                foreach (Thing thing in cell.GetThingList(map))
                {
                    if (thing == null || thing.Destroyed) continue;
                    // Blueprints, frames and ordinary dropped items do not prevent a new blueprint.
                    if (thing is Blueprint || thing is Frame) continue;
                    if (allowPresetRemovableObstacles && IsPresetRemovableObstacle(thing)) continue;
                    if (thing.def == null) continue;
                    Plant plant = thing as Plant;
                    bool blockingTree = plant != null && plant.def.plant != null && plant.def.plant.IsTree;
                    bool naturalRock = thing.def.building != null && thing.def.building.isNaturalRock;
                    bool building = (thing is Building || thing.def.category == ThingCategory.Building) && !naturalRock;
                    bool stoneChunk = IsStoneChunk(thing);
                    if (!blockingTree && !naturalRock && !building && !stoneChunk) continue;

                    string kind;
                    string action;
                    if (blockingTree)
                    {
                        kind = "树木";
                        action = "T chop";
                    }
                    else if (naturalRock)
                    {
                        kind = "自然岩石";
                        action = "T mine";
                    }
                    else if (stoneChunk)
                    {
                        kind = "石块";
                        action = "先建立至少 7x7 的 S dump 临时垃圾储存区，再用 T haul_chunks";
                    }
                    else
                    {
                        Building existingBuilding = thing as Building;
                        bool attackOnly = (existingBuilding == null || !existingBuilding.DeconstructibleBy(Faction.OfPlayer).Accepted) &&
                            thing.def.IsNonDeconstructibleAttackableBuilding;
                        kind = attackOnly ? "只能攻击的障碍" : "现有建筑";
                        action = attackOnly ? "A" : "T deconstruct";
                    }
                    AICoopGameComponent.Current.AddCommandResult("FAIL build_area_not_clear kind=" + kind + " cell=" + cell.x + ":" + cell.z + " action=" + action.Replace(' ', '_'));
                    AICoopGameComponent.Current.AddLog("[拒绝] 建造区域未清理（" + kind + "，位置 " + cell + "），请先用 " + action + " 清理后再执行：" + line);
                    return false;
                }
            }
            return true;
        }

        internal static bool IsStoneChunk(Thing thing)
        {
            if (thing == null || thing.def == null) return false;
            if (thing.def.defName.StartsWith("Chunk", StringComparison.OrdinalIgnoreCase)) return true;
            return thing.def.thingCategories != null && thing.def.thingCategories.Any(category =>
                category != null && (category.defName == "StoneChunks" || category.defName == "Chunks"));
        }

        private static void AddRoomPlacement(List<KeyValuePair<IntVec3, ThingDef>> placements, IntVec3 cell,
            ThingDef wall, ThingDef door, List<IntVec3> sharedCells, List<IntVec3> sharedDoorCells)
        {
            if (placements.Any(existing => existing.Key == cell)) return;
            if (sharedCells.Contains(cell) && !sharedDoorCells.Contains(cell)) return;
            placements.Add(new KeyValuePair<IntVec3, ThingDef>(cell, sharedDoorCells.Contains(cell) ? door : wall));
        }

        private static void OpenSharedWallCells(Map map, List<IntVec3> wallOpenings)
        {
            if (map == null || wallOpenings == null) return;
            foreach (IntVec3 cell in wallOpenings)
            {
                foreach (Thing thing in cell.GetThingList(map).ToList())
                {
                    BuildableDef entity = thing is Blueprint ? (thing as Blueprint).EntityToBuild() :
                        (thing.def.IsFrame ? thing.def.entityDefToBuild : thing.def);
                    if (entity == null || entity.defName != "Wall") continue;
                    if (thing is Blueprint || thing.def.IsFrame)
                    {
                        thing.Destroy(DestroyMode.Cancel);
                        continue;
                    }
                    if (thing is Building)
                    {
                        if (map.designationManager.DesignationOn(thing, DesignationDefOf.Deconstruct) == null)
                            map.designationManager.AddDesignation(new Designation(thing, DesignationDefOf.Deconstruct));
                    }
                }
            }
        }

        private static void ExecutePerimeter(Pawn pawn, string[] parts, string line, bool outerLayer)
        {
            outerLayer = false; // F2 is retained only as an alias; never build a second layer.
            if (pawn == null || pawn.Map == null || parts.Length < 2 || parts.Length > 4)
            {
                AICoopGameComponent.Current.AddLog("[拒绝] F 格式：F mapID [岩石stuffDefName]；旧间距参数兼容但不再使用。");
                return;
            }
            int margin = 1, legacyMargin;
            string stuffName = parts.Length > 2 ? parts[2] : null;
            bool legacy = parts.Length > 2 && Int32.TryParse(parts[2], out legacyMargin);
            if (parts.Length == 4 && !legacy)
            {
                AICoopGameComponent.Current.AddLog("[拒绝] F 格式：F mapID [岩石stuffDefName]。");
                return;
            }
            if (legacy) stuffName = parts.Length == 4 ? parts[3] : null;
            ThingDef wall = DefDatabase<ThingDef>.GetNamedSilentFail("Wall");
            if (wall == null)
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 当前版本没有可用的墙定义。");
                return;
            }
            ThingDef stuff = stuffName == null ? null : DefDatabase<ThingDef>.GetNamedSilentFail(stuffName);
            if (stuffName != null && (stuff == null || !IsRockStuff(stuff) || stuff.stuffProps == null ||
                !stuff.stuffProps.CanMake(wall)))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 外围墙材料必须是能够建造墙的岩石材料。");
                return;
            }
            if (stuff == null) stuff = ChooseStoneStuff(wall, null, pawn.Map);
            if (stuff == null)
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 地图上没有可用的岩石墙材料。");
                return;
            }

            int minX, minZ, maxX, maxZ;
            AICoopGameComponent.Current.ClearPerimeterHome();
            if (!TryGetHomeBounds(pawn.Map, out minX, out minZ, out maxX, out maxZ))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 地图没有居住区，请先设置居住区。");
                return;
            }
            int colonyWidth = maxX - minX + 1;
            int colonyHeight = maxZ - minZ + 1;
            if (colonyWidth < 13 || colonyHeight < 13)
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 居住区外围不足 13x13，暂不能建设外围墙（当前 " + colonyWidth + "x" + colonyHeight + "）。");
                AICoopGameComponent.Current.AddCommandResult("FAIL perimeter_requires_13x13=1 width=" + colonyWidth + " height=" + colonyHeight);
                return;
            }
            if (outerLayer && !HasPerimeter(pawn.Map, minX, minZ, maxX, maxZ, margin, 2))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] F2 必须建立在已有普通外墙之上；请先执行 F。" );
                return;
            }
            int layerMargin = outerLayer ? margin + 4 : margin;
            if (HasPerimeter(pawn.Map, minX, minZ, maxX, maxZ, layerMargin, outerLayer ? 1 : 2))
            {
                var existingRing = new List<IntVec3>();
                for (int x = minX - layerMargin; x <= maxX + layerMargin; x++)
                {
                    existingRing.Add(new IntVec3(x, 0, minZ - layerMargin));
                    existingRing.Add(new IntVec3(x, 0, maxZ + layerMargin));
                }
                for (int z = minZ - layerMargin; z <= maxZ + layerMargin; z++)
                {
                    existingRing.Add(new IntVec3(minX - layerMargin, 0, z));
                    existingRing.Add(new IntVec3(maxX + layerMargin, 0, z));
                }
                AICoopGameComponent.Current.RememberPerimeter(pawn.Map, existingRing);
                AICoopGameComponent.Current.AddLog("[执行] 殖民地外围已有足够的围墙，跳过重复建设。");
                return;
            }

            List<IntVec3> targets = new List<IntVec3>();
            for (int x = minX - layerMargin; x <= maxX + layerMargin; x++)
                targets.Add(new IntVec3(x, 0, minZ - layerMargin));
            for (int z = minZ - layerMargin + 1; z < maxZ + layerMargin; z++)
                targets.Add(new IntVec3(maxX + layerMargin, 0, z));
            for (int x = maxX + layerMargin; x >= minX - layerMargin; x--)
                targets.Add(new IntVec3(x, 0, maxZ + layerMargin));
            for (int z = maxZ + layerMargin - 1; z > minZ - layerMargin; z--)
                targets.Add(new IntVec3(minX - layerMargin, 0, z));

            List<IntVec3> ring = new List<IntVec3>();
            IntVec3 previous = IntVec3.Invalid;
            foreach (IntVec3 target in targets)
            {
                IntVec3 adjusted = FindNearestPerimeterCell(pawn.Map, target, minX, minZ, maxX, maxZ, layerMargin, wall, stuff);
                if (!adjusted.IsValid)
                {
                    AICoopGameComponent.Current.AddLog("[拒绝] 外围墙遇到无法绕行的地形，未放置任何外围蓝图：" + target);
                    return;
                }
                if (previous.IsValid)
                {
                    List<IntVec3> segment = FindPerimeterPath(pawn.Map, previous, adjusted, minX, minZ, maxX, maxZ, layerMargin, wall, stuff);
                    if (segment == null)
                    {
                        AICoopGameComponent.Current.AddLog("[拒绝] 外围墙无法绕过不可建地形闭合，未放置任何外围蓝图。");
                        return;
                    }
                    foreach (IntVec3 cell in segment) AddUniqueCell(ring, cell);
                }
                else AddUniqueCell(ring, adjusted);
                previous = adjusted;
            }
            if (previous.IsValid && ring.Count > 0)
            {
                List<IntVec3> closing = FindPerimeterPath(pawn.Map, previous, ring[0], minX, minZ, maxX, maxZ, layerMargin, wall, stuff);
                if (closing == null)
                {
                    AICoopGameComponent.Current.AddLog("[拒绝] 外围墙无法闭合，未放置任何外围蓝图。");
                    return;
                }
                foreach (IntVec3 cell in closing) AddUniqueCell(ring, cell);
            }

            var plannedBoundary = new AICoopPerimeterRecord { mapId = pawn.Map.uniqueID, ring = ring };
            var outsideBoundary = plannedBoundary.OutsideCells(pawn.Map);
            if (pawn.Map.areaManager.Home.ActiveCells.Any(cell => outsideBoundary.Contains(cell) || ring.Contains(cell)))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 地形绕行后无法包围全部居住区，未放置新围墙，也未拆除旧墙。");
                return;
            }
            List<IntVec3> openingCells = FindPerimeterOpenings(ring, minX, minZ, maxX, maxZ, layerMargin, outerLayer ? 1 : 2);
            if (openingCells.Count != (outerLayer ? 1 : 2))
            {
                AICoopGameComponent.Current.AddLog(outerLayer
                    ? "[拒绝] 外层墙无法稳定确定左侧正中安全大门位置，未放置任何外围蓝图。"
                    : "[拒绝] 外围墙无法稳定确定左、右两个正中开口，未放置任何外围蓝图。");
                return;
            }
            List<IntVec3> doorCells = new List<IntVec3>(openingCells);
            ThingDef safetyDoor = null;
            ThingDef doorStuff = null;
            Rot4 doorRotation = Rot4.North;
            if (outerLayer)
            {
                safetyDoor = FindSafetyDoor();
                if (safetyDoor == null)
                {
                    AICoopGameComponent.Current.AddLog("[拒绝] 当前版本没有安全大门、自动门或普通门定义，未放置外层墙。");
                    return;
                }
                doorStuff = safetyDoor.MadeFromStuff ? ChooseStuff(safetyDoor, pawn.Map, null) : null;
                if (!TryFindPerimeterDoorPlacement(pawn.Map, ring, openingCells[0], safetyDoor, doorStuff, out doorRotation, out doorCells))
                {
                    AICoopGameComponent.Current.AddLog("[拒绝] 外层墙开口无法放置安全大门，未放置任何外围蓝图。");
                    return;
                }
            }
            foreach (IntVec3 opening in doorCells) ClearPerimeterOpening(pawn.Map, opening);

            foreach (IntVec3 cell in ring)
            {
                if (doorCells.Contains(cell)) continue;
                if (HasExistingWallOrDoor(pawn.Map, cell)) continue;
                if (!GenConstruct.CanPlaceBlueprintAt(wall, cell, Rot4.North, pawn.Map, false, null, null, stuff).Accepted)
                {
                    AICoopGameComponent.Current.AddLog("[拒绝] 外围墙位置不可用，未放置任何外围蓝图：" + cell);
                    return;
                }
            }
            List<Blueprint_Build> blueprints = new List<Blueprint_Build>();
            if (outerLayer)
            {
                Blueprint_Build doorBlueprint = GenConstruct.PlaceBlueprintForBuild(safetyDoor, openingCells[0], pawn.Map,
                    doorRotation, Faction.OfPlayer, doorStuff);
                if (doorBlueprint != null)
                {
                    AICoopStateSerializer.ReportBlueprintMaterialStatus(pawn.Map, doorBlueprint);
                    blueprints.Add(doorBlueprint);
                }
            }
            foreach (IntVec3 cell in ring)
            {
                if (doorCells.Contains(cell)) continue;
                if (HasExistingWallOrDoor(pawn.Map, cell)) continue;
                Blueprint_Build blueprint;
                blueprint = GenConstruct.PlaceBlueprintForBuild(wall, cell, pawn.Map, Rot4.North, Faction.OfPlayer, stuff);
                if (blueprint == null) continue;
                AICoopStateSerializer.ReportBlueprintMaterialStatus(pawn.Map, blueprint);
                blueprints.Add(blueprint);
            }
            bool deliveryStarted = blueprints.Count > 0 && StartMaterialDelivery(pawn, blueprints[0]);
            AICoopGameComponent.Current.RememberPerimeter(pawn.Map, ring);
            AICoopGameComponent.Current.AddLog("[执行] 已生成 " + blueprints.Count + " 个" + (outerLayer ? "外层石墙/安全大门" : "岩石外围墙") +
                "蓝图；" + (outerLayer ? "外层仅保留一个左侧正中安全大门，" : "保留左、右两个正中开口，") +
                (deliveryStarted ? "已安排 AI 立即交付第一批材料。" : "材料将由施工优先级处理。"));
        }

        private static ThingDef FindSafetyDoor()
        {
            string[] names = { "SecurityDoor", "Autodoor", "Door" };
            foreach (string name in names)
            {
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                if (def != null && def.thingClass != null && typeof(Building_Door).IsAssignableFrom(def.thingClass)) return def;
            }
            return null;
        }

        private static bool TryFindPerimeterDoorPlacement(Map map, List<IntVec3> ring, IntVec3 opening,
            ThingDef door, ThingDef stuff, out Rot4 rotation, out List<IntVec3> occupiedCells)
        {
            rotation = Rot4.North;
            occupiedCells = new List<IntVec3>();
            if (map == null || ring == null || door == null) return false;
            Rot4[] rotations = { Rot4.North, Rot4.East, Rot4.South, Rot4.West };
            foreach (Rot4 candidateRotation in rotations)
            {
                List<IntVec3> cells = GenAdj.OccupiedRect(opening, candidateRotation, door.Size).Cells.ToList();
                if (cells.Count == 0 || !cells.All(ring.Contains) || !cells.All(cell => cell.InBounds(map))) continue;
                foreach (IntVec3 cell in cells) ClearPerimeterOpening(map, cell);
                if (!GenConstruct.CanPlaceBlueprintAt(door, opening, candidateRotation, map, false, null, null, stuff).Accepted) continue;
                rotation = candidateRotation;
                occupiedCells = cells;
                return true;
            }
            return false;
        }

        internal static ThingDef ChooseStoneStuff(ThingDef wall, ThingDef door, Map map)
        {
            List<ThingDef> allowed = GenStuff.AllowedStuffsFor(wall)
                .Where(stuff => IsRockStuff(stuff) && stuff.stuffProps != null && (door == null || stuff.stuffProps.CanMake(door)))
                .ToList();
            ThingDef availableGranite = allowed.FirstOrDefault(stuff => IsGraniteStuff(stuff) &&
                AICoopStateSerializer.EstimateMaterialAvailability(map, stuff) > 0);
            if (availableGranite != null) return availableGranite;
            return allowed
                .OrderByDescending(stuff => AICoopStateSerializer.EstimateMaterialAvailability(map, stuff))
                .ThenByDescending(stuff => IsGraniteStuff(stuff) ? 1 : 0)
                .ThenBy(stuff => stuff.defName)
                .FirstOrDefault();
        }

        private static bool IsGraniteStuff(ThingDef stuff)
        {
            return stuff != null && stuff.defName.IndexOf("granite", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsRockStuff(ThingDef stuff)
        {
            if (stuff == null || !stuff.IsStuff) return false;
            string name = stuff.defName.ToLowerInvariant();
            return name.Contains("granite") || name.Contains("slate") || name.Contains("sandstone") ||
                name.Contains("limestone") || name.Contains("marble") || name.Contains("schist") ||
                name.Contains("gneiss") || name.Contains("obsidian") || name.Contains("stone");
        }

        private static bool FindColonyBounds(Map map, out int minX, out int minZ, out int maxX, out int maxZ)
        {
            minX = minZ = Int32.MaxValue;
            maxX = maxZ = Int32.MinValue;
            if (map == null || map.listerThings == null) return false;
            AICoopGameComponent component = AICoopGameComponent.Current;
            bool hasRoomBounds = component != null && component.TryGetRoomBounds(map, out minX, out minZ, out maxX, out maxZ);
            bool hasNonWallThing = map.listerThings.AllThings.Any(thing => thing != null && thing.Spawned && thing.def != null &&
                (thing is Blueprint || thing is Frame || thing is Building) && !IsWallOrDoorThing(thing) &&
                !(thing is Building && thing.def.building != null && thing.def.building.isNaturalRock) &&
                (thing.Faction == Faction.OfPlayer || thing is Blueprint));
            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (thing == null || !thing.Spawned || thing.def == null) continue;
                if (!(thing is Blueprint) && !(thing is Frame) && !(thing is Building)) continue;
                if ((hasRoomBounds || hasNonWallThing) && IsWallOrDoorThing(thing)) continue;
                if (thing is Building && thing.def.building != null && thing.def.building.isNaturalRock) continue;
                if (thing.Faction != Faction.OfPlayer && !(thing is Blueprint)) continue;
                minX = Math.Min(minX, thing.Position.x);
                maxX = Math.Max(maxX, thing.Position.x);
                minZ = Math.Min(minZ, thing.Position.z);
                maxZ = Math.Max(maxZ, thing.Position.z);
            }
            return minX != Int32.MaxValue;
        }

        internal static bool TryGetColonyBounds(Map map, out int minX, out int minZ, out int maxX, out int maxZ)
        {
            return FindColonyBounds(map, out minX, out minZ, out maxX, out maxZ);
        }

        internal static bool TryGetHomeBounds(Map map, out int minX, out int minZ, out int maxX, out int maxZ)
        {
            minX = minZ = int.MaxValue;
            maxX = maxZ = int.MinValue;
            if (map?.areaManager?.Home == null) return false;
            foreach (IntVec3 cell in map.areaManager.Home.ActiveCells)
            {
                minX = Math.Min(minX, cell.x); maxX = Math.Max(maxX, cell.x);
                minZ = Math.Min(minZ, cell.z); maxZ = Math.Max(maxZ, cell.z);
            }
            return minX != int.MaxValue;
        }

        private static int RemoveOverlappingRoomWalls(Map map, int minX, int minZ, int maxX, int maxZ)
        {
            if (map == null) return 0;
            int startX = Math.Max(0, minX);
            int endX = Math.Min(map.Size.x - 1, maxX);
            int startZ = Math.Max(0, minZ);
            int endZ = Math.Min(map.Size.z - 1, maxZ);
            if (startX > endX || startZ > endZ) return 0;
            int removed = 0;
            foreach (IntVec3 cell in CellRect.FromLimits(startX, startZ, endX, endZ).Cells)
            {
                if (!cell.InBounds(map)) continue;
                foreach (Thing thing in cell.GetThingList(map).ToList())
                {
                    if (!IsWallOrDoorThing(thing)) continue;
                    Blueprint blueprint = thing as Blueprint;
                    Frame frame = thing as Frame;
                    if (blueprint != null || frame != null)
                    {
                        thing.Destroy(DestroyMode.Cancel);
                        removed++;
                        continue;
                    }
                    Building building = thing as Building;
                    if (building == null || !building.DeconstructibleBy(Faction.OfPlayer).Accepted) continue;
                    building.Destroy(DestroyMode.Deconstruct);
                    removed++;
                }
            }
            if (removed > 0)
                AICoopGameComponent.Current?.AddLog("[执行] 新房间目标范围内已拆除 " + removed + " 个重叠墙/门，范围外外围墙保持不变。");
            return removed;
        }

        private static bool HasPerimeter(Map map, int minX, int minZ, int maxX, int maxZ, int margin, int expectedOpenings)
        {
            int expected = Math.Max(8, (maxX - minX + 1 + margin * 2) * 2 + (maxZ - minZ - 1 + margin * 2) * 2);
            int actual = 0;
            int gaps = 0;
            int doors = 0;
            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (!IsWallOrDoorThing(thing)) continue;
                int distance = DistanceToBounds(thing.Position, minX, minZ, maxX, maxZ);
                if (distance >= margin - 1 && distance <= margin + 5) actual++;
                if (HasExistingDoor(map, thing.Position)) doors++;
            }
            for (int x = minX - margin; x <= maxX + margin; x++)
            {
                if (!HasExistingWallOrDoor(map, new IntVec3(x, 0, minZ - margin))) gaps++;
                if (!HasExistingWallOrDoor(map, new IntVec3(x, 0, maxZ + margin))) gaps++;
            }
            for (int z = minZ - margin + 1; z < maxZ + margin; z++)
            {
                if (!HasExistingWallOrDoor(map, new IntVec3(minX - margin, 0, z))) gaps++;
                if (!HasExistingWallOrDoor(map, new IntVec3(maxX + margin, 0, z))) gaps++;
            }
            bool completeOpenings = expectedOpenings == 1
                ? (gaps == 1 || (gaps == 0 && doors > 0))
                : gaps == expectedOpenings;
            return actual >= expected * 0.8f && completeOpenings;
        }

        private static int DistanceToBounds(IntVec3 cell, int minX, int minZ, int maxX, int maxZ)
        {
            int dx = cell.x < minX ? minX - cell.x : (cell.x > maxX ? cell.x - maxX : 0);
            int dz = cell.z < minZ ? minZ - cell.z : (cell.z > maxZ ? cell.z - maxZ : 0);
            return dx + dz;
        }

        private static bool IsWallOrDoorThing(Thing thing)
        {
            if (thing == null || thing.def == null) return false;
            Frame frame = thing as Frame;
            BuildableDef entity = thing is Blueprint ? (thing as Blueprint).EntityToBuild() :
                (frame == null ? thing.def : frame.def.entityDefToBuild);
            return entity != null && (entity.defName == "Wall" || entity.defName == "Door");
        }

        private static bool HasExistingWallOrDoor(Map map, IntVec3 cell)
        {
            return cell.GetThingList(map).Any(IsWallOrDoorThing);
        }

        private static bool HasExistingDoor(Map map, IntVec3 cell)
        {
            return cell.GetThingList(map).Any(thing =>
            {
                if (thing == null || thing.def == null) return false;
                BuildableDef entity = thing is Blueprint ? (thing as Blueprint).EntityToBuild() : thing.def;
                return IsDoorEntity(entity);
            });
        }

        private static bool IsDoorEntity(BuildableDef entity)
        {
            if (entity == null) return false;
            if (entity.defName == "Door") return true;
            ThingDef thingDef = entity as ThingDef;
            return thingDef != null && thingDef.thingClass != null && typeof(Building_Door).IsAssignableFrom(thingDef.thingClass);
        }

        private static bool CanPlacePerimeterWall(Map map, IntVec3 cell, ThingDef wall, ThingDef stuff)
        {
            return cell.InBounds(map) && !map.areaManager.Home[cell] && (HasExistingWallOrDoor(map, cell) ||
                GenConstruct.CanPlaceBlueprintAt(wall, cell, Rot4.North, map, false, null, null, stuff).Accepted);
        }

        private static IntVec3 FindNearestPerimeterCell(Map map, IntVec3 target, int minX, int minZ, int maxX, int maxZ,
            int margin, ThingDef wall, ThingDef stuff)
        {
            for (int radius = 0; radius <= 12; radius++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    int dz = radius - Math.Abs(dx);
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        IntVec3 candidate = new IntVec3(target.x + dx, 0, target.z + dz * sign);
                        if (DistanceToBounds(candidate, minX, minZ, maxX, maxZ) < margin) continue;
                        if (CanPlacePerimeterWall(map, candidate, wall, stuff)) return candidate;
                    }
                }
            }
            return IntVec3.Invalid;
        }

        private static List<IntVec3> FindPerimeterPath(Map map, IntVec3 start, IntVec3 goal, int minX, int minZ, int maxX, int maxZ,
            int margin, ThingDef wall, ThingDef stuff)
        {
            if (start == goal) return new List<IntVec3> { start };
            Queue<IntVec3> queue = new Queue<IntVec3>();
            Dictionary<IntVec3, IntVec3> previous = new Dictionary<IntVec3, IntVec3>();
            queue.Enqueue(start);
            previous[start] = IntVec3.Invalid;
            int visited = 0;
            while (queue.Count > 0 && visited++ < 16000)
            {
                IntVec3 current = queue.Dequeue();
                foreach (IntVec3 next in CardinalNeighbors(current))
                {
                    if (!next.InBounds(map) || previous.ContainsKey(next) ||
                        (next != goal && DistanceToBounds(next, minX, minZ, maxX, maxZ) < margin) ||
                        !CanPlacePerimeterWall(map, next, wall, stuff)) continue;
                    previous[next] = current;
                    if (next == goal)
                    {
                        List<IntVec3> path = new List<IntVec3>();
                        IntVec3 cursor = goal;
                        while (cursor.IsValid)
                        {
                            path.Add(cursor);
                            cursor = previous[cursor];
                        }
                        path.Reverse();
                        return path;
                    }
                    queue.Enqueue(next);
                }
            }
            return null;
        }

        private static IEnumerable<IntVec3> CardinalNeighbors(IntVec3 cell)
        {
            yield return new IntVec3(cell.x + 1, 0, cell.z);
            yield return new IntVec3(cell.x - 1, 0, cell.z);
            yield return new IntVec3(cell.x, 0, cell.z + 1);
            yield return new IntVec3(cell.x, 0, cell.z - 1);
        }

        private static void AddUniqueCell(List<IntVec3> cells, IntVec3 cell)
        {
            if (!cells.Contains(cell)) cells.Add(cell);
        }

        private static List<IntVec3> FindPerimeterOpenings(List<IntVec3> ring, int minX, int minZ, int maxX, int maxZ, int margin, int expectedOpenings)
        {
            List<IntVec3> openings = new List<IntVec3>();
            if (ring == null || ring.Count == 0) return openings;
            IntVec3[] targets = expectedOpenings == 1
                ? new[] { new IntVec3(minX - margin, 0, (minZ + maxZ) / 2) }
                : new[]
                {
                    new IntVec3(minX - margin, 0, (minZ + maxZ) / 2),
                    new IntVec3(maxX + margin, 0, (minZ + maxZ) / 2)
                };
            foreach (IntVec3 target in targets)
            {
                IntVec3 opening = ring.Where(cell => !openings.Contains(cell))
                    .OrderBy(cell => cell.DistanceToSquared(target)).FirstOrDefault();
                if (opening.IsValid) openings.Add(opening);
            }
            return openings;
        }

        private static void ClearPerimeterOpening(Map map, IntVec3 cell)
        {
            if (map == null || !cell.IsValid) return;
            foreach (Thing thing in cell.GetThingList(map).ToList())
            {
                if (!IsWallOrDoorThing(thing)) continue;
                if (thing is Blueprint || thing is Frame) thing.Destroy(DestroyMode.Cancel);
                else if (thing is Building) thing.Destroy(DestroyMode.Deconstruct);
            }
        }

        private static void ExecuteGrowingZone(Pawn pawn, string[] parts, string line)
        {
            if (parts.Length == 6 && parts[5].Equals("fertile_adjacent", StringComparison.OrdinalIgnoreCase))
            {
                ExecuteAdjacentFertileGrowingZone(pawn, parts, line);
                return;
            }
            IntVec3[] cells;
            ThingDef plant = DefDatabase<ThingDef>.GetNamedSilentFail(parts[2]);
            if (!TryBuildRectangle(pawn, parts, 7, 3, out cells) || plant == null ||
                plant.plant == null || !plant.plant.Sowable)
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 无效种植区或植物 defName：" + line);
                return;
            }
            if (!EnsureWorkEnabled(pawn, WorkTypeDefOf.Growing)) return;
            if (cells.Any(cell => pawn.Map.zoneManager.ZoneAt(cell) != null))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 种植区不能覆盖已有区域。");
                return;
            }
            if (cells.Any(cell => cell.GetFertility(pawn.Map) < plant.plant.fertilityMin || !CanAddZoneCell(pawn.Map, cell)))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 种植区包含肥力不足或不能建立区域的地格。");
                return;
            }
            Zone_Growing zone = new Zone_Growing(pawn.Map.zoneManager);
            pawn.Map.zoneManager.RegisterZone(zone);
            zone.SetPlantDefToGrow(plant);
            foreach (IntVec3 cell in cells) zone.AddCell(cell);
            AICoopGameComponent.Current.AddLog("[执行] 建立 " + plant.defName + " 种植区（" + cells.Length + " 格）。");
        }

        private static void ExecuteAdjacentFertileGrowingZone(Pawn pawn, string[] parts, string line)
        {
            if (pawn == null || pawn.Map == null || parts.Length != 6)
            {
                AICoopGameComponent.Current.AddLog("[拒绝] fertile_adjacent 格式：G mapID plantDefName x z fertile_adjacent。");
                return;
            }
            ThingDef plant = DefDatabase<ThingDef>.GetNamedSilentFail(parts[2]);
            int x, z;
            if (plant == null || plant.plant == null || !plant.plant.Sowable ||
                !Int32.TryParse(parts[3], out x) || !Int32.TryParse(parts[4], out z))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 无效最高肥力种植区或植物 defName：" + line);
                return;
            }
            if (!EnsureWorkEnabled(pawn, WorkTypeDefOf.Growing)) return;

            IntVec3 start = new IntVec3(x, 0, z);
            List<IntVec3> eligible = new List<IntVec3>();
            float maxFertility = -1f;
            foreach (IntVec3 cell in pawn.Map.AllCells)
            {
                if (!CanUseForGrowing(pawn.Map, cell, plant)) continue;
                float fertility = cell.GetFertility(pawn.Map);
                if (fertility > maxFertility) maxFertility = fertility;
                eligible.Add(cell);
            }
            if (maxFertility < plant.plant.fertilityMin || eligible.Count == 0)
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 地图上没有满足 " + plant.defName + " 最低肥力的可用地格。");
                return;
            }
            List<IntVec3> highestCells = eligible
                .Where(cell => Math.Abs(cell.GetFertility(pawn.Map) - maxFertility) <= 0.001f)
                .OrderBy(cell => Math.Abs(cell.x - pawn.Map.Center.x) + Math.Abs(cell.z - pawn.Map.Center.z))
                .ThenBy(cell => cell.x)
                .ThenBy(cell => cell.z)
                .ToList();
            if (!CanUseForGrowing(pawn.Map, start, plant) ||
                Math.Abs(start.GetFertility(pawn.Map) - maxFertility) > 0.001f)
            {
                if (highestCells.Count == 0)
                {
                    AICoopGameComponent.Current.AddLog("[拒绝] 地图上没有可用的最高肥力种植区起点。");
                    return;
                }
                start = highestCells[0];
                AICoopGameComponent.Current.AddLog("[执行] 种植区起点已按地图中心向外搜索，改用 " + start.x + ":" + start.z + "（肥力 " + maxFertility.ToString("0.##") + "）。");
            }

            HashSet<IntVec3> highest = new HashSet<IntVec3>(highestCells);
            int cellLimit = 15 * PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_FreeColonists.Count();
            if (cellLimit == 0) return;
            Queue<IntVec3> pending = new Queue<IntVec3>();
            HashSet<IntVec3> selected = new HashSet<IntVec3>();
            pending.Enqueue(start);
            selected.Add(start);
            while (pending.Count > 0 && selected.Count < cellLimit)
            {
                IntVec3 current = pending.Dequeue();
                foreach (IntVec3 next in CardinalNeighbors(current))
                {
                    if (selected.Count >= cellLimit) break;
                    if (highest.Contains(next) && selected.Add(next)) pending.Enqueue(next);
                }
            }

            Zone_Growing zone = new Zone_Growing(pawn.Map.zoneManager);
            pawn.Map.zoneManager.RegisterZone(zone);
            zone.SetPlantDefToGrow(plant);
            foreach (IntVec3 cell in selected) zone.AddCell(cell);
            AICoopGameComponent.Current.AddLog("[执行] 已建立 " + plant.defName + " 最高肥力连续种植区，共 " + selected.Count + " 格（肥力 " + maxFertility.ToString("0.##") + "）。");
        }

        private static bool CanUseForGrowing(Map map, IntVec3 cell, ThingDef plant)
        {
            if (map == null || plant == null || !cell.InBounds(map) || !cell.Walkable(map)) return false;
            TerrainDef terrain = map.terrainGrid.TerrainAt(cell);
            if (terrain != null && (terrain.IsWater || terrain.IsOcean)) return false;
            if (map.zoneManager.ZoneAt(cell) != null || !CanAddZoneCell(map, cell)) return false;
            return cell.GetFertility(map) >= plant.plant.fertilityMin;
        }

        private static void ExecuteStockpile(Pawn pawn, string[] parts, string line)
        {
            bool dumping = parts.Length == 7 && parts[6].Equals("dump", StringComparison.OrdinalIgnoreCase);
            IntVec3[] cells;
            if ((!dumping && parts.Length != 6) || !TryBuildRectangle(pawn, parts, dumping ? 7 : 6, 2, out cells))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 无效仓储区命令；格式为 S mapID x1 z1 x2 z2 [dump]：" + line);
                return;
            }
            if (!EnsureWorkEnabled(pawn, WorkTypeDefOf.Hauling)) return;
            if (dumping)
            {
                int width = cells.Max(cell => cell.x) - cells.Min(cell => cell.x) + 1;
                int height = cells.Max(cell => cell.z) - cells.Min(cell => cell.z) + 1;
                if (width < 7 || height < 7)
                {
                    AICoopGameComponent.Current.AddLog("[拒绝] 临时垃圾储存区长宽都必须至少为 7 格，当前为 " + width + "x" + height + "。");
                    return;
                }
                Zone_Stockpile existingDump = pawn.Map.zoneManager.AllZones.OfType<Zone_Stockpile>()
                    .FirstOrDefault(IsValidTemporaryDumpingStockpile);
                if (existingDump != null)
                {
                    AICoopGameComponent.Current.AddLog("[执行] 已有 AI 临时垃圾储存区 " + existingDump.ID + "，复用该区域搬运石块。");
                    return;
                }
            }
            if (cells.Any(cell => pawn.Map.zoneManager.ZoneAt(cell) != null))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 仓储区不能覆盖已有区域。");
                return;
            }
            if (cells.Any(cell => !CanAddZoneCell(pawn.Map, cell)))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 仓储区包含不能建立区域的地格。");
                return;
            }
            Zone_Stockpile zone = new Zone_Stockpile(dumping ? StorageSettingsPreset.DumpingStockpile : StorageSettingsPreset.DefaultStockpile,
                pawn.Map.zoneManager);
            pawn.Map.zoneManager.RegisterZone(zone);
            foreach (IntVec3 cell in cells) zone.AddCell(cell);
            if (dumping) zone.RenamableLabel = TemporaryDumpingStockpileLabel;
            AICoopGameComponent.Current.AddLog(dumping
                ? "[执行] 建立 AI 临时垃圾储存区 " + zone.ID + "（" + cells.Length + " 格）；石块搬运结束后必须用 C storeDelete 删除。"
                : "[执行] 建立普通仓储区（" + cells.Length + " 格）。");
        }

        internal static bool IsTemporaryDumpingStockpile(Zone_Stockpile zone)
        {
            return zone != null && zone.RenamableLabel == TemporaryDumpingStockpileLabel;
        }

        private static bool IsValidTemporaryDumpingStockpile(Zone_Stockpile zone)
        {
            if (!IsTemporaryDumpingStockpile(zone) || zone.cells == null || zone.cells.Count == 0) return false;
            int width = zone.cells.Max(cell => cell.x) - zone.cells.Min(cell => cell.x) + 1;
            int height = zone.cells.Max(cell => cell.z) - zone.cells.Min(cell => cell.z) + 1;
            return width >= 7 && height >= 7;
        }

        internal static int PendingStoneChunkHaulCount(Map map)
        {
            if (map == null) return 0;
            HashSet<int> pending = new HashSet<int>();
            foreach (Designation designation in map.designationManager.AllDesignations)
            {
                Thing target = designation.def == DesignationDefOf.Haul ? designation.target.Thing : null;
                if (IsStoneChunk(target)) pending.Add(target.thingIDNumber);
            }
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned)
            {
                Thing jobTarget = pawn.CurJob == null ? null : pawn.CurJob.targetA.Thing;
                if (IsStoneChunk(jobTarget)) pending.Add(jobTarget.thingIDNumber);
                Thing carried = pawn.carryTracker == null ? null : pawn.carryTracker.CarriedThing;
                if (IsStoneChunk(carried)) pending.Add(carried.thingIDNumber);
            }
            return pending.Count;
        }

        private static bool CanAddZoneCell(Map map, IntVec3 cell)
        {
            return cell.GetThingList(map).All(thing => thing.def.CanOverlapZones);
        }

        private static bool IsSpecialRoomTerrain(Map map, int minX, int minZ, int maxX, int maxZ)
        {
            if (map == null) return false;
            object parent = map.Parent;
            string parentName = parent == null ? string.Empty : parent.GetType().Name;
            if (parentName.IndexOf("Odyssey", StringComparison.OrdinalIgnoreCase) >= 0 ||
                parentName.IndexOf("Ship", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            for (int x = minX - 1; x <= maxX + 1; x++)
            {
                for (int z = minZ - 1; z <= maxZ + 1; z++)
                {
                    IntVec3 cell = new IntVec3(x, 0, z);
                    if (!cell.InBounds(map)) continue;
                    TerrainDef terrain = map.terrainGrid.TerrainAt(cell);
                    if (terrain != null && (terrain.IsWater || terrain.IsOcean)) return true;
                }
            }
            return false;
        }

        private static bool TryBuildRectangle(Pawn pawn, string[] parts, int expectedLength, int coordinateStart, out IntVec3[] cells)
        {
            cells = new IntVec3[0];
            if (pawn == null || pawn.Map == null || parts.Length != expectedLength) return false;
            int x1, z1, x2, z2;
            if (!Int32.TryParse(parts[coordinateStart], out x1) || !Int32.TryParse(parts[coordinateStart + 1], out z1) ||
                !Int32.TryParse(parts[coordinateStart + 2], out x2) || !Int32.TryParse(parts[coordinateStart + 3], out z2)) return false;
            int minX = Math.Min(x1, x2);
            int maxX = Math.Max(x1, x2);
            int minZ = Math.Min(z1, z2);
            int maxZ = Math.Max(z1, z2);
            int width = maxX - minX + 1;
            int height = maxZ - minZ + 1;
            if (width <= 0 || height <= 0 || width * height > 400) return false;
            List<IntVec3> result = new List<IntVec3>(width * height);
            for (int x = minX; x <= maxX; x++)
            {
                for (int z = minZ; z <= maxZ; z++)
                {
                    IntVec3 cell = new IntVec3(x, 0, z);
                    if (!cell.InBounds(pawn.Map) || !cell.Walkable(pawn.Map)) return false;
                    result.Add(cell);
                }
            }
            cells = result.ToArray();
            return true;
        }

        private static void ExecuteProduction(Pawn pawn, string[] parts, string line)
        {
            if (pawn.Map == null || parts.Length != 5)
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 无效生产账单命令：" + line);
                return;
            }
            int benchId, count;
            if (!Int32.TryParse(parts[2], out benchId) || !Int32.TryParse(parts[4], out count) || count < 1 || count > 1000)
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 工作台 ID 或生产数量无效：" + line);
                return;
            }
            Building_WorkTable table = pawn.Map.listerThings.AllThings.FirstOrDefault(thing => thing.thingIDNumber == benchId) as Building_WorkTable;
            RecipeDef recipe = DefDatabase<RecipeDef>.GetNamedSilentFail(parts[3]);
            if (table == null || recipe == null || table.def.AllRecipes == null || !table.def.AllRecipes.Contains(recipe) ||
                (recipe.researchPrerequisite != null && !recipe.researchPrerequisite.IsFinished) ||
                (recipe.researchPrerequisites != null && recipe.researchPrerequisites.Any(project => !project.IsFinished)))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 工作台、配方或研究前置无效：" + line);
                return;
            }
            if (recipe.requiredGiverWorkType != null && !EnsureWorkEnabled(pawn, recipe.requiredGiverWorkType)) return;
            if (table.BillStack == null)
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 工作台没有可用账单列表。");
                return;
            }
            Bill_Production existingBill = table.BillStack.Bills.FirstOrDefault(existing => existing.recipe == recipe) as Bill_Production;
            if (existingBill != null)
            {
                existingBill.repeatMode = BillRepeatModeDefOf.RepeatCount;
                existingBill.repeatCount = Math.Max(existingBill.repeatCount, count);
                AICoopGameComponent.Current.AddLog("[执行] 已有 " + recipe.defName + " 账单，剩余次数至少设为 " + count + "。");
                return;
            }
            if (table.BillStack.Count >= BillStack.MaxCount)
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 工作台账单已达到上限。");
                return;
            }
            Bill_Production bill = new Bill_Production(recipe);
            bill.repeatMode = BillRepeatModeDefOf.RepeatCount;
            bill.repeatCount = count;
            table.BillStack.AddBill(bill);
            AICoopGameComponent.Current.AddLog("[执行] 在 " + table.LabelShort + " 添加 " + recipe.defName + " 生产账单（" + count + " 次）。");
        }

        private static void ExecuteResearch(Pawn pawn, string[] parts, string line)
        {
            if (parts.Length != 3)
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 无效研究命令：" + line);
                return;
            }
            ResearchProjectDef project = DefDatabase<ResearchProjectDef>.GetNamedSilentFail(parts[2]);
            if (project == null || project.IsFinished || !project.CanStartNow)
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 研究项目不存在、已完成或前置条件未满足：" + parts[2]);
                return;
            }
            if (Find.ResearchManager.IsCurrentProject(project))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] " + project.defName + " 已经是当前研究项目。");
                return;
            }
            if (!EnsureWorkEnabled(pawn, WorkTypeDefOf.Research)) return;
            Find.ResearchManager.SetCurrentProject(project);
            AICoopGameComponent.Current.AddLog("[执行] 选择研究项目 " + project.defName + "。");
        }

        private static void ExecuteDesignation(Pawn pawn, string[] parts, string line)
        {
            if (pawn.Map == null || parts.Length != 4)
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 无效指定命令：" + line);
                return;
            }
            int targetId;
            if (!Int32.TryParse(parts[2], out targetId))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 指定目标 ID 无效：" + line);
                return;
            }
            Thing target = pawn.Map.listerThings.AllThings.FirstOrDefault(thing => thing.thingIDNumber == targetId);
            DesignationDef designation;
            WorkTypeDef workType;
            if (!TryGetDesignation(target, parts[3], out designation, out workType))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 目标与指定类型不匹配：" + line);
                return;
            }
            if (workType != null && !EnsureWorkEnabled(pawn, workType)) return;
            if (DesignationOnTarget(pawn.Map, target, designation) != null)
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 目标已经存在该指定：" + targetId);
                return;
            }
            AddDesignationForTarget(pawn.Map, target, designation);
            AICoopGameComponent.Current.AddLog("[执行] 对 " + target.LabelShort + " 添加 " + parts[3].ToLowerInvariant() + " 指定。");
        }

        private static bool TryGetDesignation(Thing target, string kind, out DesignationDef designation, out WorkTypeDef workType)
        {
            designation = null;
            workType = null;
            if (target == null || !target.Spawned) return false;
            switch ((kind ?? string.Empty).ToLowerInvariant())
            {
                case "chop":
                    designation = target is Plant chopPlant && chopPlant.def.plant.IsTree
                        ? DesignationDefOf.CutPlant : null;
                    workType = WorkTypeDefOf.PlantCutting;
                    break;
                case "cut":
                    designation = target is Plant cutPlant && !cutPlant.def.plant.IsTree ? DesignationDefOf.CutPlant : null;
                    workType = WorkTypeDefOf.PlantCutting;
                    break;
                case "harvest":
                    designation = target is Plant harvestPlant && harvestPlant.HarvestableNow ? DesignationDefOf.HarvestPlant : null;
                    workType = WorkTypeDefOf.PlantCutting;
                    break;
                case "mine":
                    designation = target.def.building != null && target.def.building.isNaturalRock ? DesignationDefOf.Mine : null;
                    workType = WorkTypeDefOf.Mining;
                    break;
                case "deconstruct":
                    Building deconstructBuilding = target as Building;
                    designation = deconstructBuilding != null && target.def.building != null && !target.def.building.isNaturalRock &&
                        deconstructBuilding.DeconstructibleBy(Faction.OfPlayer).Accepted ? DesignationDefOf.Deconstruct : null;
                    workType = WorkTypeDefOf.Construction;
                    break;
                case "hunt":
                    designation = target is Pawn targetPawn && targetPawn.RaceProps.Animal ? DesignationDefOf.Hunt : null;
                    workType = WorkTypeDefOf.Hunting;
                    break;
                case "slaughter":
                    designation = target is Pawn slaughterPawn && slaughterPawn.RaceProps.Animal && slaughterPawn.Faction == Faction.OfPlayer
                        ? DesignationDefOf.Slaughter : null;
                    workType = WorkTypeDefOf.Handling;
                    break;
                case "tame":
                    designation = target is Pawn tamePawn && tamePawn.RaceProps.Animal ? DesignationDefOf.Tame : null;
                    workType = WorkTypeDefOf.Handling;
                    break;
                case "haul":
                    designation = target.def.EverHaulable && !IsStoneChunk(target) ? DesignationDefOf.Haul : null;
                    workType = WorkTypeDefOf.Hauling;
                    break;
                case "haul_chunks":
                    designation = IsStoneChunk(target) ? DesignationDefOf.Haul : null;
                    workType = WorkTypeDefOf.Hauling;
                    break;
                case "strip":
                    designation = target is Pawn ? DesignationDefOf.Strip : null;
                    break;
                case "smooth":
                    designation = target is Building smoothBuilding && smoothBuilding.def.IsSmoothable ? DesignationDefOf.SmoothWall : null;
                    workType = WorkTypeDefOf.Construction;
                    break;
            }
            return designation != null;
        }

        private static void ExecuteTargetJob(Pawn pawn, string[] parts, string line)
        {
            if (pawn.Map == null || parts.Length != 4)
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 无效直接任务命令：" + line);
                return;
            }
            int targetId;
            if (!Int32.TryParse(parts[2], out targetId))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 任务目标 ID 无效：" + line);
                return;
            }
            Thing target = pawn.Map.listerThings.AllThings.FirstOrDefault(thing => thing.thingIDNumber == targetId);
            Pawn targetPawn = target as Pawn;
            JobDef jobDef = null;
            Thing secondaryTarget = null;
            string kind = parts[3].ToLowerInvariant();
            if (kind == "rescue" && targetPawn != null && targetPawn.Downed)
            {
                secondaryTarget = RestUtility.FindBedFor(targetPawn, pawn, false, false) ?? RestUtility.FindBedFor(targetPawn, pawn, false, true);
                if (secondaryTarget != null) jobDef = JobDefOf.Rescue;
            }
            else if (kind == "tend" && targetPawn != null)
            {
                secondaryTarget = HealthAIUtility.FindBestMedicine(pawn, targetPawn);
                jobDef = JobDefOf.TendPatient;
            }
            else if ((kind == "capture" || kind == "arrest") && targetPawn != null &&
                ((kind == "capture" && targetPawn.HostileTo(pawn) && targetPawn.Downed) || (kind == "arrest" && targetPawn.Faction != pawn.Faction)))
            {
                secondaryTarget = RestUtility.FindBedFor(targetPawn, pawn, false, false, GuestStatus.Prisoner) ??
                    RestUtility.FindBedFor(targetPawn, pawn, false, true, GuestStatus.Prisoner);
                if (secondaryTarget != null) jobDef = kind == "capture" ? JobDefOf.Capture : JobDefOf.Arrest;
            }
            else if (kind == "clean" && target is Filth) jobDef = JobDefOf.Clean;
            else if (kind == "repair" && target is Building && target.HitPoints < target.MaxHitPoints) jobDef = JobDefOf.Repair;
            else if (kind == "equip" && target != null && target.def.IsWeapon) jobDef = JobDefOf.Equip;
            else if (kind == "wear" && target is Apparel) jobDef = JobDefOf.Wear;
            if (jobDef == null)
            {
                AICoopGameComponent.Current.AddLog("[拒绝] 目标与直接任务类型不匹配：" + line);
                return;
            }
            string skillReason;
            if ((kind == "equip" || kind == "wear") && !AICoopSkillRules.CanEquip(pawn, target, out skillReason))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] " + skillReason);
                return;
            }
            WorkTypeDef requiredWork = null;
            if (kind == "tend") requiredWork = WorkTypeDefOf.Doctor;
            else if (kind == "clean") requiredWork = WorkTypeDefOf.Cleaning;
            else if (kind == "repair") requiredWork = WorkTypeDefOf.Construction;
            else if (kind == "capture" || kind == "arrest") requiredWork = WorkTypeDefOf.Warden;
            if (requiredWork != null)
            {
                if (!AICoopSkillRules.CheckDirectJob(pawn, requiredWork, out skillReason))
                {
                    AICoopGameComponent.Current.AddLog("[拒绝] " + skillReason);
                    return;
                }
                if (!EnsureWorkEnabled(pawn, requiredWork)) return;
            }
            Job job = secondaryTarget == null
                ? JobMaker.MakeJob(jobDef, new LocalTargetInfo(target))
                : JobMaker.MakeJob(jobDef, new LocalTargetInfo(target), new LocalTargetInfo(secondaryTarget));
            if (pawn.jobs.TryTakeOrderedJob(job))
            {
                if (kind == "wear" && target is Apparel) AICoopGameComponent.Current.RegisterNonForcedApparel(pawn, (Apparel)target);
                AICoopGameComponent.Current.AddLog("[执行] " + pawn.LabelShort + " 执行 " + kind + "。");
                if (kind == "capture" && targetPawn != null) AICoopGameComponent.Current.RecordPrisonerCapture(targetPawn, pawn);
            }
            else AICoopGameComponent.Current.AddLog("[拒绝] " + pawn.LabelShort + " 未接受直接任务。");
        }

        private static ThingDef ChooseStuff(BuildableDef buildDef, Map map, BuildableDef alsoMustMake)
        {
            List<ThingDef> allowed = GenStuff.AllowedStuffsFor(buildDef)
                .Where(stuff => stuff.stuffProps != null && (alsoMustMake == null || stuff.stuffProps.CanMake(alsoMustMake)))
                .ToList();
            List<ThingDef> ordinary = allowed.Where(stuff => !AICoopStateSerializer.IsStrategicMaterial(stuff)).ToList();
            ThingDef best = (ordinary.Count > 0 ? ordinary : allowed)
                .OrderByDescending(stuff => AICoopStateSerializer.EstimateMaterialAvailability(map, stuff))
                .ThenBy(stuff => stuff.defName)
                .FirstOrDefault();
            return best ?? GenStuff.DefaultStuffFor(buildDef);
        }

        private static bool EnsureWorkEnabled(Pawn pawn, WorkTypeDef workType)
        {
            if (workType == null) return true;
            if (pawn == null || pawn.workSettings == null || pawn.WorkTypeIsDisabled(workType))
            {
                AICoopGameComponent.Current.AddLog("[拒绝] " + (pawn == null ? "该殖民者" : pawn.LabelShort) + " 无法执行 " + workType.defName + " 工作。");
                return false;
            }
            pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
            return true;
        }

        internal static void ConfigureAutomaticProductionBill(Map map, IntVec3 cell, ThingDef expectedDef)
        {
            if (map == null || expectedDef == null) return;
            Building_WorkTable table = cell.GetThingList(map).OfType<Building_WorkTable>().FirstOrDefault(building => building.def == expectedDef);
            if (table == null || table.BillStack == null) return;
            string recipeName = null;
            if (expectedDef.defName == "TableButcher") recipeName = "ButcherCorpseFlesh";
            else if (expectedDef.defName == "ElectricStove" || expectedDef.defName == "FueledStove") recipeName = "CookMealSimple";
            else if (expectedDef.defName == "ElectricCrematorium") recipeName = "CremateCorpse";
            if (recipeName.NullOrEmpty()) return;
            RecipeDef recipe = DefDatabase<RecipeDef>.GetNamedSilentFail(recipeName);
            if (recipe == null || expectedDef.AllRecipes == null || !expectedDef.AllRecipes.Contains(recipe) ||
                (recipe.researchPrerequisite != null && !recipe.researchPrerequisite.IsFinished) ||
                (recipe.researchPrerequisites != null && recipe.researchPrerequisites.Any(project => !project.IsFinished))) return;
            Bill_Production bill = table.BillStack.Bills.OfType<Bill_Production>().FirstOrDefault(existing => existing.recipe == recipe);
            if (bill == null)
            {
                if (table.BillStack.Count >= BillStack.MaxCount) return;
                bill = new Bill_Production(recipe);
                table.BillStack.AddBill(bill);
            }
            bill.repeatMode = BillRepeatModeDefOf.Forever;
            AICoopGameComponent.Current?.AddLog("[执行] AI 建成 " + expectedDef.defName + "，已自动添加无限“" + recipe.LabelCap + "”账单。");
        }

        private static bool StartMaterialDelivery(Pawn builder, Blueprint_Build blueprint)
        {
            if (builder == null || blueprint == null || !blueprint.Spawned || builder.Map != blueprint.Map) return false;
            AICoopGameComponent component = AICoopGameComponent.Current;
            if (component == null) return false;
            string buildName = blueprint.EntityToBuild() == null ? blueprint.def.defName : blueprint.EntityToBuild().defName;
            Job delivery;
            try
            {
                delivery = new WorkGiver_ConstructDeliverResourcesToBlueprints().JobOnThing(builder, blueprint, false);
            }
            catch (Exception ex)
            {
                string reason = CompactException(ex);
                component.AddCommandResult("WAIT build=" + buildName + " blueprint=" + blueprint.thingIDNumber + " reason=material_delivery_check_error " + reason);
                component.AddLog("[警告] 无法立即检查 " + buildName + " 的材料交付工作，已保留蓝图并交由普通建造优先级处理：" + reason);
                return false;
            }
            if (delivery == null)
            {
                component.AddCommandResult("WAIT build=" + buildName + " blueprint=" + blueprint.thingIDNumber + " reason=no_material_delivery_job");
                return false;
            }
            try
            {
                if (builder.jobs != null && builder.jobs.TryTakeOrderedJob(delivery)) return true;
            }
            catch (Exception ex)
            {
                string reason = CompactException(ex);
                component.AddCommandResult("FAIL build=" + buildName + " blueprint=" + blueprint.thingIDNumber + " reason=material_delivery_not_started " + reason);
                return false;
            }
            component.AddCommandResult("FAIL build=" + buildName + " blueprint=" + blueprint.thingIDNumber + " reason=material_delivery_not_started");
            return false;
        }

        private static Pawn AssignBestAIBuilder(Map map, int priority, BuildableDef buildDef = null, BuildableDef secondBuildDef = null)
        {
            AICoopGameComponent component = AICoopGameComponent.Current;
            if (map == null || component == null)
            {
                if (component != null) component.AddLog("[拒绝] 当前地图不可用，无法分配建造工作。");
                return null;
            }

            List<Pawn> builders = map.mapPawns.FreeColonistsSpawned
                .Where(candidate => component.CanAIControl(candidate) && !candidate.Downed && candidate.workSettings != null &&
                    !candidate.WorkTypeIsDisabled(WorkTypeDefOf.Construction) && MeetsConstructionRequirement(candidate, buildDef) &&
                    MeetsConstructionRequirement(candidate, secondBuildDef))
                .OrderByDescending(ConstructionSkill)
                .ThenBy(candidate => candidate.thingIDNumber)
                .ToList();
            if (builders.Count == 0)
            {
                component.AddLog("[拒绝] 当前地图没有能够建造的 AI 殖民者。");
                return null;
            }

            Pawn best = builders[0];
            component.AddLog("[系统] 建造目标已选择 " + best.LabelShort + "（建造技能 " + ConstructionSkill(best) + "）；工作优先级保持玩家设置。");
            return best;
        }

        private static int ConstructionSkill(Pawn pawn)
        {
            return pawn.skills == null ? 0 : pawn.skills.GetSkill(SkillDefOf.Construction).Level;
        }

        private static bool MeetsConstructionRequirement(Pawn pawn, BuildableDef buildDef)
        {
            ThingDef thingDef = buildDef as ThingDef;
            if (thingDef == null) return true;
            return pawn.skills != null && ConstructionSkill(pawn) >= thingDef.constructionSkillPrerequisite &&
                pawn.skills.GetSkill(SkillDefOf.Artistic).Level >= thingDef.artisticSkillPrerequisite;
        }

        private static Pawn FindPawn(string identifier, out bool ambiguousName)
        {
            ambiguousName = false;
            IEnumerable<Pawn> pawns = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists;
            int id;
            if (Int32.TryParse(identifier, out id)) return pawns.FirstOrDefault(pawn => pawn.thingIDNumber == id);

            List<Pawn> matches = pawns.Where(pawn =>
                string.Equals(pawn.LabelShort, identifier, StringComparison.OrdinalIgnoreCase) ||
                (pawn.Name != null && string.Equals(pawn.Name.ToStringShort, identifier, StringComparison.OrdinalIgnoreCase)))
                .Take(2)
                .ToList();
            ambiguousName = matches.Count > 1;
            return matches.Count == 1 ? matches[0] : null;
        }

        private static string CompactException(Exception ex)
        {
            if (ex == null) return "unknown_exception";
            return (ex.GetType().Name + ":" + ex.Message).Replace("\r", " ").Replace("\n", " ").Replace(",", " ").Replace(" ", "_");
        }

    }
}
