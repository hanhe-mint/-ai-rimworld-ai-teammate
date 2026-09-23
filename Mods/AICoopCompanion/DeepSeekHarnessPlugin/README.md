# RimWorld AI Coop - DeepSeek Harness 插件

这是一个独立的 DeepSeek Harness Cordis 插件，不修改 Harness 的 `resources/app` 核心文件。插件按会话完成三件事：

- 注入 RimWorld AI 队友专用提示词，并让 Harness 管理会话上下文。
- 启动随包提供的 MCP 子进程，只向 Agent 注册唯一的 `cli_execute` 游戏操作入口；状态、事件和资料由插件随每轮游戏输入附带，不作为 Agent 工具暴露。
- 自动接收定时决策、半日需求、精神崩溃、攻略推进和读档事件；读档时取消旧回合并发送读档后的状态。
- 首轮发送固定基线与完整殖民地状态；后续只发送游戏通知、100% 成熟作物、伤势完全恢复、恢复行动、新敌人等关键变化，以及上一轮结果/错误/拒绝。普通地形、布局和生长变化不重复发送。
- Harness 成功写入 `compaction/summary` 后，插件会在下一轮重新发送固定基线；无需用字符数猜测上下文 token。
- 将游戏发给 Agent 的内容、Agent 输出和工具调用同步写回游戏日志。
- 在会话结束或插件停用时撤销工具并关闭 MCP 子进程。

## 安装

在 PowerShell 中执行：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\install.ps1
```

安装脚本在实际 DeepSeek Harness 用户目录下创建 `.agent-presets\rimworld`，通过绝对路径引用本插件。默认读取 `DSH_HOME`，未设置时读取启动器配置中的唯一有效用户目录，不再使用旧桌面版目录。存在多个目录或独立 CLI 安装时，使用 `-DshUserRoot '实际用户目录'` 明确指定。安装不会修改 Harness 核心。

支持同一台 Windows 电脑上运行的 DSH CLI 或启动器 Web 界面；实际连接由本机 DSH 的 Python 子进程完成，浏览器只是界面。远程服务器、WSL 和容器中的 DSH 不能直接访问此 Windows 命名管道。Python 可用 `-PythonCommand` 指定；插件脚本和游戏资料均相对插件自身目录定位。

## 开启与关闭

- 开启：启用 AI 协作队友 Mod 并载入存档，在 Harness 新建“环世界 AI 队友”会话，点击 `/rimworld-connect` 或发送“连接游戏”。游戏只使用外接 Agent，不再提供模式开关或内置 API 配置。
- 停止：在 Harness 使用 `/rimworld-stop`、`/rimworld-disconnect` 或发送“停止”；需要恢复时重新连接。
- Harness 会话创建后不能中途更换预设；插件更新后请重启 Harness 或新建会话，已有会话及其历史不会被删除。

## 按需资料

- 16 个建筑预设位于 Mod 的 `Presets/room_*.txt`；当前回合所需的预设/任务信息由游戏状态随事件提供，Agent 不再自行读取资料工具。
- 50 个攻略任务位于 `Guides/Tasks/`，游戏每次只发送当前阶段信息，完成后才推送下一条。
- 工具关联链和长期知识由游戏状态与系统提示词提供；不会因资料读取触发额外 Agent 回合。
- 插件修改后，已经打开的 Harness 会话不会热加载；请重启 Harness 或新建“环世界 AI 队友”会话。

## Agent 模式协议

- 聊天页显示 AI 回复正文，不要求任何前缀；思考文本和工具调用不进入聊天。回合结束由 Harness 完成事件判定，不靠回复格式判断。模组不再提供角色人设设置或注入角色口吻。


- 游戏内“聊天 / 日志”分页分别显示对话和执行记录；玩家聊天及请求的三选一回复立即发送给 Agent，不等待决策间隔，Enter 用于发送。
- `C 地图ID work AI殖民者ID 1,3,0,...` 按状态中的 `WORK_PRIORITY_ORDER` 一次提交全部工作优先级（0停用、1最高、4最低）；任一项非法则整组不应用。

- 每条游戏输入只允许输出一次最终回答；在回复前可以连续调用多次 `cli_execute`。回复完成后插件等待游戏设置中的决策间隔，再发送下一轮游戏输入，不需要玩家再次发消息。
- 所有游戏动作（包括 `REQUEST_PLAYER`、`PLAN` 和无操作 `N`）必须放在 `cli_execute.commands` 中，每行一条 CLI 指令；不得在普通消息中粘贴 CLI、JSON 或模拟工具调用。
- 插件不再设置或覆盖模型输出 token 上限；实际上限由 Harness 和所选模型自身决定。
## Manual game connection

The plugin starts its MCP process without opening the RimWorld named pipe. The game is connected only after the user sends a message containing `连接游戏` (or `connect to RimWorld`) in the Harness agent session. The plugin then polls game events and exposes only the single-step `cli_execute` tool; the current state is attached to each event.

When the pipe or save is lost, the plugin stops polling, cancels the active game turn, and sends a disconnect notice to the same Harness agent. Send `连接游戏` again after returning to a save to establish a fresh session. `断开游戏` can be used to stop the bridge intentionally. Send `stop` or `停止`, or use `/rimworld-stop`, to stop the automatic round loop; reconnect to resume.

If a game tool is called before the connection command, it returns a clear `not connected` error. Restart Harness after installing or updating this plugin so the new event listener is loaded.

The composer command menu also contains `/rimworld-connect` and `/rimworld-disconnect`. Clicking `/rimworld-connect` is equivalent to sending the connection phrase and is the recommended manual workflow when the UI exposes command buttons.
