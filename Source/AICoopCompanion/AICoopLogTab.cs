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

        public AICoopLogTab() { closeOnAccept = false; }

        public override Vector2 InitialSize
        {
            get { return new Vector2(760f, 520f); }
        }

        public override void OnAcceptKeyPressed()
        {
            if (chatPage) SendChat();
            if (Event.current != null) Event.current.Use();
        }

        private void SendChat()
        {
            var component = AICoopGameComponent.Current;
            if (component != null && component.SendPlayerChat(chatInput)) chatInput = "";
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
            if (Event.current.type == EventType.KeyDown &&
                (Event.current.keyCode == KeyCode.Return || Event.current.keyCode == KeyCode.KeypadEnter))
                OnAcceptKeyPressed();
            if (Widgets.ButtonText(new Rect(inRect.x, inRect.y, 110f, 30f), "聊天")) chatPage = true;
            if (Widgets.ButtonText(new Rect(inRect.x + 116f, inRect.y, 110f, 30f), "日志")) chatPage = false;
            Rect body = new Rect(inRect.x + 8f, inRect.y + 38f, inRect.width - 16f, inRect.height - 46f);
            if (chatPage) DrawChatPage(body, component);
            else DrawLogPage(body, component);
        }

        private void DrawChatPage(Rect rect, AICoopGameComponent component)
        {
            Rect outer = new Rect(rect.x, rect.y, rect.width, rect.height - 42f);
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
            chatInput = Widgets.TextField(new Rect(rect.x, rect.yMax - 34f, rect.width - 88f, 32f), chatInput);
            if (Widgets.ButtonText(new Rect(rect.xMax - 80f, rect.yMax - 34f, 80f, 32f), "发送")) SendChat();
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
