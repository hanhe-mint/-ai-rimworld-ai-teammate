# 环世界AI队友 · RimWorld AI Teammate

作者：寒荷mint

**必须使用 DeepSeek Harness（DSH）0.1.7 及以上版本；适用于 Windows 上的 RimWorld 1.6。** 独立提示词界面按 0.1.7-rc.2 接口适配，其他版本尚未逐一验证。DSH 需要自行安装，模型和 API 在 DSH 中配置；Harmony 作为独立前置模组启用，不要重复安装。

接入deepseek harness，让deepseek加入你的殖民地，和你一起在环世界生存。 你和AI分别管理自己的殖民者，也可以开启共享控制。AI直接读取游戏数据，安排种植、采集、建造、生产、研究和战斗。你可以在游戏或Harness内和它聊天、提出需求，或回应它的求助。 支持房间预设、分阶段攻略、工具权限管理、决策间隔调整和思考时暂停。攻略进度及相关记录随存档保存。

本人第一次做这种类型的，做的不是很好，现在属于半成品，希望有感兴趣的大佬们优化这个作品。

## 下载与使用

普通玩家请下载 [v0.4.0 Release 的玩家版 ZIP](https://github.com/hanhe-mint/-ai-rimworld-ai-teammate/releases/tag/v0.4.0)，完整解压后阅读包内《使用说明/安装与使用教程.txt》。包含安装、卸载脚本，官方 DSH 和 dsh-launcher 两种安装教程；不包含 DSH 本体。

## 玩家交流论坛

欢迎到 [环世界AI队友论坛](https://github.com/hanhe-mint/-ai-rimworld-ai-teammate/discussions) 上传自己制作的殖民地房间预设、成品布局，以及本Mod的补丁和扩展，也可以交流玩法、反馈问题。

新版 DSH 使用 `install-modern.ps1` 向实际用户目录的 `profiles/<profile>/cordis.patch.yml` 注册插件，不再使用旧 `.agent-presets` 目录。更新后请重启游戏和 DSH。

这不是真人联机模组。AI 可能判断失误或操作失败，不能保证自动通关；部分 DLC 尚未支持，其他 Mod 的兼容性需要测试。请备份存档，模型调用可能产生费用。

## 文件夹用途

| 文件夹 | 作用 |
| --- | --- |
| `Mods/AICoopCompanion` | Mod 资源、配置和插件；编译后的游戏 DLL 输出到其中的 `Assemblies`，仓库不跟踪生成的 DLL。 |
| `Mods/AICoopCompanion/Source/AICoopCompanion` | Mod 的 C# 源码与编译脚本。 |
| `Mods/AICoopCompanion/Development/packages` | 编译依赖：C# 编译器、.NET 参考程序集和 Harmony 编译引用，不包含游戏程序集。 |
| `使用说明` | 源码构建、上传方法、依赖说明和更新日志，统一为 TXT。 |
| `Mods/AICoopCompanion/DeepSeekHarnessPlugin` | DSH 插件、Python CLI 工具、提示词及插件注册脚本，可直接编辑。 |
| `Mods/AICoopCompanion/About` | Mod 名称、作者、简介和前置依赖等元数据。 |
| `Mods/AICoopCompanion/Defs` | 游戏 XML 定义，如剧本、界面入口等。 |
| `Mods/AICoopCompanion/Guides` | AI 分阶段攻略与任务资料。 |
| `Mods/AICoopCompanion/Knowledge` | AI 工具用法、关联操作链和长期知识。 |
| `Mods/AICoopCompanion/Presets` | 殖民地房间与防御建筑预设。 |
| `Mods/AICoopCompanion/Textures` | Mod 图标及纹理资源。 |
| `Mods/AICoopCompanion/Development/Distribution` | 发布用安装、卸载脚本及说明源文件。 |
| `Mods/AICoopCompanion/DeepSeekHarnessPlugin/PromptUI` | 实验性AI提示词编辑按钮与界面。 |
| `Mods/AICoopCompanion/Tools` | 房间预设和成品布局导出工具及源码。 |
| `Mods/AICoopCompanion/成品` | 玩家自定义成品布局的说明与示例。 |

## 源码构建

源码以独立文件直接保存在仓库，不需要下载源码版 ZIP。克隆仓库后运行根目录的 `编译Mod.cmd`，输入包含 `RimWorldWin64.exe` 的游戏目录。输出位于 `Mods/AICoopCompanion/Assemblies/AICoopCompanion.dll`。

游戏 DLL、DSH 程序、个人配置、API 密钥及存档不随源码上传。修改 C# 后需重新编译并重启游戏；修改插件后需重启 DSH。

查看 [0.4.0 开发日志](使用说明/开发日志.txt)。

## 开源协议

Copyright (C) 2026 寒荷mint。项目原创代码采用 [GNU GPL v3.0](LICENSE)（GPL-3.0-only）。允许使用、修改和商用；发布修改版或衍生版本时，须按 GPL-3.0 提供对应源码、保留版权和协议声明，并注明修改。不发布的私人修改无需公开。软件不提供担保。

第三方依赖和素材保留各自的许可证与权利，不因本项目采用 GPL 而改变；已按其他许可证发布的旧版本授权不被追溯撤回。详情见 [授权说明](使用说明/授权说明.txt)。
