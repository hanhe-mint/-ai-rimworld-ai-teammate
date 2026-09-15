#!/usr/bin/env python3
"""RimWorld AI Coop Companion named-pipe CLI bundled with the Harness plugin."""

from __future__ import annotations

import argparse
import json
import os
import sys
import time
from pathlib import Path
from typing import Any, BinaryIO

from aicoop_tools import build_command, catalog


PIPE_PATH = r"\\.\pipe\AICoopCompanion-v1"


class AgentBridgeClient:
    def __init__(self, pipe_path: str = PIPE_PATH) -> None:
        if os.name != "nt":
            raise RuntimeError("此 CLI 目前只支持 Windows 命名管道。")
        self.pipe_path = pipe_path
        self.pipe: BinaryIO | None = None
        self.buffer = bytearray()
        self.request_id = 0

    def __enter__(self) -> "AgentBridgeClient":
        # RimWorld recreates its single-instance pipe after each client closes
        # the stream. Retry the short gap so back-to-back MCP calls do not
        # report a false disconnect.
        last_error: OSError | None = None
        for _ in range(40):
            try:
                self.pipe = open(self.pipe_path, "r+b", buffering=0)
                return self
            except OSError as error:
                last_error = error
                if not isinstance(error, FileNotFoundError) and getattr(error, "winerror", None) not in (2, 231):
                    raise
                time.sleep(0.05)
        raise ConnectionError(f"无法打开 RimWorld 命名管道：{last_error}")

    def __exit__(self, exc_type: object, exc: object, traceback: object) -> None:
        if self.pipe is not None:
            self.pipe.close()
            self.pipe = None

    def call(self, method: str, **parameters: Any) -> dict[str, Any]:
        if self.pipe is None:
            raise RuntimeError("尚未连接命名管道。")
        self.request_id += 1
        request = {"id": str(self.request_id), "method": method, **parameters}
        payload = (json.dumps(request, ensure_ascii=False, separators=(",", ":")) + "\n").encode("utf-8")
        while payload:
            written = self.pipe.write(payload)
            payload = payload[written:]
        return json.loads(self._read_line().decode("utf-8"))

    def _read_line(self) -> bytes:
        if self.pipe is None:
            raise RuntimeError("尚未连接命名管道。")
        while True:
            newline = self.buffer.find(b"\n")
            if newline >= 0:
                line = bytes(self.buffer[:newline])
                del self.buffer[: newline + 1]
                return line
            chunk = self.pipe.read(65536)
            if not chunk:
                raise ConnectionError("游戏已断开命名管道。")
            self.buffer.extend(chunk)


def print_json(value: Any) -> None:
    print(json.dumps(value, ensure_ascii=False, indent=2), flush=True)


def load_commands(args: argparse.Namespace, parser: argparse.ArgumentParser) -> str:
    if args.file:
        return Path(args.file).read_text(encoding="utf-8")
    if args.commands:
        return args.commands
    if not sys.stdin.isatty():
        return sys.stdin.read()
    parser.error("execute 需要命令文本、--file 文件，或通过标准输入传入命令。")
    return ""


def load_tool_arguments(args: argparse.Namespace) -> dict[str, Any]:
    if args.args_file:
        value = json.loads(Path(args.args_file).read_text(encoding="utf-8"))
    elif args.arg:
        value = {}
        for entry in args.arg:
            if "=" not in entry:
                raise ValueError("--arg 格式必须是 名称=值。")
            name, raw_value = entry.split("=", 1)
            if not name or name in value:
                raise ValueError("--arg 参数名为空或重复：" + name)
            try:
                value[name] = json.loads(raw_value)
            except json.JSONDecodeError:
                value[name] = raw_value
    else:
        value = json.loads(args.args_json or "{}")
    if not isinstance(value, dict):
        raise ValueError("工具参数必须是 JSON 对象。")
    return value


def run() -> int:
    parser = argparse.ArgumentParser(description="通过独立 CLI 工具控制 RimWorld 的 AI 协作队友。")
    subparsers = parser.add_subparsers(dest="command", required=True)
    tools_parser = subparsers.add_parser("tools", help="列出全部可调用工具及 JSON Schema。")
    tools_parser.add_argument("--name", help="只显示一个工具或别名。")

    call = subparsers.add_parser("call", help="按工具名和 JSON 参数执行一项游戏操作。")
    call.add_argument("tool", help="tools 返回的工具名。")
    call_arguments = call.add_mutually_exclusive_group()
    call_arguments.add_argument("--args-json", help="JSON 对象参数。")
    call_arguments.add_argument("--args-file", help="读取 UTF-8 JSON 参数文件。")
    call_arguments.add_argument("--arg", action="append", help="可重复使用的 名称=值；数字和 true/false 自动按 JSON 类型解析。")

    subparsers.add_parser("ping", help="检查连接和协议版本。")
    subparsers.add_parser("status", help="读取当前殖民地完整文本状态。")

    execute = subparsers.add_parser("execute", help="提交现有白名单指令。")
    execute.add_argument("commands", nargs="?", help="命令文本；多条命令用换行分隔。")
    execute.add_argument("--file", help="读取 UTF-8 命令文件。")

    results = subparsers.add_parser("results", help="读取指定游标后的执行结果。")
    results.add_argument("--after", type=int, default=0, help="上次返回的 next 游标，默认 0。")

    watch = subparsers.add_parser("watch", help="先输出一次状态，再持续输出新的执行结果。")
    watch.add_argument("--interval", type=float, default=1.0, help="轮询间隔秒数，默认 1。")

    args = parser.parse_args()
    try:
        if args.command == "tools":
            print_json(catalog(args.name))
            return 0

        prepared_tool: str | None = None
        prepared_command: str | None = None
        if args.command == "call":
            prepared_tool = args.tool
            prepared_command = build_command(args.tool, load_tool_arguments(args))

        with AgentBridgeClient() as client:
            if args.command == "call":
                response = client.call("execute", commands=prepared_command)
                response["tool"] = prepared_tool
                response["command"] = prepared_command
                print_json(response)
                return 0 if response.get("ok") else 2
            if args.command == "ping":
                response = client.call("ping")
                print_json(response)
                return 0 if response.get("ok") else 2
            if args.command == "status":
                response = client.call("status")
                print_json(response)
                return 0 if response.get("ok") else 2
            if args.command == "execute":
                response = client.call("execute", commands=load_commands(args, parser))
                print_json(response)
                return 0 if response.get("ok") else 2
            if args.command == "results":
                response = client.call("results", after=args.after)
                print_json(response)
                return 0 if response.get("ok") else 2
            if args.command == "watch":
                status = client.call("status")
                print_json({"type": "status", "response": status})
                if not status.get("ok"):
                    return 2
                cursor = 0
                while True:
                    response = client.call("results", after=cursor)
                    if not response.get("ok"):
                        print_json({"type": "error", "response": response})
                        return 2
                    result = response.get("result", {})
                    cursor = int(result.get("next", cursor))
                    for event in result.get("events", []):
                        print_json(event)
                    time.sleep(max(0.1, args.interval))
    except KeyboardInterrupt:
        return 130
    except (OSError, ValueError, RuntimeError, ConnectionError) as error:
        print(json.dumps({"ok": False, "error": str(error)}, ensure_ascii=False), file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(run())
