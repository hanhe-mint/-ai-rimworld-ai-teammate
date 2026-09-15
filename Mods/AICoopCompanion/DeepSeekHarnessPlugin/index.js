import { spawn } from "node:child_process";
import { createHash, randomUUID } from "node:crypto";
import { readFile } from "node:fs/promises";
import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";

export const name = "rimworld-ai-coop";
export const inject = ["systemPrompt", "tools", "agents", "commands"];

const PLUGIN_DIR = dirname(fileURLToPath(import.meta.url));
const PROTOCOL_SECTION = "deployment:persona";
const PROTOCOL_ORDER = 0;
const MAX_TOOL_NAME_LENGTH = 64;
const DEFAULT_POLL_INTERVAL_MS = 750;

function publicToolName(serverName, rawName) {
  const joined = `mcp__${serverName}__${rawName}`;
  const normalized = joined.replace(/[^A-Za-z0-9_-]/g, "_");
  if (normalized === joined && normalized.length <= MAX_TOOL_NAME_LENGTH) return normalized;
  const hash = createHash("sha256").update(`${serverName}\0${rawName}`).digest("hex").slice(0, 12);
  return `${normalized.slice(0, MAX_TOOL_NAME_LENGTH - 13)}_${hash}`;
}

function resultText(content) {
  if (!Array.isArray(content)) return "(无工具输出)";
  return content.map((item) => item?.type === "text" ? String(item.text ?? "") : `[${item?.type ?? "unknown"}]`).join("\n");
}

function messageText(content) {
  if (typeof content === "string") return content;
  if (!Array.isArray(content)) return "";
  return content.map((block) => {
    if (block?.type === "text" && !["analysis", "reasoning"].includes(block.channel)) return String(block.text ?? "");
    return "";
  }).filter(Boolean).join("\n");
}

function pluginMessage(text) {
  return Object.freeze({
    id: randomUUID(),
    role: "user",
    content: Object.freeze([Object.freeze({ type: "text", text })]),
    source: Object.freeze({ kind: "plugin", plugin: name }),
  });
}

function activeAgentFor(ctx, targetSession) {
  if (!targetSession) return null;
  try {
    const registry = ctx?.agents;
    const exact = registry?.get?.(targetSession);
    if (exact && sessionIdOf(exact.session) === targetSession) return exact;
    const agents = registry?.list?.() ?? [];
    return agents.find(agent => sessionIdOf(agent.session) === targetSession) ?? null;
  } catch {
    return null;
  }
}
function cancelAgent(agent) {
  if (!agent || typeof agent.cancel !== "function") return;
  try {
    agent.cancel({ kind: "user" });
  } catch {
  }
}

function sendAgent(agent, message) {
  if (!agent) return false;
  try {
    if (agent.status === "idle" && typeof agent.followup === "function") agent.followup(message);
    else if (typeof agent.steer === "function") agent.steer(message);
    else if (typeof agent.inject === "function") agent.inject(message);
    else return false;
    return true;
  } catch {
    return false;
  }
}

function sessionIdOf(session) {
  if (typeof session === "string") return session;
  return session?.id ?? session?.header?.id ?? session?.header?.sessionId ?? null;
}

function userMessageText(event) {
  if (event?.type !== "user/message") return "";
  if (event?.data?.source?.kind !== "user") return "";
  const content = event.data?.content;
  if (typeof content === "string") return content;
  return messageText(Array.isArray(content) ? content : []);
}

function connectionCommand(text) {
  const normalized = String(text ?? "").trim().toLocaleLowerCase();
  if (!normalized) return null;
  if (/^(?:stop|停止|停止协作|停止发送|停下|暂停协作)$/i.test(normalized)) return "stop";
  if (/(?:\u65ad\u5f00|\u65b7\u958b|disconnect|offline|\u9000\u51fa\u8fde\u63a5|\u9000\u51fa\u9023\u63a5)/i.test(normalized)
    && /(?:\u6e38\u620f|\u904a\u6232|rimworld|rw|\u8fde\u63a5|\u9023\u63a5|connect)/i.test(normalized)) return "disconnect";
  if (/(?:\u8fde\u63a5|\u9023\u63a5|connect).*?(?:\u6e38\u620f|\u904a\u6232|rimworld|rw)|(?:\u6e38\u620f|\u904a\u6232|rimworld|rw).*?(?:\u8fde\u63a5|\u9023\u63a5|connect)/i.test(normalized)) return "connect";
  return null;
}


function childEnvironment() {
  const output = {};
  const secretPattern = /(?:^DSH_|TOKEN|SECRET|PASSWORD|PASSWD|API[_-]?KEY|AUTHORIZATION|CREDENTIAL)/i;
  for (const [key, value] of Object.entries(process.env)) {
    if (value !== undefined && !secretPattern.test(key)) output[key] = value;
  }
  output.PYTHONUTF8 = "1";
  output.PYTHONIOENCODING = "utf-8";
  return output;
}

class McpProcess {
  constructor(command, serverPath, timeoutMs) {
    this.timeoutMs = timeoutMs;
    this.nextId = 0;
    this.pending = new Map();
    this.buffer = "";
    this.stderr = "";
    this.closed = false;
    this.child = spawn(command, ["-X", "utf8", "-B", serverPath], {
      cwd: dirname(serverPath),
      env: childEnvironment(),
      stdio: ["pipe", "pipe", "pipe"],
      windowsHide: true,
    });
    this.child.stdout.setEncoding("utf8");
    this.child.stderr.setEncoding("utf8");
    this.child.stdout.on("data", (chunk) => this.onData(chunk));
    this.child.stderr.on("data", (chunk) => { this.stderr = (this.stderr + chunk).slice(-4096); });
    this.child.on("error", (error) => this.failAll(error));
    this.child.on("exit", (code, signal) => {
      this.closed = true;
      const detail = this.stderr.trim();
      this.failAll(new Error(`RimWorld MCP 已退出（code=${code}, signal=${signal ?? "-"}）${detail ? `：${detail}` : ""}`));
    });
  }

  onData(chunk) {
    this.buffer += chunk;
    while (true) {
      const newline = this.buffer.indexOf("\n");
      if (newline < 0) return;
      const line = this.buffer.slice(0, newline).trim();
      this.buffer = this.buffer.slice(newline + 1);
      if (!line) continue;
      let message;
      try {
        message = JSON.parse(line);
      } catch (error) {
        this.failAll(new Error(`RimWorld MCP 返回了无效 JSON：${error.message}`));
        continue;
      }
      const pending = this.pending.get(message.id);
      if (!pending) continue;
      this.pending.delete(message.id);
      clearTimeout(pending.timer);
      if (message.error) pending.reject(new Error(String(message.error.message ?? "MCP 请求失败")));
      else pending.resolve(message.result);
    }
  }

  failAll(error) {
    for (const pending of this.pending.values()) {
      clearTimeout(pending.timer);
      pending.reject(error);
    }
    this.pending.clear();
  }

  request(method, params = {}, timeoutMs = this.timeoutMs) {
    if (this.closed) return Promise.reject(new Error("RimWorld MCP 已关闭。"));
    const id = ++this.nextId;
    return new Promise((resolveRequest, rejectRequest) => {
      const timer = setTimeout(() => {
        this.pending.delete(id);
        rejectRequest(new Error(`RimWorld MCP 请求超时：${method}`));
      }, timeoutMs);
      this.pending.set(id, { resolve: resolveRequest, reject: rejectRequest, timer });
      this.child.stdin.write(JSON.stringify({ jsonrpc: "2.0", id, method, params }) + "\n", "utf8", (error) => {
        if (!error) return;
        const pending = this.pending.get(id);
        if (!pending) return;
        this.pending.delete(id);
        clearTimeout(timer);
        rejectRequest(error);
      });
    });
  }

  notify(method, params = {}) {
    if (!this.closed) this.child.stdin.write(JSON.stringify({ jsonrpc: "2.0", method, params }) + "\n", "utf8");
  }

  async start() {
    await this.request("initialize", {
      protocolVersion: "2025-06-18",
      capabilities: {},
      clientInfo: { name: "dsh-plugin-rimworld-ai-coop", version: "0.3.0" },
    }, 10000);
    this.notify("notifications/initialized");
    const listed = await this.request("tools/list", {}, 10000);
    if (!Array.isArray(listed?.tools)) throw new Error("RimWorld MCP 没有返回工具目录。");
    return listed.tools;
  }

  async close() {
    if (this.closed) return;
    try {
      await this.request("shutdown", {}, 1000);
      this.notify("exit");
    } catch {
    }
    this.child.stdin.end();
    if (!this.closed) this.child.kill();
  }
}

function outputDefinition(rawName) {
  return {
    schema: {
      type: "object",
      properties: {
        content: { type: "array", items: {} },
        structuredContent: {},
      },
      required: ["content"],
      additionalProperties: false,
    },
    render(_args, value) {
      return [{ type: "text", text: resultText(value.content) || `${rawName} 已完成` }];
    },
  };
}

export async function apply(ctx, config = {}) {
  if (config.enabled === false) return;
  const promptPath = resolve(config.promptPath || resolve(PLUGIN_DIR, "rimworld_harness_prompt.txt"));
  const serverPath = resolve(config.mcpServerPath || resolve(PLUGIN_DIR, "aicoop_mcp.py"));
  const pythonCommand = config.pythonCommand || "python";
  const serverName = config.serverName || "rimworld";
  const timeoutMs = Number.isFinite(config.toolCallTimeoutMs) && config.toolCallTimeoutMs > 0
    ? config.toolCallTimeoutMs
    : 60000;
  const prompt = await readFile(promptPath, "utf8");
  const connectionPreamble = [
    "EXTERNAL AGENT CONNECTION (manual): do not call any RimWorld game tool until the user explicitly connects the game.",
    "The user may click /rimworld-connect in the Harness command menu, or send connect to RimWorld / \u8fde\u63a5\u6e38\u620f.",
    "After a disconnect or stop notice, stop game actions and wait for another explicit connect command. A new connection starts a fresh round; use the state included in the next RimWorld event. The user can send stop or 停止, or use /rimworld-stop, to stop automatic rounds.",
  ].join("\n");
  // The MCP process may be started with the plugin, but game traffic is gated
  // until the user explicitly sends a connection command in Harness.
  const bridgeControl = {
    requested: false,
    connected: false,
    connecting: false,
    disconnectNoticeSent: false,
    lastNotice: "disconnected",
    baselineRequired: true,
    turn: {
      phase: "awaiting_game_input",
      number: 0,
      planSent: false,
    },
  };

  const mcp = new McpProcess(pythonCommand, serverPath, timeoutMs);
  ctx.effect(() => () => mcp.close(), "rimworld MCP process");
  // The game state and reference tools remain available inside the MCP
  // process for bridge internals, but are intentionally not published to the
  // Harness agent.  Every round receives its state from poll() exactly once.
  const allowedAgentTools = new Set(["cli_execute"]);
  const tools = (await mcp.start()).filter((tool) => allowedAgentTools.has(tool?.name));
  ctx.effect(() => {
    const runtimeProtocol = [
      "外接 Agent 不由本插件限制模型输出 token；输出内容不要求固定前缀或口吻。",
      "严格单步：每条 [RimWorld 自动协作事件] 或 [RimWorld 玩家输入事件] 可以连续调用一次或多次 mcp__rimworld__cli_execute；所有动作完成后只能输出一次最终回答。",
      "游戏状态、变化、错误和当前任务已经在事件文本中，不得调用 game_state、game_updates、observe、reference_index 或 reference_read；这些工具不会提供给你。",
      "本轮必要操作完成后给出最终回答并结束回合，等待下一条游戏输入，不自行追加目标或读取状态。",
    ].join("\n");
    const disposeSection = ctx.systemPrompt.section({
      name: PROTOCOL_SECTION,
      order: PROTOCOL_ORDER,
      text: connectionPreamble + "\n\n" + prompt + "\n\n" + runtimeProtocol,
      complete: true,
    });
    const disposeRuntimeContext = ctx.systemPrompt.suppressRuntimeContext();
    return () => {
      disposeRuntimeContext();
      disposeSection();
    };
  }, "rimworld protocol");

  ctx.effect(() => {
    const disposers = tools.map((tool) => {
      const publicName = publicToolName(serverName, tool.name);
      return ctx.tools.register({
        name: publicName,
        description: tool.description || "",
        parameters: tool.inputSchema,
        output: outputDefinition(tool.name),
        async execute(args) {
          if (bridgeControl.requested === false) {
            throw new Error("RimWorld 尚未连接。请先在 Harness 中发送“连接游戏”后再调用游戏工具。");
          }
          if (tool.name === "cli_execute") {
            const turn = bridgeControl.turn;
            if (turn.phase !== "awaiting_cli" || turn.planSent) {
              const blocked = {
                content: [{ type: "text", text: JSON.stringify({
                  ok: false,
                  error: {
                    code: "round_gate",
                    message: "本轮已结束或尚未收到输入，请等待下一轮游戏事件。",
                  },
                }) }],
                structuredContent: { ok: false, error: { code: "round_gate" } },
              };
              return blocked;
            }
          }
          let result;
          try {
            result = await mcp.request("tools/call", { name: tool.name, arguments: args || {} });
          } catch (error) {
            if (tool.name !== "cli_execute") throw error;
            return {
              content: [{ type: "text", text: JSON.stringify({
                ok: false,
                error: {
                  code: "cli_transport",
                  message: `${error instanceof Error ? error.message : String(error)}；本轮 CLI 调用失败，请说明情况并结束回合，等待下一条游戏输入。`,
                },
              }) }],
              structuredContent: { ok: false, error: { code: "cli_transport" } },
            };
          }
          if (tool.name === "cli_execute" && Array.isArray(result?.content)) {
            result.content = [
              ...result.content,
              { type: "text", text: "本次 CLI 已返回。需要时可在本轮继续调用 cli_execute；不要继续扩展新的目标；完成必要操作后给出最终回答并结束回合，并等待下一条 RimWorld 游戏事件。" },
            ];
          }
          if (result?.isError === true) {
            return {
              content: result.content,
              structuredContent: result.structuredContent,
            };
          }
          if (!Array.isArray(result?.content)) throw new Error(`RimWorld 工具 ${tool.name} 返回格式无效。`);
          return {
            content: result.content,
            ...(result.structuredContent !== undefined ? { structuredContent: result.structuredContent } : {}),
          };
        },
      });
    });
    return () => {
      for (const dispose of disposers) dispose();
    };
  }, "rimworld tools");

  ctx.effect(() => {
    let disposed = false;
    let polling = false;
    let wasConnected = false;
    let activeSession = null;
    let activeAgent = null;
    const handledConnectionMessages = new Set();
    let recordQueue = Promise.resolve();
    let manualRoundSerial = 0;
    let nextContextAt = 0;
    let decisionIntervalMs = 30000;
    let pendingGameEvents = [];

    const sendGameMessage = (agent, text) => {
      if (!sendAgent(agent, pluginMessage(text))) return false;
      record("input", text);
      return true;
    };
    const pollIntervalMs = Number.isFinite(config.pollIntervalMs) && config.pollIntervalMs >= 250
      ? config.pollIntervalMs
      : DEFAULT_POLL_INTERVAL_MS;

    const record = (kind, text) => {
      if (!text || disposed || !bridgeControl.requested) return;
      recordQueue = recordQueue
        .then(() => mcp.request("plugin/record", { kind, text }, 10000))
        .catch(() => undefined);
    };

    const rememberConnectionMessage = (event) => {
      const key = event?.data?.id ?? event?.seq;
      if (key === undefined || key === null) return false;
      const value = String(key);
      if (handledConnectionMessages.has(value)) return true;
      handledConnectionMessages.add(value);
      if (handledConnectionMessages.size > 128) {
        const first = handledConnectionMessages.values().next().value;
        handledConnectionMessages.delete(first);
      }
      return false;
    };

    const statusMessage = (text, agent = activeAgent, notifyAgent = true) => {
      if (disposed || !agent) return;
      if (text === "disconnect") text = "RimWorld 已断开连接。发送“连接游戏”可以重新连接。";
      record("error", text);
      // A disconnect can cancel a running turn. Queue the notice after the
      // cancellation so it is retained by Harness and visible to the user.
      if (notifyAgent) {
        setTimeout(() => {
          if (!disposed) sendAgent(agent, pluginMessage(text));
        }, 0);
      }
    };

    const updateDecisionInterval = (stateResponse) => {
      const seconds = Number(stateResponse?.result?.decisionIntervalSeconds);
      if (Number.isFinite(seconds) && seconds >= 0) {
        const updatedInterval = Math.min(120000, Math.max(0, seconds * 1000));
        if (nextContextAt > 0) nextContextAt += updatedInterval - decisionIntervalMs;
        decisionIntervalMs = updatedInterval;
      }
    };

    const sendPlayerRound = async (agent, playerText, serial) => {
      try {
        const includeBaseline = bridgeControl.baselineRequired;
        const stateResponse = await mcp.request("plugin/state", { full: includeBaseline }, 30000);
        if (serial !== manualRoundSerial || !bridgeControl.requested) return;
        if (!stateResponse?.ok || typeof stateResponse.result?.state !== "string") {
          bridgeControl.turn.phase = "awaiting_game_input";
          record("error", "无法为玩家输入读取当前 RimWorld 状态；本轮已结束，等待下一条游戏输入。");
          return;
        }
        updateDecisionInterval(stateResponse);
        nextContextAt = 0;
        const text = [
          "[RimWorld 玩家输入事件]",
          "玩家请求：",
          playerText,
          `游戏时间刻：${stateResponse.result.tick ?? "-"}`,
          includeBaseline ? "固定基线与完整当前状态（已附带，不要再次读取）：" : "本轮关键变化（普通地形、布局和生长变化已省略）：",
          stateResponse.result.state,
          `第 ${bridgeControl.turn.number} 轮：可以连续调用多次 cli_execute；不要继续扩展新的目标；完成必要操作后给出最终回答并结束回合，等待下一条 RimWorld 游戏事件。`,
        ].join("\n");
        bridgeControl.turn.phase = "awaiting_cli";
        if (!sendGameMessage(agent, text)) {
          bridgeControl.turn.phase = "awaiting_game_input";
          record("error", "找不到可用的 Harness Agent 会话；玩家输入已结束，等待下一条游戏输入。");
        } else if (includeBaseline) bridgeControl.baselineRequired = false;
      } catch (error) {
        if (serial !== manualRoundSerial) return;
        bridgeControl.turn.phase = "awaiting_game_input";
        record("error", `玩家输入状态读取失败：${error instanceof Error ? error.message : String(error)}`);
      }
    };

    const onSessionEvent = (session, event) => {
      if (disposed) return;
      const sessionId = sessionIdOf(session);
      if (event?.type === "compaction/summary" && bridgeControl.requested && activeAgent
        && sessionIdOf(activeAgent.session) === sessionId) bridgeControl.baselineRequired = true;
      const commandText = userMessageText(event);
      const command = connectionCommand(commandText);
      if (command && !rememberConnectionMessage(event)) {
        const candidate = activeAgentFor(ctx, sessionId);
        if (!candidate) return;
        if (bridgeControl.requested && activeAgent && sessionIdOf(activeAgent.session) !== sessionId) return;
        if (command === "connect" && bridgeControl.connecting) return;
        ++manualRoundSerial;
        if (command === "connect") {
          activeAgent = candidate;
          pendingGameEvents = [{ type: "manual_connect", text: "玩家在此会话连接游戏。" }];
          nextContextAt = 0;
          bridgeControl.turn.phase = "awaiting_game_input";
          bridgeControl.turn.planSent = false;
          bridgeControl.requested = true;
          bridgeControl.connected = false;
          bridgeControl.connecting = false;
          bridgeControl.disconnectNoticeSent = false;
          bridgeControl.lastNotice = "error";
          bridgeControl.baselineRequired = true;
          void connectNow(candidate);
        } else if (command === "stop") {
          activeAgent = candidate;
          nextContextAt = 0;
          record("error", "RimWorld 自动协作已停止。发送“连接游戏”或使用 /rimworld-connect 重新开始。\n");
          bridgeControl.turn.phase = "awaiting_game_input";
          bridgeControl.turn.planSent = false;
          bridgeControl.requested = false;
          bridgeControl.connected = false;
          bridgeControl.connecting = false;
          bridgeControl.disconnectNoticeSent = false;
          bridgeControl.lastNotice = "stopped";
          bridgeControl.baselineRequired = true;
          void mcp.request("plugin/disconnect", {}, 5000).catch(() => undefined);
          cancelAgent(candidate);
          statusMessage("RimWorld 自动协作已停止。发送“连接游戏”或使用 /rimworld-connect 重新开始。", candidate, false);
          return;
        } else {
          nextContextAt = 0;
          bridgeControl.turn.phase = "awaiting_game_input";
          bridgeControl.turn.planSent = false;
          bridgeControl.requested = false;
          bridgeControl.connected = false;
          bridgeControl.connecting = false;
          bridgeControl.disconnectNoticeSent = false;
          bridgeControl.lastNotice = "disconnected";
          bridgeControl.baselineRequired = true;
          void mcp.request("plugin/disconnect", {}, 5000).catch(() => undefined);
          cancelAgent(candidate);
          statusMessage("RimWorld 已断开连接。发送“连接游戏”可以重新连接。", candidate);
        }
      }

      if (!bridgeControl.requested || !activeAgent || sessionIdOf(activeAgent.session) !== sessionId) return;
      if (event?.type === "user/message" && command === null && commandText.trim()
        && bridgeControl.turn.phase === "awaiting_game_input") {
        bridgeControl.turn.number += 1;
        bridgeControl.turn.planSent = false;
        bridgeControl.turn.phase = "preparing_player_input";
        const serial = ++manualRoundSerial;
        void sendPlayerRound(activeAgent, commandText.trim(), serial);
        return;
      }
      if (event?.type === "user/message" && command === null && commandText.trim()) {
        record("error", "当前回合仍在执行或等待回复；玩家输入将在下一条游戏事件后处理。");
        return;
      }
      if (event?.type === "assistant/message") {
        const content = event.data?.message?.content;
        const text = messageText(content)
          .replace(/<(think|analysis|reasoning)\b[^>]*>[\s\S]*?(?:<\/\1\s*>|$)/gi, "").trim();
        if (text) record("output", text);
      } else if (event?.type === "turn/end" && event.data?.reason?.kind === "completed"
        && bridgeControl.turn.phase === "awaiting_cli" && !bridgeControl.turn.planSent) {
        bridgeControl.turn.planSent = true;
        bridgeControl.turn.phase = "awaiting_game_input";
        nextContextAt = Date.now() + decisionIntervalMs;
        record("round_end", "本轮已完成。");
      } else if (event?.type === "tool/call") {
        const args = event.data?.arguments;
        record("tool", `${event.data?.name ?? "tool"} ${typeof args === "string" ? args : JSON.stringify(args ?? {})}`.trim());
      }
    };
    const disposeSessionListener = ctx.on("session/event", onSessionEvent);

    const connectNow = async (agent) => {
      if (disposed || !agent || activeAgent !== agent || !bridgeControl.requested || bridgeControl.connecting) return;
      const connectionSerial = manualRoundSerial;
      bridgeControl.connecting = true;
      try {
        const connection = await mcp.request("plugin/connect", {}, 35000);
        if (connectionSerial !== manualRoundSerial || !bridgeControl.requested || activeAgent !== agent) return;
        if (!connection?.ok || !connection.connected) {
          const error = connection?.error;
          throw new Error(typeof error === "string" ? error : JSON.stringify(error ?? connection));
        }
        bridgeControl.connected = true;
        bridgeControl.connecting = false;
        await poll();
        if (bridgeControl.connected) {
          bridgeControl.lastNotice = "connected";
          statusMessage("RimWorld 已连接。游戏事件会自动提供状态；每轮可连续调用多次 cli_execute，输出一次最终回答后自动进入下一轮。", agent, false);
        } else {
          bridgeControl.requested = false;
          bridgeControl.lastNotice = "error";
          statusMessage("无法连接 RimWorld。请确认已启用 AI 协作队友 Mod 并进入存档，然后再次发送“连接游戏”。", agent);
        }
      } catch (error) {
        if (connectionSerial !== manualRoundSerial || activeAgent !== agent) return;
        bridgeControl.requested = false;
        bridgeControl.connected = false;
        bridgeControl.lastNotice = "error";
        const detail = error instanceof Error ? error.message : String(error);
        statusMessage(`无法连接 RimWorld：${detail}。请再次发送“连接游戏”重试。`, agent);
      } finally {
        if (connectionSerial === manualRoundSerial) bridgeControl.connecting = false;
      }
    };

    const poll = async () => {
      if (disposed || polling || bridgeControl.connecting || !bridgeControl.requested) return;
      const roundAgent = activeAgent;
      const roundSerial = manualRoundSerial;
      if (!roundAgent) return;
      polling = true;
      try {
        const update = await mcp.request("plugin/poll", {
          flush: bridgeControl.turn.phase === "awaiting_game_input" && Date.now() >= nextContextAt,
        }, 10000);
        if (roundSerial !== manualRoundSerial || !bridgeControl.requested || activeAgent !== roundAgent) return;
        if (!update?.connected) {
          if (wasConnected && !bridgeControl.disconnectNoticeSent) {
            bridgeControl.disconnectNoticeSent = true;
            bridgeControl.requested = false;
            bridgeControl.connected = false;
            bridgeControl.lastNotice = "disconnected";
            cancelAgent(activeAgent);
            statusMessage("RimWorld 已断开连接。发送“连接游戏”可以重新连接。", activeAgent);
          }
          wasConnected = false;
          bridgeControl.connected = false;
          activeSession = null;
          return;
        }

        const sessionChanged = activeSession !== null && update.session !== activeSession;
        if (sessionChanged || update.sessionChanged) {
          pendingGameEvents = [];
          nextContextAt = 0;
          cancelAgent(activeAgent);
          bridgeControl.turn.phase = "awaiting_game_input";
          bridgeControl.turn.planSent = false;
          bridgeControl.baselineRequired = true;
        }
        wasConnected = true;
        bridgeControl.connected = true;
        bridgeControl.disconnectNoticeSent = false;
        activeSession = update.session ?? activeSession;
        updateDecisionInterval({ result: update });

        for (const event of (Array.isArray(update.events) ? update.events : [])) {
          if (event.type === "agent_trigger" && String(event.text).startsWith("reason=scheduled_decision "))
            pendingGameEvents = pendingGameEvents.filter(item => !String(item.text).startsWith("reason=scheduled_decision "));
          pendingGameEvents.push(event);
        }
        const playerEvents = pendingGameEvents.filter(event => event.type === "agent_trigger"
          && String(event.text).startsWith("reason=player_message details="));
        if (playerEvents.length) {
          const playerText = playerEvents.map(event => String(event.text).split(" details=").slice(1).join(" details=")).join("\n");
          if (playerEvents.some(event => connectionCommand(String(event.text).split("玩家说：").slice(1).join("玩家说：")) === "stop")) {
            record("input", playerText);
            await recordQueue;
            pendingGameEvents = [];
            bridgeControl.requested = false;
            bridgeControl.connected = false;
            bridgeControl.turn.phase = "awaiting_game_input";
            bridgeControl.turn.planSent = false;
            bridgeControl.baselineRequired = true;
            cancelAgent(activeAgent);
            await mcp.request("plugin/disconnect", {}, 5000);
            return;
          }
          // Player input steers a running turn or starts an idle one immediately.
          // Do not fetch another state snapshot or wait for the decision timer.
          if (sendGameMessage(activeAgent, playerText)) {
            pendingGameEvents = pendingGameEvents.filter(event => !playerEvents.includes(event));
            bridgeControl.turn.number += 1;
            bridgeControl.turn.planSent = false;
            bridgeControl.turn.phase = "awaiting_cli";
            nextContextAt = 0;
          }
          return;
        }
        if (bridgeControl.turn.phase !== "awaiting_game_input" || Date.now() < nextContextAt) return;
        const events = pendingGameEvents.length ? pendingGameEvents :
          (bridgeControl.turn.planSent ? [{ type: "round_completed", text: "上一轮交流完成，决策间隔已到。" }] : []);
        if (events.length === 0) return;
        const includeBaseline = bridgeControl.baselineRequired;
        const stateResponse = await mcp.request("plugin/state", { full: includeBaseline }, 30000);
        if (roundSerial !== manualRoundSerial || !bridgeControl.requested || activeAgent !== roundAgent) return;
        if (!stateResponse?.ok || typeof stateResponse.result?.state !== "string") return;
        updateDecisionInterval(stateResponse);
        nextContextAt = 0;

        const reasons = events.map((event) => {
          const type = String(event?.type ?? "game_event");
          const detail = String(event?.text ?? "unknown");
          return `[${type}] ${detail}`;
        }).join("\n") || "上一轮计划已经输出；这是游戏执行结果后的自动续轮。";
        bridgeControl.turn.number += 1;
        bridgeControl.turn.planSent = false;
        bridgeControl.turn.phase = "awaiting_cli";
        const text = [
          "[RimWorld 自动协作事件]",
          "游戏触发原因：",
          reasons,
          `游戏时间刻：${stateResponse.result.tick ?? "-"}`,
          includeBaseline ? "固定基线与完整当前状态（已附带，不要再次读取）：" : "本轮关键变化（普通地形、布局和生长变化已省略）：",
          stateResponse.result.state,
          `第 ${bridgeControl.turn.number} 轮：可以连续调用多次 cli_execute；不要继续扩展新的目标；完成必要操作后给出最终回答并结束回合，等待下一条 RimWorld 游戏事件。`,
        ].join("\n");
        if (!sendGameMessage(activeAgent, text)) {
          bridgeControl.turn.phase = "awaiting_game_input";
          record("error", "无法找到可用的 Harness Agent 会话，游戏事件已保留，等待会话就绪后重试。");
        } else {
          pendingGameEvents = [];
          if (includeBaseline) bridgeControl.baselineRequired = false;
        }
      } catch (error) {
        if (roundSerial !== manualRoundSerial || activeAgent !== roundAgent) return;
        const detail = error instanceof Error ? `${error.name}: ${error.message}` : String(error);
        record("error", `RimWorld Agent 桥接异常：${detail}`);
        if (wasConnected && !bridgeControl.disconnectNoticeSent) {
          nextContextAt = 0;
          bridgeControl.disconnectNoticeSent = true;
          bridgeControl.requested = false;
          bridgeControl.connected = false;
          bridgeControl.turn.phase = "awaiting_game_input";
          bridgeControl.turn.planSent = false;
          bridgeControl.lastNotice = "disconnected";
          cancelAgent(activeAgent);
          statusMessage("RimWorld 已断开连接。发送“连接游戏”可以重新连接。", activeAgent);
        }
        wasConnected = false;
        bridgeControl.connected = false;
        activeSession = null;
      } finally {
        polling = false;
      }
    };

    const commandDisposers = [];
    if (ctx.commands && typeof ctx.commands.register === "function") {
      commandDisposers.push(ctx.commands.register({
        name: "rimworld-connect",
        description: "Connect the current RimWorld save to this Harness agent.",
        handler: async ({ agent }) => {
          if (!sessionIdOf(agent?.session)) return { kind: "error", text: "无法识别发起连接的会话，未发送游戏上下文。" };
          if (bridgeControl.requested && activeAgent && sessionIdOf(activeAgent.session) !== sessionIdOf(agent.session))
            return { kind: "error", text: "游戏已绑定另一个会话，请先在原会话断开连接。" };
          if (bridgeControl.connecting) return { kind: "error", text: "此会话正在连接，请稍候。" };
          ++manualRoundSerial;
          activeAgent = agent;
          pendingGameEvents = [{ type: "manual_connect", text: "玩家在此会话连接游戏。" }];
          nextContextAt = 0;
          record("error", "RimWorld 自动协作已停止。使用 /rimworld-connect 重新开始。\n");
          bridgeControl.turn.phase = "awaiting_game_input";
          bridgeControl.turn.planSent = false;
          bridgeControl.requested = true;
          bridgeControl.connected = false;
          bridgeControl.connecting = false;
          bridgeControl.disconnectNoticeSent = false;
          bridgeControl.lastNotice = "error";
          bridgeControl.baselineRequired = true;
          await connectNow(agent);
          return { kind: "success", text: "RimWorld connection requested." };
        },
      }));
      commandDisposers.push(ctx.commands.register({
        name: "rimworld-disconnect",
        description: "Disconnect this Harness agent from RimWorld.",
        handler: async ({ agent }) => {
          if (activeAgent && sessionIdOf(activeAgent.session) !== sessionIdOf(agent?.session))
            return { kind: "error", text: "请在绑定游戏的会话中断开连接。" };
          ++manualRoundSerial;
          activeAgent = agent;
          nextContextAt = 0;
          bridgeControl.turn.phase = "awaiting_game_input";
          bridgeControl.turn.planSent = false;
          bridgeControl.requested = false;
          bridgeControl.connected = false;
          bridgeControl.connecting = false;
          bridgeControl.disconnectNoticeSent = false;
          bridgeControl.lastNotice = "disconnected";
          bridgeControl.baselineRequired = true;
          await mcp.request("plugin/disconnect", {}, 5000).catch(() => undefined);
          cancelAgent(agent);
          statusMessage("disconnect", agent);
          return { kind: "success", text: "RimWorld disconnected." };
        },
      }));
      commandDisposers.push(ctx.commands.register({
        name: "rimworld-stop",
        description: "Stop automatic RimWorld Agent rounds and disconnect the current save.",
        handler: async ({ agent }) => {
          if (activeAgent && sessionIdOf(activeAgent.session) !== sessionIdOf(agent?.session))
            return { kind: "error", text: "请在绑定游戏的会话中停止协作。" };
          ++manualRoundSerial;
          activeAgent = agent;
          nextContextAt = 0;
          bridgeControl.turn.phase = "awaiting_game_input";
          bridgeControl.turn.planSent = false;
          bridgeControl.requested = false;
          bridgeControl.connected = false;
          bridgeControl.connecting = false;
          bridgeControl.disconnectNoticeSent = false;
          bridgeControl.lastNotice = "stopped";
          bridgeControl.baselineRequired = true;
          await mcp.request("plugin/disconnect", {}, 5000).catch(() => undefined);
          cancelAgent(agent);
          statusMessage("RimWorld 自动协作已停止。使用 /rimworld-connect 重新开始。", agent, false);
          return { kind: "success", text: "RimWorld Agent rounds stopped." };
        },
      }));
    }

    const timer = setInterval(poll, pollIntervalMs);
    return () => {
      disposed = true;
      nextContextAt = 0;
      bridgeControl.requested = false;
      clearInterval(timer);
      for (const dispose of commandDisposers) dispose();
      if (typeof disposeSessionListener === "function") disposeSessionListener();
    };
  }, "rimworld autonomous bridge");
}
