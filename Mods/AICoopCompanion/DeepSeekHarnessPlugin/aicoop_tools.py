"""Typed CLI tool catalog bundled with the DeepSeek Harness plugin."""

from __future__ import annotations

from typing import Any


CATALOG_VERSION = 9


def argument(
    name: str,
    value_type: str,
    description: str,
    *,
    required: bool = True,
    enum: list[Any] | None = None,
    minimum: int | float | None = None,
    maximum: int | float | None = None,
    allow_spaces: bool = False,
) -> dict[str, Any]:
    result: dict[str, Any] = {
        "name": name,
        "type": value_type,
        "description": description,
        "required": required,
    }
    if enum is not None:
        result["enum"] = enum
    if minimum is not None:
        result["minimum"] = minimum
    if maximum is not None:
        result["maximum"] = maximum
    if allow_spaces:
        result["allow_spaces"] = True
    return result


def tool(
    name: str,
    category: str,
    description: str,
    permission: str,
    template: list[str],
    arguments: list[dict[str, Any]],
    example: dict[str, Any],
    *,
    aliases: list[str] | None = None,
    required_together: list[list[str]] | None = None,
) -> dict[str, Any]:
    return {
        "name": name,
        "category": category,
        "description": description,
        "permission": permission,
        "template": template,
        "arguments": arguments,
        "example": example,
        "aliases": aliases or [],
        "required_together": required_together or [],
    }


INT_MAP = lambda: argument("map_id", "integer", "状态中的地图 ID。")
RECT = lambda: [
    argument("x1", "integer", "矩形第一个角的 X。"),
    argument("z1", "integer", "矩形第一个角的 Z。"),
    argument("x2", "integer", "矩形另一个角的 X。"),
    argument("z2", "integer", "矩形另一个角的 Z。"),
]
BOOL = lambda name, text: argument(name, "boolean", text)

TIME_ASSIGNMENTS = ["Work", "Joy", "Sleep", "Anything", "Meditate"]
DESIGNATIONS = [
    "cancel", "deconstruct", "mine", "chop", "cut", "harvest", "hunt", "slaughter", "tame",
    "haul", "haul_chunks", "unforbid", "forbid", "claim", "smooth", "paint_building", "paint_floor",
    "remove_building_paint", "remove_floor_paint", "remove_plan", "strip",
]
TARGET_DESIGNATIONS = [
    "chop", "cut", "harvest", "mine", "deconstruct", "hunt", "slaughter", "tame", "haul",
    "haul_chunks", "strip", "smooth",
]
DIRECT_JOBS = ["rescue", "tend", "capture", "arrest", "clean", "repair", "equip", "wear"]


TOOLS: list[dict[str, Any]] = [
    tool("dlc_features", "dlc", "查询所有DLC启用状态、已接入能力与需要玩家处理的边界；不触发事件。", "DLC.query", ["DLC", "features", "${map_id}"], [INT_MAP()], {"map_id": 0}),
    *[tool(name, "dlc", description, "DLC.mech", ["DLC", name, "${map_id}", "${pawn_id}", "${mech_id}"],
           [INT_MAP(), argument("pawn_id", "integer", "AI机械师ID；mech_haul可用普通AI搬运者"), argument("mech_id", "integer", "殖民地机械体ID")], {"map_id": 0, "pawn_id": 1, "mech_id": 2})
      for name, description in [
          ("mech_control", "安排原生控制机械体工作；校验带宽、路径及控制资格，不夺取玩家专属机械师的机械体。"),
          ("mech_repair", "安排AI机械师维修受损机械体，需走到旁边工作，不是立即恢复生命。"),
          ("mech_haul", "让AI搬运者把倒地/缺电停机机械体搬到原生可用充电器。")]],
    *[tool(name, "dlc", description, "DLC.mech_destructive", ["DLC", name, "${map_id}", "${mechanitor_id}", "${mech_id}"],
           [INT_MAP(), argument("mechanitor_id", "integer", "该机械体实际AI控制者"), argument("mech_id", "integer", "机械体ID")], {"map_id": 0, "mechanitor_id": 1, "mech_id": 2})
      for name, description in [("mech_disconnect", "危险：解除机械体控制，默认禁止。"), ("mech_disassemble", "危险：安排拆解自身机械体，默认禁止，不立即销毁或生成材料。")]],
    tool("mech_autorepair", "dlc", "开启/关闭受控机械体允许自动维修的标记。", "DLC.mech", ["DLC", "mech_autorepair", "${map_id}", "${mech_id}", "${enabled}"],
         [INT_MAP(), argument("mech_id", "integer", "受控机械体ID"), BOOL("enabled", "自动维修标记")], {"map_id": 0, "mech_id": 2, "enabled": True}),
    tool("mech_carrier_release", "dlc", "让AI控制的携带子机机械体原生释放子机，检查库存和冷却，不免费生成。", "DLC.mech", ["DLC", "mech_carrier_release", "${map_id}", "${mech_id}"],
         [INT_MAP(), argument("mech_id", "integer", "带CompMechCarrier的受控机械体ID")], {"map_id": 0, "mech_id": 2}),
    tool("mech_escort", "dlc", "设置已有机械控制组护送己方人物，不修改被护送者的操作。", "DLC.mech", ["DLC", "mech_escort", "${map_id}", "${mechanitor_id}", "${group}", "${target_id}"],
         [INT_MAP(), argument("mechanitor_id", "integer", "机械师ID"), argument("group", "integer", "dlc_status返回的组索引"), argument("target_id", "integer", "己方护送目标ID")], {"map_id": 0, "mechanitor_id": 1, "group": 0, "target_id": 3}),
    tool("mech_bill", "dlc", "给孕育器设置原生孕育/复活账单，限制由指定机械师执行。需要实际材料、带宽和孕育周期；相同配方和执行者重复调用更新次数而不叠加。", "DLC.mech",
         ["DLC", "mech_bill", "${map_id}", "${mechanitor_id}", "${gestator_id}", "${recipe}", "${count}"],
         [INT_MAP(), argument("mechanitor_id", "integer", "机械师ID"), argument("gestator_id", "integer", "孕育器ID"), argument("recipe", "string", "dlc_inspect返回的可用机械配方DefName"), argument("count", "integer", "次数", minimum=1, maximum=100)], {"map_id": 0, "mechanitor_id": 1, "gestator_id": 3, "recipe": "实际孕育配方", "count": 1}),
    tool("pollution_area", "dlc", "批量划定/擦除污染清理区域，随后由原版人员清理并产生废料，不直接抹除污染。", "DLC.pollution",
         ["DLC", "pollution", "${map_id}", "${x1}", "${z1}", "${x2}", "${z2}", "${enabled}"], [INT_MAP(), *RECT(), BOOL("enabled", "划定为true，擦除为false")], {"map_id": 0, "x1": 10, "z1": 10, "x2": 15, "z2": 15, "enabled": True}),
    *[tool(name, "dlc", description, "DLC.device", ["DLC", name, "${map_id}", "${scanner_id}"],
           [INT_MAP(), argument("scanner_id", "integer", "次核心扫描器ID")], {"map_id": 0, "scanner_id": 2})
      for name, description in [("scanner_init", "点击原生开始准备扫描按钮，默认询问；只是等待材料，不选择扫描对象。"), ("scanner_cancel", "取消扫描器装载/普通扫描。若致死扫描已经有人进入，则请求玩家亲自确认，不自动中止。")]],
    tool("dlc_enter_device", "dlc", "选择AI可控人物或AI俘获且未被任务保留的囚犯进入基因提取器、成长舱、普通次核心扫描器；默认询问，等待步行/搬运，拒绝撕裂扫描。", "DLC.device",
         ["DLC", "enter", "${map_id}", "${pawn_id}", "${building_id}"], [INT_MAP(), argument("pawn_id", "integer", "受控人物/AI俘虏ID"), argument("building_id", "integer", "设备ID")], {"map_id": 0, "pawn_id": 1, "building_id": 2}),
    tool("mech_ripscan", "dlc", "危险且致死：安排指定AI人物/AI俘虏进入撕裂扫描器。默认禁止，严禁任务保留人物；检查原版材料、状态和接受条件。", "DLC.ripscan",
         ["DLC", "ripscan", "${map_id}", "${pawn_id}", "${scanner_id}"], [INT_MAP(), argument("pawn_id", "integer", "AI人物/AI俘虏ID"), argument("scanner_id", "integer", "撕裂扫描器ID")], {"map_id": 0, "pawn_id": 1, "scanner_id": 2}),
    *[tool(name, "dlc", description, "DLC.care", ["DLC", operation, "${map_id}", "${pawn_id}", "${percent}"],
           [INT_MAP(), argument("pawn_id", "integer", "AI可控人物ID"), argument("percent", "integer", "目标百分比", minimum=0, maximum=100)], {"map_id": 0, "pawn_id": 1, "percent": 80})
      for name, operation, description in [("psyfocus_target", "psyfocus", "设置灵能者冥想灵能专注度目标，不增加当前专注；冥想时段使用Meditate作息。"), ("hemogen_target", "hemogen", "设置血族血源目标，不生成血包、不强制吸血。")]],
    tool("deathrest_auto", "dlc", "设置血族完成死眠后自动醒来，不强制提前醒来。", "DLC.care", ["DLC", "deathrest_auto", "${map_id}", "${pawn_id}", "${enabled}"],
         [INT_MAP(), argument("pawn_id", "integer", "AI可控血族ID"), BOOL("enabled", "完成后自动醒来")], {"map_id": 0, "pawn_id": 1, "enabled": True}),
    tool("dlc_assign", "dlc", "按原生资格分配空闲王座、冥想点、床等有分配组件的设施，不挤掉已有成员。", "DLC.care", ["DLC", "assign", "${map_id}", "${pawn_id}", "${building_id}"],
         [INT_MAP(), argument("pawn_id", "integer", "AI可控人物ID"), argument("building_id", "integer", "可分配设施ID")], {"map_id": 0, "pawn_id": 1, "building_id": 2}),
    tool("ideology_role", "dlc", "将符合原生要求的AI人物分配到其意识形态的空闲角色，默认询问，不覆盖已有成员。", "DLC.role", ["DLC", "role", "${map_id}", "${pawn_id}", "${role_id}"],
         [INT_MAP(), argument("pawn_id", "integer", "AI可控人物ID"), argument("role_id", "integer", "dlc_status/dlc_inspect返回的角色ID")], {"map_id": 0, "pawn_id": 1, "role_id": 2}),
    tool("dlc_ability_cell", "dlc", "默认禁止：向地图坐标排队施放已有能力/超能。检查原生冷却和目标条件，不创造能力，不选择世界地图。", "DLC.ability",
         ["DLC", "ability_cell", "${map_id}", "${pawn_id}", "${ability}", "${x}", "${z}"], [INT_MAP(), argument("pawn_id", "integer", "AI可控施术者"), argument("ability", "string", "真实能力DefName"), argument("x", "integer", "目标X"), argument("z", "integer", "目标Z")], {"map_id": 0, "pawn_id": 1, "ability": "实际能力", "x": 100, "z": 100}),
    tool("dlc_ability_pair", "dlc", "默认禁止：为需要物体目标和目的格的已有能力排队施放；只能在本地图，复杂目标被拒绝时请求玩家。", "DLC.ability",
         ["DLC", "ability_pair", "${map_id}", "${pawn_id}", "${ability}", "${target_id}", "${x}", "${z}"], [INT_MAP(), argument("pawn_id", "integer", "AI可控施术者"), argument("ability", "string", "真实能力DefName"), argument("target_id", "integer", "目标物体"), argument("x", "integer", "目的X"), argument("z", "integer", "目的Z")], {"map_id": 0, "pawn_id": 1, "ability": "实际能力", "target_id": 2, "x": 100, "z": 100}),
    tool("ghoul_infuse", "dlc", "危险：为AI人物或AI俘虏添加食尸鬼灌注手术，默认禁止，排除任务人物；消耗原生材料并由医生操作，不立即变身。", "DLC.medical",
         ["DLC", "ghoul_infuse", "${map_id}", "${pawn_id}"], [INT_MAP(), argument("pawn_id", "integer", "AI人物/AI俘虏ID")], {"map_id": 0, "pawn_id": 1}),
    tool("ghoul_move", "dlc", "征召并移动AI食尸鬼到可达位置，遵守AI战斗区。", "DLC.ghoul", ["DLC", "ghoul_move", "${map_id}", "${pawn_id}", "${x}", "${z}"],
         [INT_MAP(), argument("pawn_id", "integer", "AI食尸鬼ID"), argument("x", "integer", "X"), argument("z", "integer", "Z")], {"map_id": 0, "pawn_id": 1, "x": 100, "z": 100}),
    *[tool(name, "dlc", description, "DLC.ghoul", ["DLC", name, "${map_id}", "${pawn_id}", "${target_id}"],
           [INT_MAP(), argument("pawn_id", "integer", "AI食尸鬼ID"), argument("target_id", "integer", target)], {"map_id": 0, "pawn_id": 1, "target_id": 2})
      for name, description, target in [("ghoul_attack", "征召AI食尸鬼近战攻击可达敌人。", "敌人ID"), ("ghoul_feed", "安排AI食尸鬼食用可达、可食用的肉类，不生成食物。", "肉类物品ID"), ("ghoul_rest", "用原生床铺休息选项安排AI食尸鬼休养。", "床铺ID")]],
    tool("ghoul_undraft", "dlc", "解除AI食尸鬼征召。", "DLC.ghoul", ["DLC", "ghoul_undraft", "${map_id}", "${pawn_id}"], [INT_MAP(), argument("pawn_id", "integer", "AI食尸鬼ID")], {"map_id": 0, "pawn_id": 1}),
    tool("dlc_prioritize", "dlc", "安排原生工作：实体调查研究/照护/收集生物铁/压制/审问、儿童喂食/转移安全处/陪玩。study和dark_study走默认禁止研究权限，其余默认询问；工作不存在或条件不满足如实返回。", "DLC.maintenance",
         ["DLC", "prioritize", "${map_id}", "${pawn_id}", "${target_id}", "${action}"],
         [INT_MAP(), argument("pawn_id", "integer", "AI可控执行者"), argument("target_id", "integer", "调查物、实体或儿童ID"), argument("action", "string", "原生工作种类", enum=["study", "dark_study", "tend_entity", "bioferrite", "empty_harvester", "suppress", "interrogate", "feed_baby", "baby_safety", "play_baby"])], {"map_id": 0, "pawn_id": 1, "target_id": 2, "action": "study"}),
    tool("dlc_status", "dlc", "读取地图上的机械师带宽/控制组、收容与研究设备、孕育器/充电器、重力飞船缺件/燃料和捕鱼区。先确认实际启用DLC。", "DLC.query",
         ["DLC", "status", "${map_id}"], [INT_MAP()], {"map_id": 0}),
    tool("dlc_inspect", "dlc", "读取指定对象的原生状态、可用配方、收容状态、能力或飞船缺件。可读取收容平台内实体。", "DLC.query",
         ["DLC", "inspect", "${map_id}", "${thing_id}"], [INT_MAP(), argument("thing_id", "integer", "实际对象ID")], {"map_id": 0, "thing_id": 123}),
    tool("mech_mode", "dlc", "设置AI可控机械师现有控制组工作/充电/自关机模式；不会赠送机械师能力或带宽。", "DLC.mech",
         ["DLC", "mech_mode", "${map_id}", "${mechanitor_id}", "${group}", "${mode}"],
         [INT_MAP(), argument("mechanitor_id", "integer", "机械师ID"), argument("group", "integer", "dlc_status返回的group索引"), argument("mode", "string", "原生模式", enum=["Work", "Recharge", "SelfShutdown"])],
         {"map_id": 0, "mechanitor_id": 1, "group": 0, "mode": "Recharge"}),
    tool("mech_group", "dlc", "把机械师已经控制的机械体调入其已有控制组；不能夺取玩家或敌方机械体。", "DLC.mech",
         ["DLC", "mech_group", "${map_id}", "${mechanitor_id}", "${group}", "${mech_id}"],
         [INT_MAP(), argument("mechanitor_id", "integer", "机械师ID"), argument("group", "integer", "现有group索引"), argument("mech_id", "integer", "该机械师已控制的机械体ID")], {"map_id": 0, "mechanitor_id": 1, "group": 0, "mech_id": 2}),
    tool("mech_charge", "dlc", "配置现有控制组开始/结束充电的电量百分比，0≤low≤high≤100。", "DLC.mech",
         ["DLC", "mech_charge", "${map_id}", "${mechanitor_id}", "${group}", "${low}", "${high}"],
         [INT_MAP(), argument("mechanitor_id", "integer", "机械师ID"), argument("group", "integer", "现有group索引"), argument("low", "integer", "下限", minimum=0, maximum=100), argument("high", "integer", "上限", minimum=0, maximum=100)],
         {"map_id": 0, "mechanitor_id": 1, "group": 0, "low": 20, "high": 90}),
    tool("mech_move", "dlc", "原生范围/可达检查后征召并移动AI机械师控制的机械体。", "DLC.mech",
         ["DLC", "mech_order", "${map_id}", "${mech_id}", "move", "${x}", "${z}"], [INT_MAP(), argument("mech_id", "integer", "机械体ID"), argument("x", "integer", "X"), argument("z", "integer", "Z")], {"map_id": 0, "mech_id": 2, "x": 100, "z": 100}),
    tool("mech_attack", "dlc", "征召AI机械师控制的机械体攻击敌对目标；受指挥范围和原生攻击能力限制，不保证立即命中。", "DLC.mech",
         ["DLC", "mech_order", "${map_id}", "${mech_id}", "attack", "${target_id}"], [INT_MAP(), argument("mech_id", "integer", "机械体ID"), argument("target_id", "integer", "敌对目标ID")], {"map_id": 0, "mech_id": 2, "target_id": 3}),
    tool("mech_undraft", "dlc", "解除AI机械师控制的机械体征召。", "DLC.mech",
         ["DLC", "mech_order", "${map_id}", "${mech_id}", "undraft"], [INT_MAP(), argument("mech_id", "integer", "机械体ID")], {"map_id": 0, "mech_id": 2}),
    tool("entity_contain", "dlc", "为当前可捕获实体指定空闲收容平台，等待原生搬运，默认询问玩家；不是瞬移或已完成收容。", "DLC.contain",
         ["DLC", "contain", "${map_id}", "${entity_id}", "${platform_id}"], [INT_MAP(), argument("entity_id", "integer", "实体ID"), argument("platform_id", "integer", "空闲平台ID")], {"map_id": 0, "entity_id": 3, "platform_id": 4}),
    tool("entity_study", "dlc", "启用/停用异常物或实体研究。可能导致魔方成瘾、异常推进等风险，默认禁止，须玩家更改权限。", "DLC.study",
         ["DLC", "study", "${map_id}", "${thing_id}", "${enabled}"], [INT_MAP(), argument("thing_id", "integer", "可研究对象ID"), BOOL("enabled", "启用研究")], {"map_id": 0, "thing_id": 3, "enabled": False}),
    tool("fishing_zone", "dlc", "在矩形内的可捕鱼、未占用水域建立捕鱼区；淡水/海水分开，使用原生捕鱼流程。", "DLC.fishing",
         ["DLC", "fish", "${map_id}", "${x1}", "${z1}", "${x2}", "${z2}"], [INT_MAP(), *RECT()], {"map_id": 0, "x1": 100, "z1": 100, "x2": 105, "z2": 105}),
    tool("fishing_config", "dlc", "设置捕鱼区目标鱼库存与最低保留鱼群百分比，避免过度捕捞。", "DLC.fishing",
         ["DLC", "fish_config", "${map_id}", "${zone_id}", "${target}", "${reserve}"], [INT_MAP(), argument("zone_id", "integer", "捕鱼区ID"), argument("target", "integer", "鱼库存目标", minimum=1, maximum=100000), argument("reserve", "integer", "保留鱼群百分比", minimum=0, maximum=100)], {"map_id": 0, "zone_id": 1, "target": 100, "reserve": 50}),
    tool("refuel_config", "dlc", "设置殖民地设备燃料目标百分比和自动补充开关；不凭空生成燃料，不起飞。", "DLC.refuel",
         ["DLC", "refuel", "${map_id}", "${thing_id}", "${percent}", "${enabled}"], [INT_MAP(), argument("thing_id", "integer", "带原生燃料组件的设备ID"), argument("percent", "integer", "目标燃料百分比", minimum=0, maximum=100), BOOL("enabled", "允许自动补充")], {"map_id": 0, "thing_id": 4, "percent": 100, "enabled": True}),
    tool("dlc_options", "dlc", "只读列出目标对指定AI人物提供的物体菜单和1.6原生右键菜单提供器选项（含调查/控制/设备使用等）；不是完整DLC功能清单。飞船起飞和选图不提供。", "DLC.query",
         ["DLC", "options", "${map_id}", "${thing_id}", "${pawn_id}"], [INT_MAP(), argument("thing_id", "integer", "DLC对象ID"), argument("pawn_id", "integer", "AI可控殖民者ID")], {"map_id": 0, "thing_id": 3, "pawn_id": 1}),
    tool("dlc_interact", "dlc", "执行dlc_options刚返回的原生选项token。高风险默认禁止；原生确认窗口由玩家处理，不代表动作已完成。不会操作飞船起飞或地图选择。", "DLC.interact",
         ["DLC", "interact", "${map_id}", "${thing_id}", "${pawn_id}", "${option}"], [INT_MAP(), argument("thing_id", "integer", "DLC对象ID"), argument("pawn_id", "integer", "AI可控殖民者ID"), argument("option", "string", "刚查询的16位token，不能猜测")], {"map_id": 0, "thing_id": 3, "pawn_id": 1, "option": "0123456789ABCDEF"}),
    tool("mech_boss_options", "dlc", "只读列出该机械师原生Boss召唤选项与条件；不会召唤。", "DLC.query",
         ["DLC", "boss_options", "${map_id}", "${mechanitor_id}"], [INT_MAP(), argument("mechanitor_id", "integer", "AI可控机械师ID")], {"map_id": 0, "mechanitor_id": 1}),
    tool("mech_boss_call", "dlc", "危险：提交原生机械Boss召唤选项，默认禁止，若出现风险确认需玩家确认，不直接生成Boss。", "DLC.boss",
         ["DLC", "boss_call", "${map_id}", "${mechanitor_id}", "${option}"], [INT_MAP(), argument("mechanitor_id", "integer", "AI可控机械师ID"), argument("option", "string", "mech_boss_options返回token")], {"map_id": 0, "mechanitor_id": 1, "option": "0123456789ABCDEF"}),
    tool("dlc_ability", "dlc", "危险：给AI可控人物/机械体排队施放已有能力，检查冷却、资源和目标；默认禁止。仅支持物体目标，不能进行世界地图/起飞选择。", "DLC.ability",
         ["DLC", "ability", "${map_id}", "${pawn_id}", "${ability}", "${target_id}"], [INT_MAP(), argument("pawn_id", "integer", "人物/机械体ID"), argument("ability", "string", "dlc_inspect返回的能力DefName"), argument("target_id", "integer", "本地图物体目标ID")], {"map_id": 0, "pawn_id": 1, "ability": "实际能力DefName", "target_id": 3}),
    tool("work_priorities", "pawns", "按固定工作类型一对一设置优先级，不跟随界面顺序；数字模式固定20项，第1灭火、第2就医。也可批量提交DefName=数值，Mod工作用名称；未指定不变，低技能不要设1/2。", "C.work",
         ["C", "${map_id}", "work", "${pawn_id}", "${priorities}"],
         [INT_MAP(), argument("pawn_id", "integer", "AI殖民者ID"), argument("priorities", "string", "固定WORK_PRIORITY_ORDER的20个0-4数字；或Firefighter=1,Patient=1等一对一名称列表，不得混用；禁用工作填0")],
         {"map_id": 0, "pawn_id": 123, "priorities": "Firefighter=1,Patient=1"}),
    tool("plan", "control", "记录一条公开工作计划，不执行隐藏思维链。", "none", ["PLAN", "${text}"],
         [argument("text", "string", "公开计划文本；不能包含换行。", allow_spaces=True)], {"text": "先稳定食物和住房"}),
    tool("no_action", "control", "明确表示本轮没有安全且必要的操作。", "none", ["N"], [], {}),
    tool("say", "communication", "把普通状态说明写入游戏内 AI 协作日志。", "none", ["SAY", "${text}"],
         [argument("text", "string", "发送给玩家的文字；不能包含换行。", allow_spaces=True)], {"text": "当前正在等待资源"}),
    tool("request_player", "communication", "请求玩家处理 AI 无法、无权或不应自动完成的操作。", "none",
         ["REQUEST_PLAYER", "${text}"], [argument("text", "string", "请求内容；不能包含换行。", allow_spaces=True)],
         {"text": "请玩家确认这次贸易"}),
    tool("note_add", "memory", "把会被紧急事件打断的长期计划写入存档内待办文件，并自动记录游戏时间刻。", "none",
         ["NOTE_ADD", "${text}"], [argument("text", "string", "待办内容；不能包含换行。", allow_spaces=True)],
         {"text": "袭击结束后继续铺设电缆"}),
    tool("note_read", "memory", "读取当前存档中尚未完成的 AI 待办及其时间刻。", "none", ["NOTE_READ"], [], {}),
    tool("colonist_status", "inspection", "随时只读查询AI与玩家殖民者的心情、精神状态、负面心情原因、伤病、饥饿、休息和娱乐；省略ID查询全部，不计入执行次数。", "COLONIST_STATUS",
         ["COLONIST_STATUS", "${pawn_id}"], [argument("pawn_id", "integer", "可选殖民者ID。", required=False, minimum=0)], {}),
    tool("quest_list", "inspection", "只读查询玩家可见任务列表，包含ID、名称、状态、星级和到期时间；每页30项，next非done时续读。不能接取或操作任务。", "QUEST_LIST",
         ["QUEST_LIST", "${offset}"], [argument("offset", "integer", "分页偏移，默认0。", required=False, minimum=0)], {}),
    tool("quest_detail", "inspection", "只读查询指定任务的完整内容、奖励组选项、星级、状态和补充说明；不接取、不选择奖励、不操作任务。", "QUEST_DETAIL",
         ["QUEST_DETAIL", "${quest_id}"], [argument("quest_id", "integer", "任务列表中的ID。", minimum=0)], {"quest_id": 1}),
    tool("note_done", "memory", "将存档内指定编号的 AI 待办标记为完成。", "none", ["NOTE_DONE", "${note_id}"],
         [argument("note_id", "integer", "NOTE_READ 返回的待办编号。")], {"note_id": 1}),
    tool("map_scan", "inspection", "读取矩形内地形、物品、人物、蓝图、区域与设施状态；每页128格，next非done时用相同范围及next作为offset续读。", "MAP_SCAN",
         ["MAP_SCAN", "${map_id}", "${x1}", "${z1}", "${x2}", "${z2}", "${offset}"],
         [INT_MAP(), *RECT(), argument("offset", "integer", "续页偏移，默认0。", required=False, minimum=0)],
         {"map_id": 0, "x1": 100, "z1": 100, "x2": 110, "z2": 110}),
    tool("guide_done", "progress", "将当前攻略任务标记为完成。", "none", ["GUIDE_DONE", "${task_id}"],
         [argument("task_id", "string", "当前攻略任务编号。")], {"task_id": "01.03"}),
    tool("preset_build", "building", "放置一个房间。先让13x13房间边线对齐紧贴共墙，再检查门；不能为对门错位。坐标作为拼接优先位置，以返回origin为准。原位旋转或强制拼接须玩家确认；WAIT时等待，不重复调用。", "PRESET",
         ["PRESET", "${preset}", "${map_id}", "${x}", "${z}", "${rotation}"],
         [argument("preset", "string", "预设目录中真实的中文文件名，可省略.txt；先列出目录，不要编造文件名。"),
          argument("map_id", "integer", "可选地图 ID。", required=False),
          argument("x", "integer", "可选锚点 X。", required=False),
          argument("z", "integer", "可选锚点 Z。", required=False),
          argument("rotation", "integer", "可选旋转，0-3。", required=False, minimum=0, maximum=3)],
         {"preset": "开局小屋.txt"}, required_together=[["map_id", "x", "z"]]),
    tool("preset_done", "progress", "确认当前预设房间已经稳定完成。", "PRESET_DONE", ["PRESET_DONE", "${room}"],
         [argument("room", "string", "当前房间标识。")], {"room": "starting_core"}),
    tool("preset_retry", "building", "重试所有因研究、制作材料或位置问题未放置的预设设施。", "PRESET_RETRY",
         ["PRESET_RETRY"], [], {}),

    tool("move_all", "pawn", "让指定地图所有可用 AI 殖民者移动到一个格子。", "M", ["M", "${map_id}", "${x}", "${z}"],
         [INT_MAP(), argument("x", "integer", "目标 X。"), argument("z", "integer", "目标 Z。")],
         {"map_id": 1, "x": 80, "z": 90}),
    tool("draft", "combat", "征召或解除征召一个 AI 殖民者。", "D", ["D", "${pawn_id}", "${drafted}"],
         [argument("pawn_id", "integer", "AI 殖民者 ID。"), BOOL("drafted", "true 征召，false 解除。")],
         {"pawn_id": 101, "drafted": True}),
    tool("attack_all", "combat", "立即征召地图上所有有攻击能力的 AI 殖民者，先机动到射程或掩体后攻击目标。", "A",
         ["A", "${map_id}", "${target_id}"], [INT_MAP(), argument("target_id", "integer", "敌人或可攻击障碍 ID。")],
         {"map_id": 1, "target_id": 9001}),
    tool("build_room", "building", "放置一个矩形房间的墙和门蓝图；普通地形通常必须为 13x13，墙体优先使用花岗岩等石材。", "Q",
         ["Q", "${map_id}", "${x1}", "${z1}", "${x2}", "${z2}", "${stuff_def}", "${purpose}"],
         [INT_MAP(), *RECT(), argument("stuff_def", "string", "墙和门使用的材料 ThingDef。"),
          argument("purpose", "string", "单一房间用途，用下划线代替空格。")],
         {"map_id": 1, "x1": 40, "z1": 40, "x2": 52, "z2": 52, "stuff_def": "BlocksGranite", "purpose": "bedroom"}),
    tool("build", "building", "放置一个已解锁建筑蓝图。", "B",
         ["B", "${map_id}", "${build_def}", "${x}", "${z}", "${rotation}", "${stuff_def}"],
         [INT_MAP(), argument("build_def", "string", "BUILD_DEFS 中的建筑 ThingDef。"),
          argument("x", "integer", "放置 X。"), argument("z", "integer", "放置 Z。"),
          argument("rotation", "integer", "可选旋转，0-3。", required=False, minimum=0, maximum=3),
          argument("stuff_def", "string", "可选建筑材料 ThingDef；提供时也必须提供 rotation。", required=False)],
         {"map_id": 1, "build_def": "ElectricStove", "x": 46, "z": 46, "rotation": 0, "stuff_def": "Steel"}),
    tool("build_perimeter", "building", "围住所有居住区（包括分离区域），单层矩形石墙不占居住区，仅遇不可建地形绕行，左右留口；新圈内记录的旧外墙自动标记拆除，旧蓝图取消，房间墙及重合段保留。", "F",
         ["F", "${map_id}", "${stuff_def}"],
         [INT_MAP(),
          argument("stuff_def", "string", "可选岩石材料 ThingDef。", required=False)],
         {"map_id": 1, "stuff_def": "BlocksGranite"}),
    tool("grow_fertile", "zone", "从起点扩展为连续最高肥力种植区，每次最多15×所有存活殖民者人数（AI+玩家）格。", "G",
         ["G", "${map_id}", "${plant_def}", "${x}", "${z}", "fertile_adjacent"],
         [INT_MAP(), argument("plant_def", "string", "已解锁可种植植物 ThingDef。"),
          argument("x", "integer", "候选起点 X。"), argument("z", "integer", "候选起点 Z。")],
         {"map_id": 1, "plant_def": "Plant_Rice", "x": 60, "z": 60}),
    tool("grow_rectangle", "zone", "建立一个矩形种植区。", "G",
         ["G", "${map_id}", "${plant_def}", "${x1}", "${z1}", "${x2}", "${z2}"],
         [INT_MAP(), argument("plant_def", "string", "已解锁可种植植物 ThingDef。"), *RECT()],
         {"map_id": 1, "plant_def": "Plant_Rice", "x1": 60, "z1": 60, "x2": 72, "z2": 72}),
    tool("stockpile", "zone", "建立普通矩形仓储区。", "S", ["S", "${map_id}", "${x1}", "${z1}", "${x2}", "${z2}"],
         [INT_MAP(), *RECT()], {"map_id": 1, "x1": 30, "z1": 30, "x2": 36, "z2": 36}),
    tool("dump_stockpile", "zone", "建立至少 7x7 的 AI 临时垃圾储存区。", "S",
         ["S", "${map_id}", "${x1}", "${z1}", "${x2}", "${z2}", "dump"], [INT_MAP(), *RECT()],
         {"map_id": 1, "x1": 30, "z1": 30, "x2": 36, "z2": 36}),
    tool("production_add", "production", "在工作台添加一个基础重复次数生产账单。", "P",
         ["P", "${map_id}", "${table_id}", "${recipe_def}", "${count}"],
         [INT_MAP(), argument("table_id", "integer", "工作台 ID。"), argument("recipe_def", "string", "配方 RecipeDef。"),
          argument("count", "integer", "生产次数，1-1000。", minimum=1, maximum=1000)],
         {"map_id": 1, "table_id": 7001, "recipe_def": "Make_Parka", "count": 5}),
    tool("research", "research", "选择一个当前可开始的研究项目。", "R", ["R", "${map_id}", "${research_def}"],
         [INT_MAP(), argument("research_def", "string", "RESEARCH_AVAILABLE 中的 ResearchProjectDef。")],
         {"map_id": 1, "research_def": "Electricity"}),
    tool("designate_area", "designation", "在矩形范围批量添加或移除原版指定。", "T.<action>",
         ["T", "${map_id}", "${x1}", "${z1}", "${x2}", "${z2}", "${action}", "${color_def}"],
         [INT_MAP(), *RECT(), argument("action", "string", "批量指定操作。", enum=DESIGNATIONS),
          argument("color_def", "string", "粉饰操作可选 ColorDef。", required=False)],
         {"map_id": 1, "x1": 20, "z1": 20, "x2": 40, "z2": 40, "action": "chop"}),
    tool("mine_ore_vein", "designation", "从指定坐标沿四向相邻的同类地表矿脉标记开采，不包括普通岩石、深层矿和不相连矿脉。", "ORE",
         ["ORE", "${map_id}", "${x}", "${z}"], [INT_MAP(), argument("x", "integer", "矿脉起点 X。"), argument("z", "integer", "矿脉起点 Z。")],
         {"map_id": 1, "x": 40, "z": 50}),
    tool("designate_target", "designation", "让指定 AI 殖民者为单个目标添加原版指定。", "X.<action>",
         ["X", "${pawn_id}", "${target_id}", "${action}"],
         [argument("pawn_id", "integer", "AI 殖民者 ID。"), argument("target_id", "integer", "目标 Thing ID。"),
          argument("action", "string", "单目标指定操作。", enum=TARGET_DESIGNATIONS)],
         {"pawn_id": 101, "target_id": 9001, "action": "chop"}),
    tool("direct_job", "pawn", "让一个 AI 殖民者立即执行紧急或装备类直接任务。", "J.<action>",
         ["J", "${pawn_id}", "${target_id}", "${action}"],
         [argument("pawn_id", "integer", "AI 殖民者 ID。"), argument("target_id", "integer", "目标 Thing ID。"),
          argument("action", "string", "直接任务类型。", enum=DIRECT_JOBS)],
         {"pawn_id": 101, "target_id": 9001, "action": "tend"}),

    tool("timetable_all", "pawn_settings", "把地图上全部 AI 殖民者的 24 小时作息设为同一种类型。", "C.timetable",
         ["C", "${map_id}", "timetable", "${assignment}"],
         [INT_MAP(), argument("assignment", "string", "作息类型。", enum=TIME_ASSIGNMENTS)],
         {"map_id": 1, "assignment": "Anything"}),
    tool("medical_care", "pawn_settings", "设置一个 AI 殖民者允许使用的医疗等级。", "C.medical",
         ["C", "${map_id}", "medical", "${pawn_id}", "${care}"],
         [INT_MAP(), argument("pawn_id", "integer", "AI 殖民者 ID。"),
          argument("care", "string", "医疗等级。", enum=["NoMeds", "HerbalOrWorse", "NormalOrWorse", "IndustrialOrWorse", "GlitterworldOrWorse"])],
         {"map_id": 1, "pawn_id": 101, "care": "NormalOrWorse"}),
    tool("assign_policy", "pawn_settings", "给一个 AI 殖民者分配服装、药物或食物方案。", "C.policy",
         ["C", "${map_id}", "policy", "${pawn_id}", "${policy_type}", "${policy_index}"],
         [INT_MAP(), argument("pawn_id", "integer", "AI 殖民者 ID。"),
          argument("policy_type", "string", "方案类型。", enum=["outfit", "drug", "food"]),
          argument("policy_index", "integer", "POLICY_INDEX 中的序号。", minimum=0)],
         {"map_id": 1, "pawn_id": 101, "policy_type": "outfit", "policy_index": 0}),
    tool("gear", "pawn", "装备、穿戴、丢下、携带或主动使用物品；装备与穿戴会检查技能。", "C.gear",
         ["C", "${map_id}", "gear", "${pawn_id}", "${item_id}", "${action}", "${target_id}"],
         [INT_MAP(), argument("pawn_id", "integer", "AI 殖民者 ID。"), argument("item_id", "integer", "物品 ID。"),
          argument("action", "string", "物品操作。", enum=["equip", "wear", "drop", "drop_inventory", "carry", "use"]),
          argument("target_id", "integer", "use 操作的可选目标 ID。", required=False)],
         {"map_id": 1, "pawn_id": 101, "item_id": 8001, "action": "equip"}),
    tool("roof", "zone", "批量建造或移除屋顶。", "C.roof",
         ["C", "${map_id}", "roof", "${x1}", "${z1}", "${x2}", "${z2}", "${action}"],
         [INT_MAP(), *RECT(), argument("action", "string", "屋顶操作。", enum=["build", "remove"])],
         {"map_id": 1, "x1": 40, "z1": 40, "x2": 52, "z2": 52, "action": "remove"}),
    tool("home_area", "zone", "把矩形范围加入或移出居住区。", "C.home",
         ["C", "${map_id}", "home", "${x1}", "${z1}", "${x2}", "${z2}", "${enabled}"],
         [INT_MAP(), *RECT(), BOOL("enabled", "true 加入，false 移出。")],
         {"map_id": 1, "x1": 40, "z1": 40, "x2": 52, "z2": 52, "enabled": True}),
    tool("storage_priority", "storage", "设置仓储区优先级。", "C.storepriority",
         ["C", "${map_id}", "storePriority", "${zone_id}", "${priority}"],
         [INT_MAP(), argument("zone_id", "integer", "仓储区 ID。"),
          argument("priority", "string", "仓储优先级。", enum=["Low", "Normal", "Preferred", "Important", "Critical"])],
         {"map_id": 1, "zone_id": 7000, "priority": "Important"}),
    tool("storage_allow", "storage", "允许或禁止仓储区存放一个 ThingDef。", "C.storeallow",
         ["C", "${map_id}", "storeAllow", "${zone_id}", "${thing_def}", "${allow}"],
         [INT_MAP(), argument("zone_id", "integer", "仓储区 ID。"), argument("thing_def", "string", "物品 ThingDef。"),
          BOOL("allow", "true 允许，false 禁止。")],
         {"map_id": 1, "zone_id": 7000, "thing_def": "Steel", "allow": True}),
    tool("storage_clear", "storage", "清空仓储区的全部物品许可。", "C.storeclear",
         ["C", "${map_id}", "storeClear", "${zone_id}"], [INT_MAP(), argument("zone_id", "integer", "仓储区 ID。")],
         {"map_id": 1, "zone_id": 7000}),
    tool("storage_delete_dump", "storage", "石块搬运完成后删除 AI 临时垃圾储存区。", "C.storedelete",
         ["C", "${map_id}", "storeDelete", "${zone_id}"], [INT_MAP(), argument("zone_id", "integer", "临时垃圾区 ID。")],
         {"map_id": 1, "zone_id": 7000}),
    tool("prisoner_recruit", "prisoner", "把囚犯互动设为招募。", "C.prisoner",
         ["C", "${map_id}", "prisoner", "${pawn_id}", "recruit"],
         [INT_MAP(), argument("pawn_id", "integer", "囚犯 ID。")], {"map_id": 1, "pawn_id": 9101}),
    tool("prisoner_release", "prisoner", "把囚犯设为释放。", "C.prisoner",
         ["C", "${map_id}", "prisoner", "${pawn_id}", "release"],
         [INT_MAP(), argument("pawn_id", "integer", "囚犯 ID。")], {"map_id": 1, "pawn_id": 9101}),
    tool("prisoner_interaction", "prisoner", "设置囚犯的原版互动模式。", "C.prisoner",
         ["C", "${map_id}", "prisoner", "${pawn_id}", "interaction", "${interaction_def}"],
         [INT_MAP(), argument("pawn_id", "integer", "囚犯 ID。"),
          argument("interaction_def", "string", "PrisonerInteractionModeDef。")],
         {"map_id": 1, "pawn_id": 9101, "interaction_def": "Recruit"}),
    tool("bed_mode", "building_settings", "切换床的殖民者、囚犯、奴隶和医疗状态。", "C.bed",
         ["C", "${map_id}", "bed", "${bed_id}", "${mode}"],
         [INT_MAP(), argument("bed_id", "integer", "已建成床位 ID。"),
          argument("mode", "string", "床位模式。", enum=["colonist", "prisoner", "slave", "medical", "medical_colonist", "medical_prisoner", "medical_slave", "normal"])],
         {"map_id": 1, "bed_id": 8100, "mode": "medical_prisoner"}),
    tool("door_mode", "building_settings", "打开、关闭或保持门敞开。", "C.door",
         ["C", "${map_id}", "door", "${door_id}", "${mode}"],
         [INT_MAP(), argument("door_id", "integer", "已建成门 ID。"),
          argument("mode", "string", "门状态。", enum=["open", "close", "hold_open"])],
         {"map_id": 1, "door_id": 8200, "mode": "hold_open"}),
    tool("deep_resource", "resource", "读取一个坐标上的已探测深层资源。", "C.deep",
         ["C", "${map_id}", "deep", "${x}", "${z}"],
         [INT_MAP(), argument("x", "integer", "坐标 X。"), argument("z", "integer", "坐标 Z。")],
         {"map_id": 1, "x": 50, "z": 50}),
    tool("timetable_hours", "pawn_settings", "一次设置一个或全部 AI 殖民者的连续小时作息。", "C.timetablehour",
         ["C", "${map_id}", "timetableHour", "${pawn_id}", "${start_hour}", "${end_hour}", "${assignment}"],
         [INT_MAP(), argument("pawn_id", "integer", "AI 殖民者 ID；-1 表示全部。"),
          argument("start_hour", "integer", "开始小时。", minimum=0, maximum=23),
          argument("end_hour", "integer", "结束小时。", minimum=1, maximum=24),
          argument("assignment", "string", "作息类型。", enum=TIME_ASSIGNMENTS)],
         {"map_id": 1, "pawn_id": -1, "start_hour": 8, "end_hour": 18, "assignment": "Work"}),
    tool("self_tend", "pawn_settings", "允许或禁止一个或全部 AI 殖民者自我治疗。", "C.selftend",
         ["C", "${map_id}", "selfTend", "${pawn_id}", "${enabled}"],
         [INT_MAP(), argument("pawn_id", "integer", "AI 殖民者 ID；-1 表示全部。"), BOOL("enabled", "是否允许自我治疗。")],
         {"map_id": 1, "pawn_id": -1, "enabled": True}),
    tool("hostility_response", "pawn_settings", "设置一个 AI 殖民者遇敌反应。", "C.hostility",
         ["C", "${map_id}", "hostility", "${pawn_id}", "${mode}"],
         [INT_MAP(), argument("pawn_id", "integer", "AI 殖民者 ID。"),
          argument("mode", "string", "遇敌模式。", enum=["Ignore", "Attack", "Flee"])],
         {"map_id": 1, "pawn_id": 101, "mode": "Attack"}),
    tool("allowed_area_assign", "zone", "给一个 AI 殖民者分配现有活动区或取消限制。", "C.area",
         ["C", "${map_id}", "area", "${pawn_id}", "${area_id}"],
         [INT_MAP(), argument("pawn_id", "integer", "AI 殖民者 ID。"),
          argument("area_id", "string", "活动区 ID，或 none。")],
         {"map_id": 1, "pawn_id": 101, "area_id": "none"}),
    tool("allowed_area_create", "zone", "创建矩形活动区并分配给一个或全部 AI 殖民者。", "C.area_new",
         ["C", "${map_id}", "area_new", "${pawn_id}", "${x1}", "${z1}", "${x2}", "${z2}", "${name}"],
         [INT_MAP(), argument("pawn_id", "integer", "AI 殖民者 ID；-1 表示全部。"), *RECT(),
          argument("name", "string", "活动区名称，用下划线代替空格。")],
         {"map_id": 1, "pawn_id": -1, "x1": 20, "z1": 20, "x2": 40, "z2": 40, "name": "safe_zone"}),
    tool("policy_create", "policy", "创建服装、药物或食物方案。", "C.policycreate",
         ["C", "${map_id}", "policyCreate", "${policy_type}", "${name}"],
         [INT_MAP(), argument("policy_type", "string", "方案类型。", enum=["outfit", "drug", "food"]),
          argument("name", "string", "方案名称，用下划线代替空格。")],
         {"map_id": 1, "policy_type": "outfit", "name": "Combat"}),
    tool("policy_edit", "policy", "允许或禁止一个方案使用指定 ThingDef。", "C.policyedit",
         ["C", "${map_id}", "policyEdit", "${policy_type}", "${policy_index}", "${thing_def}", "${allow}"],
         [INT_MAP(), argument("policy_type", "string", "方案类型。", enum=["outfit", "food", "drug"]),
          argument("policy_index", "integer", "方案序号。", minimum=0), argument("thing_def", "string", "物品 ThingDef。"),
          BOOL("allow", "是否允许。")],
         {"map_id": 1, "policy_type": "outfit", "policy_index": 1, "thing_def": "FlakVest", "allow": True}),
    tool("storage_category", "storage", "批量允许或禁止仓储区/储物建筑中的一类物品。", "C.storecategory",
         ["C", "${map_id}", "storeCategory", "${target_id}", "${category}", "${allow}"],
         [INT_MAP(), argument("target_id", "integer", "仓储区或储物建筑 ID。"),
          argument("category", "string", "物品类别。", enum=["food", "weapons", "apparel", "medicine", "raw", "chunks", "corpses", "plants"]),
          BOOL("allow", "是否允许。")],
         {"map_id": 1, "target_id": 7000, "category": "food", "allow": True}),
    tool("storage_dedicated", "storage", "把仓储区或储物建筑设为只存放一个 ThingDef。", "C.storededicated",
         ["C", "${map_id}", "storeDedicated", "${target_id}", "${thing_def}"],
         [INT_MAP(), argument("target_id", "integer", "仓储区或储物建筑 ID。"), argument("thing_def", "string", "唯一允许的 ThingDef。")],
         {"map_id": 1, "target_id": 7000, "thing_def": "Steel"}),
    tool("temperature", "building_settings", "设置温控建筑的目标摄氏温度。", "C.temp",
         ["C", "${map_id}", "temp", "${building_id}", "${celsius}"],
         [INT_MAP(), argument("building_id", "integer", "温控建筑 ID。"), argument("celsius", "number", "目标摄氏温度。")],
         {"map_id": 1, "building_id": 7100, "celsius": -5}),
    tool("auto_rebuild", "building_settings", "开启或关闭地图自动重建。", "C.rebuild",
         ["C", "${map_id}", "rebuild", "${enabled}"], [INT_MAP(), BOOL("enabled", "是否自动重建。")],
         {"map_id": 1, "enabled": True}),
    tool("home_area_auto", "zone", "按当前玩家建筑自动补充居住区。", "C.homeauto",
         ["C", "${map_id}", "homeAuto"], [INT_MAP()], {"map_id": 1}),
    tool("floor", "building", "在矩形范围放置地板蓝图。", "C.floor",
         ["C", "${map_id}", "floor", "${terrain_def}", "${x1}", "${z1}", "${x2}", "${z2}", "${stuff_def}"],
         [INT_MAP(), argument("terrain_def", "string", "地板 TerrainDef。"), *RECT(),
          argument("stuff_def", "string", "可选地板材料 ThingDef。", required=False)],
         {"map_id": 1, "terrain_def": "Tile_Sandstone", "x1": 40, "z1": 40, "x2": 52, "z2": 52}),
    tool("power_conduit", "building", "在矩形范围放置普通电缆蓝图。", "C.conduit",
         ["C", "${map_id}", "conduit", "${x1}", "${z1}", "${x2}", "${z2}"],
         [INT_MAP(), *RECT()], {"map_id": 1, "x1": 40, "z1": 45, "x2": 52, "z2": 45}),
    tool("hidden_conduit", "building", "沿起点到终点的直角路径放置隐藏电缆蓝图。", "C.hiddenconduit",
         ["C", "${map_id}", "hiddenConduit", "${x1}", "${z1}", "${x2}", "${z2}"],
         [INT_MAP(), *RECT()], {"map_id": 1, "x1": 40, "z1": 45, "x2": 52, "z2": 45}),
    tool("defense", "combat", "在外围墙缺口外按 4x8 斜交叉放置至少 16 个钢铁陷阱。", "C.defense",
         ["C", "${map_id}", "defense"], [INT_MAP()], {"map_id": 1}, aliases=["fortify"]),
    tool("surgery", "medical", "给 AI 患者添加一个手术账单。", "C.surgery",
         ["C", "${map_id}", "surgery", "${pawn_id}", "${recipe_def}", "${body_part_def}"],
         [INT_MAP(), argument("pawn_id", "integer", "AI 患者 ID。"), argument("recipe_def", "string", "手术 RecipeDef。"),
          argument("body_part_def", "string", "可选 BodyPartDef。", required=False)],
         {"map_id": 1, "pawn_id": 101, "recipe_def": "InstallBionicEye", "body_part_def": "Eye"}),
    tool("animal_train", "animal", "开启或关闭一个动物训练项目。", "C.animal",
         ["C", "${map_id}", "animal", "${animal_id}", "train", "${trainable_def}", "${wanted}"],
         [INT_MAP(), argument("animal_id", "integer", "动物 ID。"), argument("trainable_def", "string", "TrainableDef。"),
          BOOL("wanted", "是否需要训练。")],
         {"map_id": 1, "animal_id": 9200, "trainable_def": "Obedience", "wanted": True}),
    tool("animal_release", "animal", "将属于殖民地的动物放归野外。", "C.animal",
         ["C", "${map_id}", "animal", "${animal_id}", "release"],
         [INT_MAP(), argument("animal_id", "integer", "动物 ID。")], {"map_id": 1, "animal_id": 9200}),
    tool("animal_flag", "animal", "开启或关闭动物的觅食或挖掘设置。", "C.animal",
         ["C", "${map_id}", "animal", "${animal_id}", "${flag}", "${enabled}"],
         [INT_MAP(), argument("animal_id", "integer", "动物 ID。"),
          argument("flag", "string", "动物设置。", enum=["forage", "dig"]), BOOL("enabled", "是否启用。")],
         {"map_id": 1, "animal_id": 9200, "flag": "forage", "enabled": True}),
    tool("deep_drill_auto", "resource", "在已探测的最佳深层资源位置放置深钻井蓝图。", "C.deepauto",
         ["C", "${map_id}", "deepAuto", "${resource_def}"],
         [INT_MAP(), argument("resource_def", "string", "可选目标资源 ThingDef。", required=False)],
         {"map_id": 1, "resource_def": "Steel"}),
    tool("bill_config", "production", "修改已有生产账单的原料、质量、数量、暂停、存放、技能等设置。", "C.bill",
         ["C", "${map_id}", "bill", "${table_id}", "${recipe_def}", "${field}", "${value}"],
         [INT_MAP(), argument("table_id", "integer", "工作台 ID。"), argument("recipe_def", "string", "已有账单的 RecipeDef。"),
          argument("field", "string", "账单字段。", enum=["ingredient", "quality", "target", "repeat", "pause", "suspend", "unpause", "store", "include", "skill", "stuff", "name"]),
          argument("value", "string", "字段值；列表用逗号，范围用连字符，名称用下划线。")],
         {"map_id": 1, "table_id": 7001, "recipe_def": "Make_Parka", "field": "repeat", "value": "forever"},
         aliases=["billconfig"]),
    tool("bill_order", "production", "调整一个工作台内的账单顺序。", "C.billorder",
         ["C", "${map_id}", "billOrder", "${table_id}", "${bill_index}", "${delta}"],
         [INT_MAP(), argument("table_id", "integer", "工作台 ID。"), argument("bill_index", "integer", "零开始账单序号。", minimum=0),
          argument("delta", "integer", "上下移动格数；负数向前。")],
         {"map_id": 1, "table_id": 7001, "bill_index": 1, "delta": -1}),
    tool("ritual", "ideology", "启动一个当前可用的 Ideology 文化仪式。", "C.ritual",
         ["C", "${map_id}", "ritual", "${ritual_id}", "${target}", "${organizer_id}", "${obligation_id}"],
         [INT_MAP(), argument("ritual_id", "integer", "IDEOLOGY_RITUAL 中的仪式 ID。"),
          argument("target", "string", "目标 ThingID、x:z 或 none。"),
          argument("organizer_id", "string", "可选 AI 组织者 ID；无组织者但有义务时填 -。", required=False),
          argument("obligation_id", "integer", "可选活动义务 ID。", required=False)],
         {"map_id": 1, "ritual_id": 4501, "target": "8200", "organizer_id": "101"}),
    tool("culture_chairs", "ideology", "将地图上的椅子切换为当前主文化可用的专属样式；可指定单个 ThingID 或 all。", "C.culturechair",
         ["C", "${map_id}", "cultureChair", "${target}"],
         [INT_MAP(), argument("target", "string", "椅子 ThingID，或 all 表示地图上所有可换样式的椅子。")],
         {"map_id": 1, "target": "all"}),

    tool("world_caravan", "world", "组建只含 AI 殖民者的商队并前往目的地。", "WORLD.caravan",
         ["WORLD", "caravan", "${map_id}", "${destination_tile}", "${pawn_ids}", "${items}"],
         [INT_MAP(), argument("destination_tile", "integer", "目的地世界 tile。"),
          argument("pawn_ids", "string", "逗号分隔的 AI 殖民者 ID。"),
          argument("items", "string", "可选物品 ID[:数量]，多个用逗号。", required=False)],
         {"map_id": 1, "destination_tile": 456, "pawn_ids": "101,102"}),
    tool("world_move", "world", "让 AI 专属商队改道。", "WORLD.move",
         ["WORLD", "move", "${caravan_id}", "${destination_tile}"],
         [argument("caravan_id", "integer", "AI 商队 ID。"), argument("destination_tile", "integer", "目的地世界 tile。")],
         {"caravan_id": 3001, "destination_tile": 456}),
    tool("world_load", "world", "把物品或 AI 殖民者装入运输舱。", "WORLD.load",
         ["WORLD", "load", "${transporter_id}", "${target_id}", "${count}"],
         [argument("transporter_id", "integer", "运输舱建筑 ID。"), argument("target_id", "integer", "物品或 AI 殖民者 ID。"),
          argument("count", "integer", "可选物品数量。", required=False, minimum=1)],
         {"transporter_id": 8300, "target_id": 8001, "count": 10}),
    tool("world_launch", "world", "发射已满足条件的运输舱。", "WORLD.launch",
         ["WORLD", "launch", "${transporter_id}", "${destination_tile}", "${mode}"],
         [argument("transporter_id", "integer", "运输舱建筑 ID。"), argument("destination_tile", "integer", "目的地世界 tile。"),
          argument("mode", "string", "抵达方式。", required=False, enum=["caravan", "land"])],
         {"transporter_id": 8300, "destination_tile": 456, "mode": "land"}),
    tool("world_trade_ui", "world", "打开原版贸易窗口并请求玩家完成交易。", "WORLD.trade",
         ["WORLD", "trade", "${world_object_id}", "${pawn_id}", "ui"],
         [argument("world_object_id", "integer", "可贸易世界对象 ID。"), argument("pawn_id", "integer", "AI 谈判者 ID。")],
         {"world_object_id": 3001, "pawn_id": 101}),
    tool("world_trade", "world", "在设置允许 AI 直接交易时买入或卖出物品。", "WORLD.trade",
         ["WORLD", "trade", "${world_object_id}", "${pawn_id}", "${direction}", "${thing_def}", "${count}"],
         [argument("world_object_id", "integer", "可贸易世界对象 ID。"), argument("pawn_id", "integer", "AI 谈判者 ID。"),
          argument("direction", "string", "交易方向。", enum=["buy", "sell"]), argument("thing_def", "string", "交易物 ThingDef。"),
          argument("count", "integer", "交易数量。", minimum=1)],
         {"world_object_id": 3001, "pawn_id": 101, "direction": "buy", "thing_def": "Steel", "count": 100}),
]


def _tool_map() -> dict[str, dict[str, Any]]:
    result: dict[str, dict[str, Any]] = {}
    for item in TOOLS:
        for name in [item["name"], *item["aliases"]]:
            if name in result:
                raise RuntimeError(f"重复 CLI 工具名：{name}")
            result[name] = item
    return result


TOOL_MAP = _tool_map()


def public_tool(item: dict[str, Any]) -> dict[str, Any]:
    properties: dict[str, Any] = {}
    required: list[str] = []
    for item_arg in item["arguments"]:
        schema = {key: value for key, value in item_arg.items() if key not in {"name", "required", "allow_spaces"}}
        properties[item_arg["name"]] = schema
        if item_arg["required"]:
            required.append(item_arg["name"])
    result = {
        "name": item["name"],
        "category": item["category"],
        "description": item["description"],
        "permission": item["permission"],
        "input_schema": {
            "type": "object",
            "properties": properties,
            "required": required,
            "additionalProperties": False,
        },
        "example": item["example"],
    }
    if item["aliases"]:
        result["aliases"] = item["aliases"]
    return result


def catalog(tool_name: str | None = None) -> dict[str, Any]:
    if tool_name:
        item = TOOL_MAP.get(tool_name)
        if item is None:
            raise ValueError(f"未知工具：{tool_name}")
        return {"catalog_version": CATALOG_VERSION, "count": 1, "tools": [public_tool(item)]}
    return {"catalog_version": CATALOG_VERSION, "count": len(TOOLS), "tools": [public_tool(item) for item in TOOLS]}


def build_command(tool_name: str, values: dict[str, Any]) -> str:
    item = TOOL_MAP.get(tool_name)
    if item is None:
        raise ValueError(f"未知工具：{tool_name}")
    if not isinstance(values, dict):
        raise ValueError("工具参数必须是 JSON 对象。")

    definitions = {item_arg["name"]: item_arg for item_arg in item["arguments"]}
    unknown = sorted(set(values) - set(definitions))
    if unknown:
        raise ValueError("未知参数：" + ", ".join(unknown))
    missing = [name for name, item_arg in definitions.items() if item_arg["required"] and (name not in values or values[name] is None)]
    if missing:
        raise ValueError("缺少必填参数：" + ", ".join(missing))
    for group in item["required_together"]:
        present = [name for name in group if name in values and values[name] is not None]
        if present and len(present) != len(group):
            raise ValueError("这些参数必须一起提供：" + ", ".join(group))

    converted: dict[str, str] = {}
    for name, value in values.items():
        if value is None:
            continue
        converted[name] = _convert_value(definitions[name], value)

    output: list[str] = []
    omitted_optional = False
    for template_item in item["template"]:
        if not (template_item.startswith("${") and template_item.endswith("}")):
            output.append(template_item)
            continue
        name = template_item[2:-1]
        if name not in converted:
            omitted_optional = True
            continue
        if omitted_optional:
            raise ValueError(f"参数 {name} 前面的可选参数不能省略；请按工具 schema 补齐。")
        output.append(converted[name])
    return " ".join(output)


def _convert_value(definition: dict[str, Any], value: Any) -> str:
    value_type = definition["type"]
    name = definition["name"]
    if value_type == "boolean":
        if not isinstance(value, bool):
            raise ValueError(f"参数 {name} 必须是 JSON boolean。")
        text = "1" if value else "0"
    elif value_type == "integer":
        if isinstance(value, bool) or not isinstance(value, int):
            raise ValueError(f"参数 {name} 必须是 JSON integer。")
        text = str(value)
    elif value_type == "number":
        if isinstance(value, bool) or not isinstance(value, (int, float)):
            raise ValueError(f"参数 {name} 必须是 JSON number。")
        text = str(value)
    elif value_type == "string":
        if not isinstance(value, str) or not value:
            raise ValueError(f"参数 {name} 必须是非空字符串。")
        if "\r" in value or "\n" in value:
            raise ValueError(f"参数 {name} 不能包含换行。")
        if not definition.get("allow_spaces") and any(character.isspace() for character in value):
            raise ValueError(f"参数 {name} 不能包含空白；名称请使用下划线。")
        text = value
    else:
        raise ValueError(f"参数 {name} 使用未知类型 {value_type}。")

    if "minimum" in definition and value < definition["minimum"]:
        raise ValueError(f"参数 {name} 不能小于 {definition['minimum']}。")
    if "maximum" in definition and value > definition["maximum"]:
        raise ValueError(f"参数 {name} 不能大于 {definition['maximum']}。")
    if "enum" in definition:
        canonical = next((candidate for candidate in definition["enum"] if str(candidate).lower() == text.lower()), None)
        if canonical is None:
            raise ValueError(f"参数 {name} 必须是：{', '.join(str(item) for item in definition['enum'])}")
        text = str(canonical)
    return text


def validate_catalog() -> None:
    for item in TOOLS:
        build_command(item["name"], item["example"])


if __name__ == "__main__":
    import json

    validate_catalog()
    print(json.dumps({"ok": True, "catalog_version": CATALOG_VERSION, "count": len(TOOLS)}, ensure_ascii=False))
