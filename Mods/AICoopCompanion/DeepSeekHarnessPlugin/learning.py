"""Player-taught lessons, never arbitrary file access or automatic game commands."""
import hashlib
import json
import os
from datetime import datetime
from pathlib import Path

LIMIT = 272 * 1024
SUMMARY_LIMIT = 200 * 1024


class LearningFile:
    def __init__(self, path: Path):
        self.path = path
        self.enabled = False

    def read(self):
        text = self.path.read_text(encoding="utf-8") if self.path.exists() else ""
        data = text.encode("utf-8")
        return {"ok": True, "text": text, "bytes": len(data),
                "sha256": hashlib.sha256(data).hexdigest(),
                "compression_required": len(data) > LIMIT}

    def write(self, text):
        self.path.parent.mkdir(parents=True, exist_ok=True)
        temporary = self.path.with_suffix(".tmp")
        temporary.write_text(text, encoding="utf-8")
        os.replace(temporary, self.path)

    def execute(self, command):
        if not self.enabled:
            raise ValueError("学习模式已关闭，未读写学习内容。")
        operation, _, argument = command.strip().partition(" ")
        current = self.read()
        if operation == "LEARN_READ":
            if argument:
                raise ValueError("LEARN_READ不接受参数。")
            return current
        if operation == "LEARN_ADD":
            lesson = json.loads(argument)
            if not isinstance(lesson, str) or not lesson.strip() or len(lesson.encode("utf-8")) > 16384:
                raise ValueError("每条学习内容须为非空JSON字符串，最多16KiB。")
            if current["compression_required"]:
                return {**current, "ok": False, "error": "先LEARN_COMPACT，再重试记录；没有丢弃已有教训。"}
            line = " ".join(lesson.split())
            if not any(item.partition("] ")[2] == line for item in current["text"].splitlines()):
                self.write(current["text"].rstrip() + f"\n[{datetime.now().isoformat(timespec='seconds')}] {line}\n")
        elif operation == "LEARN_COMPACT":
            summary = json.loads(argument)
            if not isinstance(summary, dict) or summary.get("sha256") != current["sha256"]:
                raise ValueError("学习内容已经变化，请重新LEARN_READ再压缩，未覆盖文件。")
            text = summary.get("text")
            if not current["compression_required"]:
                raise ValueError("尚未超过272KiB，无需压缩。")
            if not isinstance(text, str) or not text.strip() or len(text.encode("utf-8")) > SUMMARY_LIMIT:
                raise ValueError("压缩摘要须非空且不超过200KiB。")
            self.write(text.strip() + "\n")
        else:
            raise ValueError("仅支持LEARN_READ、LEARN_ADD、LEARN_COMPACT。")
        updated = self.read()
        return {k: v for k, v in updated.items() if k != "text"}
