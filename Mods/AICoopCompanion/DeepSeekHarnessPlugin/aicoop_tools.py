"""Typed CLI tool catalog bundled with the DeepSeek Harness plugin."""

from __future__ import annotations

from typing import Any


CATALOG_VERSION = 6


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

TIME_ASSIGNMENTS = ["Work", "Joy", "Sleep", "Anything"]
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
    tool("work_priorities", "pawns", "按 WORK_PRIORITY_ORDER 一次设置一名 AI 殖民者全部工作优先级；禁用项为0，低技能不要设1或2。", "C.work",
         ["C", "${map_id}", "work", "${pawn_id}", "${priorities}"],
         [INT_MAP(), argument("pawn_id", "integer", "AI殖民者ID"), argument("priorities", "string", "全部0到4的数字，逗号分隔；数量必须匹配 WORK_PRIORITY_ORDER")],
         {"map_id": 0, "pawn_id": 123, "priorities": "1,3,3,3,2,3,3"}),
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
    tool("note_done", "memory", "将存档内指定编号的 AI 待办标记为完成。", "none", ["NOTE_DONE", "${note_id}"],
         [argument("note_id", "integer", "NOTE_READ 返回的待办编号。")], {"note_id": 1}),
    tool("map_scan", "inspection", "读取矩形内地形、物品、人物、蓝图、区域与设施状态；每页128格，next非done时用相同范围及next作为offset续读。", "MAP_SCAN",
         ["MAP_SCAN", "${map_id}", "${x1}", "${z1}", "${x2}", "${z2}", "${offset}"],
         [INT_MAP(), *RECT(), argument("offset", "integer", "续页偏移，默认0。", required=False, minimum=0)],
         {"map_id": 0, "x1": 100, "z1": 100, "x2": 110, "z2": 110}),
    tool("guide_done", "progress", "将当前攻略任务标记为完成。", "none", ["GUIDE_DONE", "${task_id}"],
         [argument("task_id", "string", "当前攻略任务编号。")], {"task_id": "01.03"}),
    tool("preset_build", "building", "按房间预设自动选址，或在指定锚点放置一个预设房间。", "PRESET",
         ["PRESET", "${preset}", "${map_id}", "${x}", "${z}", "${rotation}"],
         [argument("preset", "string", "reference_index 返回的准确 room_*.txt 文件名。"),
          argument("map_id", "integer", "可选地图 ID。", required=False),
          argument("x", "integer", "可选锚点 X。", required=False),
          argument("z", "integer", "可选锚点 Z。", required=False),
          argument("rotation", "integer", "可选旋转，0-3。", required=False, minimum=0, maximum=3)],
         {"preset": "room_starting_core.txt"}, required_together=[["map_id", "x", "z"]]),
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
    tool("mine_all_ore", "designation", "将地图上指定矿物类型的全部天然矿体标为待开采。", "ORE",
         ["ORE", "${map_id}", "${mineral_def}"], [INT_MAP(), argument("mineral_def", "string", "矿物 ThingDef，不能是石块。")],
         {"map_id": 1, "mineral_def": "Steel"}),
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
    tool("organ_harvest", "medical", "给符合规则的 AI 俘虏按肺、肾、心脏/肝顺序添加手术。", "C.organharvest",
         ["C", "${map_id}", "organHarvest", "${pawn_id}"],
         [INT_MAP(), argument("pawn_id", "integer", "囚犯 ID。")], {"map_id": 1, "pawn_id": 9101}),
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
