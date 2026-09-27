using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;

namespace AICoopCompanion
{
    // Native, non-debug operations only. No spawning, instant research, forced success or flight UI.
    internal static partial class AICoopDlcActions
    {
        internal static string Permission(string[] p)
        {
            if (p.Length < 2) return "DLC.query";
            switch (p[1].ToLowerInvariant())
            {
                case "status": case "inspect": case "options": case "boss_options": case "features": return "DLC.query";
                case "mech_control": case "mech_repair": case "mech_haul": case "mech_autorepair": case "mech_escort": case "mech_bill": case "mech_carrier_release": return "DLC.mech";
                case "mech_disconnect": case "mech_disassemble": return "DLC.mech_destructive";
                case "pollution": return "DLC.pollution";
                case "psyfocus": case "hemogen": case "deathrest_auto": case "assign": return "DLC.care";
                case "ability_cell": case "ability_pair": return "DLC.ability";
                case "role": return "DLC.role";
                case "enter": case "scanner_init": case "scanner_cancel": return "DLC.device";
                case "ripscan": return "DLC.ripscan";
                case "ghoul_infuse": return "DLC.medical";
                case "ghoul_move": case "ghoul_attack": case "ghoul_undraft": case "ghoul_feed": case "ghoul_rest": return "DLC.ghoul";
                case "prioritize": return p.Length > 5 && (p[5] == "study" || p[5] == "dark_study") ? "DLC.study" : "DLC.maintenance";
                case "mech_mode": case "mech_group": case "mech_charge": case "mech_order": return "DLC.mech";
                case "fish": case "fish_config": return "DLC.fishing";
                case "refuel": return "DLC.refuel";
                case "contain": return "DLC.contain";
                case "study": return "DLC.study";
                case "boss_call": return "DLC.boss";
                case "ability": return "DLC.ability";
                case "interact": return "DLC.interact";
                default: return "DLC.special";
            }
        }

        internal static void Execute(string[] p)
        {
            if (p.Length < 3) throw new ArgumentException("格式：DLC 操作 地图ID ...；先用 dlc_status 查询。");
            Map map = Find.Maps.FirstOrDefault(m => m.uniqueID == Number(p, 2));
            if (map == null) throw new ArgumentException("地图不存在。");
            string op = p[1].ToLowerInvariant();
            if (ExecuteExtended(map, p)) return;
            if (op == "status") { Count(p, 3); Result(Status(map)); return; }
            if (op == "fish") { Fish(map, p); return; }
            if (op == "fish_config")
            {
                Require(ModsConfig.OdysseyActive, "Odyssey未启用"); Count(p, 6);
                Zone_Fishing zone = map.zoneManager.AllZones.OfType<Zone_Fishing>().FirstOrDefault(z => z.ID == Number(p, 3));
                Require(zone != null, "捕鱼区不存在");
                int target = Number(p, 4), reserve = Number(p, 5);
                Require(target >= 1 && target <= 100000 && reserve >= 0 && reserve <= 100, "库存1..100000，保留鱼群百分比0..100");
                zone.repeatMode = FishRepeatMode.TargetCount; zone.targetCount = target; zone.targetPopulationPct = reserve / 100f;
                zone.RecheckPausedDueToResourceCount(); Result("fishing_configured zone=" + zone.ID); return;
            }
            Thing thing = FindThing(map, Number(p, 3));
            if (op == "inspect") { Count(p, 4); Result(Inspect(thing)); return; }
            if (op == "boss_options" || op == "boss_call")
            {
                Require(ModsConfig.BiotechActive, "Biotech未启用"); Count(p, op == "boss_options" ? 4 : 5);
                Pawn caller = thing as Pawn;
                Require(caller?.mechanitor != null && caller.Spawned && !caller.Downed && !caller.InMentalState &&
                    AICoopGameComponent.Current.CanAIControl(caller), "需要可行动的AI可控机械师");
                var command = new Command_CallBossgroup(caller.mechanitor);
                var property = typeof(Command_CallBossgroup).GetProperty("FloatMenuOptions", BindingFlags.Instance | BindingFlags.NonPublic);
                var disabledMethod = typeof(Command_CallBossgroup).GetMethod("IsDisabled", BindingFlags.Instance | BindingFlags.NonPublic);
                Require(property != null && disabledMethod != null, "当前版本没有兼容的原生Boss选项接口，请玩家操作");
                object[] disabledArgs = { null };
                bool disabled = (bool)disabledMethod.Invoke(command, disabledArgs);
                var options = ((IEnumerable<FloatMenuOption>)property.GetValue(command, null)).ToList();
                if (op == "boss_options") Result("boss_options default=denied disabled=" + disabled + " reason=" + disabledArgs[0] + "\n" + string.Join("\n", options.Select(o => Key(o) + " disabled=" + o.Disabled + " " + Clean(o.Label)).ToArray()));
                else
                {
                    Require(!disabled, "原生Boss召唤不可用：" + disabledArgs[0]);
                    var matches = options.Where(o => Key(o) == p[4]).ToList();
                    Require(matches.Count == 1 && !matches[0].Disabled && matches[0].action != null, "召唤条件不满足或选项已变化");
                    matches[0].action(); Result("boss_native_order_issued（如出现原生风险确认窗口，由玩家确认；尚未保证召唤成功）");
                }
                return;
            }
            if (op == "mech_order") { MechOrder(thing as Pawn, p); return; }
            if (op == "ability")
            {
                Count(p, 6);
                Pawn caster = thing as Pawn;
                Require(caster != null && caster.Spawned && !caster.Downed && !caster.Dead && !caster.InMentalState &&
                    (AICoopGameComponent.Current.CanAIControl(caster) || ControlledMech(caster)), "施术者不可控或无法行动");
                var ability = Abilities(caster).FirstOrDefault(a => a.def.defName == p[4]);
                Require(ability != null && ability.CanCast.Accepted, "能力不存在、冷却中或资源不足");
                var target = new LocalTargetInfo(FindThing(map, Number(p, 5)));
                Require(ability.CanApplyOn(target) && ability.AICanTargetNow(target), "原生能力目标检查失败");
                ability.QueueCastingJob(target, LocalTargetInfo.Invalid);
                Result("ability_queued=" + ability.def.defName + "（等待施放，非立即生效）"); return;
            }
            if (op.StartsWith("mech_", StringComparison.Ordinal)) { Mech(thing as Pawn, p); return; }
            if (op == "refuel")
            {
                Count(p, 6); Owned(thing);
                var fuel = thing.TryGetComp<CompRefuelable>(); Require(fuel != null, "目标不支持燃料配置");
                int percent = Number(p, 4); Require(percent >= 0 && percent <= 100, "燃料目标百分比0..100");
                bool enabled = Flag(p, 5);
                fuel.TargetFuelLevel = fuel.Props.fuelCapacity * percent / 100f; fuel.allowAutoRefuel = enabled;
                Result("refuel_configured（仅设置搬运补给，不会生成燃料）"); return;
            }
            if (op == "contain")
            {
                Require(ModsConfig.AnomalyActive, "Anomaly未启用"); Count(p, 5);
                var comp = thing.TryGetComp<CompHoldingPlatformTarget>();
                var platform = FindThing(map, Number(p, 4)) as Building_HoldingPlatform;
                Require(comp != null && comp.CanBeCaptured, "目标当前不可捕获");
                Require(platform != null && !platform.Occupied, "需要空置收容平台"); Owned(platform);
                Require(!Things(map).Any(t => t != thing && t.TryGetComp<CompHoldingPlatformTarget>()?.targetHolder == platform), "平台已有捕获目标");
                comp.targetHolder = platform;
                Result("capture_designated target=" + thing.thingIDNumber + " platform=" + platform.thingIDNumber + "（等待搬运，尚未完成收容）"); return;
            }
            if (op == "study")
            {
                Require(ModsConfig.AnomalyActive, "Anomaly未启用"); Count(p, 5);
                var study = thing.TryGetComp<CompStudiable>(); bool enable = Flag(p, 4);
                Require(study != null, "目标不可研究");
                Require(!enable || study.EverStudiable(), "研究尚未解锁或目标不可研究");
                study.SetStudyEnabled(enable); Result("study_enabled=" + enable + " target=" + thing.thingIDNumber); return;
            }
            if (op == "options" || op == "interact")
            {
                Count(p, op == "options" ? 5 : 6);
                Require(!(thing is Building_GravEngine) && thing.TryGetComp<CompPilotConsole>() == null,
                    "飞船起飞界面、目的地/落点选择由玩家操作，请使用request_player");
                Pawn pawn = FindThing(map, Number(p, 4)) as Pawn;
                Require(pawn != null && pawn.Spawned && !pawn.Dead && !pawn.Downed && !pawn.InMentalState &&
                    AICoopGameComponent.Current.CanAIControl(pawn), "需要可行动且允许AI控制的殖民者");
                var options = NativeOptions(thing, pawn);
                if (op == "options")
                {
                    Result("native_options special_permission=默认禁止\n" + string.Join("\n", options.Select(o =>
                        Key(o) + " disabled=" + o.Disabled + " " + Clean(o.Label)).ToArray()) +
                        "\n已包含1.6原生菜单提供器；无可用选项或需专用窗口时用request_player，不得猜测指令。"); return;
                }
                var matches = options.Where(o => Key(o) == p[5]).ToList();
                Require(matches.Count == 1, "选项已变化或不存在；重新读取options");
                var option = matches[0]; Require(!option.Disabled && option.action != null, "原生选项不可用：" + option.Label);
                Require(!IsUnsafeUiOption(option), "此选项涉及开发者/飞船界面，必须由玩家操作");
                if (thing is Building_SubcoreScanner || thing is Building_GeneExtractor || thing is Building_GrowthVat || thing is Pawn)
                    throw new NotSupportedException("人物及扫描/成长/基因设备须使用对应专用工具，不能通过通用交互绕过独立权限");
                option.action();
                Result("native_order_issued=" + Clean(option.Label) + "（已提交原生操作，不代表已完成；若弹出确认界面请玩家处理）"); return;
            }
            throw new NotSupportedException("未支持的DLC操作；请玩家完成该步骤，不能模拟成功。");
        }

        private static void Mech(Pawn pawn, string[] p)
        {
            Require(ModsConfig.BiotechActive, "Biotech未启用");
            Require(pawn != null && pawn.mechanitor != null && AICoopGameComponent.Current.CanAIControl(pawn), "需要AI可控机械师");
            Require(pawn.mechanitor.CanControlMechs.Accepted, "机械师当前不能控制机械体");
            string op = p[1].ToLowerInvariant();
            Count(p, op == "mech_charge" ? 7 : 6);
            var group = pawn.mechanitor.controlGroups.FirstOrDefault(g => g.Index == Number(p, 4));
            Require(group != null, "控制组不存在，请读取status提供的group索引");
            if (op == "mech_mode")
            {
                Require(p[5] == "Work" || p[5] == "Recharge" || p[5] == "SelfShutdown", "模式仅支持Work/Recharge/SelfShutdown");
                group.SetWorkMode(DefDatabase<MechWorkModeDef>.GetNamed(p[5]));
            }
            else if (op == "mech_charge")
            {
                int low = Number(p, 5), high = Number(p, 6);
                Require(low >= 0 && high <= 100 && low <= high, "充电阈值必须0≤下限≤上限≤100");
                group.mechRechargeThresholds = new FloatRange(low / 100f, high / 100f);
            }
            else if (op == "mech_group")
            {
                Pawn mech = pawn.mechanitor.ControlledPawns.FirstOrDefault(m => m.thingIDNumber == Number(p, 5));
                Require(mech != null, "此机械体不受该机械师控制"); group.Assign(mech);
            }
            else throw new ArgumentException("未知机械管理操作");
            Result(op + " group=" + group.Index);
        }

        private static bool ControlledMech(Pawn mech)
        {
            Pawn owner = mech == null ? null : MechanitorUtility.GetOverseer(mech);
            return owner?.mechanitor != null && AICoopGameComponent.Current.CanAIControl(owner) && owner.mechanitor.ControlledPawns.Contains(mech);
        }

        private static void MechOrder(Pawn mech, string[] p)
        {
            Require(ModsConfig.BiotechActive, "Biotech未启用");
            Require(mech != null && ControlledMech(mech) && mech.Spawned && !mech.Downed && !mech.Dead, "需要AI机械师控制的可行动机械体");
            string action = p.Length > 4 ? p[4] : "";
            if (action == "undraft") { Count(p, 5); mech.drafter.Drafted = false; Result("mech_undrafted"); return; }
            Require(MechanitorUtility.CanDraftMech(mech).Accepted, "原版不允许此机械体征召");
            LocalTargetInfo target;
            Job job;
            if (action == "move")
            {
                Count(p, 7); IntVec3 cell = new IntVec3(Number(p, 5), 0, Number(p, 6));
                Require(cell.InBounds(mech.Map) && cell.Standable(mech.Map) && mech.CanReach(cell, PathEndMode.OnCell, Danger.Deadly), "目的地不可达");
                target = new LocalTargetInfo(cell); job = JobMaker.MakeJob(JobDefOf.Goto, target);
            }
            else if (action == "attack")
            {
                Count(p, 6); Thing enemy = FindThing(mech.Map, Number(p, 5)); Require(enemy.HostileTo(Faction.OfPlayer), "仅能攻击敌对目标");
                target = new LocalTargetInfo(enemy);
                Verb verb = mech.TryGetAttackVerb(enemy, false); Require(verb != null, "没有可用攻击方式");
                job = JobMaker.MakeJob(verb.IsMeleeAttack ? JobDefOf.AttackMelee : JobDefOf.AttackStatic, target); job.verbToUse = verb;
            }
            else throw new ArgumentException("操作仅支持move/attack/undraft");
            Require(MechanitorUtility.InMechanitorCommandRange(mech, target), "目标超出机械师指挥范围");
            mech.drafter.Drafted = true; mech.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            Result("mech_order_queued=" + action);
        }

        private static void Fish(Map map, string[] p)
        {
            Require(ModsConfig.OdysseyActive, "Odyssey未启用"); Count(p, 7);
            int x1 = Number(p, 3), z1 = Number(p, 4), x2 = Number(p, 5), z2 = Number(p, 6);
            Require(new IntVec3(x1, 0, z1).InBounds(map) && new IntVec3(x2, 0, z2).InBounds(map), "坐标超出地图");
            CellRect rect = CellRect.FromLimits(x1, z1, x2, z2); Require(rect.Area <= 10000, "单次最多10000格");
            var cells = rect.Cells.Where(c => map.zoneManager.ZoneAt(c) == null && c.GetWaterBodyType(map) != WaterBodyType.None &&
                c.GetTerrain(map).passability != Traversability.Impassable && map.waterBodyTracker.AnyFishPopulationAt(c)).ToList();
            Require(cells.Count > 0, "范围内没有未分区且可捕鱼的水域");
            // Each native fishing zone has one water type. Do not mix fresh and salt water.
            foreach (var water in cells.GroupBy(c => c.GetWaterBodyType(map)))
            {
                var zone = new Zone_Fishing(map.zoneManager);
                map.zoneManager.RegisterZone(zone);
                foreach (var cell in water) zone.AddCell(cell);
                Result("fishing_zone=" + zone.ID + " cells=" + zone.Cells.Count + "（使用原生捕鱼，不生成鱼）");
            }
        }

        private static string Status(Map map)
        {
            var s = new StringBuilder("DLC active royalty=" + ModsConfig.RoyaltyActive + " ideology=" + ModsConfig.IdeologyActive + " biotech=" + ModsConfig.BiotechActive + " anomaly=" + ModsConfig.AnomalyActive + " odyssey=" + ModsConfig.OdysseyActive);
            foreach (Pawn pawn in map.mapPawns.AllPawnsSpawned.Where(p => p.Faction == Faction.OfPlayer).Take(80)) s.Append('\n').Append(PawnDlcStatus(pawn));
            foreach (Pawn p in map.mapPawns.FreeColonists.Where(p => p.mechanitor != null))
            {
                s.Append("\nMECHANITOR id=").Append(p.thingIDNumber).Append(" owner=").Append(AICoopGameComponent.Current.GetOwner(p))
                    .Append(" bandwidth=").Append(p.mechanitor.UsedBandwidth).Append('/').Append(p.mechanitor.TotalBandwidth);
                foreach (var g in p.mechanitor.controlGroups)
                    s.Append("\nGROUP owner=").Append(p.thingIDNumber).Append(" group=").Append(g.Index).Append(" mode=").Append(g.WorkMode.defName)
                        .Append(" charge=").Append(g.mechRechargeThresholds).Append(" mechs=").Append(string.Join(",", g.MechsForReading.Select(m => m.thingIDNumber.ToString()).ToArray()));
            }
            var objects = Things(map).Where(IsDlc).OrderBy(t => t.thingIDNumber).ToList();
            foreach (Thing t in objects.Where(t => t is Building_GravEngine || t is Building_HoldingPlatform || t is Building_MechGestator || t is Building_MechCharger ||
                t.TryGetComp<CompStudiable>() != null).Take(80)) s.Append('\n').Append(Inspect(t));
            s.Append("\nDLC_OBJECTS total=").Append(objects.Count).Append("（设备摘要最多80项；指定对象用dlc_inspect或map_scan查询）");
            foreach (var z in map.zoneManager.AllZones.OfType<Zone_Fishing>())
                s.Append("\nFISH_ZONE id=").Append(z.ID).Append(" cells=").Append(z.Cells.Count).Append(" mode=").Append(z.repeatMode)
                    .Append(" target=").Append(z.targetCount).Append(" reserve=").Append(z.targetPopulationPct);
            return s.ToString();
        }

        private static string Inspect(Thing t)
        {
            var s = new StringBuilder("OBJECT id=" + t.thingIDNumber + " def=" + t.def.defName + " pos=" + t.Position + " " + Clean(t.GetInspectString()));
            var table = t as Building_WorkTable;
            if (table != null) s.Append("\nRECIPES ").Append(string.Join(",", table.def.AllRecipes.Where(r => r.AvailableNow).Select(r => r.defName).ToArray()));
            var target = t.TryGetComp<CompHoldingPlatformTarget>();
            if (target != null) s.Append("\nCONTAIN capture=").Append(target.CanBeCaptured).Append(" held=").Append(target.CurrentlyHeldOnPlatform).Append(" holder=").Append(target.targetHolder?.thingIDNumber).Append(" escaping=").Append(target.isEscaping);
            var pawn = t as Pawn;
            if (pawn != null) s.Append('\n').Append(PawnDlcStatus(pawn)).Append("\nABILITIES ").Append(string.Join(",", Abilities(pawn).Select(a => a.def.defName + ":canCast=" + a.CanCast.Accepted).ToArray()));
            var engine = t as Building_GravEngine;
            if (engine != null) s.Append("\nGRAVSHIP fuel=").Append(engine.TotalFuel).Append('/').Append(engine.MaxFuel).Append(" maxDistance=").Append(engine.MaxLaunchDistance)
                .Append(" missing=").Append(string.Join(",", engine.MissingComponents.Select(d => d.defName).ToArray())).Append(" flight_and_destination=player_only");
            return s.ToString();
        }

        private static IEnumerable<Thing> Things(Map map)
        {
            return map.listerThings.AllThings.Concat(map.listerThings.AllThings.OfType<Building_HoldingPlatform>().Where(p => p.HeldPawn != null).Select(p => (Thing)p.HeldPawn)).Distinct();
        }
        private static Thing FindThing(Map map, int id) { var t = Things(map).FirstOrDefault(x => x.thingIDNumber == id); Require(t != null, "对象不存在：" + id); return t; }
        private static bool IsDlc(Thing t) { string id = t.def.modContentPack?.PackageId ?? ""; return new[] { "ludeon.rimworld.royalty", "ludeon.rimworld.ideology", "ludeon.rimworld.biotech", "ludeon.rimworld.anomaly", "ludeon.rimworld.odyssey" }.Contains(id); }
        private static bool IsUnsafeUiOption(FloatMenuOption o)
        {
            string text = (o.Label + " " + o.action?.Method.DeclaringType?.FullName + " " + o.action?.Method.Name).ToLowerInvariant();
            return new[] { "debug", "dev:", "developer", "pilotconsole", "gravengine", "launch", "destination", "起飞", "目的地", "开发者" }.Any(text.Contains);
        }
        private static string Key(FloatMenuOption o)
        {
            using (var hash = SHA256.Create()) return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(o.Label + "|" + o.action?.Method.DeclaringType?.FullName + "|" + o.action?.Method.Name))).Replace("-", "").Substring(0, 16);
        }
        private static int Number(string[] p, int i) { int n; if (i >= p.Length || !int.TryParse(p[i], out n)) throw new ArgumentException("缺少或无效整数参数#" + i); return n; }
        private static bool Flag(string[] p, int i) { int n = Number(p, i); Require(n == 0 || n == 1, "开关只能0/1"); return n == 1; }
        private static void Count(string[] p, int n) { Require(p.Length == n, "参数数量错误，查看CLI工具示例"); }
        private static void Require(bool ok, string reason) { if (!ok) throw new ArgumentException(reason); }
        private static void Owned(Thing t) { Require(t.Faction == Faction.OfPlayer, "目标必须属于玩家殖民地"); }
        private static string Clean(string text) { return (text ?? "-").Replace('\n', ' ').Replace('\r', ' '); }
        private static void Result(string text) { AICoopGameComponent.Current.AddCommandResult("OK DLC " + text); }
    }
}
