AI 协作知识库

DeepSeekHarnessPlugin/aicoop_tools.py 是外接 Agent 的工具参数与示例权威目录。
ToolChains 目录把完整工具关联链按主题拆分；LongTerm 目录保存按主题拆分的长期知识。外接 Agent 通过 reference_index 和 reference_read 每次只读取一份，不一次注入全部内容。
当前游戏和其他 Mod 的全部可建造 ThingDef 会在每次工作请求的 KNOWLEDGE_BUILD_DEFS 状态段中动态生成；defName=中文名称，BUILD_DEFS 只列出当前已研究且可以立即放置的项目。
运行时还会发送 KNOWLEDGE_TOOL 和 KNOWLEDGE_RELATIONS，包含每个工具的用途、示例、前置条件、失败处理以及建造、种植、生产、深钻、远征、贸易、文化椅子和 Ideology 仪式之间的依赖关系。
