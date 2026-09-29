import { dirname, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { promptStore, learningStates, LEARNING_PROMPT } from "../ai-prompts.js";

export const name = "rimworld-prompt-ui";
export const inject = ["connection", "sessions", "webServer"];

export function isRimworldSession(session) {
  if (!session) return false;
  let preset = session.header?.agentPreset;
  for (const event of session.snapshotEvents())
    if (event.type === "agent-preset/selected") preset = event.data.agentPreset;
  return preset === "rimworld";
}

export function apply(ctx) {
  const store = promptStore(resolve(dirname(fileURLToPath(import.meta.url)), ".."));
  ctx.inject(["connection", "webServer", "sessions"], scoped => scoped.connection.rpc.handle("/rimworld-prompts", async (operation, payload) => {
    try {
      if (!payload || typeof payload.sessionId !== "string" || !isRimworldSession(ctx.sessions.get(payload.sessionId)))
        return { ok: false, error: { code: "forbidden", message: "仅环世界AI队友模式可以编辑此配置。" } };
      if (operation !== "read" && operation !== "save") throw new Error("未知操作。");
      const value = operation === "save" ? await store.save(payload.text, payload.revision) : store.read();
      const learning = [...learningStates].some(state => state.requested && state.learningMode);
      return { ok: true, value: { ...value, order: -850, learning: learning ? LEARNING_PROMPT : "" } };
    } catch (error) {
      return { ok: false, error: { code: "invalid_request", message: error.message } };
    }
  }));
}
