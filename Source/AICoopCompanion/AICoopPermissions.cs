using System;
using System.Linq;

namespace AICoopCompanion
{
    public enum AICoopPermissionMode
    {
        Allow,
        Ask,
        Deny
    }

    public sealed class AICoopToolPermissionDefinition
    {
        public readonly string Key;
        public readonly string Label;

        public AICoopToolPermissionDefinition(string key, string label)
        {
            Key = key;
            Label = label;
        }
    }

    public static class AICoopToolPermissions
    {
        public static readonly AICoopToolPermissionDefinition[] Definitions =
        {
            new AICoopToolPermissionDefinition("M", "移动"),
            new AICoopToolPermissionDefinition("D", "征召/解除征召"),
            new AICoopToolPermissionDefinition("A", "攻击"),
            new AICoopToolPermissionDefinition("Q", "房间蓝图"),
            new AICoopToolPermissionDefinition("B", "建筑蓝图"),
            new AICoopToolPermissionDefinition("G", "种植区"),
            new AICoopToolPermissionDefinition("S", "仓储区"),
            new AICoopToolPermissionDefinition("P", "生产账单"),
            new AICoopToolPermissionDefinition("R", "研究项目"),
            new AICoopToolPermissionDefinition("PRESET", "激活殖民地预设"),
            new AICoopToolPermissionDefinition("PRESET_DONE", "完成预设房间"),
            new AICoopToolPermissionDefinition("PRESET_RETRY", "重试预设失败设施"),
            new AICoopToolPermissionDefinition("C.work", "一次设置全部工作优先级"),
            new AICoopToolPermissionDefinition("C.timetable", "批量作息"),
            new AICoopToolPermissionDefinition("C.medical", "医疗等级"),
            new AICoopToolPermissionDefinition("C.policy", "服装/药物/食物方案"),
            new AICoopToolPermissionDefinition("C.gear", "装备/穿戴（完成后取消强制）"),
            new AICoopToolPermissionDefinition("C.roof", "屋顶"),
            new AICoopToolPermissionDefinition("C.home", "居住区"),
            new AICoopToolPermissionDefinition("C.storepriority", "仓储优先级"),
            new AICoopToolPermissionDefinition("C.storeallow", "仓储物品筛选"),
            new AICoopToolPermissionDefinition("C.storeclear", "清空仓储筛选"),
            new AICoopToolPermissionDefinition("C.storedelete", "删除AI临时垃圾储存区"),
            new AICoopToolPermissionDefinition("C.prisoner", "囚犯招募/释放"),
            new AICoopToolPermissionDefinition("C.bed", "床位类型/医疗标记"),
            new AICoopToolPermissionDefinition("C.door", "门开关/保持敞开"),
            new AICoopToolPermissionDefinition("C.deep", "深层资源查询"),
            new AICoopToolPermissionDefinition("C.timetablehour", "逐小时作息"),
            new AICoopToolPermissionDefinition("C.selftend", "自我治疗"),
            new AICoopToolPermissionDefinition("C.hostility", "敌对反应模式"),
            new AICoopToolPermissionDefinition("C.area", "允许活动区分配"),
            new AICoopToolPermissionDefinition("C.area_new", "创建允许活动区"),
            new AICoopToolPermissionDefinition("C.policycreate", "创建服装药物食物方案"),
            new AICoopToolPermissionDefinition("C.policyedit", "编辑方案物品许可"),
            new AICoopToolPermissionDefinition("C.storecategory", "仓储类别筛选"),
            new AICoopToolPermissionDefinition("C.storededicated", "专属仓储"),
            new AICoopToolPermissionDefinition("ORE", "按矿物批量标记开采"),
            new AICoopToolPermissionDefinition("C.temp", "冷库温度"),
            new AICoopToolPermissionDefinition("C.rebuild", "自动重建"),
            new AICoopToolPermissionDefinition("C.homeauto", "自动居住区"),
            new AICoopToolPermissionDefinition("C.floor", "批量地板"),
            new AICoopToolPermissionDefinition("C.conduit", "批量电缆"),
            new AICoopToolPermissionDefinition("C.hiddenconduit", "隐藏电缆路径"),
            new AICoopToolPermissionDefinition("C.fortify", "兼容防御工事命令"),
            new AICoopToolPermissionDefinition("C.defense", "建造防御工事（钢铁陷阱）"),
            new AICoopToolPermissionDefinition("C.surgery", "手术账单"),
            new AICoopToolPermissionDefinition("C.organharvest", "按顺序摘除囚犯器官"),
            new AICoopToolPermissionDefinition("C.animal", "动物训练管理"),
            new AICoopToolPermissionDefinition("C.deepauto", "自动深钻井"),
            new AICoopToolPermissionDefinition("C.bill", "高级生产账单"),
            new AICoopToolPermissionDefinition("C.billconfig", "高级生产账单"),
            new AICoopToolPermissionDefinition("C.billorder", "生产账单排序"),
            new AICoopToolPermissionDefinition("C.ritual", "Ideology 文化仪式"),
            new AICoopToolPermissionDefinition("C.culturechair", "切换文化专属椅子样式"),
            new AICoopToolPermissionDefinition("WORLD.caravan", "组建 AI 商队"),
            new AICoopToolPermissionDefinition("WORLD.move", "选择商队目的地"),
            new AICoopToolPermissionDefinition("WORLD.load", "运输舱/穿梭机装载"),
            new AICoopToolPermissionDefinition("WORLD.launch", "运输舱/穿梭机发射"),
            new AICoopToolPermissionDefinition("WORLD.trade", "世界地图贸易"),
            new AICoopToolPermissionDefinition("WORLD.quest", "接取任务"),
            new AICoopToolPermissionDefinition("T.cancel", "取消指定"),
            new AICoopToolPermissionDefinition("T.deconstruct", "拆除指定"),
            new AICoopToolPermissionDefinition("T.mine", "采矿指定"),
            new AICoopToolPermissionDefinition("T.chop", "伐木指定"),
            new AICoopToolPermissionDefinition("T.cut", "割除非树植物"),
            new AICoopToolPermissionDefinition("T.harvest", "收获指定"),
            new AICoopToolPermissionDefinition("T.hunt", "狩猎指定"),
            new AICoopToolPermissionDefinition("T.slaughter", "屠宰指定"),
            new AICoopToolPermissionDefinition("T.tame", "驯服指定"),
            new AICoopToolPermissionDefinition("T.haul", "搬运指定"),
            new AICoopToolPermissionDefinition("T.haul_chunks", "仅搬运石块"),
            new AICoopToolPermissionDefinition("T.unforbid", "解禁指定"),
            new AICoopToolPermissionDefinition("T.forbid", "禁用指定"),
            new AICoopToolPermissionDefinition("T.claim", "认领指定"),
            new AICoopToolPermissionDefinition("T.smooth", "打磨指定"),
            new AICoopToolPermissionDefinition("T.paint_building", "粉饰建筑"),
            new AICoopToolPermissionDefinition("T.paint_floor", "粉饰地板"),
            new AICoopToolPermissionDefinition("T.remove_building_paint", "移除建筑涂料"),
            new AICoopToolPermissionDefinition("T.remove_floor_paint", "移除地板涂料"),
            new AICoopToolPermissionDefinition("T.remove_plan", "移除计划"),
            new AICoopToolPermissionDefinition("T.strip", "剥取衣物"),
            new AICoopToolPermissionDefinition("F", "外围岩石墙"),
            new AICoopToolPermissionDefinition("X.chop", "单目标伐木"),
            new AICoopToolPermissionDefinition("X.cut", "单目标割除非树植物"),
            new AICoopToolPermissionDefinition("X.harvest", "单目标收获"),
            new AICoopToolPermissionDefinition("X.mine", "单目标采矿"),
            new AICoopToolPermissionDefinition("X.deconstruct", "单目标拆除"),
            new AICoopToolPermissionDefinition("X.hunt", "单目标狩猎"),
            new AICoopToolPermissionDefinition("X.slaughter", "单目标屠宰"),
            new AICoopToolPermissionDefinition("X.tame", "单目标驯服"),
            new AICoopToolPermissionDefinition("X.haul", "单目标搬运"),
            new AICoopToolPermissionDefinition("X.strip", "单目标剥取衣物"),
            new AICoopToolPermissionDefinition("X.smooth", "单目标打磨"),
            new AICoopToolPermissionDefinition("J.rescue", "救援"),
            new AICoopToolPermissionDefinition("J.tend", "治疗"),
            new AICoopToolPermissionDefinition("J.capture", "捕获"),
            new AICoopToolPermissionDefinition("J.arrest", "拘捕"),
            new AICoopToolPermissionDefinition("J.clean", "清洁"),
            new AICoopToolPermissionDefinition("J.repair", "维修"),
            new AICoopToolPermissionDefinition("J.equip", "直接装备"),
            new AICoopToolPermissionDefinition("J.wear", "直接穿戴（完成后取消强制）")
        };

        public static string ModeLabel(AICoopPermissionMode mode)
        {
            switch (mode)
            {
                case AICoopPermissionMode.Ask: return "询问玩家";
                case AICoopPermissionMode.Deny: return "禁止";
                default: return "允许";
            }
        }

        public static string Summary(AICoopSettings settings)
        {
            if (settings == null) return string.Empty;
            return string.Join(",", Definitions.Select(definition => definition.Key + "=" + settings.GetToolPermission(definition.Key).ToString().ToLowerInvariant()).ToArray());
        }
    }
}
