window.__ModuleLoader__.load({
  id: "dsh-plugin-rimworld-prompt-ui",
  factory: (require) => {
    const React = require("react");
    const h = React.createElement;
    const buttonStyle = { padding: "6px 10px", borderRadius: 8, border: "1px solid var(--dsw-alias-border-l2, #777)", background: "var(--dsw-alias-interactive-bg-hover, transparent)", color: "inherit", cursor: "pointer" };
    function Editor({ sessionId, rpc }) {
      const [open, setOpen] = React.useState(false);
      const [text, setText] = React.useState("");
      const [revision, setRevision] = React.useState(null);
      const [learning, setLearning] = React.useState("");
      const [busy, setBusy] = React.useState(false);
      const [error, setError] = React.useState("");
      const request = async (operation, data = {}) => {
        const result = await rpc(operation, { sessionId, ...data });
        if (!result.ok) throw new Error(result.error?.message || "无法读取配置");
        return result.value;
      };
      const load = async () => {
        setOpen(true); setBusy(true); setError(""); setRevision(null);
        try { const value = await request("read"); setText(value.text); setRevision(value.revision); setLearning(value.learning); }
        catch (e) { setError(e.message); }
        finally { setBusy(false); }
      };
      const save = async () => {
        setBusy(true); setError("");
        try { await request("save", { text, revision }); setOpen(false); }
        catch (e) { setError(e.message); }
        finally { setBusy(false); }
      };
      return h(React.Fragment, null,
        h("button", { type: "button", style: buttonStyle, onClick: load, "aria-label": "AI提示词（实验性）" }, "AI提示词（实验性）"),
        open && h("div", { style: { position: "fixed", inset: 0, background: "#0008", zIndex: 10000, display: "grid", placeItems: "center" }, onKeyDown: e => { e.stopPropagation(); if (e.key === "Escape" && !busy) setOpen(false); } },
          h("section", { role: "dialog", "aria-modal": true, "aria-label": "AI提示词配置", style: { width: "min(720px, 92vw)", maxHeight: "90vh", overflow: "auto", padding: 24, borderRadius: 14, background: "var(--dsw-alias-bg-layer-1, #202124)", color: "var(--dsw-alias-label-primary, #eee)", boxShadow: "0 12px 50px #0006" } },
            h("h2", null, "AI提示词（实验性）"),
            h("p", null, "仅环世界AI队友模式生效 · 排序 -850 · 保存后下次模型请求生效"),
            h("textarea", { "aria-label": "自定义AI提示词", autoFocus: true, value: text, disabled: busy || revision === null, onChange: e => setText(e.target.value), placeholder: "直接在这里编写希望AI遵守的规则……", style: { width: "100%", boxSizing: "border-box", minHeight: 280, padding: 12, resize: "vertical", background: "transparent", color: "inherit", border: "1px solid #888", borderRadius: 8, lineHeight: 1.6 } }),
            learning && h("details", null, h("summary", null, "学习模式附加指令（只读，不会改动上面的内容）"), h("p", { style: { whiteSpace: "pre-wrap" } }, learning)),
            error && h("p", { role: "alert", style: { color: "#ff8b8b" } }, error),
            h("div", { style: { display: "flex", gap: 10, justifyContent: "flex-end", marginTop: 16 } },
              h("button", { type: "button", style: buttonStyle, disabled: busy || revision === null, onClick: () => setText("") }, "清空输入"),
              h("button", { type: "button", style: buttonStyle, disabled: busy, onClick: () => setOpen(false) }, "取消"),
              h("button", { type: "button", style: buttonStyle, disabled: busy || revision === null, onClick: save }, busy ? "处理中…" : "保存")))));
    }
    function PromptButton({ sessionId, useSessions, rpc }) {
      const preset = useSessions(state => state.byId[sessionId]?.projectionValues?.agentPreset);
      return preset === "rimworld" ? h(Editor, { key: sessionId, sessionId, rpc }) : null;
    }
    return {
      inject: ["slots", "conversation", "sessions", "connection"],
      apply(ctx) {
        ctx.effect(() => ctx.slots.register({
          name: "conversation.session.header.actions", id: "rimworld-ai-prompt", order: -9,
          inject: () => ({ rpc: (operation, payload) => ctx.connection.rpc.call("/rimworld-prompts", operation, payload) }),
        }, PromptButton), "rimworld prompt editor button");
      },
    };
  },
});
