using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;
using Verse.AI.Group;

namespace AICoopCompanion
{
    internal static partial class AICoopDlcActions
    {
        internal static void RequestPlayer(string line, string reason)
        {
            var component = AICoopGameComponent.Current;
            string text = "这一步需要你处理：" + Clean(reason) + "；操作：" + Clean(line) + "。请完成后在聊天中告诉我，或告诉我跳过。";
            component.AddCommandResult("FAIL player_action_required=1 reason=" + Clean(reason));
            if (!component.PlayerRequests.Any(r => r.EndsWith(text, StringComparison.Ordinal))) component.AddPlayerRequest(text);
        }

        private static Pawn Actor(Map map, int id, bool mustAct = true)
        {
            Pawn pawn = FindThing(map, id) as Pawn;
            Require(pawn != null && pawn.Faction == Faction.OfPlayer && !pawn.Dead && AICoopGameComponent.Current.CanAIControl(pawn), "人物必须属于AI可控制范围");
            if (mustAct) Require(pawn.Spawned && !pawn.Downed && !pawn.InMentalState && !pawn.Deathresting, "人物现在无法行动");
            return pawn;
        }

        private static Pawn Subject(Map map, int id)
        {
            Pawn pawn = FindThing(map, id) as Pawn;
            Require(pawn != null && !pawn.Dead && !Find.QuestManager.IsReservedByAnyQuest(pawn), "不能操作不存在、死亡或任务保留的人物");
            Require((pawn.Faction == Faction.OfPlayer && AICoopGameComponent.Current.CanAIControl(pawn)) ||
                (pawn.IsPrisonerOfColony && AICoopGameComponent.Current.WasCapturedByAI(pawn)), "仅操作AI可控人物或AI俘获的囚犯");
            return pawn;
        }

        private static IEnumerable<Ability> Abilities(Pawn pawn)
        {
            return (pawn.abilities?.AllAbilitiesForReading ?? new List<Ability>())
                .Concat(pawn.royalty?.AllAbilitiesForReading ?? new List<Ability>())
                .Concat(pawn.mutant?.AllAbilitiesForReading ?? new List<Ability>()).Distinct();
        }

        private static List<FloatMenuOption> NativeOptions(Thing target, Pawn pawn)
        {
            Require(target.Spawned && pawn.Map == target.Map, "原生交互需要人物和目标在同一地图；被容器收纳的目标请先处理容器");
            var context = new FloatMenuContext(new List<Pawn> { pawn }, target.DrawPos, target.Map);
            var lord = pawn.GetLord();
            Require(lord == null || lord.AllowsFloatMenu(pawn).Accepted, "原版当前不允许该人物接受右键指令");
            var field = typeof(FloatMenuMakerMap).GetField("providers", BindingFlags.Static | BindingFlags.NonPublic);
            if (field == null) throw new NotSupportedException("当前版本原生菜单接口不兼容");
            var providers = field.GetValue(null) as List<FloatMenuOptionProvider>;
            if (providers == null) { FloatMenuMakerMap.Init(); providers = field.GetValue(null) as List<FloatMenuOptionProvider>; }
            var options = (target.GetFloatMenuOptions(pawn) ?? Enumerable.Empty<FloatMenuOption>()).ToList();
            var oldProvider = FloatMenuMakerMap.currentProvider;
            var oldPawn = FloatMenuMakerMap.makingFor;
            try
            {
                FloatMenuMakerMap.makingFor = pawn;
                foreach (var provider in providers)
                {
                    // Unknown mod providers may have UI-dependent side effects. Keep this adapter native-only.
                    if (provider.GetType().Assembly != typeof(FloatMenuMakerMap).Assembly) continue;
                    FloatMenuMakerMap.currentProvider = provider;
                    if (!provider.Applies(context) || !provider.SelectedPawnValid(pawn, context)) continue;
                    if (provider.TargetThingValid(target, context)) options.AddRange(provider.GetOptionsFor(target, context));
                    var targetPawn = target as Pawn;
                    if (targetPawn != null && provider.TargetPawnValid(targetPawn, context)) options.AddRange(provider.GetOptionsFor(targetPawn, context));
                }
            }
            finally { FloatMenuMakerMap.currentProvider = oldProvider; FloatMenuMakerMap.makingFor = oldPawn; }
            return options.Where(o => o != null && !IsUnsafeUiOption(o)).GroupBy(Key).Select(g => g.First()).ToList();
        }

        private static bool ExecuteExtended(Map map, string[] p)
        {
            string op = p[1].ToLowerInvariant();
            if (op == "features")
            {
                Count(p, 3);
                Result("FEATURES Royalty=" + ModsConfig.RoyaltyActive + " Ideology=" + ModsConfig.IdeologyActive + " Biotech=" + ModsConfig.BiotechActive + " Anomaly=" + ModsConfig.AnomalyActive + " Odyssey=" + ModsConfig.OdysseyActive +
                    "\n工具参数以CLI目录为准。机械师：控制/分组/护送/充电/维修/搬回充电器/孕育复活账单/扫描器/污染清理/战斗/Boss原生召唤；食尸鬼：转化手术/移动战斗/供食/休息和原生交互；皇权：要求查询/冥想目标/超能；文化：角色/分配设施/已有仪式；生物科技：血源目标/死眠自动醒/设备进入；异象：调查研究/收容维护；奥德赛：捕鱼/燃料/飞船准备。\n仍需玩家：接取与选择任务奖励、授勋/许可界面、儿童成长选择、复杂基因编辑/仪式角色祭品界面、多层地图入口确认、飞船起飞及选图。工具缺失或禁用时request_player，不反复试错。"); return true;
            }
            if (op == "pollution")
            {
                Require(ModsConfig.BiotechActive, "Biotech未启用"); Count(p, 8);
                int x1 = Number(p, 3), z1 = Number(p, 4), x2 = Number(p, 5), z2 = Number(p, 6); bool enabled = Flag(p, 7);
                Require(new IntVec3(x1, 0, z1).InBounds(map) && new IntVec3(x2, 0, z2).InBounds(map), "坐标超出地图");
                var rect = CellRect.FromLimits(x1, z1, x2, z2); Require(rect.Area <= 10000, "一次最多10000格");
                foreach (var cell in rect.Cells) map.areaManager.PollutionClear[cell] = enabled;
                Result("pollution_clear_area=" + rect.Area + " enabled=" + enabled + "（仅指定清理，不直接删除污染）"); return true;
            }
            if (op == "mech_control" || op == "mech_repair" || op == "mech_haul" || op == "mech_disconnect" || op == "mech_disassemble")
            {
                Require(ModsConfig.BiotechActive, "Biotech未启用"); Count(p, 5);
                Pawn actor = Actor(map, Number(p, 3)); Pawn mech = FindThing(map, Number(p, 4)) as Pawn;
                Require(mech != null && mech.IsColonyMech && !mech.Dead, "需要活着的殖民地机械体");
                Pawn owner = mech.GetOverseer();
                Require(owner == null || AICoopGameComponent.Current.CanAIControl(owner), "不能操作玩家专属机械师的机械体");
                if (op == "mech_disconnect")
                {
                    Require(owner == actor, "只有实际控制者可以断开"); MechanitorUtility.ForceDisconnectMechFromOverseer(mech); Result("mech_disconnected"); return true;
                }
                Require(actor.CanReserveAndReach(mech, PathEndMode.Touch, Danger.Deadly), "无法预留或接近机械体");
                Job job;
                if (op == "mech_haul")
                {
                    var giver = new WorkGiver_HaulMechToCharger();
                    Require(giver.HasJobOnThing(actor, mech, true), "原版未找到适用的充电器或不需要搬运充电"); job = giver.JobOnThing(actor, mech, true);
                }
                else
                {
                    Require(actor.mechanitor != null && actor.mechanitor.CanControlMechs.Accepted && actor.health.capacities.CapableOf(PawnCapacityDefOf.Manipulation), "需要能操控机械体的机械师");
                    if (op == "mech_control")
                    {
                        Require(owner != actor, "已经受此机械师控制"); var report = MechanitorUtility.CanControlMech(actor, mech); Require(report.Accepted, "原生控制检查：" + report.Reason);
                        job = JobMaker.MakeJob(JobDefOf.ControlMech, mech);
                    }
                    else if (op == "mech_repair")
                    {
                        Require(MechRepairUtility.CanRepair(mech) && !mech.HostileTo(actor), "机械体无可修复损伤或敌对"); job = JobMaker.MakeJob(JobDefOf.RepairMech, mech);
                    }
                    else { Require(owner == actor && !mech.IsFighting(), "需是自身控制且未战斗的机械体"); job = JobMaker.MakeJob(JobDefOf.DisassembleMech, mech); }
                }
                actor.jobs.TryTakeOrderedJob(job, JobTag.Misc); Result(op + " queued=1"); return true;
            }
            if (op == "mech_autorepair")
            {
                Require(ModsConfig.BiotechActive, "Biotech未启用"); Count(p, 5);
                Pawn mech = FindThing(map, Number(p, 3)) as Pawn; Require(ControlledMech(mech), "机械体不在AI控制范围");
                var repair = mech.TryGetComp<CompMechRepairable>(); Require(repair != null, "机械体不支持维修设置");
                repair.autoRepair = Flag(p, 4); Result("autoRepair=" + repair.autoRepair); return true;
            }
            if (op == "mech_escort")
            {
                Require(ModsConfig.BiotechActive, "Biotech未启用"); Count(p, 6);
                Pawn actor = Actor(map, Number(p, 3)); Require(actor.mechanitor != null && actor.mechanitor.CanControlMechs.Accepted, "需要可指挥机械体的机械师");
                var group = actor.mechanitor.controlGroups.FirstOrDefault(g => g.Index == Number(p, 4)); Require(group != null, "控制组不存在");
                Pawn escort = FindThing(map, Number(p, 5)) as Pawn; Require(escort != null && escort.Faction == Faction.OfPlayer, "仅护送己方人物");
                group.SetWorkMode(MechWorkModeDefOf.Escort, new GlobalTargetInfo(escort)); Result("mech_escort=" + escort.thingIDNumber); return true;
            }
            if (op == "mech_carrier_release")
            {
                Require(ModsConfig.BiotechActive, "Biotech未启用"); Count(p, 4);
                Pawn mech = FindThing(map, Number(p, 3)) as Pawn; Require(ControlledMech(mech) && mech.Spawned && !mech.Downed, "需要AI控制的可行动机械体");
                var carrier = mech.TryGetComp<CompMechCarrier>(); Require(carrier != null, "不是携带战斗子机的机械体");
                var report = carrier.CanSpawn; Require(report.Accepted, "原生释放条件不满足：" + report.Reason);
                carrier.TrySpawnPawns(); Result("carrier_release_native=1（消耗原生库存并遵守冷却，不免费生成）"); return true;
            }
            if (op == "mech_bill")
            {
                Require(ModsConfig.BiotechActive, "Biotech未启用"); Count(p, 7);
                Pawn actor = Actor(map, Number(p, 3)); Require(actor.mechanitor != null, "需要机械师");
                var table = FindThing(map, Number(p, 4)) as Building_MechGestator; Require(table != null, "目标不是孕育器"); Owned(table);
                RecipeDef recipe = DefDatabase<RecipeDef>.GetNamedSilentFail(p[5]); int count = Number(p, 6);
                Require(count > 0 && count <= 100 && recipe != null && recipe.AvailableNow && table.def.AllRecipes.Contains(recipe), "配方未解锁/不适用或次数超出1..100");
                var bill = table.BillStack.Bills.OfType<Bill_Mech>().FirstOrDefault(b => b.recipe == recipe && b.PawnRestriction == actor);
                if (bill == null)
                {
                    Require(table.BillStack.Count < BillStack.MaxCount, "账单已满");
                    bill = BillUtility.MakeNewBill(recipe) as Bill_Mech; Require(bill != null, "不是原生机械体账单");
                    bill.SetPawnRestriction(actor); table.BillStack.AddBill(bill);
                }
                bill.repeatMode = BillRepeatModeDefOf.RepeatCount; bill.repeatCount = count;
                Result("mech_bill_set recipe=" + recipe.defName + " count=" + count + " mechanitor=" + actor.thingIDNumber + "（等待原版材料、带宽、孕育/复活工作）"); return true;
            }
            if (op == "scanner_init" || op == "scanner_cancel")
            {
                Require(ModsConfig.BiotechActive, "Biotech未启用"); Count(p, 4);
                var scanner = FindThing(map, Number(p, 3)) as Building_SubcoreScanner; Require(scanner != null, "需要次核心扫描器"); Owned(scanner);
                if (op == "scanner_cancel" && scanner.Occupant != null && scanner.DestroyOccupantBrain) throw new NotSupportedException("中止撕裂扫描可能危及被扫描者，请玩家亲自确认");
                string label = (op == "scanner_init" ? "SubcoreScannerStart" : scanner.Occupant == null ? "CommandCancelLoad" : "CommandCancelSubcoreScan").Translate();
                var command = scanner.GetGizmos().OfType<Command_Action>().FirstOrDefault(g => g.defaultLabel == label);
                Require(command != null && !command.Disabled && command.action != null, "当前扫描器没有可用的原生开始/取消按钮");
                command.action(); Result(op + " native_action=1"); return true;
            }
            if (op == "enter" || op == "ripscan")
            {
                Count(p, 5); Pawn actor = Subject(map, Number(p, 3));
                var building = FindThing(map, Number(p, 4)) as Building_Enterable;
                Require(building is Building_GeneExtractor || building is Building_GrowthVat || building is Building_SubcoreScanner, "仅支持基因提取器、成长舱和次核心扫描器"); Owned(building);
                Require(ModsConfig.BiotechActive, "Biotech未启用");
                var scanner = building as Building_SubcoreScanner;
                if (scanner != null && scanner.DestroyOccupantBrain && op != "ripscan") throw new NotSupportedException("撕裂扫描会致死，必须由玩家开放DLC.ripscan专用权限，不能用普通进入设备工具");
                if (op == "ripscan") Require(scanner != null && scanner.DestroyOccupantBrain, "目标不是撕裂扫描器");
                var report = building.CanAcceptPawn(actor); Require(report.Accepted, "原生设备检查：" + report.Reason);
                if (!actor.IsPrisonerOfColony && !actor.Downed) Require(actor.CanReserveAndReach(building, PathEndMode.InteractionCell, Danger.Deadly), "无法到达或预留设备");
                building.SelectedPawn = actor;
                if (!actor.IsPrisonerOfColony && !actor.Downed) actor.jobs.TryTakeOrderedJob(JobMaker.MakeJob(JobDefOf.EnterBuilding, building), JobTag.Misc);
                Result("enter_queued building=" + building.thingIDNumber + "（不会瞬移入舱，实际消耗与风险由原版执行）"); return true;
            }
            if (op == "psyfocus" || op == "hemogen" || op == "deathrest_auto")
            {
                Count(p, 5); Pawn pawn = Actor(map, Number(p, 3), false);
                if (op == "psyfocus")
                {
                    Require(ModsConfig.RoyaltyActive && pawn.psychicEntropy != null && pawn.GetPsylinkLevel() > 0, "需要启用皇权且有灵能等级");
                    int n = Number(p, 4); Require(n >= 0 && n <= 100, "目标0..100"); pawn.psychicEntropy.SetPsyfocusTarget(n / 100f);
                }
                else if (op == "hemogen")
                {
                    var gene = pawn.genes?.GetFirstGeneOfType<Gene_Hemogen>(); Require(ModsConfig.BiotechActive && gene != null, "人物没有血源基因");
                    int n = Number(p, 4); Require(n >= 0 && n <= 100, "目标0..100"); gene.SetTargetValuePct(n / 100f);
                }
                else
                {
                    var gene = pawn.genes?.GetFirstGeneOfType<Gene_Deathrest>(); Require(ModsConfig.BiotechActive && gene != null, "人物没有死眠基因"); gene.autoWake = Flag(p, 4);
                }
                Result(op + " configured=1（仅设置目标，不填满资源）"); return true;
            }
            if (op == "assign")
            {
                Count(p, 5); Pawn pawn = Actor(map, Number(p, 3), false); Thing building = FindThing(map, Number(p, 4)); Owned(building);
                var comp = building.TryGetComp<CompAssignableToPawn>(); Require(comp != null, "目标无原生分配组件");
                Require(comp.HasFreeSlot || comp.AssignedPawnsForReading.Contains(pawn), "位置已被占用，不能替换玩家分配");
                var report = comp.CanAssignTo(pawn); Require(report.Accepted && comp.AssigningCandidates.Contains(pawn), "不符合原生分配条件：" + report.Reason);
                comp.TryAssignPawn(pawn); Require(comp.AssignedPawnsForReading.Contains(pawn), "原生分配未成功"); Result("assigned=" + pawn.thingIDNumber); return true;
            }
            if (op == "role")
            {
                Require(ModsConfig.IdeologyActive, "Ideology未启用"); Count(p, 5); Pawn pawn = Actor(map, Number(p, 3));
                var role = pawn.Ideo?.PreceptsListForReading.OfType<Precept_Role>().FirstOrDefault(r => r.Id == Number(p, 4));
                Require(role != null && role.Active && role.RequirementsMet(pawn), "角色不存在或人物不满足角色要求");
                Require(!role.ChosenPawns().Any(other => other != pawn), "该角色已有成员，不能覆盖原分配");
                role.Assign(pawn, true); Result("role_assigned=" + role.Id); return true;
            }
            if (op == "ability_cell" || op == "ability_pair")
            {
                Count(p, op == "ability_cell" ? 7 : 8); Pawn actor = Actor(map, Number(p, 3));
                Ability ability = Abilities(actor).FirstOrDefault(a => a.def.defName == p[4]); Require(ability != null && ability.CanCast.Accepted, "能力不存在或当前不能施放");
                var cell = new IntVec3(Number(p, op == "ability_cell" ? 5 : 6), 0, Number(p, op == "ability_cell" ? 6 : 7)); Require(cell.InBounds(map), "坐标超出地图");
                var target = op == "ability_cell" ? new LocalTargetInfo(cell) : new LocalTargetInfo(FindThing(map, Number(p, 5)));
                var dest = op == "ability_cell" ? LocalTargetInfo.Invalid : new LocalTargetInfo(cell);
                Require(ability.CanApplyOn(target) && ability.AICanTargetNow(target), "原生能力目标检查失败");
                if (dest.IsValid)
                {
                    var selector = ability.comps.OfType<CompAbilityEffect_WithDest>().FirstOrDefault();
                    if (selector == null) throw new NotSupportedException("此能力没有兼容的双目标接口，请玩家指定目标");
                    selector.SetTarget(target);
                    Require(selector.ValidateTarget(dest, false), "原生第二目标检查失败");
                }
                ability.QueueCastingJob(target, dest); Result("ability_queued=" + ability.def.defName); return true;
            }
            if (op.StartsWith("ghoul_", StringComparison.Ordinal)) { Ghoul(map, p); return true; }
            if (op == "prioritize") { PrioritizeDlcWork(map, p); return true; }
            return false;
        }

        private static string PawnDlcStatus(Pawn pawn)
        {
            var s = new StringBuilder("DLC_PAWN id=" + pawn.thingIDNumber + " name=" + Clean(pawn.LabelShort) + " owner=" + AICoopGameComponent.Current.GetOwner(pawn));
            if (ModsConfig.RoyaltyActive && pawn.royalty != null)
                s.Append(" titles=").Append(string.Join(",", pawn.royalty.AllTitlesForReading.Select(t => t.def.defName).ToArray()))
                    .Append(" throneMissing=").Append(Clean(string.Join(";", pawn.royalty.GetUnmetThroneroomRequirements(false, false).ToArray())))
                    .Append(" bedroomMissing=").Append(Clean(string.Join(";", pawn.royalty.GetUnmetBedroomRequirements(false, false).ToArray())));
            if (pawn.psychicEntropy != null && pawn.GetPsylinkLevel() > 0) s.Append(" psyfocus=").Append(pawn.psychicEntropy.CurrentPsyfocus).Append(" psyfocusTarget=").Append(pawn.psychicEntropy.TargetPsyfocus).Append(" entropy=").Append(pawn.psychicEntropy.EntropyValue);
            if (pawn.genes != null && ModsConfig.BiotechActive) s.Append(" xenotype=").Append(pawn.genes.Xenotype?.defName ?? "custom").Append(" genes=").Append(string.Join(",", pawn.genes.GenesListForReading.Select(g => g.def.defName + (g.Active ? "" : "(inactive)")).ToArray()));
            if (pawn.Ideo != null && ModsConfig.IdeologyActive) s.Append(" ideo=").Append(Clean(pawn.Ideo.name)).Append(" roles=").Append(string.Join(",", pawn.Ideo.PreceptsListForReading.OfType<Precept_Role>().Select(r => r.Id + ":" + Clean(r.Label) + ":eligible=" + r.RequirementsMet(pawn)).ToArray()));
            if (pawn.IsMutant) s.Append(" mutant=").Append(pawn.mutant.Def.defName);
            if (pawn.needs != null) s.Append(" needs=").Append(string.Join(",", pawn.needs.AllNeeds.Select(n => n.def.defName + ":" + Math.Round(n.CurLevelPercentage * 100)).ToArray()));
            return s.ToString();
        }

        private static void Ghoul(Map map, string[] p)
        {
            Require(ModsConfig.AnomalyActive, "Anomaly未启用");
            string op = p[1].ToLowerInvariant(); Pawn pawn = op == "ghoul_infuse" ? Subject(map, Number(p, 3)) : Actor(map, Number(p, 3));
            if (op == "ghoul_infuse")
            {
                Count(p, 4); Require(!pawn.IsMutant && !Find.QuestManager.IsReservedByAnyQuest(pawn), "不能转化已有变异者或任务保留人物");
                var recipe = DefDatabase<RecipeDef>.GetNamedSilentFail("GhoulInfusion");
                Require(recipe != null && recipe.AvailableNow && recipe.AvailableOnNow(pawn, null), "食尸鬼灌注未解锁或不适用");
                Require(!pawn.health.surgeryBills.Bills.Any(b => b.recipe == recipe), "已存在灌注手术");
                var bill = new Bill_Medical(recipe, null);
                pawn.health.surgeryBills.AddBill(bill);
                AICoopGameComponent.Current.TrackGhoulConversion(pawn, bill);
                Result("ghoul_infusion_surgery_queued（需要医生、材料、手术和风险，未立即转化）"); return;
            }
            Require(pawn.IsMutant && pawn.mutant.Def == MutantDefOf.Ghoul, "目标不是食尸鬼");
            if (op == "ghoul_undraft") { Count(p, 4); pawn.drafter.Drafted = false; Result("ghoul_undrafted"); return; }
            if (op == "ghoul_rest")
            {
                Count(p, 5); var bed = FindThing(map, Number(p, 4)) as Building_Bed; Require(bed != null, "需要床铺"); Owned(bed);
                var option = bed.GetBedRestFloatMenuOption(pawn); Require(option != null && !option.Disabled && option.action != null, "原生休息选项不可用");
                option.action(); Result("ghoul_rest_queued"); return;
            }
            if (op == "ghoul_feed")
            {
                Count(p, 5); Thing food = FindThing(map, Number(p, 4));
                Require(food.def.ingestible != null && food.IngestibleNow && food.def.ingestible.foodType.HasFlag(FoodTypeFlags.Meat) && FoodUtility.WillEat(pawn, food) && pawn.CanReserveAndReach(food, PathEndMode.ClosestTouch, Danger.Deadly), "需要可食用且可达的生肉");
                Job eat = JobMaker.MakeJob(JobDefOf.Ingest, food); eat.count = Math.Min(food.stackCount, Math.Max(1, FoodUtility.WillIngestStackCountOf(pawn, food.def, food.GetStatValue(StatDefOf.Nutrition))));
                pawn.jobs.TryTakeOrderedJob(eat, JobTag.Misc); Result("ghoul_feed_queued"); return;
            }
            Job job;
            if (op == "ghoul_move")
            {
                Count(p, 6); var cell = new IntVec3(Number(p, 4), 0, Number(p, 5)); Require(cell.InBounds(map) && cell.Standable(map) && pawn.CanReach(cell, PathEndMode.OnCell, Danger.Deadly), "目的地不可达");
                var combatArea = AICoopCombatArea.Get(map); Require(combatArea == null || combatArea.TrueCount == 0 || combatArea[cell], "目的地不在AI战斗区");
                job = JobMaker.MakeJob(JobDefOf.Goto, cell);
            }
            else if (op == "ghoul_attack")
            {
                Count(p, 5); Thing enemy = FindThing(map, Number(p, 4)); Require(enemy.HostileTo(pawn) && pawn.CanReach(enemy, PathEndMode.Touch, Danger.Deadly), "目标非敌对或不可达");
                var combatArea = AICoopCombatArea.Get(map); Require(combatArea == null || combatArea.TrueCount == 0 || combatArea[enemy.Position], "目标不在AI战斗区");
                job = JobMaker.MakeJob(JobDefOf.AttackMelee, enemy);
            }
            else throw new NotSupportedException("没有对应食尸鬼工具，请用dlc_options读取原生交互或请求玩家");
            Require(pawn.drafter != null, "食尸鬼无法征召"); pawn.drafter.Drafted = true; pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc); Result(op + " queued=1");
        }

        private static void PrioritizeDlcWork(Map map, string[] p)
        {
            Count(p, 6); Pawn actor = Actor(map, Number(p, 3)); Thing target = FindThing(map, Number(p, 4));
            string action = p[5]; string worker;
            switch (action)
            {
                case "study": worker = "WorkGiver_StudyInteract"; break;
                case "dark_study": worker = "WorkGiver_DarkStudyInteract"; break;
                case "tend_entity": worker = "WorkGiver_TendEntity"; break;
                case "bioferrite": worker = "WorkGiver_ExtractBioferrite"; break;
                case "empty_harvester": worker = "WorkGiver_TakeBioferriteOutOfHarvester"; break;
                case "suppress": worker = "WorkGiver_Warden_SuppressActivity"; break;
                case "interrogate": worker = "WorkGiver_Warden_InterrogateIdentity"; break;
                case "feed_baby": worker = "WorkGiver_FeedBabyManually"; break;
                case "baby_safety": worker = "WorkGiver_BringBabyToSafety"; break;
                case "play_baby": worker = "WorkGiver_PlayWithBaby"; break;
                default: throw new NotSupportedException("此优先工作尚未适配");
            }
            bool biotech = action.EndsWith("baby", StringComparison.Ordinal) || action == "baby_safety";
            Require(biotech ? ModsConfig.BiotechActive : ModsConfig.AnomalyActive, "对应DLC未启用");
            var def = DefDatabase<WorkGiverDef>.AllDefs.FirstOrDefault(d => d.giverClass?.Name == worker);
            var giver = def?.Worker as WorkGiver_Scanner;
            Require(giver != null && !actor.WorkTypeIsDisabled(def.workType) && !giver.ShouldSkip(actor, true), "人员能力或原生工作条件不满足");
            Require(actor.CanReach(target, PathEndMode.Touch, Danger.Deadly) && giver.HasJobOnThing(actor, target, true), "原版未提供此目标的工作，先检查目标/研究/医疗/权限");
            Job job = giver.JobOnThing(actor, target, true); Require(job != null, "原版未生成工作");
            actor.jobs.TryTakeOrderedJob(job, JobTag.Misc); Result("prioritize_queued=" + action);
        }
    }

    [HarmonyPatch(typeof(Recipe_GhoulInfusion), nameof(Recipe_GhoulInfusion.ApplyOnPawn))]
    internal static class AICoopGhoulOwnershipPatch
    {
        private static void Postfix(Pawn __0, Bill __4)
        {
            if (__0 != null && __0.Faction == Faction.OfPlayer && __0.IsMutant && __0.mutant.Def == MutantDefOf.Ghoul)
                AICoopGameComponent.Current?.FinishGhoulConversion(__0, __4);
        }
    }
}
