using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;

namespace AICoopCompanion
{
    internal static class AICoopAdvancedActions
    {
        private static readonly MethodInfo SetAreaCell = typeof(Area).GetMethod("Set", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly MethodInfo NotifyBedTypeChanged = typeof(Building_Bed).GetMethod("NotifyRoomBedTypeChanged", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        private static readonly FieldInfo PawnQuestTags = typeof(Pawn).GetField("questTags", BindingFlags.Instance | BindingFlags.NonPublic);

        public static void Execute(Map map, string[] parts, string line)
        {
            if (map == null || parts.Length < 3)
            {
                Log("[拒绝] 无效高级管理命令：" + line);
                return;
            }
            switch (parts[2].ToLowerInvariant())
            {
                case "work": ConfigureWork(map, parts, line); break;
                case "autowork":
                case "haulrules":
                    Log("[拒绝] 工作优先级由玩家在开局设置，AI 不得修改：" + line);
                    break;
                case "timetable": ConfigureTimetable(map, parts, line); break;
                case "medical": ConfigureMedicalCare(map, parts, line); break;
                case "policy": ConfigurePolicy(map, parts, line); break;
                case "gear": ConfigureGear(map, parts, line); break;
                case "roof": ConfigureRoof(map, parts, line); break;
                case "home": ConfigureHome(map, parts, line); break;
                case "storepriority": ConfigureStoragePriority(map, parts, line); break;
                case "storeallow": ConfigureStorageFilter(map, parts, line); break;
                case "storeclear": ClearStorageFilter(map, parts, line); break;
                case "storedelete": DeleteTemporaryDumpingStockpile(map, parts, line); break;
                case "prisoner": ConfigurePrisoner(map, parts, line); break;
                case "bed": ConfigureBed(map, parts, line); break;
                case "door": ConfigureDoor(map, parts, line); break;
                case "deep": QueryDeepResource(map, parts, line); break;
                case "timetablehour": ConfigureHourlyTimetable(map, parts, line); break;
                case "selftend": ConfigureSelfTend(map, parts, line); break;
                case "hostility": ConfigureHostility(map, parts, line); break;
                case "area": ConfigureAllowedArea(map, parts, line); break;
                case "area_new": ConfigureNewAllowedArea(map, parts, line); break;
                case "policycreate": CreatePolicy(map, parts, line); break;
                case "policyedit": EditPolicy(map, parts, line); break;
                case "storecategory": ConfigureStorageCategory(map, parts, line); break;
                case "storededicated": ConfigureDedicatedStorage(map, parts, line); break;
                case "temp": ConfigureTemperature(map, parts, line); break;
                case "rebuild": ConfigureAutoRebuild(map, parts, line); break;
                case "homeauto": ConfigureAutomaticHomeArea(map, parts, line); break;
                case "floor": ConfigureFloor(map, parts, line); break;
                case "conduit": ConfigureConduit(map, parts, line); break;
                case "hiddenconduit": ConfigureHiddenConduit(map, parts, line); break;
                case "fortify":
                case "defense": ConfigureDefenseWorks(map, parts, line); break;
                case "surgery": ConfigureSurgery(map, parts, line); break;
                case "organharvest": ConfigureOrganHarvest(map, parts, line); break;
                case "animal": ConfigureAnimal(map, parts, line); break;
                case "deepauto": ConfigureAutomaticDeepDrill(map, parts, line); break;
                case "bill":
                case "billconfig": ConfigureBill(map, parts, line); break;
                case "billorder": ConfigureBillOrder(map, parts, line); break;
                case "ritual": ExecuteRitual(map, parts, line); break;
                case "culturechair": ConfigureCultureChairs(map, parts, line); break;
                default: Log("[拒绝] 未实现的高级管理类型：" + parts[2]); break;
            }
        }

        internal static string BuildIdeologyPromptSection()
        {
            StringBuilder result = new StringBuilder();
            if (!ModsConfig.IdeologyActive || Faction.OfPlayer == null || Faction.OfPlayer.ideos == null || Faction.OfPlayer.ideos.PrimaryIdeo == null)
            {
                return "IDEOLOGY_STATUS unavailable";
            }
            Ideo ideo = Faction.OfPlayer.ideos.PrimaryIdeo;
            result.AppendLine("IDEOLOGY_STATUS active=1 name=" + Clean(ideo.name));
            foreach (Precept_Ritual ritual in ideo.PreceptsListForReading.OfType<Precept_Ritual>().OrderBy(item => item.Id))
            {
                string obligations = ritual.activeObligations == null
                    ? "-"
                    : string.Join(",", ritual.activeObligations.Where(item => item != null && item.StillValid)
                        .Select(item => item.ID + ":" + DescribeRitualTarget(item.FirstValidTarget)).ToArray());
                string targets = "-";
                if (ritual.isAnytime && ritual.obligationTargetFilter != null)
                {
                    List<string> availableTargets = new List<string>();
                    foreach (Map map in Find.Maps)
                    {
                        try
                        {
                            foreach (TargetInfo target in ritual.obligationTargetFilter.GetTargets(null, map).Where(item => item.IsValid).Take(20))
                            {
                                availableTargets.Add(map.uniqueID + ":" + DescribeRitualTarget(target));
                            }
                        }
                        catch
                        {
                        }
                    }
                    if (availableTargets.Count > 0) targets = string.Join(",", availableTargets.ToArray());
                }
                result.AppendLine("IDEOLOGY_RITUAL id=" + ritual.Id + " label=" + Clean(ritual.Label) +
                    " anytime=" + (ritual.isAnytime ? "1" : "0") + " obligations=" + obligations +
                    " targets=" + targets +
                    " roles=" + (ritual.behavior == null || ritual.behavior.def == null || ritual.behavior.def.roles == null
                        ? "-"
                        : string.Join(",", ritual.behavior.def.roles.Select(role => role.id + ":" + (role.required ? "required" : "optional")).ToArray())));
            }
            return result.ToString().TrimEnd();
        }

        private static void ExecuteRitual(Map map, string[] parts, string line)
        {
            if (!ModsConfig.IdeologyActive)
            {
                RitualFailure("ideology_inactive", line);
                return;
            }
            if (parts.Length < 5 || parts.Length > 7)
            {
                RitualFailure("format=C mapID ritual ritualID targetThingID|x:z|none [organizerAIPawnID] [obligationID]", line);
                return;
            }
            int ritualId;
            if (!Int32.TryParse(parts[3], out ritualId))
            {
                RitualFailure("invalid_ritual_id", line);
                return;
            }
            Ideo ideo = Faction.OfPlayer == null || Faction.OfPlayer.ideos == null ? null : Faction.OfPlayer.ideos.PrimaryIdeo;
            Precept_Ritual ritual = ideo == null ? null : ideo.PreceptsListForReading.OfType<Precept_Ritual>().FirstOrDefault(item => item.Id == ritualId);
            if (ritual == null || ritual.behavior == null || ritual.behavior.def == null)
            {
                RitualFailure("ritual_not_found", line);
                return;
            }

            TargetInfo target;
            if (!TryParseRitualTarget(map, parts[4], out target))
            {
                RitualFailure("invalid_ritual_target", line);
                return;
            }
            RitualObligation obligation = null;
            if (parts.Length == 7)
            {
                int obligationId;
                if (!Int32.TryParse(parts[6], out obligationId))
                {
                    RitualFailure("invalid_obligation_id", line);
                    return;
                }
                obligation = ritual.activeObligations == null ? null : ritual.activeObligations.FirstOrDefault(item => item != null && item.ID == obligationId && item.StillValid);
                if (obligation == null)
                {
                    RitualFailure("obligation_not_found", line);
                    return;
                }
            }
            else if (ritual.activeObligations != null)
            {
                obligation = ritual.activeObligations.FirstOrDefault(item => item != null && item.StillValid &&
                    (!target.IsValid || !item.FirstValidTarget.IsValid || item.FirstValidTarget.Map == map));
            }
            if (!target.IsValid && obligation != null && obligation.FirstValidTarget.IsValid) target = obligation.FirstValidTarget;
            if (!target.IsValid)
            {
                RitualFailure("target_required_for_ritual", line);
                return;
            }
            if (target.Map != map)
            {
                RitualFailure("target_not_on_map", line);
                return;
            }
            RitualTargetUseReport targetReport = ritual.CanUseTarget(target, obligation);
            if (!targetReport.canUse)
            {
                RitualFailure("target_not_allowed reason=" + Clean(targetReport.failReason), line);
                return;
            }

            Pawn organizer = null;
            if (parts.Length >= 6 && !parts[5].Equals("-", StringComparison.OrdinalIgnoreCase) && !parts[5].Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                int organizerId;
                if (!Int32.TryParse(parts[5], out organizerId))
                {
                    RitualFailure("invalid_organizer_id", line);
                    return;
                }
                organizer = map.mapPawns.FreeColonistsAndPrisonersSpawned.FirstOrDefault(pawn => pawn.thingIDNumber == organizerId);
                if (organizer == null || !IsAIPawn(organizer))
                {
                    RitualFailure("organizer_is_not_ai_pawn", line);
                    return;
                }
            }

            string canStart = ritual.behavior.CanStartRitualNow(target, ritual, organizer, null);
            if (!canStart.NullOrEmpty())
            {
                RitualFailure("cannot_start reason=" + Clean(canStart), line);
                return;
            }
            List<Pawn> aiPawns = map.mapPawns.FreeColonistsAndPrisonersSpawned.Where(IsAIPawn).ToList();
            if (aiPawns.Count == 0)
            {
                RitualFailure("no_ai_participants", line);
                return;
            }
            RitualRoleAssignments assignments = new RitualRoleAssignments(ritual, target);
            assignments.Setup(aiPawns, new List<Pawn>(), null, null, organizer);
            assignments.FillPawns(null, target);
            foreach (RitualRole role in ritual.behavior.def.roles.Where(item => item.required && !item.substitutable))
            {
                if (!assignments.AssignedPawns(role).Any())
                {
                    RitualFailure("required_role_missing role=" + Clean(role.id), line);
                    return;
                }
            }
            try
            {
                ritual.behavior.TryExecuteOn(target, organizer, ritual, obligation, assignments, true);
                Log("[执行] 已启动文化仪式 " + ritual.Label + "，AI参与者=" + assignments.Participants.Count + "。");
                Result("OK ritual_started=" + ritual.Id + " target=" + DescribeRitualTarget(target));
            }
            catch (Exception ex)
            {
                RitualFailure("execute_error=" + Clean(ex.Message), line);
            }
        }

        private static bool TryParseRitualTarget(Map map, string value, out TargetInfo target)
        {
            target = TargetInfo.Invalid;
            if (value.Equals("none", StringComparison.OrdinalIgnoreCase) || value == "-") return true;
            int thingId;
            if (Int32.TryParse(value, out thingId))
            {
                Thing thing = map.listerThings.AllThings.FirstOrDefault(item => item.thingIDNumber == thingId && item.Spawned);
                if (thing == null) return false;
                target = new TargetInfo(thing);
                return true;
            }
            string[] coordinates = value.Split(':');
            int x, z;
            if (coordinates.Length != 2 || !Int32.TryParse(coordinates[0], out x) || !Int32.TryParse(coordinates[1], out z)) return false;
            IntVec3 cell = new IntVec3(x, 0, z);
            if (!cell.InBounds(map)) return false;
            target = new TargetInfo(cell, map);
            return true;
        }

        private static string DescribeRitualTarget(TargetInfo target)
        {
            if (!target.IsValid) return "none";
            return target.Thing == null ? "cell:" + target.Cell.x + ":" + target.Cell.z : "thing:" + target.Thing.thingIDNumber;
        }

        private static void RitualFailure(string reason, string line)
        {
            Log("[拒绝] 文化仪式命令未执行：" + reason + "；" + line);
            Result("FAIL ritual_not_executed reason=" + reason + " line=" + Clean(line));
            AICoopGameComponent.Current?.AddWorkMessage("AI", "文化仪式未执行：" + reason + "。需要玩家处理或调整目标。");
        }

        private static void ConfigureWork(Map map, string[] parts, string line)
        {
            Pawn pawn;
            int pawnId;
            if (parts.Length != 5 || !int.TryParse(parts[3], out pawnId) ||
                !TryAIPawn(map, pawnId, out pawn) || pawn.workSettings == null)
            {
                Log("[拒绝] 格式：C mapID work AI殖民者ID 全部优先级（逗号分隔）。");
                return;
            }
            var works = DefDatabase<WorkTypeDef>.AllDefsListForReading.OrderByDescending(w => w.naturalPriority)
                .ThenBy(w => w.defName, StringComparer.Ordinal).ToList();
            string[] values = parts[4].Trim('(', ')').Split(',');
            int[] priorities = new int[works.Count];
            if (values.Length != works.Count) { Log("[拒绝] 必须按 WORK_PRIORITY_ORDER 提供全部 " + works.Count + " 项优先级。"); return; }
            for (int i = 0; i < works.Count; i++)
            {
                string reason;
                if (!int.TryParse(values[i], out priorities[i]) || priorities[i] < 0 || priorities[i] > 4 ||
                    (pawn.WorkTypeIsDisabled(works[i]) && priorities[i] != 0))
                { Log("[拒绝] 无效或禁用的工作优先级：" + works[i].defName); return; }
                if (!AICoopSkillRules.CheckPriority(pawn, works[i], priorities[i], out reason))
                { Log("[拒绝] " + reason); return; }
            }
            Find.PlaySettings.useWorkPriorities = true;
            pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
            for (int i = 0; i < works.Count; i++)
                if (!pawn.WorkTypeIsDisabled(works[i])) pawn.workSettings.SetPriority(works[i], priorities[i]);
            Log("[执行] 已一次设置 " + pawn.LabelShort + " 的全部工作优先级。");
        }

        private static void ConfigureTimetable(Map map, string[] parts, string line)
        {
            if (parts.Length != 4)
            {
                Log("[拒绝] C timetable 格式：C mapID timetable Work|Joy|Sleep|Anything。");
                return;
            }
            TimeAssignmentDef assignment = DefDatabase<TimeAssignmentDef>.GetNamedSilentFail(parts[3]);
            if (assignment == null)
            {
                Log("[拒绝] 作息类型不存在：" + parts[3]);
                return;
            }
            int changed = 0;
            foreach (Pawn pawn in AIPawns(map).Where(pawn => pawn.timetable != null))
            {
                for (int hour = 0; hour < 24; hour++) pawn.timetable.SetAssignment(hour, assignment);
                changed++;
            }
            Log("[执行] 已将 " + changed + " 名 AI 殖民者全天作息设为 " + assignment.defName + "。");
        }

        private static void ConfigureMedicalCare(Map map, string[] parts, string line)
        {
            int pawnId;
            MedicalCareCategory care;
            Pawn pawn;
            if (parts.Length != 5 || !Int32.TryParse(parts[3], out pawnId) || !Enum.TryParse(parts[4], true, out care) ||
                !TryAIPawn(map, pawnId, out pawn) || pawn.playerSettings == null)
            {
                Log("[拒绝] C medical 格式：C mapID medical AI殖民者ID NoMeds|HerbalOrWorse|NormalOrWorse|IndustrialOrWorse|GlitterworldOrWorse。");
                return;
            }
            pawn.playerSettings.medCare = care;
            Log("[执行] 已将 " + pawn.LabelShort + " 的医疗等级设为 " + care + "。");
        }

        private static void ConfigurePolicy(Map map, string[] parts, string line)
        {
            int pawnId, policyIndex;
            Pawn pawn;
            if (parts.Length != 6 || !Int32.TryParse(parts[3], out pawnId) || !Int32.TryParse(parts[5], out policyIndex) || !TryAIPawn(map, pawnId, out pawn))
            {
                Log("[拒绝] C policy 格式：C mapID policy AI殖民者ID outfit|drug|food 方案序号。");
                return;
            }
            if (parts[4].Equals("outfit", StringComparison.OrdinalIgnoreCase) && pawn.outfits != null && policyIndex >= 0 && policyIndex < Current.Game.outfitDatabase.AllOutfits.Count)
            {
                pawn.outfits.CurrentApparelPolicy = Current.Game.outfitDatabase.AllOutfits[policyIndex];
            }
            else if (parts[4].Equals("drug", StringComparison.OrdinalIgnoreCase) && pawn.drugs != null && policyIndex >= 0 && policyIndex < Current.Game.drugPolicyDatabase.AllPolicies.Count)
            {
                pawn.drugs.CurrentPolicy = Current.Game.drugPolicyDatabase.AllPolicies[policyIndex];
            }
            else if (parts[4].Equals("food", StringComparison.OrdinalIgnoreCase) && pawn.foodRestriction != null && policyIndex >= 0 && policyIndex < Current.Game.foodRestrictionDatabase.AllFoodRestrictions.Count)
            {
                pawn.foodRestriction.CurrentFoodPolicy = Current.Game.foodRestrictionDatabase.AllFoodRestrictions[policyIndex];
            }
            else
            {
                Log("[拒绝] 方案类型、方案序号或殖民者状态无效：" + line);
                return;
            }
            Log("[执行] 已将 " + pawn.LabelShort + " 的 " + parts[4] + " 方案设为序号 " + policyIndex + "。");
        }

        private static void ConfigureGear(Map map, string[] parts, string line)
        {
            int pawnId, thingId;
            Pawn pawn;
            if ((parts.Length != 6 && parts.Length != 7) || !Int32.TryParse(parts[3], out pawnId) || !Int32.TryParse(parts[4], out thingId) || !TryAIPawn(map, pawnId, out pawn))
            {
                Log("[拒绝] C gear 格式：C mapID gear AI殖民者ID 物品ID equip|wear|drop|drop_inventory|carry|use [目标ID]。");
                return;
            }
            Thing item = map.listerThings.AllThings.FirstOrDefault(thing => thing.thingIDNumber == thingId);
            if (item == null && pawn.equipment != null) item = pawn.equipment.AllEquipmentListForReading.FirstOrDefault(thing => thing.thingIDNumber == thingId);
            if (item == null && pawn.inventory != null) item = pawn.inventory.GetDirectlyHeldThings().FirstOrDefault(thing => thing.thingIDNumber == thingId);
            string mode = parts[5].ToLowerInvariant();
            if (mode == "drop" && item is ThingWithComps equipped && pawn.equipment != null && pawn.equipment.Contains(equipped))
            {
                ThingWithComps resulting;
                if (pawn.equipment.TryDropEquipment(equipped, out resulting, pawn.Position, false)) Log("[执行] 已让 " + pawn.LabelShort + " 丢下装备 " + item.LabelShort + "。");
                else Log("[拒绝] " + pawn.LabelShort + " 无法丢下装备。");
                return;
            }
            if (mode == "drop_inventory" && item != null && pawn.inventory != null && pawn.inventory.Contains(item))
            {
                pawn.inventory.DropCount(item.def, Math.Max(1, item.stackCount), true, true);
                Log("[执行] 已让 " + pawn.LabelShort + " 丢下携带物品 " + item.LabelShort + "。");
                return;
            }
            if (mode == "carry" && item != null && item.Spawned)
            {
                if (pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.TakeInventory, item))) Log("[执行] 已让 " + pawn.LabelShort + " 携带 " + item.LabelShort + "。");
                else Log("[拒绝] " + pawn.LabelShort + " 未接受携带命令。");
                return;
            }
            if (mode == "use" && item != null)
            {
                CompUsable usable = item.TryGetComp<CompUsable>();
                if (usable == null)
                {
                    CompEquippable equippable = item.TryGetComp<CompEquippable>();
                    Verb verb = equippable == null ? null : equippable.PrimaryVerb;
                    Thing targetForVerb = parts.Length == 7 ? map.listerThings.AllThings.FirstOrDefault(thing => thing.thingIDNumber.ToString() == parts[6]) : pawn;
                    if (verb == null || targetForVerb == null || !verb.IsStillUsableBy(pawn) || !verb.CanHitTarget(new LocalTargetInfo(targetForVerb)) || !verb.TryStartCastOn(new LocalTargetInfo(targetForVerb), false, false, false, false))
                    {
                        Log("[拒绝] 物品没有可主动使用的功能：" + item.LabelShort);
                        return;
                    }
                    Log("[执行] 已让 " + pawn.LabelShort + " 使用装备能力 " + item.LabelShort + "。");
                    return;
                }
                Thing target = parts.Length == 7 ? map.listerThings.AllThings.FirstOrDefault(thing => thing.thingIDNumber.ToString() == parts[6]) : pawn;
                if (target == null || !usable.CanBeUsedBy(pawn, true, false))
                {
                    Log("[拒绝] 物品当前不能由该殖民者使用：" + line);
                    return;
                }
                usable.TryStartUseJob(pawn, new LocalTargetInfo(target), true);
                Log("[执行] 已让 " + pawn.LabelShort + " 使用 " + item.LabelShort + "。");
                return;
            }
            JobDef jobDef = mode == "equip" && item != null && item.def.IsWeapon ? JobDefOf.Equip :
                mode == "wear" && item is Apparel ? JobDefOf.Wear : null;
            if (jobDef == null)
            {
                Log("[拒绝] 装备目标、物品位置或操作类型无效：" + line);
                return;
            }
            string skillReason;
            if (!AICoopSkillRules.CanEquip(pawn, item, out skillReason))
            {
                Log("[拒绝] " + skillReason);
                return;
            }
            if (pawn.jobs.TryTakeOrderedJob(JobMaker.MakeJob(jobDef, item)))
            {
                if (mode == "wear" && item is Apparel) AICoopGameComponent.Current.RegisterNonForcedApparel(pawn, (Apparel)item);
                Log("[执行] 已让 " + pawn.LabelShort + " " + parts[5] + " " + item.LabelShort + "。");
            }
            else Log("[拒绝] " + pawn.LabelShort + " 未接受装备命令。");
        }

        private static void ConfigureRoof(Map map, string[] parts, string line)
        {
            int x1, z1, x2, z2;
            if (parts.Length != 8 || !Rectangle(parts, 3, out x1, out z1, out x2, out z2) || (parts[7] != "build" && parts[7] != "remove"))
            {
                Log("[拒绝] C roof 格式：C mapID roof x1 z1 x2 z2 build|remove。");
                return;
            }
            int changed = 0;
            for (int x = x1; x <= x2; x++) for (int z = z1; z <= z2; z++)
            {
                IntVec3 cell = new IntVec3(x, 0, z);
                if (!cell.InBounds(map)) continue;
                map.roofGrid.SetRoof(cell, parts[7] == "build" ? RoofDefOf.RoofConstructed : null);
                changed++;
            }
            Log("[执行] 已对 " + changed + " 格执行" + (parts[7] == "build" ? "建造屋顶" : "移除屋顶") + "。");
        }

        private static void ConfigureHome(Map map, string[] parts, string line)
        {
            int x1, z1, x2, z2;
            bool enabled;
            if (parts.Length != 8 || !Rectangle(parts, 3, out x1, out z1, out x2, out z2) || !TryBool(parts[7], out enabled))
            {
                Log("[拒绝] C home 格式：C mapID home x1 z1 x2 z2 0|1。");
                return;
            }
            int changed = 0;
            for (int x = x1; x <= x2; x++) for (int z = z1; z <= z2; z++)
            {
                IntVec3 cell = new IntVec3(x, 0, z);
                if (!cell.InBounds(map)) continue;
                if (SetAreaCell == null)
                {
                    Log("[拒绝] 当前版本未公开居住区写入接口。");
                    return;
                }
                SetAreaCell.Invoke(map.areaManager.Home, new object[] { cell, enabled });
                changed++;
            }
            Log("[执行] 已将 " + changed + " 格" + (enabled ? "加入" : "移出") + "居住区。");
        }

        private static void ConfigureStoragePriority(Map map, string[] parts, string line)
        {
            Zone_Stockpile zone;
            StoragePriority priority;
            if (parts.Length != 5 || !TryStockpile(map, parts[3], out zone) || !Enum.TryParse(parts[4], true, out priority))
            {
                Log("[拒绝] C storePriority 格式：C mapID storePriority zoneID Low|Normal|Preferred|Important|Critical。");
                return;
            }
            zone.settings.Priority = priority;
            Log("[执行] 已将仓储区 " + zone.ID + " 优先级设为 " + priority + "。");
        }

        private static void ConfigureStorageFilter(Map map, string[] parts, string line)
        {
            Zone_Stockpile zone;
            bool allow;
            ThingDef def;
            if (parts.Length != 6 || !TryStockpile(map, parts[3], out zone) || !TryBool(parts[5], out allow) ||
                (def = DefDatabase<ThingDef>.GetNamedSilentFail(parts[4])) == null)
            {
                Log("[拒绝] C storeAllow 格式：C mapID storeAllow zoneID ThingDef 0|1。");
                return;
            }
            zone.settings.filter.SetAllow(def, allow);
            Log("[执行] 仓储区 " + zone.ID + (allow ? "允许" : "禁止") + " " + def.defName + "。");
        }

        private static void ClearStorageFilter(Map map, string[] parts, string line)
        {
            Zone_Stockpile zone;
            if (parts.Length != 4 || !TryStockpile(map, parts[3], out zone))
            {
                Log("[拒绝] C storeClear 格式：C mapID storeClear zoneID。");
                return;
            }
            zone.settings.filter.SetDisallowAll();
            Log("[执行] 已清空仓储区 " + zone.ID + " 的物品许可。");
        }

        private static void DeleteTemporaryDumpingStockpile(Map map, string[] parts, string line)
        {
            Zone_Stockpile zone;
            if (parts.Length != 4 || !TryStockpile(map, parts[3], out zone))
            {
                Log("[拒绝] C storeDelete 格式：C mapID storeDelete zoneID。");
                return;
            }
            if (!AICoopActionExecutor.IsTemporaryDumpingStockpile(zone))
            {
                Log("[拒绝] 只能删除 AI 创建的临时垃圾储存区，未删除仓储区 " + zone.ID + "。");
                return;
            }
            int pending = AICoopActionExecutor.PendingStoneChunkHaulCount(map);
            if (pending > 0)
            {
                Log("[拒绝] 仍有 " + pending + " 个石块正在等待或执行搬运，暂不能删除临时垃圾储存区 " + zone.ID + "。");
                return;
            }
            int zoneId = zone.ID;
            zone.Delete();
            Log("[执行] 已删除完成石块搬运后的临时垃圾储存区 " + zoneId + "。");
        }

        private static void ConfigurePrisoner(Map map, string[] parts, string line)
        {
            int pawnId;
            Pawn prisoner;
            if (parts.Length < 5 || parts.Length > 7 || !Int32.TryParse(parts[3], out pawnId) ||
                (prisoner = map.mapPawns.AllPawnsSpawned.FirstOrDefault(pawn => pawn.thingIDNumber == pawnId)) == null || prisoner.guest == null || !prisoner.IsPrisoner)
            {
                Log("[拒绝] C prisoner 需要当前地图的囚犯 ID 和 recruit|release|interaction。");
                return;
            }
            if (parts[4].Equals("recruit", StringComparison.OrdinalIgnoreCase))
            {
                prisoner.guest.SetExclusiveInteraction(DefDatabase<PrisonerInteractionModeDef>.GetNamed("Recruit"));
                Log("[执行] 已将 " + prisoner.LabelShort + " 设为招募。");
            }
            else if (parts[4].Equals("release", StringComparison.OrdinalIgnoreCase))
            {
                prisoner.guest.Released = true;
                Log("[执行] 已将 " + prisoner.LabelShort + " 设为释放。");
            }
            else if (parts[4].Equals("interaction", StringComparison.OrdinalIgnoreCase) && parts.Length == 6)
            {
                PrisonerInteractionModeDef mode = DefDatabase<PrisonerInteractionModeDef>.GetNamedSilentFail(parts[5]);
                if (mode == null) Log("[拒绝] 囚犯互动模式不存在：" + parts[5]);
                else
                {
                    prisoner.guest.SetExclusiveInteraction(mode);
                    Log("[执行] 已将 " + prisoner.LabelShort + " 的囚犯互动设为 " + mode.defName + "。");
                }
            }
            else Log("[拒绝] C prisoner 仅支持 recruit 或 release。");
        }

        private static void ConfigureBed(Map map, string[] parts, string line)
        {
            int bedId;
            Building_Bed bed = null;
            if (parts.Length != 5 || !Int32.TryParse(parts[3], out bedId) ||
                (bed = map.listerThings.AllThings.FirstOrDefault(thing => thing.thingIDNumber == bedId) as Building_Bed) == null)
            {
                Log("[拒绝] C bed 格式：C mapID bed 床位ID colonist|prisoner|slave|medical|medical_colonist|medical_prisoner|medical_slave|normal。");
                return;
            }

            string mode = parts[4].ToLowerInvariant();
            BedOwnerType ownerType = bed.ForOwnerType;
            bool medical = bed.Medical;
            switch (mode)
            {
                case "colonist": ownerType = BedOwnerType.Colonist; medical = false; break;
                case "prisoner": ownerType = BedOwnerType.Prisoner; medical = false; break;
                case "slave": ownerType = BedOwnerType.Slave; medical = false; break;
                case "medical": medical = true; break;
                case "medical_colonist": ownerType = BedOwnerType.Colonist; medical = true; break;
                case "medical_prisoner": ownerType = BedOwnerType.Prisoner; medical = true; break;
                case "medical_slave": ownerType = BedOwnerType.Slave; medical = true; break;
                case "normal": ownerType = BedOwnerType.Colonist; medical = false; break;
                default:
                    Log("[拒绝] C bed 类型无效：" + line);
                    return;
            }

            if (bed.ForOwnerType != ownerType) bed.ForOwnerType = ownerType;
            bed.Medical = medical;
            if (NotifyBedTypeChanged != null) NotifyBedTypeChanged.Invoke(bed, null);
            Log("[执行] 已将床位 " + bedId + " 设置为 " + BedModeLabel(ownerType, medical) + "。");
        }

        private static void ConfigureDoor(Map map, string[] parts, string line)
        {
            int doorId;
            if (parts.Length != 5 || !Int32.TryParse(parts[3], out doorId))
            {
                Log("[拒绝] C door 格式：C mapID door 门ID open|close|hold_open。");
                return;
            }
            Building_Door door = map.listerThings.AllThings.FirstOrDefault(thing => thing.thingIDNumber == doorId) as Building_Door;
            string mode = parts[4].ToLowerInvariant();
            if (door == null || (mode != "open" && mode != "close" && mode != "hold_open"))
            {
                Log("[拒绝] 门 ID 或门状态无效：" + line);
                return;
            }
            bool open = mode != "close";
            bool changed = SetDoorMember(door, "Open", open) | SetDoorMember(door, "open", open) | SetDoorMember(door, "openInt", open);
            if (mode == "hold_open") changed = SetDoorMember(door, "HoldOpen", true) | SetDoorMember(door, "holdOpen", true) | SetDoorMember(door, "holdOpenInt", true) | changed;
            else if (mode == "close") changed = SetDoorMember(door, "HoldOpen", false) | SetDoorMember(door, "holdOpen", false) | SetDoorMember(door, "holdOpenInt", false) | changed;
            if (!changed)
            {
                Log("[拒绝] 当前版本未找到可写入的门开关接口：" + doorId);
                return;
            }
            try
            {
                MethodInfo notify = typeof(Building_Door).GetMethod("CheckClearReachabilityCacheBecauseOpenedOrClosed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (notify != null) notify.Invoke(door, null);
            }
            catch
            {
            }
            Log("[执行] 已将门 " + doorId + " 设置为" + (mode == "hold_open" ? "保持敞开" : (open ? "打开" : "关闭")) + "。");
        }

        private static bool SetDoorMember(Building_Door door, string name, object value)
        {
            try
            {
                PropertyInfo property = typeof(Building_Door).GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                if (property != null && property.CanWrite && property.PropertyType.IsAssignableFrom(value.GetType()))
                {
                    property.SetValue(door, value, null);
                    return true;
                }
                FieldInfo field = typeof(Building_Door).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                if (field != null && field.FieldType.IsAssignableFrom(value.GetType()))
                {
                    field.SetValue(door, value);
                    return true;
                }
            }
            catch
            {
            }
            return false;
        }

        private static string BedModeLabel(BedOwnerType ownerType, bool medical)
        {
            if (medical)
            {
                switch (ownerType)
                {
                    case BedOwnerType.Prisoner: return "囚犯医疗床";
                    case BedOwnerType.Slave: return "奴隶医疗床";
                    default: return "殖民者医疗床";
                }
            }
            switch (ownerType)
            {
                case BedOwnerType.Prisoner: return "囚犯床";
                case BedOwnerType.Slave: return "奴隶床";
                default: return "殖民者床";
            }
        }

        private static void QueryDeepResource(Map map, string[] parts, string line)
        {
            int x, z;
            if (parts.Length != 5 || !Int32.TryParse(parts[3], out x) || !Int32.TryParse(parts[4], out z))
            {
                Log("[拒绝] C deep 格式：C mapID deep x z。");
                return;
            }
            IntVec3 cell = new IntVec3(x, 0, z);
            if (!cell.InBounds(map))
            {
                Log("[拒绝] 深层资源坐标不在地图内。");
                return;
            }
            ThingDef resource = map.deepResourceGrid.ThingDefAt(cell);
            int count = map.deepResourceGrid.CountAt(cell);
            AICoopGameComponent.Current.AddCommandResult("DEEP map=" + map.uniqueID + " cell=" + x + ":" + z + " resource=" + (resource == null ? "-" : resource.defName) + " count=" + count);
            Log("[执行] 深层资源（" + x + "，" + z + "）=" + (resource == null ? "无" : resource.defName + " x" + count) + "。");
        }

        private static void ConfigureAutomaticWork(Map map, string[] parts, string line)
        {
            if (parts.Length != 3)
            {
                Log("[拒绝] C autoWork 格式：C mapID autoWork。");
                return;
            }
            int changed = 0;
            foreach (WorkTypeDef work in DefDatabase<WorkTypeDef>.AllDefsListForReading)
            {
                SkillDef skill = AICoopSkillRules.RelevantSkill(work);
                foreach (Pawn pawn in AIPawns(map))
                {
                    if (pawn.workSettings == null || pawn.WorkTypeIsDisabled(work)) continue;
                    int level = skill == null ? 0 : AICoopSkillRules.SkillLevel(pawn, skill);
                    int minimum = AICoopSkillRules.MinimumSkill(work);
                    SkillRecord skillRecord = skill == null || pawn.skills == null ? null : pawn.skills.GetSkill(skill);
                    int priority = skill == null ? 3 : (level >= minimum ? (skillRecord != null && skillRecord.passion >= Passion.Major ? 1 : 2) : 3);
                    pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
                    if (pawn.workSettings.GetPriority(work) != priority)
                    {
                        pawn.workSettings.SetPriority(work, priority);
                        changed++;
                    }
                }
            }
            Log("[执行] 已根据技能和热情自动生成 AI 分工预设，调整 " + changed + " 项工作优先级。");
        }

        private static void ConfigureHourlyTimetable(Map map, string[] parts, string line)
        {
            int pawnId, start, end;
            TimeAssignmentDef assignment;
            if (parts.Length != 7 || !Int32.TryParse(parts[3], out pawnId) || !Int32.TryParse(parts[4], out start) ||
                !Int32.TryParse(parts[5], out end) || start < 0 || start > 23 || end < 0 || end > 23 ||
                (assignment = DefDatabase<TimeAssignmentDef>.GetNamedSilentFail(parts[6])) == null)
            {
                Log("[拒绝] C timetableHour 格式：C mapID timetableHour AI殖民者ID 起始小时 结束小时 Work|Joy|Sleep|Anything。");
                return;
            }
            List<Pawn> pawns = pawnId < 0 ? AIPawns(map).ToList() : AIPawns(map).Where(pawn => pawn.thingIDNumber == pawnId).ToList();
            if (pawns.Count == 0)
            {
                Log("[拒绝] 没有找到可设置作息的 AI 殖民者：" + pawnId);
                return;
            }
            int count = 0;
            foreach (Pawn pawn in pawns)
            {
                if (pawn.timetable == null) continue;
                int hour = start;
                while (true)
                {
                    pawn.timetable.SetAssignment(hour, assignment);
                    count++;
                    if (hour == end) break;
                    hour = (hour + 1) % 24;
                }
            }
            Log("[执行] 已设置 " + count + " 个 AI 小时作息时段。");
        }

        private static void ConfigureSelfTend(Map map, string[] parts, string line)
        {
            bool value;
            if (parts.Length != 5 || !TryBool(parts[4], out value))
            {
                Log("[拒绝] C selfTend 格式：C mapID selfTend AI殖民者ID 0|1；ID 可用 -1 表示全部。");
                return;
            }
            int id;
            if (!Int32.TryParse(parts[3], out id))
            {
                Log("[拒绝] AI 殖民者 ID 无效：" + line);
                return;
            }
            int count = 0;
            foreach (Pawn pawn in AIPawns(map).Where(item => id < 0 || item.thingIDNumber == id))
            {
                if (pawn.playerSettings == null) continue;
                pawn.playerSettings.selfTend = value;
                count++;
            }
            Log("[执行] 已将 " + count + " 名 AI 的自我治疗设为 " + (value ? "允许" : "禁止") + "。");
        }

        private static void ConfigureHostility(Map map, string[] parts, string line)
        {
            int pawnId;
            HostilityResponseMode mode;
            Pawn pawn;
            if (parts.Length != 5 || !Int32.TryParse(parts[3], out pawnId) || !Enum.TryParse(parts[4], true, out mode) ||
                !TryAIPawn(map, pawnId, out pawn) || pawn.playerSettings == null)
            {
                Log("[拒绝] C hostility 格式：C mapID hostility AI殖民者ID Ignore|Attack|Flee。");
                return;
            }
            pawn.playerSettings.hostilityResponse = mode;
            Log("[执行] 已将 " + pawn.LabelShort + " 的敌对反应设为 " + mode + "。");
        }

        private static void ConfigureAllowedArea(Map map, string[] parts, string line)
        {
            int pawnId;
            Pawn pawn;
            if (parts.Length != 5 || !Int32.TryParse(parts[3], out pawnId) || !TryAIPawn(map, pawnId, out pawn) || pawn.playerSettings == null)
            {
                Log("[拒绝] C area 格式：C mapID area AI殖民者ID areaID|none。");
                return;
            }
            if (parts[4].Equals("none", StringComparison.OrdinalIgnoreCase))
            {
                pawn.playerSettings.AreaRestrictionInPawnCurrentMap = null;
                Log("[执行] 已解除 " + pawn.LabelShort + " 的允许活动区限制。");
                return;
            }
            int areaId;
            Area area = Int32.TryParse(parts[4], out areaId) ? map.areaManager.AllAreas.FirstOrDefault(item => item.ID == areaId) : null;
            if (area == null || !area.AssignableAsAllowed())
            {
                Log("[拒绝] 允许活动区不存在或不可分配：" + parts[4]);
                return;
            }
            pawn.playerSettings.AreaRestrictionInPawnCurrentMap = area;
            Log("[执行] 已将 " + pawn.LabelShort + " 限制在活动区 " + area.Label + "。");
        }

        private static void ConfigureNewAllowedArea(Map map, string[] parts, string line)
        {
            int pawnId, x1, z1, x2, z2;
            if (parts.Length != 9 || !Int32.TryParse(parts[3], out pawnId) || !Int32.TryParse(parts[4], out x1) ||
                !Int32.TryParse(parts[5], out z1) || !Int32.TryParse(parts[6], out x2) || !Int32.TryParse(parts[7], out z2))
            {
                Log("[拒绝] C area_new 格式：C mapID area_new AI殖民者ID x1 z1 x2 z2 名称（ID 可用 -1）。");
                return;
            }
            MethodInfo make = typeof(AreaManager).GetMethod("TryMakeNewAllowed", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (make == null || map.areaManager == null || !map.areaManager.CanMakeNewAllowed())
            {
                Log("[拒绝] 当前地图不能创建新的允许活动区。");
                return;
            }
            object[] args = { null };
            if (!(bool)make.Invoke(map.areaManager, args) || !(args[0] is Area area))
            {
                Log("[拒绝] 创建允许活动区失败。");
                return;
            }
            area.RenamableLabel = parts[8].Replace('_', ' ');
            int minX = Math.Min(x1, x2), maxX = Math.Max(x1, x2), minZ = Math.Min(z1, z2), maxZ = Math.Max(z1, z2);
            int cells = 0;
            for (int x = minX; x <= maxX; x++) for (int z = minZ; z <= maxZ; z++)
            {
                IntVec3 cell = new IntVec3(x, 0, z);
                if (cell.InBounds(map))
                {
                    SetAreaCell.Invoke(area, new object[] { cell, true });
                    cells++;
                }
            }
            foreach (Pawn pawn in AIPawns(map).Where(item => pawnId < 0 || item.thingIDNumber == pawnId)) pawn.playerSettings.AreaRestrictionInPawnCurrentMap = area;
            Log("[执行] 已创建允许活动区 " + area.Label + "（" + cells + " 格）。");
        }

        private static void CreatePolicy(Map map, string[] parts, string line)
        {
            if (parts.Length != 5)
            {
                Log("[拒绝] C policyCreate 格式：C mapID policyCreate outfit|drug|food 名称。");
                return;
            }
            string kind = parts[3].ToLowerInvariant();
            string label = parts[4].Replace('_', ' ');
            Policy policy = null;
            if (kind == "outfit") policy = Current.Game.outfitDatabase.MakeNewOutfit();
            else if (kind == "drug") policy = Current.Game.drugPolicyDatabase.MakeNewDrugPolicy();
            else if (kind == "food") policy = Current.Game.foodRestrictionDatabase.MakeNewFoodRestriction();
            if (policy == null)
            {
                Log("[拒绝] 方案类型无效：" + parts[3]);
                return;
            }
            policy.RenamableLabel = label;
            Log("[执行] 已创建 " + kind + " 方案“" + label + "”。");
        }

        private static void EditPolicy(Map map, string[] parts, string line)
        {
            if (parts.Length != 7)
            {
                Log("[拒绝] C policyEdit 格式：C mapID policyEdit outfit|food|drug 方案序号 ThingDef 0|1。");
                return;
            }
            int index;
            bool allow;
            if (!Int32.TryParse(parts[4], out index) || !TryBool(parts[6], out allow))
            {
                Log("[拒绝] 方案序号或允许值无效：" + line);
                return;
            }
            ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(parts[5]);
            if (def == null)
            {
                Log("[拒绝] 物品不存在：" + parts[5]);
                return;
            }
            string kind = parts[3].ToLowerInvariant();
            ThingFilter filter = null;
            DrugPolicyEntry drugEntry = null;
            if (kind == "outfit" && index >= 0 && index < Current.Game.outfitDatabase.AllOutfits.Count) filter = Current.Game.outfitDatabase.AllOutfits[index].filter;
            else if (kind == "food" && index >= 0 && index < Current.Game.foodRestrictionDatabase.AllFoodRestrictions.Count) filter = Current.Game.foodRestrictionDatabase.AllFoodRestrictions[index].filter;
            else if (kind == "drug" && index >= 0 && index < Current.Game.drugPolicyDatabase.AllPolicies.Count)
            {
                DrugPolicy policy = Current.Game.drugPolicyDatabase.AllPolicies[index];
                MethodInfo init = typeof(DrugPolicy).GetMethod("InitializeIfNeeded", BindingFlags.Instance | BindingFlags.NonPublic);
                if (init != null) init.Invoke(policy, new object[] { });
                try { drugEntry = policy[def]; } catch { }
                if (drugEntry != null) drugEntry.allowedForJoy = allow;
            }
            else
            {
                Log("[拒绝] 方案类型或序号无效：" + line);
                return;
            }
            if (filter == null && drugEntry == null)
            {
                Log("[拒绝] 方案无法编辑：" + line);
                return;
            }
            if (filter != null) filter.SetAllow(def, allow);
            Log("[执行] 已更新 " + kind + " 方案 " + index + " 对 " + def.defName + " 的许可。");
        }

        private static void ConfigureStorageCategory(Map map, string[] parts, string line)
        {
            Zone_Stockpile zone;
            Building_Storage building;
            bool allow;
            if (parts.Length != 6 || !TryBool(parts[5], out allow))
            {
                Log("[拒绝] C storeCategory 格式：C mapID storeCategory zoneID food|weapons|apparel|medicine|raw|chunks|corpses|plants 0|1。");
                return;
            }
            StorageSettings settings = null;
            string targetLabel = parts[3];
            if (TryStockpile(map, parts[3], out zone)) settings = zone.settings;
            else if (TryStorageBuilding(map, parts[3], out building))
            {
                settings = building.GetStoreSettings();
                targetLabel = building.LabelShort;
            }
            if (settings == null)
            {
                Log("[拒绝] 找不到仓储区或储物建筑：" + parts[3]);
                return;
            }
            int changed = 0;
            string category = parts[4].ToLowerInvariant();
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefsListForReading)
            {
                if (!MatchesStorageCategory(def, category)) continue;
                settings.filter.SetAllow(def, allow);
                changed++;
            }
            Log("[执行] " + targetLabel + " 已" + (allow ? "允许" : "禁止") + "类别 " + category + "（" + changed + " 项）。");
        }

        private static bool MatchesStorageCategory(ThingDef def, string category)
        {
            if (def == null) return false;
            switch (category)
            {
                case "food": return def.IsFoodDispenser || (def.ingestible != null && def.ingestible.foodType != FoodTypeFlags.None);
                case "weapons": return def.IsWeapon;
                case "apparel": return def.IsApparel;
                case "medicine": return def.IsMedicine;
                case "raw": return def.category == ThingCategory.Item && def.stuffProps != null;
                case "chunks": return def.building != null && def.building.isNaturalRock && def.building.mineableThing != null;
                case "corpses": return def.IsCorpse;
                case "plants": return def.plant != null || def.IsPlant;
                default: return false;
            }
        }

        private static void ConfigureDedicatedStorage(Map map, string[] parts, string line)
        {
            Zone_Stockpile zone;
            ThingDef def;
            if (parts.Length != 5 || !TryStockpile(map, parts[3], out zone) || (def = DefDatabase<ThingDef>.GetNamedSilentFail(parts[4])) == null)
            {
                Building_Storage building;
                if (parts.Length != 5 || !TryStorageBuilding(map, parts[3], out building) || (def = DefDatabase<ThingDef>.GetNamedSilentFail(parts[4])) == null)
                {
                    Log("[拒绝] C storeDedicated 格式：C mapID storeDedicated zoneID|buildingID ThingDef。");
                    return;
                }
                StorageSettings buildingSettings = building.GetStoreSettings();
                buildingSettings.filter.SetDisallowAll();
                buildingSettings.filter.SetAllow(def, true);
                Log("[执行] 储物建筑 " + building.LabelShort + " 已设为专属存放 " + def.defName + "。");
                return;
            }
            zone.settings.filter.SetDisallowAll();
            zone.settings.filter.SetAllow(def, true);
            Log("[执行] 仓储区 " + zone.ID + " 已设为专属存放 " + def.defName + "。");
        }

        private static void ConfigureHaulRules(Map map, string[] parts, string line)
        {
            int priority;
            if (parts.Length != 5 || !Int32.TryParse(parts[4], out priority) || priority < 0 || priority > 4)
            {
                Log("[拒绝] C haulRules 格式：C mapID haulRules Hauling|Cleaning|Construction 0-4。");
                return;
            }
            WorkTypeDef work = DefDatabase<WorkTypeDef>.GetNamedSilentFail(parts[3]);
            if (work == null)
            {
                Log("[拒绝] 搬运规则工作类型不存在：" + parts[3]);
                return;
            }
            int changed = 0;
            foreach (Pawn pawn in AIPawns(map).Where(item => item.workSettings != null && !item.WorkTypeIsDisabled(work)))
            {
                string reason;
                if (!AICoopSkillRules.CheckPriority(pawn, work, priority, out reason)) continue;
                pawn.workSettings.EnableAndInitializeIfNotAlreadyInitialized();
                pawn.workSettings.SetPriority(work, priority);
                changed++;
            }
            Log("[执行] 已更新 " + changed + " 名 AI 的 " + work.defName + " 自动工作规则。");
        }

        private static void ConfigureTemperature(Map map, string[] parts, string line)
        {
            int buildingId;
            float temperature;
            if (parts.Length != 5 || !Int32.TryParse(parts[3], out buildingId) || !Single.TryParse(parts[4], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out temperature))
            {
                Log("[拒绝] C temp 格式：C mapID temp 温控建筑ID 摄氏温度。");
                return;
            }
            ThingWithComps building = map.listerThings.AllThings.FirstOrDefault(thing => thing.thingIDNumber == buildingId) as ThingWithComps;
            CompTempControl control = building == null ? null : building.TryGetComp<CompTempControl>();
            if (control == null)
            {
                Log("[拒绝] 目标不是可调温控设备：" + buildingId);
                return;
            }
            control.TargetTemperature = Math.Max(-273.15f, Math.Min(1000f, temperature));
            Log("[执行] 已将 " + building.LabelShort + " 目标温度设为 " + control.TargetTemperature.ToString("0.##") + "°C。");
        }

        private static void ConfigureCultureChairs(Map map, string[] parts, string line)
        {
            Ideo ideo = Faction.OfPlayer == null || Faction.OfPlayer.ideos == null
                ? null : Faction.OfPlayer.ideos.PrimaryIdeo;
            if (!ModsConfig.IdeologyActive || ideo == null || ideo.style == null)
            {
                Log("[拒绝] Ideology 未启用或没有可用文化样式，无法执行 cultureChair。");
                Result("FAIL culture_chair_ideology_unavailable=1");
                return;
            }
            if (parts.Length != 3 && parts.Length != 4)
            {
                Log("[拒绝] C cultureChair 格式：C mapID cultureChair all|椅子ID。");
                Result("FAIL culture_chair_format=1");
                return;
            }

            string target = parts.Length == 4 ? parts[3] : "all";
            int targetId = 0;
            bool specific = !target.Equals("all", StringComparison.OrdinalIgnoreCase);
            if (specific && !Int32.TryParse(target, out targetId))
            {
                Log("[拒绝] cultureChair 目标必须是 all 或椅子 ThingID：" + target);
                Result("FAIL culture_chair_target_invalid=1 target=" + target);
                return;
            }

            int changed = 0;
            int skipped = 0;
            bool foundTarget = false;
            foreach (Thing thing in map.listerThings.AllThings.ToList())
            {
                if (thing == null || thing.Destroyed) continue;
                ThingDef thingDef = StyleThingDef(thing);
                if (thingDef == null || thingDef.building == null || !thingDef.building.isSittable) continue;
                if (specific && thing.thingIDNumber != targetId) continue;
                foundTarget = true;

                StyleCategoryPair pair = ideo.style.StyleForThingDef(thingDef);
                if (pair == null || pair.styleDef == null || thing.StyleDef == pair.styleDef)
                {
                    skipped++;
                    continue;
                }
                thing.StyleDef = pair.styleDef;
                changed++;
            }

            if (specific && !foundTarget)
            {
                Log("[拒绝] 找不到指定的椅子或目标不是可换样式的椅子：" + target);
                Result("FAIL culture_chair_not_found=1 target=" + target);
                return;
            }
            Result("OK culture_chairs_changed=" + changed + " skipped=" + skipped);
            Log("[执行] 已将 " + changed + " 把椅子切换为当前文化样式；跳过 " + skipped + " 把。");
        }

        private static ThingDef StyleThingDef(Thing thing)
        {
            if (thing == null) return null;
            Blueprint_Build blueprint = thing as Blueprint_Build;
            if (blueprint != null) return blueprint.EntityToBuild() as ThingDef;
            Frame frame = thing as Frame;
            if (frame != null) return frame.def == null ? null : frame.def.entityDefToBuild as ThingDef;
            return thing.def;
        }

        private static void ConfigureAutoRebuild(Map map, string[] parts, string line)
        {
            bool enabled;
            if (parts.Length != 4 || !TryBool(parts[3], out enabled))
            {
                Log("[拒绝] C rebuild 格式：C mapID rebuild 0|1。");
                return;
            }
            object settings = Find.PlaySettings;
            if (settings == null || !SetMember(settings, "autoRebuild", enabled))
            {
                Log("[拒绝] 当前版本没有自动重建设置接口。");
                return;
            }
            Log("[执行] 已将自动重建设置为 " + (enabled ? "开启" : "关闭") + "。");
        }

        private static void ConfigureAutomaticHomeArea(Map map, string[] parts, string line)
        {
            if (parts.Length != 3 || map.areaManager == null || map.areaManager.Home == null)
            {
                Log("[拒绝] C homeAuto 格式：C mapID homeAuto。");
                return;
            }
            int minX = Int32.MaxValue, minZ = Int32.MaxValue, maxX = Int32.MinValue, maxZ = Int32.MinValue;
            foreach (Building building in map.listerBuildings.allBuildingsColonist)
            {
                minX = Math.Min(minX, building.Position.x);
                minZ = Math.Min(minZ, building.Position.z);
                maxX = Math.Max(maxX, building.Position.x);
                maxZ = Math.Max(maxZ, building.Position.z);
            }
            if (minX == Int32.MaxValue || SetAreaCell == null)
            {
                Log("[拒绝] 当前地图没有殖民地建筑，无法自动生成居住区。");
                return;
            }
            int changed = 0;
            for (int x = minX - 2; x <= maxX + 2; x++) for (int z = minZ - 2; z <= maxZ + 2; z++)
            {
                IntVec3 cell = new IntVec3(x, 0, z);
                if (!cell.InBounds(map)) continue;
                SetAreaCell.Invoke(map.areaManager.Home, new object[] { cell, true });
                changed++;
            }
            Log("[执行] 已根据殖民地建筑自动更新居住区（" + changed + " 格）。");
        }

        private static void ConfigureFloor(Map map, string[] parts, string line)
        {
            Pawn actor = AIPawns(map).FirstOrDefault();
            int x1, z1, x2, z2;
            TerrainDef terrain;
            if (actor == null || (parts.Length != 8 && parts.Length != 9) || (terrain = DefDatabase<TerrainDef>.GetNamedSilentFail(parts[3])) == null ||
                !Int32.TryParse(parts[4], out x1) || !Int32.TryParse(parts[5], out z1) || !Int32.TryParse(parts[6], out x2) || !Int32.TryParse(parts[7], out z2))
            {
                Log("[拒绝] C floor 格式：C mapID floor terrainDefName x1 z1 x2 z2。");
                return;
            }
            ThingDef stuff = parts.Length == 9 ? DefDatabase<ThingDef>.GetNamedSilentFail(parts[8]) : (terrain.MadeFromStuff ? ChooseLocalStuff(terrain, map, null) : null);
            int placed = 0;
            for (int x = Math.Min(x1, x2); x <= Math.Max(x1, x2); x++) for (int z = Math.Min(z1, z2); z <= Math.Max(z1, z2); z++)
            {
                IntVec3 cell = new IntVec3(x, 0, z);
                if (!cell.InBounds(map) || !cell.Walkable(map) || map.terrainGrid.TerrainAt(cell) == terrain) continue;
                if (GenConstruct.CanPlaceBlueprintAt(terrain, cell, Rot4.North, map, false, null, null, stuff).Accepted)
                {
                    Blueprint_Build blueprint = GenConstruct.PlaceBlueprintForBuild(terrain, cell, map, Rot4.North, Faction.OfPlayer, stuff);
                    if (blueprint != null)
                    {
                        AICoopStateSerializer.ReportBlueprintMaterialStatus(map, blueprint);
                        placed++;
                    }
                }
            }
            Log("[执行] 已放置 " + placed + " 格 " + terrain.defName +
                " 地板蓝图。");
        }

        private static void ConfigureConduit(Map map, string[] parts, string line)
        {
            Pawn actor = AIPawns(map).FirstOrDefault();
            ThingDef conduit = DefDatabase<ThingDef>.GetNamedSilentFail("PowerConduit");
            int x1, z1, x2, z2;
            if (actor == null || conduit == null || parts.Length != 7 || !Int32.TryParse(parts[3], out x1) || !Int32.TryParse(parts[4], out z1) || !Int32.TryParse(parts[5], out x2) || !Int32.TryParse(parts[6], out z2))
            {
                Log("[拒绝] C conduit 格式：C mapID conduit x1 z1 x2 z2。");
                return;
            }
            ThingDef conduitStuff = conduit.MadeFromStuff ? ChooseLocalStuff(conduit, map, null) : null;
            int placed = 0;
            for (int x = Math.Min(x1, x2); x <= Math.Max(x1, x2); x++) for (int z = Math.Min(z1, z2); z <= Math.Max(z1, z2); z++)
            {
                IntVec3 cell = new IntVec3(x, 0, z);
                if (!cell.InBounds(map)) continue;
                if (GenConstruct.CanPlaceBlueprintAt(conduit, cell, Rot4.North, map, false, null, null, conduitStuff).Accepted)
                {
                    Blueprint_Build blueprint = GenConstruct.PlaceBlueprintForBuild(conduit, cell, map, Rot4.North, Faction.OfPlayer, conduitStuff);
                    if (blueprint != null)
                    {
                        AICoopStateSerializer.ReportBlueprintMaterialStatus(map, blueprint);
                        placed++;
                    }
                }
            }
            Log("[执行] 已批量放置 " + placed + " 格电缆" +
                "蓝图。");
        }

        private static void ConfigureHiddenConduit(Map map, string[] parts, string line)
        {
            Pawn actor = AIPawns(map).FirstOrDefault();
            int x1, z1, x2, z2;
            ThingDef hidden = DefDatabase<ThingDef>.GetNamedSilentFail("HiddenConduit");
            if (actor == null || hidden == null || parts.Length != 7 || !Int32.TryParse(parts[3], out x1) || !Int32.TryParse(parts[4], out z1) ||
                !Int32.TryParse(parts[5], out x2) || !Int32.TryParse(parts[6], out z2))
            {
                Log("[拒绝] C hiddenConduit 格式：C mapID hiddenConduit x1 z1 x2 z2。");
                return;
            }
            if (!map.listerThings.AllThings.Any(AICoopActionExecutor.IsPowerGeneratorThing))
            {
                Log("[拒绝] 地图上没有发电设备或发电设备蓝图；请先建设发电设备，再铺设隐藏电缆。");
                return;
            }
            int placed = AICoopActionExecutor.PlaceHiddenConduitPath(map, new IntVec3(x1, 0, z1), new IntVec3(x2, 0, z2), hidden);
            Log("[执行] 已沿起点（" + x1 + "," + z1 + "）到终点（" + x2 + "," + z2 + "）放置 " + placed + " 格隐藏电缆蓝图。");
        }

        private static void ConfigureFortification(Map map, string[] parts, string line)
        {
            Pawn actor = AIPawns(map).FirstOrDefault();
            int x1, z1, x2, z2;
            if (actor == null || parts.Length != 7 || !Int32.TryParse(parts[3], out x1) || !Int32.TryParse(parts[4], out z1) || !Int32.TryParse(parts[5], out x2) || !Int32.TryParse(parts[6], out z2))
            {
                Log("[拒绝] C fortify 格式：C mapID fortify x1 z1 x2 z2。");
                return;
            }
            ThingDef wall = DefDatabase<ThingDef>.GetNamedSilentFail("Wall");
            ThingDef door = DefDatabase<ThingDef>.GetNamedSilentFail("Door");
            ThingDef sandbag = DefDatabase<ThingDef>.GetNamedSilentFail("Sandbags");
            ThingDef wallStuff = wall == null ? null : ChooseLocalStuff(wall, map, door);
            if (wall == null || door == null || wallStuff == null)
            {
                Log("[拒绝] 当前版本没有可用的墙或门定义。");
                return;
            }
            int minX = Math.Min(x1, x2), maxX = Math.Max(x1, x2), minZ = Math.Min(z1, z2), maxZ = Math.Max(z1, z2);
            int placed = 0;
            for (int x = minX; x <= maxX; x++)
            {
                if (PlaceBuildable(map, new IntVec3(x, 0, minZ), x == (minX + maxX) / 2 ? door : wall, wallStuff)) placed++;
                if (PlaceBuildable(map, new IntVec3(x, 0, maxZ), wall, wallStuff)) placed++;
            }
            for (int z = minZ + 1; z < maxZ; z++)
            {
                if (PlaceBuildable(map, new IntVec3(minX, 0, z), wall, wallStuff)) placed++;
                if (PlaceBuildable(map, new IntVec3(maxX, 0, z), wall, wallStuff)) placed++;
            }
            if (sandbag != null && sandbag.IsResearchFinished)
            {
                ThingDef sandbagStuff = sandbag.MadeFromStuff ? ChooseLocalStuff(sandbag, map, null) : null;
                for (int x = minX + 2; x < maxX - 1; x += 2)
                {
                    if (PlaceBuildable(map, new IntVec3(x, 0, minZ + 2), sandbag, sandbagStuff)) placed++;
                }
            }
            Log("[执行] 已建立防御工事模板，共放置 " + placed + " 个蓝图。");
        }

        private static void ConfigureDefenseWorks(Map map, string[] parts, string line)
        {
            Pawn actor = AIPawns(map).FirstOrDefault();
            ThingDef trap = DefDatabase<ThingDef>.GetNamedSilentFail("TrapSpike");
            ThingDef steel = DefDatabase<ThingDef>.GetNamedSilentFail("Steel");
            if (actor == null || trap == null || steel == null || !trap.BuildableByPlayer || !trap.IsResearchFinished ||
                steel.stuffProps == null || !steel.stuffProps.CanMake(trap))
            {
                Log("[拒绝] 建造防御工事需要已解锁的 TrapSpike 和 Steel。");
                return;
            }
            int minX, minZ, maxX, maxZ;
            if (!FindColonyBoundsForDefense(map, out minX, out minZ, out maxX, out maxZ))
            {
                Log("[拒绝] 找不到居住区外围，无法布置防御工事。");
                return;
            }
            IntVec3 gap = FindPerimeterGap(map, minX, minZ, maxX, maxZ);
            if (!gap.IsValid)
            {
                Log("[拒绝] 外围石墙没有可识别的缺口，请先使用 F 建造外围墙并保留缺口。");
                return;
            }
            IntVec3 direction = DetermineOutsideDirection(gap, minX, minZ, maxX, maxZ);
            IntVec3 start = gap + direction * 2;
            int placed = 0;
            for (int row = 0; row < 8; row++) for (int col = 0; col < 4; col++)
            {
                if (((row + col) & 1) != 0) continue;
                IntVec3 cell = new IntVec3(start.x + direction.z * col + (direction.x == 0 ? 0 : direction.x * row), 0,
                    start.z + direction.x * col + (direction.z == 0 ? 0 : direction.z * row));
                if (!cell.InBounds(map) || !cell.Walkable(map)) continue;
                if (map.listerThings.AllThings.Any(thing => thing != null && thing.Spawned && thing.Position == cell)) continue;
                if (PlaceBuildable(map, cell, trap, steel)) placed++;
            }
            if (placed < 16)
            {
                Log("[拒绝] 防御工事只放置了 " + placed + "/16 个钢铁陷阱；请确认缺口外有连续可建地形。");
                return;
            }
            Log("[执行] 已在外围墙缺口外按 4x8 斜对角交叉布局放置 " + placed + " 个钢铁陷阱蓝图。");
        }

        private static bool FindColonyBoundsForDefense(Map map, out int minX, out int minZ, out int maxX, out int maxZ)
        {
            minX = minZ = Int32.MaxValue;
            maxX = maxZ = Int32.MinValue;
            int perimeterWalls = 0;

            // The defense layout must be anchored to the actual F-generated wall ring.
            // Using all buildings would make an interior wall or a large blueprint shift
            // the inferred perimeter and cause the gap/trap positions to drift.
            foreach (Thing thing in map.listerThings.AllThings)
            {
                if (thing == null || !thing.Spawned || thing.Position == IntVec3.Invalid || thing.def == null) continue;
                BuildableDef entity = thing is Blueprint ? (thing as Blueprint).EntityToBuild() :
                    (thing is Frame ? (thing as Frame).def.entityDefToBuild : thing.def);
                if (entity == null || (entity.defName != "Wall" && entity.defName != "Door")) continue;
                if (!(thing is Blueprint) && thing.Faction != Faction.OfPlayer) continue;
                minX = Math.Min(minX, thing.Position.x); maxX = Math.Max(maxX, thing.Position.x);
                minZ = Math.Min(minZ, thing.Position.z); maxZ = Math.Max(maxZ, thing.Position.z);
                perimeterWalls++;
            }
            return perimeterWalls >= 4 && minX != Int32.MaxValue;
        }

        private static IntVec3 FindPerimeterGap(Map map, int minX, int minZ, int maxX, int maxZ)
        {
            List<IntVec3> candidates = new List<IntVec3>();
            for (int x = minX + 1; x < maxX; x++)
            {
                candidates.Add(new IntVec3(x, 0, minZ));
                candidates.Add(new IntVec3(x, 0, maxZ));
            }
            for (int z = minZ + 1; z < maxZ; z++)
            {
                candidates.Add(new IntVec3(minX, 0, z));
                candidates.Add(new IntVec3(maxX, 0, z));
            }

            // F intentionally leaves one empty cell between two wall cells. Require
            // that local pattern so ordinary empty terrain is never treated as a gap.
            return candidates.FirstOrDefault(cell => cell.InBounds(map) && !HasWallOrDoorAt(map, cell) &&
                cell.Walkable(map) && IsPerimeterGapCandidate(map, cell, minX, minZ, maxX, maxZ));
        }

        private static bool IsPerimeterGapCandidate(Map map, IntVec3 cell, int minX, int minZ, int maxX, int maxZ)
        {
            if (cell.z == minZ || cell.z == maxZ)
            {
                return HasWallOrDoorAt(map, new IntVec3(cell.x - 1, 0, cell.z)) &&
                    HasWallOrDoorAt(map, new IntVec3(cell.x + 1, 0, cell.z));
            }
            if (cell.x == minX || cell.x == maxX)
            {
                return HasWallOrDoorAt(map, new IntVec3(cell.x, 0, cell.z - 1)) &&
                    HasWallOrDoorAt(map, new IntVec3(cell.x, 0, cell.z + 1));
            }
            return false;
        }

        private static bool HasWallOrDoorAt(Map map, IntVec3 cell)
        {
            return map != null && cell.IsValid && cell.GetThingList(map).Any(thing => thing != null &&
                (thing.def.defName == "Wall" || thing.def.defName == "Door" || thing is Blueprint_Build &&
                 ((thing as Blueprint_Build).EntityToBuild() != null && ((thing as Blueprint_Build).EntityToBuild().defName == "Wall" || (thing as Blueprint_Build).EntityToBuild().defName == "Door"))));
        }

        private static IntVec3 DetermineOutsideDirection(IntVec3 gap, int minX, int minZ, int maxX, int maxZ)
        {
            if (gap.z < minZ) return new IntVec3(0, 0, -1);
            if (gap.z > maxZ) return new IntVec3(0, 0, 1);
            if (gap.x < minX) return new IntVec3(-1, 0, 0);
            return new IntVec3(1, 0, 0);
        }

        private static bool PlaceBuildable(Map map, IntVec3 cell, BuildableDef buildDef, ThingDef stuff)
        {
            if (map == null || buildDef == null || !cell.InBounds(map) ||
                !buildDef.BuildableByPlayer || !buildDef.IsResearchFinished) return false;
            if (!GenConstruct.CanPlaceBlueprintAt(buildDef, cell, Rot4.North, map, false, null, null, stuff).Accepted) return false;
            Blueprint_Build blueprint = GenConstruct.PlaceBlueprintForBuild(buildDef, cell, map, Rot4.North, Faction.OfPlayer, stuff);
            if (blueprint == null) return false;
            AICoopStateSerializer.ReportBlueprintMaterialStatus(map, blueprint);
            return true;
        }

        private static void ConfigureSurgery(Map map, string[] parts, string line)
        {
            int pawnId;
            Pawn patient;
            if ((parts.Length != 5 && parts.Length != 6) || !Int32.TryParse(parts[3], out pawnId) ||
                (patient = map.mapPawns.AllPawnsSpawned.FirstOrDefault(pawn => pawn.thingIDNumber == pawnId)) == null ||
                !AICoopGameComponent.Current.CanAIControl(patient) || patient.health == null)
            {
                Log("[拒绝] C surgery 格式：C mapID surgery AI患者ID recipeDefName [BodyPartDefName]。");
                return;
            }
            RecipeDef recipe = DefDatabase<RecipeDef>.GetNamedSilentFail(parts[4]);
            if (recipe == null || !recipe.IsSurgery || recipe.Worker == null)
            {
                Log("[拒绝] 手术配方不存在、不是手术或当前不可用：" + parts[4]);
                return;
            }
            BodyPartRecord part = recipe.Worker.GetPartsToApplyOn(patient, recipe).FirstOrDefault(bodyPart => parts.Length == 5 || bodyPart.def.defName.Equals(parts[5], StringComparison.OrdinalIgnoreCase));
            if ((recipe.targetsBodyPart && part == null) || !recipe.AvailableOnNow(patient, part))
            {
                Log("[拒绝] 找不到符合手术配方的身体部位：" + line);
                return;
            }
            Bill_Medical bill = new Bill_Medical(recipe, null);
            patient.health.surgeryBills.AddBill(bill);
            bill.Part = part;
            Log("[执行] 已为 " + patient.LabelShort + " 添加手术账单 " + recipe.defName + "。");
        }

        private static void ConfigureOrganHarvest(Map map, string[] parts, string line)
        {
            int pawnId;
            Pawn prisoner;
            AICoopGameComponent component = AICoopGameComponent.Current;
            if (parts.Length != 4 || !Int32.TryParse(parts[3], out pawnId) ||
                (prisoner = map.mapPawns.AllPawnsSpawned.FirstOrDefault(pawn => pawn.thingIDNumber == pawnId)) == null ||
                !prisoner.IsPrisonerOfColony || prisoner.health == null || component == null)
            {
                Log("[拒绝] C organHarvest 格式：C mapID organHarvest 囚犯ID。");
                return;
            }
            if (Find.QuestManager != null && Find.QuestManager.IsReservedByAnyQuest(prisoner))
            {
                Log("[拒绝] 囚犯 " + prisoner.LabelShort + " 正被进行中的任务保留，禁止器官手术；任务失败或不再保留后才能重试。");
                return;
            }
            if (!component.WasCapturedByAI(prisoner))
            {
                Pawn captor = AIPawns(map).FirstOrDefault();
                if (captor == null)
                {
                    Log("[拒绝] 无法确认 AI 俘虏来源，且地图没有 AI 殖民者可作为俘虏归属；请玩家处理。");
                    return;
                }
                component.RecordPrisonerCapture(prisoner, captor);
                Log("[系统] 囚犯 " + prisoner.LabelShort + " 当前未被任务保留，按任务失败后的规则视为 AI 俘虏并继续处理。");
            }
            List<string> questTags = PawnQuestTags == null ? null : PawnQuestTags.GetValue(prisoner) as List<string>;
            if (questTags != null && questTags.Count > 0)
            {
                Log("[拒绝] 囚犯 " + prisoner.LabelShort + " 带有任务标记，视为任务囚犯，禁止器官手术；任务失败后才能重试。");
                return;
            }
            if (component.GetOrganSurgeryStage(prisoner) >= 3 || prisoner.health.surgeryBills.Bills.Any(bill => bill.recipe != null && bill.recipe.defName == "RemoveBodyPart"))
            {
                Log("[拒绝] 囚犯 " + prisoner.LabelShort + " 已有器官摘除账单，禁止重复添加。");
                return;
            }
            RecipeDef remove = DefDatabase<RecipeDef>.GetNamedSilentFail("RemoveBodyPart");
            if (remove == null || remove.Worker == null)
            {
                Log("[拒绝] 原版 RemoveBodyPart 手术配方不可用。");
                return;
            }
            List<Pawn> aiColonists = AIPawns(map).ToList();
            bool harvestAbhorrent = aiColonists.Any(pawn => HasMoodPenaltyPrecept(pawn, "OrganUse_Abhorrent"));
            bool harvestDisapproved = aiColonists.Any(pawn =>
                HasMoodPenaltyPrecept(pawn, "OrganUse_HorribleNoSell") || HasMoodPenaltyPrecept(pawn, "OrganUse_HorribleSellOK"));
            bool prisonerDeathDisapproved = aiColonists.Any(pawn =>
                HasMoodPenaltyPrecept(pawn, "Execution_Abhorrent") ||
                HasMoodPenaltyPrecept(pawn, "Execution_Horrible") ||
                HasMoodPenaltyPrecept(pawn, "Execution_HorribleIfInnocent"));
            if (harvestAbhorrent)
            {
                Log("[拒绝] AI 殖民者文化将摘取器官视为绝对可憎，会造成心情惩罚；未添加任何器官手术账单。");
                component.AddCommandResult("FAIL organ_harvest_culture_denied=1 reason=organ_use_abhorrent");
                component.SetOrganSurgeryStage(prisoner, 0);
                return;
            }
            if (harvestDisapproved)
            {
                Log("[拒绝] AI 殖民者文化会因摘取器官产生心情惩罚；为避免影响殖民者，未添加任何器官手术账单。");
                component.AddCommandResult("FAIL organ_harvest_culture_denied=1 reason=organ_use_horrible");
                component.SetOrganSurgeryStage(prisoner, 0);
                return;
            }
            List<BodyPartRecord> available = remove.Worker.GetPartsToApplyOn(prisoner, remove).ToList();
            BodyPartRecord lung = available.FirstOrDefault(part => part.def.defName == "Lung");
            BodyPartRecord kidney = available.FirstOrDefault(part => part.def.defName == "Kidney");
            BodyPartRecord heart = available.FirstOrDefault(part => part.def.defName == "Heart");
            BodyPartRecord liver = available.FirstOrDefault(part => part.def.defName == "Liver");
            if (lung == null || kidney == null)
            {
                Log("[拒绝] 囚犯缺少可摘除的肺或肾，无法按强制顺序创建器官手术账单。");
                return;
            }
            List<BodyPartRecord> organs = new List<BodyPartRecord> { lung, kidney };
            if (heart != null && !prisonerDeathDisapproved)
            {
                organs.Add(heart);
            }
            else if (heart == null && liver != null && !prisonerDeathDisapproved)
            {
                organs.Add(liver);
            }
            foreach (BodyPartRecord part in organs)
            {
                Bill_Medical bill = new Bill_Medical(remove, null) { Part = part };
                prisoner.health.surgeryBills.AddBill(bill);
            }
            component.SetOrganSurgeryStage(prisoner, organs.Count);
            string organsText = string.Join("、", organs.Select(part => part.def.defName == "Lung" ? "肺" :
                part.def.defName == "Kidney" ? "肾" : part.def.defName == "Heart" ? "心脏" : "肝脏").ToArray());
            if (prisonerDeathDisapproved && heart != null)
            {
                component.AddCommandResult("OK organ_harvest_bills=" + organs.Count + " heart_skipped=1 reason=prisoner_death_mood");
                Log("[执行] AI 殖民者文化可能因囚犯死亡产生心情惩罚，已跳过致死的心脏手术；已添加：" + organsText + "。");
            }
            else
            {
                component.AddCommandResult("OK organ_harvest_bills=" + organs.Count);
                Log("[执行] 已为 AI 俘虏 " + prisoner.LabelShort + " 按顺序添加摘除" + organsText + "的手术账单。");
            }
        }

        private static bool HasIdeologyPrecept(Pawn pawn, string preceptDefName)
        {
            if (!ModsConfig.IdeologyActive || pawn == null || pawn.Ideo == null || preceptDefName.NullOrEmpty()) return false;
            return pawn.Ideo.PreceptsListForReading.Any(precept => precept != null && precept.def != null &&
                string.Equals(precept.def.defName, preceptDefName, StringComparison.OrdinalIgnoreCase));
        }

        private static bool HasMoodPenaltyPrecept(Pawn pawn, string preceptDefName)
        {
            if (!HasIdeologyPrecept(pawn, preceptDefName)) return false;
            if (pawn.story == null || pawn.story.traits == null || pawn.story.traits.allTraits == null) return true;
            // Bloodlust and Psychopath nullify the relevant vanilla ideology mood thoughts.
            return !pawn.story.traits.allTraits.Any(trait => trait != null && trait.def != null &&
                (trait.def.defName == "Bloodlust" || trait.def.defName == "Psychopath"));
        }

        private static void ConfigureAnimal(Map map, string[] parts, string line)
        {
            int animalId;
            Pawn animal;
            if (parts.Length < 5 || parts.Length > 7 || !Int32.TryParse(parts[3], out animalId) ||
                (animal = map.mapPawns.AllPawnsSpawned.FirstOrDefault(pawn => pawn.thingIDNumber == animalId)) == null ||
                animal.RaceProps == null || !animal.RaceProps.Animal)
            {
                Log("[拒绝] C animal 格式：C mapID animal 动物ID train trainableDefName 0|1，或 release|forage|dig。");
                return;
            }
            string mode = parts[4].ToLowerInvariant();
            if (mode == "release")
            {
                Pawn releaser = AIPawns(map).FirstOrDefault();
                if (releaser == null || animal.Faction != Faction.OfPlayer)
                {
                    Log("[拒绝] 只能释放属于殖民地的动物。");
                    return;
                }
                ReleaseAnimalToWildUtility.DoReleaseAnimal(animal, releaser);
                Log("[执行] 已将动物 " + animal.LabelShort + " 放归野外。");
                return;
            }
            if (mode == "forage" || mode == "dig")
            {
                if (animal.playerSettings == null)
                {
                    Log("[拒绝] 动物没有可配置的玩家设置。");
                    return;
                }
                bool value;
                if (parts.Length != 6 || !TryBool(parts[5], out value))
                {
                    Log("[拒绝] 动物 forage/dig 格式需要 0|1。");
                    return;
                }
                if (mode == "forage") animal.playerSettings.animalForage = value;
                else animal.playerSettings.animalDig = value;
                Log("[执行] 已将 " + animal.LabelShort + " 的 " + mode + " 设为 " + (value ? "允许" : "禁止") + "。");
                return;
            }
            if (mode == "train" && parts.Length == 7)
            {
                TrainableDef trainable = DefDatabase<TrainableDef>.GetNamedSilentFail(parts[5]);
                bool wanted;
                if (trainable == null || !TryBool(parts[6], out wanted) || animal.training == null || !animal.training.CanBeTrained(trainable))
                {
                    Log("[拒绝] 动物训练项目不存在或不可训练：" + parts[5]);
                    return;
                }
                animal.training.SetWantedRecursive(trainable, wanted);
                Log("[执行] 已将 " + animal.LabelShort + " 的训练项目 " + trainable.defName + " 设为 " + (wanted ? "需要" : "不需要") + "。");
                return;
            }
            Log("[拒绝] C animal 操作类型无效：" + line);
        }

        private static void ConfigureAutomaticDeepDrill(Map map, string[] parts, string line)
        {
            Pawn actor = AIPawns(map).FirstOrDefault();
            ThingDef drill = DefDatabase<ThingDef>.GetNamedSilentFail("DeepDrill");
            if (actor == null || drill == null || (parts.Length != 3 && parts.Length != 4))
            {
                Log("[拒绝] C deepAuto 格式：C mapID deepAuto [资源ThingDef]。");
                return;
            }
            ThingDef wanted = parts.Length == 4 ? DefDatabase<ThingDef>.GetNamedSilentFail(parts[3]) : null;
            IntVec3 bestCell = IntVec3.Invalid;
            int bestCount = 0;
            int bestDistance = Int32.MaxValue;
            foreach (IntVec3 cell in map.AllCells)
            {
                ThingDef resource = map.deepResourceGrid.ThingDefAt(cell);
                int count = map.deepResourceGrid.CountAt(cell);
                int distance = Math.Abs(cell.x - map.Center.x) + Math.Abs(cell.z - map.Center.z);
                if (resource == null || (wanted != null && resource != wanted) || count <= 0 || count < bestCount ||
                    (count == bestCount && distance >= bestDistance) || !GenConstruct.CanPlaceBlueprintAt(drill, cell, Rot4.North, map, false, null, null, null).Accepted) continue;
                bestCell = cell;
                bestCount = count;
                bestDistance = distance;
            }
            if (!bestCell.IsValid)
            {
                Log("[拒绝] 没有找到符合资源和放置条件的深钻井位置。");
                return;
            }
            ThingDef stuff = drill.MadeFromStuff ? ChooseLocalStuff(drill, map, null) : null;
            if (drill.MadeFromStuff && stuff == null)
            {
                Log("[拒绝] 没有可用的深钻井建造材料。");
                return;
            }
            if (!GenConstruct.CanPlaceBlueprintAt(drill, bestCell, Rot4.North, map, false, null, null, stuff).Accepted)
            {
                Log("[拒绝] 深钻井放置失败：" + bestCell);
                return;
            }
            Blueprint_Build drillBlueprint = GenConstruct.PlaceBlueprintForBuild(drill, bestCell, map, Rot4.North, Faction.OfPlayer, stuff);
            if (drillBlueprint == null)
            {
                Log("[拒绝] 深钻井放置失败：" + bestCell);
                return;
            }
            AICoopStateSerializer.ReportBlueprintMaterialStatus(map, drillBlueprint);
            Log("[执行] 已在 " + bestCell + " 为 " + map.deepResourceGrid.ThingDefAt(bestCell).defName +
                " 放置深钻井蓝图；采矿优先级保持玩家设置。");
        }

        private static void ConfigureBill(Map map, string[] parts, string line)
        {
            if (parts.Length < 6)
            {
                Log("[拒绝] C bill 格式：C mapID bill 工作台ID 配方defName ingredient|quality|target|repeat|pause|suspend|store|include|skill|stuff|name 值。");
                return;
            }
            int tableId;
            if (!Int32.TryParse(parts[3], out tableId))
            {
                Log("[拒绝] 工作台 ID 无效：" + parts[3]);
                return;
            }
            Building_WorkTable table = map.listerThings.AllThings.FirstOrDefault(thing => thing.thingIDNumber == tableId) as Building_WorkTable;
            RecipeDef recipe = DefDatabase<RecipeDef>.GetNamedSilentFail(parts[4]);
            Bill_Production bill = table == null || table.BillStack == null ? null : table.BillStack.Bills.OfType<Bill_Production>().FirstOrDefault(item => item.recipe == recipe);
            if (table == null || recipe == null || bill == null)
            {
                Log("[拒绝] 找不到对应工作台、配方或生产账单；先使用 P 添加账单：" + line);
                return;
            }
            string field = parts[5].ToLowerInvariant();
            string value = parts.Length > 6 ? parts[6] : string.Empty;
            bool boolValue;
            int intValue;
            if (field == "ingredient")
            {
                bill.ingredientFilter.SetDisallowAll();
                foreach (string name in value.Split(','))
                {
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(name);
                    if (def != null) bill.ingredientFilter.SetAllow(def, true);
                }
                Log("[执行] 已设置 " + recipe.defName + " 账单的原料筛选。");
                return;
            }
            if (field == "quality")
            {
                try { bill.qualityRange = QualityRange.FromString(value); }
                catch { Log("[拒绝] 质量范围无效：" + value); return; }
                Log("[执行] 已设置质量范围 " + bill.qualityRange + "。");
                return;
            }
            if (field == "target" && Int32.TryParse(value, out intValue) && intValue >= 0)
            {
                bill.repeatMode = BillRepeatModeDefOf.TargetCount;
                bill.targetCount = intValue;
                Log("[执行] 已将账单目标库存设为 " + intValue + "。");
                return;
            }
            if (field == "repeat")
            {
                if (value.Equals("forever", StringComparison.OrdinalIgnoreCase)) bill.repeatMode = BillRepeatModeDefOf.Forever;
                else if (Int32.TryParse(value, out intValue) && intValue >= 0)
                {
                    bill.repeatMode = BillRepeatModeDefOf.RepeatCount;
                    bill.repeatCount = intValue;
                }
                else { Log("[拒绝] 重复次数无效：" + value); return; }
                Log("[执行] 已设置账单重复模式为 " + value + "。");
                return;
            }
            if ((field == "pause" || field == "suspend" || field == "stuff") && TryBool(value, out boolValue))
            {
                if (field == "pause") bill.pauseWhenSatisfied = boolValue;
                else if (field == "suspend") bill.suspended = boolValue;
                else bill.limitToAllowedStuff = boolValue;
                Log("[执行] 已更新账单 " + field + " 设置。");
                return;
            }
            if (field == "unpause" && Int32.TryParse(value, out intValue) && intValue >= 0)
            {
                bill.unpauseWhenYouHave = intValue;
                Log("[执行] 已设置账单恢复库存阈值为 " + intValue + "。");
                return;
            }
            if (field == "store" || field == "include")
            {
                if (value.Equals("floor", StringComparison.OrdinalIgnoreCase) && field == "store")
                {
                    bill.SetStoreMode(BillStoreModeDefOf.DropOnFloor, null);
                    Log("[执行] 产品将存放在地面。");
                    return;
                }
                Zone_Stockpile zone;
                if (!TryStockpile(map, value, out zone))
                {
                    Log("[拒绝] 目标仓储区不存在：" + value);
                    return;
                }
                if (field == "store") bill.SetStoreMode(BillStoreModeDefOf.SpecificStockpile, zone.GetSlotGroup());
                else bill.SetIncludeGroup(zone.GetSlotGroup());
                Log("[执行] 已将账单 " + field + " 目标设为仓储区 " + zone.ID + "。");
                return;
            }
            if (field == "skill")
            {
                string[] range = value.Split('-');
                int min, max;
                if (range.Length != 2 || !Int32.TryParse(range[0], out min) || !Int32.TryParse(range[1], out max) || min < 0 || max < min)
                {
                    Log("[拒绝] 技能范围应为最小值-最大值：" + value);
                    return;
                }
                bill.allowedSkillRange = new IntRange(min, max);
                Log("[执行] 已设置账单技能范围 " + value + "。");
                return;
            }
            if (field == "name")
            {
                bill.RenamableLabel = value.Replace('_', ' ');
                Log("[执行] 已重命名账单。");
                return;
            }
            Log("[拒绝] 未知账单设置：" + field);
        }

        private static void ConfigureBillOrder(Map map, string[] parts, string line)
        {
            int tableId, billIndex, delta;
            if (parts.Length != 6 || !Int32.TryParse(parts[3], out tableId) || !Int32.TryParse(parts[4], out billIndex) || !Int32.TryParse(parts[5], out delta))
            {
                Log("[拒绝] C billOrder 格式：C mapID billOrder 工作台ID 账单序号 上下移动格数。");
                return;
            }
            Building_WorkTable table = map.listerThings.AllThings.FirstOrDefault(thing => thing.thingIDNumber == tableId) as Building_WorkTable;
            if (table == null || table.BillStack == null || billIndex < 0 || billIndex >= table.BillStack.Count)
            {
                Log("[拒绝] 工作台或账单序号无效：" + line);
                return;
            }
            table.BillStack.Reorder(table.BillStack[billIndex], delta);
            Log("[执行] 已调整工作台账单顺序。");
        }

        private static bool SetMember(object instance, string name, object value)
        {
            if (instance == null) return false;
            try
            {
                PropertyInfo property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                if (property != null && property.CanWrite && property.PropertyType.IsAssignableFrom(value.GetType()))
                {
                    property.SetValue(instance, value, null);
                    return true;
                }
                FieldInfo field = instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase);
                if (field != null && field.FieldType.IsAssignableFrom(value.GetType()))
                {
                    field.SetValue(instance, value);
                    return true;
                }
            }
            catch
            {
            }
            return false;
        }

        private static ThingDef ChooseLocalStuff(BuildableDef buildDef, Map map, BuildableDef alsoMustMake)
        {
            if (buildDef == null || !buildDef.MadeFromStuff) return null;
            return GenStuff.AllowedStuffsFor(buildDef)
                .Where(stuff => stuff != null && stuff.stuffProps != null && (alsoMustMake == null || stuff.stuffProps.CanMake(alsoMustMake)))
                .OrderByDescending(stuff => map == null ? 0 : map.listerThings.AllThings.Where(thing => thing.def == stuff).Sum(thing => thing.stackCount))
                .ThenBy(stuff => stuff.defName)
                .FirstOrDefault() ?? GenStuff.DefaultStuffFor(buildDef);
        }

        private static IEnumerable<Pawn> AIPawns(Map map)
        {
            AICoopGameComponent component = AICoopGameComponent.Current;
            return component == null ? Enumerable.Empty<Pawn>() : map.mapPawns.FreeColonistsSpawned.Where(component.CanAIControl);
        }

        private static bool IsAIPawn(Pawn pawn)
        {
            return pawn != null && AICoopGameComponent.Current != null && AICoopGameComponent.Current.CanAIControl(pawn);
        }

        private static bool TryAIPawn(Map map, int pawnId, out Pawn result)
        {
            AICoopGameComponent component = AICoopGameComponent.Current;
            result = map.mapPawns.FreeColonistsSpawned.FirstOrDefault(pawn => pawn.thingIDNumber == pawnId);
            return result != null && component != null && component.CanAIControl(result);
        }

        private static bool TryStockpile(Map map, string id, out Zone_Stockpile result)
        {
            int zoneId;
            result = null;
            return Int32.TryParse(id, out zoneId) && (result = map.zoneManager.AllZones.OfType<Zone_Stockpile>().FirstOrDefault(zone => zone.ID == zoneId)) != null;
        }

        private static bool TryStorageBuilding(Map map, string id, out Building_Storage result)
        {
            int thingId;
            result = null;
            return Int32.TryParse(id, out thingId) && (result = map.listerThings.AllThings.FirstOrDefault(thing => thing.thingIDNumber == thingId) as Building_Storage) != null;
        }

        private static bool Rectangle(string[] parts, int start, out int x1, out int z1, out int x2, out int z2)
        {
            x1 = z1 = x2 = z2 = 0;
            if (!Int32.TryParse(parts[start], out x1) || !Int32.TryParse(parts[start + 1], out z1) || !Int32.TryParse(parts[start + 2], out x2) || !Int32.TryParse(parts[start + 3], out z2)) return false;
            int lowX = Math.Min(x1, x2), highX = Math.Max(x1, x2), lowZ = Math.Min(z1, z2), highZ = Math.Max(z1, z2);
            x1 = lowX; x2 = highX; z1 = lowZ; z2 = highZ;
            return (x2 - x1 + 1) * (z2 - z1 + 1) <= 1600;
        }

        private static bool TryBool(string value, out bool result)
        {
            result = value == "1" || value.Equals("true", StringComparison.OrdinalIgnoreCase);
            return result || value == "0" || value.Equals("false", StringComparison.OrdinalIgnoreCase);
        }

        private static void Log(string message)
        {
            AICoopGameComponent.Current.AddLog(message);
        }

        private static void Result(string message)
        {
            AICoopGameComponent.Current?.AddCommandResult(message);
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Replace(",", " ").Replace("|", "/");
        }
    }
}
