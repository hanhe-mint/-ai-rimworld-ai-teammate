#!/usr/bin/env python3
"""MCP stdio adapter for the RimWorld AI Coop Companion named pipe."""

from __future__ import annotations

import json
import sys
import time
from pathlib import Path
from typing import Any

from aicoop_cli import AgentBridgeClient
from aicoop_tools import CATALOG_VERSION, TOOLS, build_command, public_tool, validate_catalog


SERVER_NAME = "rimworld-ai-coop"
SERVER_VERSION = "0.3.5"
DEFAULT_PROTOCOL_VERSION = "2025-06-18"
PLUGIN_DIR = Path(__file__).resolve().parent
MOD_ROOT = PLUGIN_DIR.parent
REFERENCE_ROOTS = {
    "preset": MOD_ROOT / "Presets",
    "task": MOD_ROOT / "Guides" / "Tasks",
    "tool_chain": MOD_ROOT / "Knowledge" / "ToolChains",
    "knowledge": MOD_ROOT / "Knowledge" / "LongTerm",
}
model_event_cursor = 0
agent_event_cursor = 0
bridge_session: str | None = None
current_task_reference: str | None = None
state_initialized = False
manual_connection = False
consecutive_poll_failures = 0
pending_agent_feedback: list[dict[str, Any]] = []


def write_message(message: dict[str, Any]) -> None:
    sys.stdout.write(json.dumps(message, ensure_ascii=False, separators=(",", ":")) + "\n")
    sys.stdout.flush()


def bridge_call(method: str, **parameters: Any) -> dict[str, Any]:
    with AgentBridgeClient() as client:
        return client.call(method, **parameters)


def text_result(payload: dict[str, Any], is_error: bool = False) -> dict[str, Any]:
    text = json.dumps(payload, ensure_ascii=False, separators=(",", ":"))
    result: dict[str, Any] = {
        "content": [{"type": "text", "text": text}],
        "structuredContent": payload,
    }
    if is_error:
        result["isError"] = True
    return result


def error_result(message: str, code: str = "adapter_error") -> dict[str, Any]:
    return text_result({"ok": False, "error": {"code": code, "message": message}}, True)


def bridge_failed(response: dict[str, Any]) -> bool:
    if not response.get("ok"):
        return True
    result = response.get("result")
    if not isinstance(result, dict):
        return False
    command_results = result.get("results") or []
    logs = result.get("logs") or []
    failure_markers = ("[拒绝]", "[权限]", "[错误]")
    failure_texts = [*command_results, *logs]
    for event in result.get("events") or []:
        if isinstance(event, dict):
            failure_texts.append(event.get("text", ""))
    for value in failure_texts:
        text = str(value).strip()
        if text.startswith("FAIL") or " FAIL " in (" " + text + " "):
            return True
        if any(marker in text for marker in failure_markers):
            return True
    return False


def response_session(response: dict[str, Any]) -> str | None:
    result = response.get("result")
    if not isinstance(result, dict):
        return None
    session = result.get("session")
    return session if isinstance(session, str) and session else None


def adopt_session(response: dict[str, Any]) -> bool:
    global bridge_session, model_event_cursor, agent_event_cursor, current_task_reference, state_initialized, pending_agent_feedback
    session = response_session(response)
    if session is None or session == bridge_session:
        return False
    changed = bridge_session is not None
    if changed and manual_connection:
        reconnect = bridge_call("agent_connect")
        if not reconnect.get("ok"):
            raise ConnectionError("载入后的游戏暂时未就绪。")
    bridge_session = session
    model_event_cursor = 0
    agent_event_cursor = 0
    current_task_reference = None
    state_initialized = False
    pending_agent_feedback = []
    return changed


def read_state(force_full: bool = False) -> dict[str, Any]:
    global current_task_reference, state_initialized
    response = bridge_call("agent_state", full=force_full or not state_initialized)
    if response.get("ok"):
        adopt_session(response)
        result = response.get("result")
        state = result.get("state", "") if isinstance(result, dict) else ""
        current_task_reference = next(
            (line[len("GUIDE_REFERENCE "):].strip() for line in str(state).splitlines()
             if line.startswith("GUIDE_REFERENCE ")),
            None,
        )
        state_initialized = True
    return response


def reference_files(category: str) -> dict[str, Path]:
    root = REFERENCE_ROOTS.get(category)
    if root is None:
        raise ValueError("category 必须是 preset、task、tool_chain 或 knowledge。")
    if not root.is_dir():
        raise RuntimeError("资料目录不存在：" + str(root))
    pattern = "room_*.txt" if category == "preset" else ("task_*.txt" if category == "task" else "*.txt")
    return {
        path.relative_to(root).as_posix(): path
        for path in sorted(root.rglob(pattern), key=lambda item: item.as_posix().lower())
        if path.is_file()
    }


def reference_index(category: str) -> dict[str, Any]:
    files = reference_files(category)
    if category == "task":
        stages: dict[str, int] = {}
        for name in files:
            stage = name.split("/", 1)[0]
            stages[stage] = stages.get(stage, 0) + 1
        return {
            "ok": True,
            "category": category,
            "rule": "只读取游戏状态 GUIDE_REFERENCE 指定的当前单个 TASK；不得读取未来 TASK。",
            "stages": [{"name": name, "count": count} for name, count in stages.items()],
        }
    entries = []
    for name, path in files.items():
        first_line = next((line.strip() for line in path.read_text(encoding="utf-8").splitlines() if line.strip()), "-")
        entries.append({"file": name, "summary": first_line[:240]})
    return {"ok": True, "category": category, "files": entries}


def reference_read(category: str, name: str) -> dict[str, Any]:
    if not isinstance(name, str) or not name:
        raise ValueError("file 不能为空。")
    normalized_name = name.replace("\\", "/")
    if category == "task":
        if current_task_reference is None:
            raise ValueError("读取 TASK 前必须先调用 game_state，取得当前 GUIDE_REFERENCE。")
        if normalized_name != current_task_reference:
            raise ValueError("只能读取当前 GUIDE_REFERENCE 指定的 TASK，未来任务尚未开放。")
    files = reference_files(category)
    path = files.get(normalized_name)
    if path is None:
        raise ValueError("文件不在该资料索引中；先调用 reference_index 获取准确文件名。")
    content = path.read_text(encoding="utf-8")
    return {"ok": True, "category": category, "file": name, "content": content}


def action_tools() -> list[dict[str, Any]]:
    result: list[dict[str, Any]] = []
    for item in TOOLS:
        public = public_tool(item)
        example = json.dumps(public["example"], ensure_ascii=False, separators=(",", ":"))
        result.append(
            {
                "name": public["name"],
                "description": (
                    public["description"]
                    + f" 权限项：{public['permission']}。示例参数：{example}"
                ),
                "inputSchema": public["input_schema"],
            }
        )
    return result


def cli_catalog(tool_name: str | None = None) -> dict[str, Any]:
    """Return the raw CLI vocabulary without publishing one tool per action."""
    selected = TOOLS
    if tool_name:
        query = tool_name.strip()
        selected = [item for item in TOOLS
                    if query == item["name"] or query in item["aliases"]]
        # Prefixes such as T, C and J are command families, not individual
        # tools. Accept them as a convenience when an agent asks for a group.
        if not selected and len(query) <= 2:
            selected = [item for item in TOOLS
                        if str(item["template"][0]).upper() == query.upper()
                        or str(item["permission"]).upper().startswith(query.upper() + ".")]
        if not selected:
            raise ValueError("未知 CLI 工具：" + tool_name)
    entries = []
    for item in selected:
        entries.append(
            {
                "name": item["name"],
                "aliases": item["aliases"],
                "category": item["category"],
                "description": item["description"],
                "permission": item["permission"],
                "template": " ".join(item["template"]),
                "arguments": item["arguments"],
                "required_together": item["required_together"],
                "example": item["example"],
            }
        )
    return {
        "ok": True,
        "catalog_version": CATALOG_VERSION,
        "count": len(entries),
        "tools": entries,
    }


def mcp_tools() -> list[dict[str, Any]]:
    bridge_tools = [
        {
            "name": "game_state",
            "description": "读取 RimWorld 当前殖民地状态。首次或读档后自动返回完整状态，之后默认返回变化；full=true 强制返回完整状态。",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "full": {"type": "boolean", "description": "是否强制返回完整状态。"}
                },
                "additionalProperties": False,
            },
        },
        {
            "name": "game_updates",
            "description": "读取游戏产生的新命令结果、拒绝、错误、日志和 Agent 触发事件。省略 after 时从本 MCP 会话的上次游标继续。",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "after": {"type": "integer", "minimum": 0, "description": "可选事件游标。"}
                },
                "additionalProperties": False,
            },
        },
        {
            "name": "observe",
            "description": "等待短暂时间后同时读取新事件和殖民地增量状态，用于持续协作而不重复传输全部状态。",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "wait_seconds": {
                        "type": "number",
                        "minimum": 0,
                        "maximum": 30,
                        "description": "等待秒数，默认 5，最大 30。",
                    }
                },
                "additionalProperties": False,
            },
        },
        {
            "name": "reference_index",
            "description": "列出按需资料。preset 返回 16 个独立建筑预设文件；tool_chain 和 knowledge 返回主题文件；task 只返回阶段计数，不泄露未来任务。",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "category": {"type": "string", "enum": ["preset", "task", "tool_chain", "knowledge"]}
                },
                "required": ["category"],
                "additionalProperties": False,
            },
        },
        {
            "name": "reference_read",
            "description": "按索引中的准确文件名读取一份资料。每次只读一个文件；TASK 只能读取当前状态 GUIDE_REFERENCE 指定的文件。",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "category": {"type": "string", "enum": ["preset", "task", "tool_chain", "knowledge"]},
                    "file": {"type": "string", "description": "reference_index 或 GUIDE_REFERENCE 给出的相对文件名。"},
                },
                "required": ["category", "file"],
                "additionalProperties": False,
            },
        },
        {
            "name": "cli_catalog",
            "description": "按需读取 CLI 指令目录。游戏操作不会以独立工具发布；先查目录，再把一条或多条完整 CLI 指令交给 cli_execute。",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "tool_name": {"type": "string", "description": "可选的 CLI 工具名或别名；省略则返回完整目录。"},
                },
                "additionalProperties": False,
            },
        },
        {
            "name": "cli_execute",
            "description": "执行一条或多条 RimWorld CLI 指令。每行一条；这是 agent 模式唯一的游戏操作入口。不要在普通文本中输出待执行指令。",
            "inputSchema": {
                "type": "object",
                "properties": {
                    "commands": {
                        "type": "string",
                        "description": "要执行的 CLI 文本，每行一条完整指令；可包含 PLAN、REQUEST_PLAYER、NOTE_ADD、NOTE_READ、NOTE_DONE、N 等控制指令。",
                    },
                },
                "required": ["commands"],
                "additionalProperties": False,
            },
        },
    ]
    return bridge_tools


def read_updates(arguments: dict[str, Any]) -> dict[str, Any]:
    global model_event_cursor
    after = arguments.get("after", model_event_cursor)
    if isinstance(after, bool) or not isinstance(after, int) or after < 0:
        raise ValueError("after 必须是大于等于 0 的整数。")
    response = bridge_call("results", after=after)
    if response.get("ok"):
        if adopt_session(response):
            response = bridge_call("results", after=0)
        result = response.get("result")
        if isinstance(result, dict) and isinstance(result.get("next"), int):
            model_event_cursor = result["next"]
    return response


def poll_agent_triggers(flush_feedback: bool = False) -> dict[str, Any]:
    global agent_event_cursor, manual_connection, pending_agent_feedback, consecutive_poll_failures
    try:
        response = bridge_call("results", after=agent_event_cursor)
        if not response.get("ok"):
            consecutive_poll_failures += 1
            if consecutive_poll_failures < 3:
                return {"connected": True, "transient": True, "events": []}
            manual_connection = False
            return {"connected": False, "response": response}
        consecutive_poll_failures = 0
        changed = adopt_session(response)
        if changed:
            response = bridge_call("results", after=0)
        result = response.get("result")
        if not isinstance(result, dict):
            manual_connection = False
            return {"connected": False, "error": "游戏返回的 results 格式无效。"}
        next_cursor = result.get("next")
        if isinstance(next_cursor, int):
            agent_event_cursor = next_cursor
        all_events = [event for event in result.get("events") or []
                      if isinstance(event, dict)]
        triggers = [event for event in all_events if event.get("type") == "agent_trigger"]
        # Command results, refusals and log feedback reach the next round;
        # `flush_feedback` also lets the plugin advance immediately after the
        # Agent has published its one plan for the current round.
        pending_agent_feedback.extend(
            event for event in all_events if forward_agent_feedback(event)
        )
        if len(pending_agent_feedback) > 128:
            pending_agent_feedback = pending_agent_feedback[-128:]
        events = []
        if triggers or flush_feedback:
            events = [*pending_agent_feedback, *triggers]
            pending_agent_feedback = []
        return {
            "connected": True,
            "session": result.get("session"),
            "sessionChanged": changed,
            "events": events,
            "decisionIntervalSeconds": result.get("decisionIntervalSeconds"),
        }
    except (OSError, ConnectionError, RuntimeError) as error:
        consecutive_poll_failures += 1
        if consecutive_poll_failures < 3:
            return {"connected": True, "transient": True, "events": [], "error": str(error)}
        manual_connection = False
        return {"connected": False, "error": str(error)}


def forward_agent_feedback(event: dict[str, Any]) -> bool:
    """Keep game feedback, but do not echo Agent-to-player requests or bridge transcripts."""
    event_type = str(event.get("type", ""))
    text = str(event.get("text", ""))
    if event_type == "agent_trigger":
        return False
    if event_type in {"work_message", "chat_message"}:
        return False
    if event_type == "command_result" and "request_player=1" in text:
        return False
    if event_type == "command_result" and " MAP_SCAN map=" in text:
        return False  # Already delivered as the read-only CLI result, not a new event.
    if event_type == "log":
        if text.startswith("[Agent ") or (text.startswith("[") and "Agent]" in text[:40]):
            return False
        ignored_markers = (
            "[发送给 Agent]", "[Agent 原始指令]", "[Agent 操作过程]", "[Agent 思考与输出]",
            "AI请求玩家", "AI 请求玩家",
        )
        if any(marker in text for marker in ignored_markers):
            return False
    return True


def connect_game() -> dict[str, Any]:
    """Perform the first named-pipe request only after an explicit user command."""
    global manual_connection, consecutive_poll_failures
    consecutive_poll_failures = 0
    response = bridge_call("agent_connect")
    if response.get("ok"):
        manual_connection = True
        adopt_session(response)
        return {"ok": True, "connected": True, "session": response_session(response)}
    manual_connection = False
    return {"ok": False, "connected": False, "error": response.get("error", "无法连接 RimWorld。")}


def disconnect_game() -> dict[str, Any]:
    global manual_connection, model_event_cursor, agent_event_cursor, bridge_session, state_initialized, pending_agent_feedback
    try:
        bridge_call("agent_disconnect")
    except (OSError, ConnectionError, RuntimeError):
        pass
    manual_connection = False
    model_event_cursor = 0
    agent_event_cursor = 0
    bridge_session = None
    state_initialized = False
    pending_agent_feedback = []
    return {"ok": True, "connected": False}


def call_tool(name: str, arguments: dict[str, Any]) -> dict[str, Any]:
    if not isinstance(arguments, dict):
        return error_result("工具参数必须是 JSON 对象。", "invalid_arguments")
    try:
        if name == "game_state":
            full = arguments.get("full", False)
            if not isinstance(full, bool):
                raise ValueError("full 必须是 boolean。")
            response = read_state(full)
            return text_result(response, bridge_failed(response))

        if name == "game_updates":
            response = read_updates(arguments)
            return text_result(response, bridge_failed(response))

        if name == "observe":
            wait_seconds = arguments.get("wait_seconds", 5)
            if isinstance(wait_seconds, bool) or not isinstance(wait_seconds, (int, float)):
                raise ValueError("wait_seconds 必须是数字。")
            if wait_seconds < 0 or wait_seconds > 30:
                raise ValueError("wait_seconds 必须在 0 到 30 之间。")
            if wait_seconds:
                time.sleep(wait_seconds)
            updates = read_updates({})
            state = read_state()
            payload = {"ok": updates.get("ok") and state.get("ok"), "updates": updates, "state": state}
            return text_result(payload, bridge_failed(updates) or bridge_failed(state))

        if name == "reference_index":
            return text_result(reference_index(arguments.get("category")))

        if name == "reference_read":
            return text_result(reference_read(arguments.get("category"), arguments.get("file")))

        if name == "cli_catalog":
            tool_name = arguments.get("tool_name")
            if tool_name is not None and not isinstance(tool_name, str):
                raise ValueError("tool_name 必须是字符串。")
            return text_result(cli_catalog(tool_name))

        if name == "cli_execute":
            commands = arguments.get("commands")
            if not isinstance(commands, str) or not commands.strip():
                raise ValueError("commands 必须是非空字符串。")
            if "\x00" in commands:
                raise ValueError("commands 不能包含 NUL 字符。")
            response = bridge_call("execute", commands=commands)
            payload = {"ok": response.get("ok"), "commands": commands, "bridge": response}
            return text_result(payload, bridge_failed(response))

        return error_result("未知 MCP 工具：" + name, "unknown_tool")
    except (OSError, ConnectionError) as error:
        return error_result(
            "无法连接 RimWorld。请启用 AI 协作队友 Mod 并进入存档，然后在 Harness 中手动连接游戏。详情：" + str(error),
            "game_unavailable",
        )
    except (ValueError, RuntimeError) as error:
        return error_result(str(error), "invalid_arguments")
    except Exception as error:  # Keep the MCP process alive and return the exact adapter failure.
        return error_result(type(error).__name__ + ": " + str(error))


def handle_request(request: dict[str, Any]) -> tuple[dict[str, Any] | None, bool]:
    request_id = request.get("id")
    method = request.get("method")
    params = request.get("params") or {}
    if method == "initialize":
        requested = params.get("protocolVersion") if isinstance(params, dict) else None
        return {
            "jsonrpc": "2.0",
            "id": request_id,
            "result": {
                "protocolVersion": requested if isinstance(requested, str) else DEFAULT_PROTOCOL_VERSION,
                "capabilities": {"tools": {"listChanged": False}},
                "serverInfo": {"name": SERVER_NAME, "version": SERVER_VERSION},
            },
        }, False
    if method in {"notifications/initialized", "notifications/cancelled"}:
        return None, False
    if method == "ping":
        return {"jsonrpc": "2.0", "id": request_id, "result": {}}, False
    if method == "tools/list":
        return {"jsonrpc": "2.0", "id": request_id, "result": {"tools": mcp_tools()}}, False
    if method == "tools/call":
        if not isinstance(params, dict) or not isinstance(params.get("name"), str):
            result = error_result("tools/call 缺少工具名称。", "invalid_request")
        else:
            result = call_tool(params["name"], params.get("arguments") or {})
        return {"jsonrpc": "2.0", "id": request_id, "result": result}, False
    if method == "plugin/poll":
        if not manual_connection:
            return {"jsonrpc": "2.0", "id": request_id,
                    "result": {"connected": False, "manual": True, "error": "尚未收到连接游戏指令。"}}, False
        flush_feedback = params.get("flush", False) if isinstance(params, dict) else False
        return {"jsonrpc": "2.0", "id": request_id,
                "result": poll_agent_triggers(bool(flush_feedback))}, False
    if method == "plugin/connect":
        try:
            return {"jsonrpc": "2.0", "id": request_id, "result": connect_game()}, False
        except (OSError, ConnectionError, RuntimeError) as error:
            return {"jsonrpc": "2.0", "id": request_id,
                    "result": {"ok": False, "connected": False, "error": str(error)}}, False
    if method == "plugin/disconnect":
        return {"jsonrpc": "2.0", "id": request_id, "result": disconnect_game()}, False
    if method == "plugin/state":
        try:
            force_full = params.get("full", False) if isinstance(params, dict) else False
            if not isinstance(force_full, bool):
                force_full = False
            response = read_state(force_full)
            if response.get("ok") and isinstance(response.get("result"), dict) and response["result"].get("mode") == "full":
                reference_lines = [
                    "HARNESS_REFERENCE_ROOTS",
                    "preset=" + str(REFERENCE_ROOTS["preset"]),
                    "task=" + str(REFERENCE_ROOTS["task"]),
                    "tool_chain=" + str(REFERENCE_ROOTS["tool_chain"]),
                    "knowledge=" + str(REFERENCE_ROOTS["knowledge"]),
                ]
                response["result"]["state"] = "\n".join(reference_lines) + "\n" + str(response["result"].get("state", ""))
            return {"jsonrpc": "2.0", "id": request_id, "result": response}, False
        except (OSError, ConnectionError, RuntimeError) as error:
            return {"jsonrpc": "2.0", "id": request_id, "result": {"ok": False, "error": str(error)}}, False
    if method == "plugin/record":
        try:
            kind = params.get("kind", "output") if isinstance(params, dict) else "output"
            text = params.get("text", "") if isinstance(params, dict) else ""
            result = bridge_call("agent_record", kind=str(kind), text=str(text))
        except (OSError, ConnectionError, RuntimeError) as error:
            result = {"ok": False, "error": str(error)}
        return {"jsonrpc": "2.0", "id": request_id, "result": result}, False
    if method == "shutdown":
        return {"jsonrpc": "2.0", "id": request_id, "result": {}}, True
    if method == "exit":
        return None, True
    if request_id is None:
        return None, False
    return {
        "jsonrpc": "2.0",
        "id": request_id,
        "error": {"code": -32601, "message": "未知 MCP 方法：" + str(method)},
    }, False


def run() -> int:
    validate_catalog()
    for raw_line in sys.stdin.buffer:
        if not raw_line.strip():
            continue
        try:
            request = json.loads(raw_line.decode("utf-8"))
            if not isinstance(request, dict):
                raise ValueError("请求必须是 JSON 对象。")
            response, should_stop = handle_request(request)
            if response is not None:
                write_message(response)
            if should_stop:
                return 0
        except Exception as error:
            write_message(
                {
                    "jsonrpc": "2.0",
                    "id": None,
                    "error": {"code": -32700, "message": type(error).__name__ + ": " + str(error)},
                }
            )
    return 0


if __name__ == "__main__":
    raise SystemExit(run())
