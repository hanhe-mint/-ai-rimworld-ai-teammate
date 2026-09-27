using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace AICoopCompanion
{
    public sealed class AICoopLogTab : MainTabWindow
    {
        private Vector2 logScrollPosition;
        private int lastLogCount;
        private bool chatPage = true;
        private string chatInput = "";
        private Vector2 chatScroll;
        private int lastChatCount;
        private TextEditor chatEditor;
        private bool chatFocused;
        private readonly GUIContent chatContent = new GUIContent();
        private delegate void DrawTextField(Rect rect, int id, GUIContent content, bool multiline, int maxLength, GUIStyle style);
        private static readonly DrawTextField drawTextField = (DrawTextField)Delegate.CreateDelegate(typeof(DrawTextField),
            HarmonyLib.AccessTools.Method(typeof(GUI), "DoTextField", new[]
            { typeof(Rect), typeof(int), typeof(GUIContent), typeof(bool), typeof(int), typeof(GUIStyle) }));

        public AICoopLogTab() { closeOnAccept = false; }

        public override void PostOpen()
        {
            forcePause = chatPage && AICoopMod.Settings != null && AICoopMod.Settings.pauseWhileChatOpen;
            base.PostOpen();
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(760f, 520f); }
        }

        public override void OnAcceptKeyPressed()
        {
            // The text field handles Enter before Unity's multiline editor.
        }

        private void SendChat()
        {
            var component = AICoopGameComponent.Current;
            if (component != null && component.SendPlayerChat(chatInput))
            {
                chatInput = "";
                chatFocused = false;
                GUI.FocusControl(null);
                GUIUtility.keyboardControl = 0;
            }
        }

        public override void DoWindowContents(Rect inRect)
        {
            AICoopAgentRuntime.Update();
            AICoopGameComponent component = AICoopGameComponent.Current;
            if (component == null)
            {
                Widgets.Label(inRect, "请先进入一个游戏存档。");
                return;
            }
            if (Widgets.ButtonText(new Rect(inRect.x, inRect.y, 110f, 30f), "聊天")) chatPage = true;
            if (Widgets.ButtonText(new Rect(inRect.x + 116f, inRect.y, 110f, 30f), "日志")) chatPage = false;
            forcePause = chatPage && AICoopMod.Settings != null && AICoopMod.Settings.pauseWhileChatOpen;
            Rect body = new Rect(inRect.x + 8f, inRect.y + 38f, inRect.width - 16f, inRect.height - 46f);
            if (chatPage) DrawChatPage(body, component);
            else DrawLogPage(body, component);
        }

        private void DrawChatPage(Rect rect, AICoopGameComponent component)
        {
            // Allocate the editor before variable-length chat history so its control ID stays stable.
            Event e = Event.current;
            Rect inputRect = new Rect(rect.x, rect.yMax - 100f, rect.width - 88f, 72f);
            int inputId = GUIUtility.GetControlID(0x41494348, FocusType.Keyboard, inputRect);
            var editor = (TextEditor)GUIUtility.GetStateObject(typeof(TextEditor), inputId);
            if (e.type == EventType.MouseDown) chatFocused = inputRect.Contains(e.mousePosition);
            if (chatFocused && Find.WindowStack.GetsInput(this))
            {
                if (chatEditor != null && chatEditor != editor)
                {
                    editor.text = chatInput;
                    editor.cursorIndex = chatEditor.cursorIndex;
                    editor.selectIndex = chatEditor.selectIndex;
                    editor.scrollOffset = chatEditor.scrollOffset;
                }
                GUIUtility.keyboardControl = inputId;
            }
            chatEditor = editor;
            bool enter = chatFocused && Find.WindowStack.GetsInput(this) &&
                e.type == EventType.KeyDown && string.IsNullOrEmpty(Input.compositionString) &&
                (e.keyCode == KeyCode.Return || e.keyCode == KeyCode.KeypadEnter);
            bool submit = enter && !e.shift;
            if (enter)
            {
                if (e.shift)
                {
                    // Keep the focused editor's text, selection and caret in sync after a newline.
                    editor.text = chatInput;
                    editor.ReplaceSelection("\n");
                    chatInput = editor.text;
                    GUI.changed = true;
                }
                e.Use();
            }
            // Windows can emit a separate newline character after the Return key event.
            // The key event above owns send/newline; do not insert its character a second time.
            if (chatFocused && e.type == EventType.KeyDown && e.keyCode == KeyCode.None &&
                (e.character == '\n' || e.character == '\r')) e.Use();
            // Use an explicit control ID and owned content, rather than GUI.TextArea's implicit ID.
            chatContent.text = chatInput;
            drawTextField(inputRect, inputId, chatContent, true, -1, Text.CurTextAreaStyle);
            chatInput = chatContent.text;
            bool clickedSend = Widgets.ButtonText(new Rect(rect.xMax - 80f, rect.yMax - 100f, 80f, 72f), "发送");
            if (submit || clickedSend) SendChat();
            Rect outer = new Rect(rect.x, rect.y, rect.width, rect.height - 108f);
            float width = outer.width - 20f;
            float height = 8f;
            foreach (string message in component.ChatMessages)
                height += Math.Max(24f, Text.CalcHeight(message, width - 16f)) + 12f;
            if (lastChatCount != component.ChatMessages.Count)
            {
                chatScroll.y = height;
                lastChatCount = component.ChatMessages.Count;
            }
            Widgets.BeginScrollView(outer, ref chatScroll, new Rect(0, 0, width, Math.Max(outer.height, height)));
            float y = 4f;
            foreach (string message in component.ChatMessages)
            {
                float rowHeight = Math.Max(24f, Text.CalcHeight(message, width - 16f));
                Widgets.Label(new Rect(8f, y, width - 16f, rowHeight), message);
                y += rowHeight + 12f;
            }
            Widgets.EndScrollView();
            Widgets.Label(new Rect(rect.x, rect.yMax - 24f, rect.width, 24f), "Enter 发送 · Shift+Enter 换行");
        }

        private void DrawLogPage(Rect inRect, AICoopGameComponent component)
        {
            Rect statusRect = new Rect(inRect.x, inRect.y, inRect.width - 108f, 30f);
            Widgets.Label(statusRect, "DeepSeek Harness：" + AICoopAgentBridge.StatusLabel);

            Rect clearRect = new Rect(inRect.xMax - 100f, inRect.y, 92f, 32f);
            if (Widgets.ButtonText(clearRect, "清空记录")) component.ClearLog();

            bool showDetailedCommands = AICoopMod.Settings != null && AICoopMod.Settings.showDetailedCommands;
            Rect detailRect = new Rect(inRect.x, inRect.y + 38f, 180f, 24f);
            Widgets.CheckboxLabeled(detailRect, "显示详细指令", ref showDetailedCommands);
            if (AICoopMod.Settings != null && AICoopMod.Settings.showDetailedCommands != showDetailedCommands)
            {
                AICoopMod.Settings.showDetailedCommands = showDetailedCommands;
                if (AICoopMod.Instance != null) AICoopMod.Instance.WriteSettings();
                lastLogCount = -1;
            }

            List<string> visibleLines = new List<string>();
            foreach (string line in component.LogLines)
            {
                if (showDetailedCommands || (!line.Contains("[详细指令]") && !line.Contains("[模型输出]"))) visibleLines.Add(line);
            }

            Rect outRect = new Rect(inRect.x, inRect.y + 66f, inRect.width, inRect.height - 66f);
            float viewWidth = outRect.width - 20f;
            float contentHeight = 8f;
            foreach (string line in visibleLines)
            {
                contentHeight += Math.Max(24f, Text.CalcHeight(line, viewWidth - 16f)) + 8f;
            }
            contentHeight = Math.Max(outRect.height, contentHeight);
            if (lastLogCount != component.LogLines.Count)
            {
                logScrollPosition.y = contentHeight;
                lastLogCount = component.LogLines.Count;
            }

            Rect viewRect = new Rect(0f, 0f, viewWidth, contentHeight);
            Widgets.BeginScrollView(outRect, ref logScrollPosition, viewRect);
            float y = 4f;
            foreach (string line in visibleLines)
            {
                float height = Math.Max(24f, Text.CalcHeight(line, viewWidth - 16f));
                Rect rowRect = new Rect(0f, y, viewWidth, height + 4f);
                Widgets.DrawHighlightIfMouseover(rowRect);
                Widgets.Label(new Rect(8f, y + 2f, viewWidth - 16f, height), line);
                y += height + 8f;
            }
            Widgets.EndScrollView();
        }
    }
}
