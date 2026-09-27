AI 协作知识库

DeepSeekHarnessPlugin/aicoop_tools.py 是外接 Agent 的工具参数与示例权威目录。
ToolChains 目录只保存本模组工具操作与权限规则；LongTerm 保存玩家设定的协作规则。外接 Agent 通过 reference_index 和 reference_read 按需读取。
当前游戏和其他 Mod 的全部可建造 ThingDef 会在每次工作请求的 KNOWLEDGE_BUILD_DEFS 状态段中动态生成；defName=中文名称，BUILD_DEFS 只列出当前已研究且可以立即放置的项目。
游戏物品用途、设施搭配、研究前置和玩法机制不由本知识库讲解。不确定时使用 Harness 的 web_search、web_fetch 联网查询，优先匹配 RimWorld 1.6、当前 DLC 和相关 Mod 的资料。网页仅作参考；对象 ID、DefName、解锁条件和命令格式以当前游戏状态和工具目录为准。搜索失败时说明不确定性，不能猜测；不得向网页发送存档、聊天或密钥。

DLC操作链：ToolChains/dlc_mechanitor.txt、dlc_anomaly.txt、dlc_odyssey.txt、dlc_royalty_biotech.txt，以及ideology_and_rituals.txt。先通过dlc_features确认实际可用能力。这些文件描述已接入工具及缺口，不代表全部DLC事件和界面均已自动化。复杂仪式/多阶段目标与特殊探索事件无完整接口时必须请求玩家；任务接取、飞船起飞与选图由玩家进行。收到player_action_required=1时游戏已发送请求，不重复发送。
