using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using RimWorld;
using RimWorld.Planet;
using Verse;

namespace AICoopCompanion
{
    internal static class AICoopWorldActions
    {
        public static void Execute(string[] parts, string line)
        {
            if (parts.Length < 3)
            {
                Log("[拒绝] WORLD 格式：caravan|move|load|launch|trade|quest。");
                return;
            }
            switch (parts[1].ToLowerInvariant())
            {
                case "caravan": FormCaravan(parts, line); break;
                case "move": MoveCaravan(parts, line); break;
                case "load": LoadTransporter(parts, line); break;
                case "launch": LaunchTransporter(parts, line); break;
                case "trade": Trade(parts, line); break;
                case "quest": HandleQuest(parts, line); break;
                default: Log("[拒绝] 未知 WORLD 操作：" + parts[1]); break;
            }
        }

        public static string BuildPromptSection(AICoopGameComponent component)
        {
            if (Find.World == null || Find.WorldObjects == null) return "WORLD_STATUS unavailable";
            System.Text.StringBuilder result = new System.Text.StringBuilder();
            result.AppendLine("TRADE_MODE " + (AICoopMod.Settings == null || AICoopMod.Settings.tradeMode == AICoopTradeMode.PlayerWindow ? "player_window" : "ai") + "；player_window 会打开原版贸易窗口并等待玩家操作，ai 才会自动买卖；WORLD trade ... ui 始终强制打开窗口。");
            foreach (WorldObject worldObject in Find.WorldObjects.AllWorldObjects.OrderBy(item => item.ID))
            {
                if (worldObject == null || worldObject.Destroyed) continue;
                string faction = worldObject.Faction == null ? "-" : Clean(worldObject.Faction.Name);
                string line = "WORLD_OBJECT id=" + worldObject.ID + " type=" + worldObject.GetType().Name + " tile=" + worldObject.Tile + " label=" + Clean(worldObject.Label) + " faction=" + faction;
                Settlement settlement = worldObject as Settlement;
                if (settlement != null) line += " canTrade=" + (settlement.CanTradeNow ? "1" : "0") + " trader=" + Clean(settlement.TraderName);
                MapParent mapParent = worldObject as MapParent;
                if (mapParent != null) line += " hasMap=" + (mapParent.HasMap ? "1" : "0");
                Caravan caravan = worldObject as Caravan;
                if (caravan != null)
                {
                    line += " moving=" + (caravan.pather != null && caravan.pather.Moving ? "1" : "0") + " destination=" + (caravan.pather == null ? PlanetTile.Invalid.ToString() : caravan.pather.Destination.ToString()) +
                        " aiPawns=" + string.Join(",", caravan.PawnsListForReading.Where(pawn => component != null && component.IsAI(pawn)).Select(pawn => pawn.thingIDNumber.ToString()).ToArray());
                }
                result.AppendLine(line);
            }
            foreach (Quest quest in CurrentAvailableQuests()) result.AppendLine(DescribeQuest(quest));
            return result.ToString().TrimEnd();
        }

        internal static IEnumerable<Quest> CurrentAvailableQuests()
        {
            if (Find.QuestManager == null) return Enumerable.Empty<Quest>();
            return Find.QuestManager.QuestsListForReading
                .Where(item => item != null && !item.hidden && !item.Historical && item.State == QuestState.NotYetAccepted)
                .OrderBy(item => item.id);
        }

        internal static string DescribeQuest(Quest quest)
        {
            if (quest == null) return string.Empty;
            return "WORLD_QUEST id=" + quest.id + " state=" + quest.State + " name=" + Clean(quest.name) +
                " expiresTicks=" + quest.TicksUntilExpiry + " points=" + ReadQuestMember(quest, "points", "-") +
                " challenge=" + ReadQuestMember(quest, "challengeRating", "-") + " requiresAccepter=" + (quest.RequiresAccepter ? "1" : "0") +
                " reward=" + QuestRewardSummary(quest) + " " + EvaluateQuest(quest);
        }

        private static string EvaluateQuest(Quest quest)
        {
            if (quest == null) return "aiAbility=unknown aiRisk=unknown aiReward=unknown aiRecommendation=review";

            List<Pawn> aiPawns = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists
                .Where(IsAIPawn).Where(pawn => pawn != null && !pawn.Downed && !pawn.InMentalState).ToList();
            int bestSkill = 0;
            foreach (Pawn pawn in aiPawns)
            {
                if (pawn.skills == null) continue;
                foreach (string skillName in new[] { "Shooting", "Melee", "Medicine", "Construction", "Plants", "Intellectual", "Social" })
                {
                    SkillDef skill = DefDatabase<SkillDef>.GetNamedSilentFail(skillName);
                    SkillRecord record = skill == null ? null : pawn.skills.GetSkill(skill);
                    if (record != null) bestSkill = Math.Max(bestSkill, record.Level);
                }
            }

            int challenge;
            Int32.TryParse(ReadQuestMember(quest, "challengeRating", "-"), out challenge);
            bool highRisk = challenge >= 4 || quest.points >= 1500f;
            bool mediumRisk = highRisk || challenge >= 2 || quest.points >= 700f;
            string ability = aiPawns.Count == 0 || bestSkill < 3 ? "low" : (bestSkill < 7 ? "medium" : "high");
            string risk = highRisk ? "high" : (mediumRisk ? "medium" : "low");

            string rewardText = (quest.name ?? string.Empty) + " " + quest.description + " " + QuestRewardSummary(quest);
            string lowerRewardText = rewardText.ToLowerInvariant();
            int rewardScore = 0;
            foreach (string keyword in new[] { "silver", "component", "steel", "medicine", "food", "weapon", "apparel", "research", "favor", "relic", "pawn", "transport", "白银", "组件", "钢", "药", "食物", "武器", "衣服", "研究", "荣誉", "遗物" })
            {
                if (lowerRewardText.Contains(keyword.ToLowerInvariant())) rewardScore++;
            }
            string reward = rewardScore >= 2 ? "high" : (rewardScore == 1 ? "medium" : "low");
            bool insufficient = ability == "low";
            bool riskExceedsReward = risk == "high" && reward != "high";
            string recommendation = insufficient || riskExceedsReward ? "decline_review" : "accept";
            string reason = insufficient ? "insufficient_capability" : (riskExceedsReward ? "risk_exceeds_reward" : "capability_and_reward_acceptable");
            return "aiAbility=" + ability + " aiRisk=" + risk + " aiReward=" + reward + " aiRecommendation=" + recommendation + " aiReason=" + reason;
        }

        private static string QuestRewardSummary(Quest quest)
        {
            if (quest == null) return "-";
            List<string> parts = new List<string>();
            try
            {
                if (quest.PartsListForReading != null)
                {
                    foreach (QuestPart part in quest.PartsListForReading)
                    {
                        if (part == null || part.DescriptionPart.NullOrEmpty()) continue;
                        string description = Clean(part.DescriptionPart).Trim();
                        if (!description.NullOrEmpty() && !parts.Contains(description)) parts.Add(description);
                    }
                }
            }
            catch
            {
            }
            string descriptionText = ReadQuestMember(quest, "description", string.Empty);
            string factions = string.Empty;
            try
            {
                factions = string.Join("|", (quest.InvolvedFactions ?? Enumerable.Empty<Faction>())
                    .Where(faction => faction != null).Select(faction => Clean(faction.Name)).ToArray());
            }
            catch
            {
            }
            return "desc=" + Truncate(Clean(descriptionText), 240) +
                " parts=" + Truncate(string.Join("|", parts.ToArray()), 600) +
                " factions=" + (factions.NullOrEmpty() ? "-" : Truncate(factions, 180));
        }

        private static string ReadQuestMember(Quest quest, string name, string fallback)
        {
            if (quest == null || name.NullOrEmpty()) return fallback;
            try
            {
                FieldInfo field = typeof(Quest).GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                object value = field == null ? null : field.GetValue(quest);
                return value == null ? fallback : value.ToString();
            }
            catch
            {
                return fallback;
            }
        }

        private static string Truncate(string value, int maxLength)
        {
            if (value.NullOrEmpty() || value.Length <= maxLength) return value ?? string.Empty;
            return value.Substring(0, maxLength) + "...";
        }

        private static void FormCaravan(string[] parts, string line)
        {
            if (parts.Length < 5 || parts.Length > 6)
            {
                Log("[拒绝] WORLD caravan 格式：WORLD caravan 地图ID 目的地tile AI殖民者ID,ID [物品ID[:数量],...]。");
                return;
            }
            Map map = FindMap(parts[2]);
            PlanetTile destination;
            if (map == null || !PlanetTile.TryParse(parts[3], out destination) || !destination.Valid || !map.Tile.Valid)
            {
                Log("[拒绝] 商队来源地图或目的地 tile 无效：" + line);
                return;
            }
            List<Pawn> pawns = ParseAIPawns(map, parts[4]);
            if (pawns.Count == 0)
            {
                Log("[拒绝] 没有找到可组建商队的 AI 殖民者：" + parts[4]);
                return;
            }
            Caravan caravan;
            try
            {
                caravan = CaravanMaker.MakeCaravan(pawns, Faction.OfPlayer, map.Tile, true);
                if (parts.Length == 6) AddItemsToCaravan(map, caravan, parts[5]);
                CaravanArrivalAction arrivalAction = ArrivalActionFor(destination);
                if (!caravan.pather.StartPath(destination, arrivalAction))
                {
                    caravan.Destroy();
                    Log("[拒绝] 商队无法规划到目的地 " + destination + " 的路线。");
                    return;
                }
            }
            catch (Exception ex)
            {
                Log("[拒绝] 组建商队失败：" + ex.Message);
                return;
            }
            Log("[执行] 已组建 AI 商队 " + caravan.ID + "，成员 " + pawns.Count + " 人，目的地 tile=" + destination + "。");
            Result("OK world_caravan=" + caravan.ID + " destination=" + destination);
        }

        private static void MoveCaravan(string[] parts, string line)
        {
            if (parts.Length != 4)
            {
                Log("[拒绝] WORLD move 格式：WORLD move 商队ID 目的地tile。");
                return;
            }
            int caravanId;
            PlanetTile destination;
            Caravan caravan = Int32.TryParse(parts[2], out caravanId) ? Find.WorldObjects.AllWorldObjects.OfType<Caravan>().FirstOrDefault(item => item.ID == caravanId) : null;
            if (caravan == null || !PlanetTile.TryParse(parts[3], out destination) || !destination.Valid || !IsAICaravan(caravan))
            {
                Log("[拒绝] 找不到 AI 商队、目的地无效或商队包含玩家殖民者：" + line);
                return;
            }
            if (!caravan.pather.StartPath(destination, ArrivalActionFor(destination)))
            {
                Log("[拒绝] 商队无法规划到目的地 " + destination + " 的路线。");
                return;
            }
            Log("[执行] AI 商队 " + caravan.ID + " 已改道至 " + destination + "。");
            Result("OK world_caravan_moved=" + caravan.ID + " destination=" + destination);
        }

        private static void LoadTransporter(string[] parts, string line)
        {
            if (parts.Length != 4 && parts.Length != 5)
            {
                Log("[拒绝] WORLD load 格式：WORLD load 运输舱建筑ID 物品或AI殖民者ID [数量]。");
                return;
            }
            Map map;
            CompTransporter transporter = FindTransporter(parts[2], out map);
            int targetId, count = -1;
            if (transporter == null || !Int32.TryParse(parts[3], out targetId) || (parts.Length == 5 && (!Int32.TryParse(parts[4], out count) || count <= 0)))
            {
                Log("[拒绝] 运输舱或装载目标无效：" + line);
                return;
            }
            Thing target = map.listerThings.AllThings.FirstOrDefault(item => item.thingIDNumber == targetId && item.Spawned);
            Pawn pawn = target as Pawn;
            AICoopGameComponent component = AICoopGameComponent.Current;
            if (target == null || (pawn != null && (component == null || !component.IsAI(pawn))))
            {
                Log("[拒绝] 装载目标不存在或不是 AI 殖民者：" + targetId);
                return;
            }
            if (pawn != null && pawn.Downed)
            {
                Log("[拒绝] 不能直接装载倒地殖民者，请使用原版救援流程：" + pawn.LabelShort);
                return;
            }
            if (count > 0 && target.stackCount > count) target = target.SplitOff(count);
            target.DeSpawn();
            if (!transporter.innerContainer.TryAdd(target, false))
            {
                if (!target.Destroyed) GenSpawn.Spawn(target, map.Center, map);
                Log("[拒绝] 运输舱容量不足或无法装载目标：" + targetId);
                return;
            }
            if (transporter.groupID < 0) transporter.groupID = Find.UniqueIDsManager.GetNextTransporterGroupID();
            Log("[执行] 已将 " + target.LabelShort + " 装入运输舱 " + transporter.parent.thingIDNumber + "。");
            Result("OK transporter_loaded=" + transporter.parent.thingIDNumber + " target=" + targetId);
        }

        private static void LaunchTransporter(string[] parts, string line)
        {
            if (parts.Length < 4 || parts.Length > 5)
            {
                Log("[拒绝] WORLD launch 格式：WORLD launch 运输舱建筑ID 目的地tile [caravan|land]。");
                return;
            }
            Map map;
            CompTransporter transporter = FindTransporter(parts[2], out map);
            PlanetTile destination;
            if (transporter == null || !PlanetTile.TryParse(parts[3], out destination) || !destination.Valid)
            {
                Log("[拒绝] 运输舱或目的地 tile 无效：" + line);
                return;
            }
            CompLaunchable launchable = transporter.parent.GetComp<CompLaunchable>();
            if (launchable == null)
            {
                Log("[拒绝] 目标不是可发射运输舱：" + parts[2]);
                return;
            }
            AcceptanceReport canLaunch = launchable.CanLaunch();
            if (!canLaunch.Accepted)
            {
                Log("[拒绝] 运输舱当前不能发射：" + canLaunch.Reason);
                return;
            }
            if (Find.World == null || Find.WorldGrid == null || Find.World.Impassable(destination))
            {
                Log("[拒绝] 运输舱目的地不可到达：" + destination);
                return;
            }
            WorldObject destinationObject = Find.WorldObjects.WorldObjectAt<WorldObject>(destination);
            if (destinationObject != null && destinationObject.def != null && !destinationObject.def.validLaunchTarget)
            {
                Log("[拒绝] 运输舱目的地世界对象不允许发射：" + destinationObject.Label);
                return;
            }
            int travelDistance = Find.WorldGrid.TraversalDistanceBetween(map.Tile, destination, true, Int32.MaxValue, true);
            if (travelDistance > launchable.MaxLaunchDistanceAtFuelLevel(launchable.FuelLevel, destination.Layer))
            {
                Log("[拒绝] 运输舱燃料不足以到达目的地 " + destination + "（距离 " + travelDistance + "）。");
                return;
            }
            List<CompTransporter> group = new List<CompTransporter>();
            if (transporter.groupID >= 0) TransporterUtility.GetTransportersInGroup(transporter.groupID, map, group);
            if (group.Count == 0) group.Add(transporter);
            TransportersArrivalAction arrivalAction = parts.Length == 5 && parts[4].Equals("land", StringComparison.OrdinalIgnoreCase)
                ? LandAction(destination)
                : new TransportersArrivalAction_FormCaravan();
            try
            {
                TransporterUtility.InitiateLoading(group);
                launchable.TryLaunch(destination, arrivalAction);
            }
            catch (Exception ex)
            {
                Log("[拒绝] 运输舱发射失败：" + ex.Message);
                return;
            }
            Log("[执行] 已发射运输舱 " + parts[2] + "，目的地 tile=" + destination + "。");
            Result("OK transporter_launched=" + parts[2] + " destination=" + destination);
        }

        private static void Trade(string[] parts, string line)
        {
            if (parts.Length != 5 && parts.Length != 7)
            {
                Log("[拒绝] WORLD trade 格式：WORLD trade 世界对象ID AI谈判者ID ui，或 WORLD trade 世界对象ID AI谈判者ID buy|sell ThingDef 数量。");
                return;
            }
            int worldId, pawnId;
            if (!Int32.TryParse(parts[2], out worldId) || !Int32.TryParse(parts[3], out pawnId))
            {
                Log("[拒绝] 贸易对象 ID 或谈判者 ID 无效：" + line);
                return;
            }
            ITrader trader = Find.WorldObjects.AllWorldObjects.FirstOrDefault(item => item.ID == worldId) as ITrader;
            Pawn pawn = PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists.FirstOrDefault(item => item.thingIDNumber == pawnId);
            if (trader == null || pawn == null || !IsAIPawn(pawn) || !trader.CanTradeNow || !IsAtTrader(pawn, trader))
            {
                Log("[拒绝] 贸易商、AI 谈判者不满足贸易条件或不在同一 tile：" + line);
                return;
            }
            bool uiRequested = parts[4].Equals("ui", StringComparison.OrdinalIgnoreCase);
            bool buy = parts[4].Equals("buy", StringComparison.OrdinalIgnoreCase);
            bool sell = parts[4].Equals("sell", StringComparison.OrdinalIgnoreCase);
            if (!uiRequested && !buy && !sell)
            {
                Log("[拒绝] 贸易方向无效：" + line);
                return;
            }
            bool openUi = uiRequested || (AICoopMod.Settings == null || AICoopMod.Settings.tradeMode == AICoopTradeMode.PlayerWindow);
            bool keepSessionForUi = false;
            try
            {
                TradeSession.SetupWith(trader, pawn, false);
                if (openUi)
                {
                    Find.WindowStack.Add(new Dialog_Trade(pawn, trader, false));
                    Log("[执行] 已打开与 " + trader.TraderName + " 的贸易界面。");
                    AICoopGameComponent.Current.AddWorkMessage("AI", "贸易界面已打开，请玩家完成买卖操作。");
                    Result("WAIT world_trade_ui=1 reason=player_window_open");
                    keepSessionForUi = true;
                    return;
                }
                int count;
                if ((!buy && !sell) || !Int32.TryParse(parts[6], out count) || count <= 0)
                {
                    Log("[拒绝] 贸易方向或数量无效：" + line);
                    return;
                }
                ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(parts[5]);
                Tradeable tradeable = TradeSession.deal.AllTradeables.FirstOrDefault(item => item.ThingDef == def);
                if (def == null || tradeable == null || !tradeable.TraderWillTrade)
                {
                    Log("[拒绝] 贸易商没有该物品或不愿交易：" + parts[5]);
                    return;
                }
                int transferCount = buy
                    ? Math.Min(count, tradeable.CountHeldBy(Transactor.Trader))
                    : Math.Min(count, tradeable.CountHeldBy(Transactor.Colony));
                if (transferCount <= 0)
                {
                    Log("[拒绝] 贸易商或殖民地没有可转移的 " + parts[5] + "。");
                    return;
                }
                if (buy) tradeable.ForceToSource(transferCount);
                else tradeable.ForceToDestination(transferCount);
                bool actuallyTraded;
                if (!TradeSession.deal.TryExecute(out actuallyTraded) || !actuallyTraded)
                {
                    Log("[拒绝] 贸易未完成，可能是银两、库存或贸易规则不足。");
                    return;
                }
                Log("[执行] 已完成贸易：" + (buy ? "购买 " : "出售 ") + def.defName + " x" + transferCount + "。");
                Result("OK world_trade=" + def.defName + " count=" + transferCount);
            }
            catch (Exception ex)
            {
                Log("[拒绝] 贸易失败：" + ex.Message);
            }
            finally
            {
                if (!keepSessionForUi && TradeSession.Active) TradeSession.Close();
            }
        }

        private static void HandleQuest(string[] parts, string line)
        {
            if (parts.Length != 5)
            {
                Log("[拒绝] WORLD quest 格式：WORLD quest 任务ID accept AI殖民者ID。");
                return;
            }
            int questId, pawnId;
            Quest quest = Int32.TryParse(parts[2], out questId) && Find.QuestManager != null
                ? Find.QuestManager.QuestsListForReading.FirstOrDefault(item => item.id == questId) : null;
            Pawn pawn = Int32.TryParse(parts[4], out pawnId) ? PawnsFinder.AllMapsCaravansAndTravellingTransporters_Alive_Colonists.FirstOrDefault(item => item.thingIDNumber == pawnId) : null;
            if (quest == null || pawn == null || !IsAIPawn(pawn) || !parts[3].Equals("accept", StringComparison.OrdinalIgnoreCase) || quest.State != QuestState.NotYetAccepted)
            {
                Log("[拒绝] 任务不存在、已接受、操作类型无效或 AI 接受者无效：" + line);
                return;
            }
            try
            {
                quest.Accept(pawn);
                Log("[执行] AI 殖民者 " + pawn.LabelShort + " 已接受任务 " + quest.id + "：" + quest.name + "。");
                Result("OK world_quest_accepted=" + quest.id);
            }
            catch (Exception ex)
            {
                Log("[拒绝] 接受任务失败：" + ex.Message);
            }
        }

        private static CaravanArrivalAction ArrivalActionFor(PlanetTile destination)
        {
            MapParent mapParent = Find.WorldObjects.MapParentAt(destination);
            if (mapParent != null && mapParent.HasMap) return new CaravanArrivalAction_Enter(mapParent);
            Settlement settlement = Find.WorldObjects.SettlementAt(destination);
            if (settlement != null && settlement.Visitable) return new CaravanArrivalAction_VisitSettlement(settlement);
            Site site = Find.WorldObjects.SiteAt(destination);
            return site == null ? null : new CaravanArrivalAction_VisitSite(site);
        }

        private static TransportersArrivalAction LandAction(PlanetTile destination)
        {
            MapParent mapParent = Find.WorldObjects.MapParentAt(destination);
            return mapParent == null || !mapParent.HasMap
                ? new TransportersArrivalAction_FormCaravan()
                : new TransportersArrivalAction_LandInSpecificCell(mapParent, mapParent.Map.Center);
        }

        private static List<Pawn> ParseAIPawns(Map map, string value)
        {
            AICoopGameComponent component = AICoopGameComponent.Current;
            List<Pawn> pawns = new List<Pawn>();
            foreach (string token in value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                int id;
                Pawn pawn = Int32.TryParse(token, out id) ? map.mapPawns.FreeColonistsSpawned.FirstOrDefault(item => item.thingIDNumber == id) : null;
                if (pawn != null && component != null && component.IsAI(pawn) && !pawn.Downed && !pawn.InMentalState && !pawns.Contains(pawn)) pawns.Add(pawn);
            }
            return pawns;
        }

        private static void AddItemsToCaravan(Map map, Caravan caravan, string value)
        {
            foreach (string token in value.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] itemParts = token.Split(new[] { ':' }, 2);
                int id, count;
                if (!Int32.TryParse(itemParts[0], out id)) continue;
                Thing item = map.listerThings.AllThings.FirstOrDefault(thing => thing.thingIDNumber == id && thing.Spawned && !(thing is Pawn));
                if (item == null || !item.def.canLoadIntoCaravan) continue;
                count = itemParts.Length == 2 && Int32.TryParse(itemParts[1], out count) ? count : item.stackCount;
                if (count <= 0) continue;
                if (count < item.stackCount) item = item.SplitOff(count);
                item.DeSpawn();
                CaravanInventoryUtility.GiveThing(caravan, item);
            }
        }

        private static CompTransporter FindTransporter(string id, out Map map)
        {
            map = null;
            int thingId;
            if (!Int32.TryParse(id, out thingId)) return null;
            foreach (Map candidate in Find.Maps)
            {
                Thing thing = candidate.listerThings.AllThings.FirstOrDefault(item => item.thingIDNumber == thingId);
                CompTransporter transporter = thing == null ? null : thing.TryGetComp<CompTransporter>();
                if (transporter != null)
                {
                    map = candidate;
                    return transporter;
                }
            }
            return null;
        }

        private static bool IsAtTrader(Pawn pawn, ITrader trader)
        {
            Settlement settlement = trader as Settlement;
            if (settlement == null) return true;
            Caravan caravan = pawn.GetCaravan();
            return caravan != null ? caravan.Tile == settlement.Tile : pawn.Map != null && pawn.Map.Tile == settlement.Tile;
        }

        private static bool IsAICaravan(Caravan caravan)
        {
            AICoopGameComponent component = AICoopGameComponent.Current;
            return component != null && caravan != null && caravan.IsPlayerControlled && caravan.PawnsListForReading.Any(component.IsAI) && !caravan.PawnsListForReading.Any(pawn => pawn.IsColonist && !component.IsAI(pawn));
        }

        private static bool IsAIPawn(Pawn pawn)
        {
            return pawn != null && AICoopGameComponent.Current != null && AICoopGameComponent.Current.IsAI(pawn);
        }

        private static Map FindMap(string value)
        {
            int id;
            return Int32.TryParse(value, out id) ? Find.Maps.FirstOrDefault(map => map.uniqueID == id) : null;
        }

        private static string Clean(string value)
        {
            return (value ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Replace(",", " ");
        }

        private static void Result(string message)
        {
            AICoopGameComponent.Current?.AddCommandResult(message);
        }

        private static void Log(string message)
        {
            AICoopGameComponent.Current?.AddLog(message);
        }
    }
}
