using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace AICoopCompanion
{
    public enum AICoopTradeMode
    {
        PlayerWindow,
        AI
    }

    public sealed class AICoopMod : Mod
    {
        public static AICoopMod Instance;
        public static AICoopSettings Settings;
        private string maxLogEntriesBuffer;
        private int settingsPage;
        private Vector2 settingsScrollPosition;
        private Vector2 permissionScrollPosition;

        public AICoopMod(ModContentPack content) : base(content)
        {
            Instance = this;
            Settings = GetSettings<AICoopSettings>();
            maxLogEntriesBuffer = Settings.maxLogEntries.ToString();
        }

        public override string SettingsCategory()
        {
            return "AI 协作队友";
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Rect pageTabsRect = new Rect(inRect.x, inRect.y, inRect.width, 30f);
            float pageTabWidth = pageTabsRect.width / 2f;
            if (Widgets.ButtonText(new Rect(pageTabsRect.x, pageTabsRect.y, pageTabWidth - 2f, pageTabsRect.height), "DeepSeek Harness Agent", true, true, settingsPage != 0)) settingsPage = 0;
            if (Widgets.ButtonText(new Rect(pageTabsRect.x + pageTabWidth + 2f, pageTabsRect.y, pageTabWidth - 2f, pageTabsRect.height), "允许 AI 进行的操作", true, true, settingsPage != 1)) settingsPage = 1;
            Rect bodyRect = new Rect(inRect.x, inRect.y + 38f, inRect.width, inRect.height - 38f);
            if (settingsPage == 1)
            {
                DrawPermissionsSettings(bodyRect);
                return;
            }
            DrawAgentSettings(bodyRect);
        }

        private void DrawAgentSettings(Rect inRect)
        {
            float viewHeight = Math.Max(inRect.height, 640f);
            Rect viewRect = new Rect(0f, 0f, inRect.width - 20f, viewHeight);
            Widgets.BeginScrollView(inRect, ref settingsScrollPosition, viewRect);
            Listing_Standard listing = new Listing_Standard();
            listing.Begin(viewRect);
            listing.Label("本模组使用 DeepSeek Harness Agent：提示词、上下文和玩家对话都由 Harness 管理。请在 Harness 中点击连接游戏，使用 stop 停止协作。");
            listing.Label("支持本机 Windows 上运行的 DeepSeek Harness（含启动器 Web 界面）；在 Harness 加载环世界插件后手动连接。仅打开远程网页不能访问本机游戏。");
            listing.Label("命名管道：" + AICoopAgentBridge.PipePath);
            listing.Label("连接状态：" + AICoopAgentBridge.StatusLabel);
            string[] controlLabels = { "各自控制自己的殖民者", "双方可控制全部殖民者", "玩家可控制AI，AI不可控制玩家" };
            if (listing.ButtonText("双方操控方式：" + controlLabels[Settings.ControlMode]))
            {
                var options = new List<FloatMenuOption>();
                for (int i = 0; i < controlLabels.Length; i++)
                {
                    int mode = i;
                    options.Add(new FloatMenuOption(controlLabels[i], () => Settings.ControlMode = mode));
                }
                Find.WindowStack.Add(new FloatMenu(options));
            }
            listing.CheckboxLabeled("AI 思考时暂停游戏（外接 Agent）", ref Settings.pauseDuringWorkAI);
            listing.CheckboxLabeled("聊天时暂停游戏（打开聊天页面时）", ref Settings.pauseWhileChatOpen);
            if (Settings.pauseDuringWorkAI)
            {
                listing.Label("两次 AI 暂停之间最短工作时间：" + Settings.minWorkSecondsBetweenPauses + " 秒");
                Settings.minWorkSecondsBetweenPauses = (int)listing.Slider(Settings.minWorkSecondsBetweenPauses, 1f, 300f);
            }
            listing.Label("AI 决策间隔：" + Settings.decisionIntervalSeconds + " 秒（回复输出后等待，再发送下一轮）");
            Settings.decisionIntervalSeconds = (int)listing.Slider(Settings.decisionIntervalSeconds, 0f, 120f);
            listing.Label("AI 交易执行方式");
            Rect tradeModeRect = listing.GetRect(Text.LineHeight);
            Widgets.Dropdown<AICoopTradeMode, AICoopTradeMode>(tradeModeRect, Settings.tradeMode, value => value,
                CreateTradeModeOptions, GetTradeModeLabel(Settings.tradeMode), null, null, null, null, true);
            listing.Label("最多保留日志：" + Settings.maxLogEntries + " 条");
            int previousMaxLogEntries = Settings.maxLogEntries;
            Rect logLimitRect = listing.GetRect(Text.LineHeight);
            Rect logSliderRect = new Rect(logLimitRect.x, logLimitRect.y, logLimitRect.width - 96f, logLimitRect.height);
            Rect logInputRect = new Rect(logLimitRect.xMax - 86f, logLimitRect.y, 86f, logLimitRect.height);
            int sliderLogLimit = (int)Widgets.HorizontalSlider(logSliderRect, Settings.maxLogEntries, 10f, 2000f, true, null, "10", "2000", 1f);
            if (sliderLogLimit != Settings.maxLogEntries)
            {
                Settings.maxLogEntries = sliderLogLimit;
                maxLogEntriesBuffer = sliderLogLimit.ToString();
            }
            if (maxLogEntriesBuffer.NullOrEmpty()) maxLogEntriesBuffer = Settings.maxLogEntries.ToString();
            Widgets.TextFieldNumeric<int>(logInputRect, ref Settings.maxLogEntries, ref maxLogEntriesBuffer, 10f, 2000f);
            Settings.maxLogEntries = Math.Max(10, Math.Min(2000, Settings.maxLogEntries));
            if (Settings.maxLogEntries != previousMaxLogEntries && AICoopGameComponent.Current != null)
            {
                AICoopGameComponent.Current.TrimLogsToLimit();
            }
            listing.End();
            Widgets.EndScrollView();
        }

        private void DrawPermissionsSettings(Rect inRect)
        {
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, Text.LineHeight), "每个工具可单独设置：允许、询问玩家或禁止。询问玩家会在执行前逐条等待确认。");
            Rect outRect = new Rect(inRect.x, inRect.y + Text.LineHeight + 8f, inRect.width, inRect.height - Text.LineHeight - 8f);
            float rowHeight = Text.LineHeight + 8f;
            Rect viewRect = new Rect(0f, 0f, outRect.width - 20f, AICoopToolPermissions.Definitions.Length * rowHeight + 8f);
            Widgets.BeginScrollView(outRect, ref permissionScrollPosition, viewRect);
            for (int i = 0; i < AICoopToolPermissions.Definitions.Length; i++)
            {
                AICoopToolPermissionDefinition definition = AICoopToolPermissions.Definitions[i];
                float y = 4f + i * rowHeight;
                Widgets.Label(new Rect(8f, y, viewRect.width - 190f, Text.LineHeight), definition.Key + "  " + definition.Label);
                AICoopPermissionMode mode = Settings.GetToolPermission(definition.Key);
                Rect modeRect = new Rect(viewRect.width - 176f, y, 168f, Text.LineHeight);
                Widgets.Dropdown<AICoopPermissionMode, AICoopPermissionMode>(modeRect, mode, value => value,
                    ignored => CreatePermissionOptions(definition.Key), AICoopToolPermissions.ModeLabel(mode), null, null, null, null, true);
            }
            Widgets.EndScrollView();
        }

        private IEnumerable<Widgets.DropdownMenuElement<AICoopPermissionMode>> CreatePermissionOptions(string key)
        {
            foreach (AICoopPermissionMode mode in Enum.GetValues(typeof(AICoopPermissionMode)))
            {
                AICoopPermissionMode selectedMode = mode;
                yield return new Widgets.DropdownMenuElement<AICoopPermissionMode>
                {
                    option = new FloatMenuOption(AICoopToolPermissions.ModeLabel(selectedMode), delegate
                    {
                        Settings.SetToolPermission(key, selectedMode);
                        WriteSettings();
                    }),
                    payload = selectedMode
                };
            }
        }

        private IEnumerable<Widgets.DropdownMenuElement<AICoopTradeMode>> CreateTradeModeOptions(AICoopTradeMode ignored)
        {
            foreach (AICoopTradeMode mode in Enum.GetValues(typeof(AICoopTradeMode)))
            {
                AICoopTradeMode selectedMode = mode;
                yield return new Widgets.DropdownMenuElement<AICoopTradeMode>
                {
                    option = new FloatMenuOption(GetTradeModeLabel(selectedMode), delegate
                    {
                        Settings.tradeMode = selectedMode;
                        WriteSettings();
                    }),
                    payload = selectedMode
                };
            }
        }

        private static string GetTradeModeLabel(AICoopTradeMode mode)
        {
            return mode == AICoopTradeMode.AI ? "AI 自动完成买卖" : "打开原版贸易界面（玩家操作）";
        }
    }

    public sealed class AICoopSettings : ModSettings
    {
        public AICoopTradeMode tradeMode = AICoopTradeMode.PlayerWindow;
        public bool allowSharedControl;
        private int controlMode = -1;
        public int ControlMode
        {
            get { return controlMode < 0 ? (allowSharedControl ? 1 : 0) : controlMode; }
            set { controlMode = Math.Max(0, Math.Min(2, value)); allowSharedControl = controlMode == 1; }
        }
        public bool PlayerCanControlAI { get { return ControlMode != 0; } }
        public bool AICanControlPlayer { get { return ControlMode == 1; } }
        public bool pauseDuringWorkAI;
        public bool pauseWhileChatOpen;
        public int minWorkSecondsBetweenPauses = 10;
        public bool showDetailedCommands;
        public int decisionIntervalSeconds = 30;
        public int maxLogEntries = 200;
        public List<string> toolPermissions = new List<string>();

        public AICoopPermissionMode GetToolPermission(string key)
        {
            if (toolPermissions != null)
            {
                foreach (string entry in toolPermissions)
                {
                    if (entry == null) continue;
                    string[] parts = entry.Split(new[] { '=' }, 2);
                    if (parts.Length != 2 || !string.Equals(parts[0], key, StringComparison.OrdinalIgnoreCase)) continue;
                    AICoopPermissionMode mode;
                    if (Enum.TryParse(parts[1], true, out mode)) return mode;
                }
            }
            if (key.StartsWith("WORLD.", StringComparison.OrdinalIgnoreCase))
            {
                if (key.Equals("WORLD.trade", StringComparison.OrdinalIgnoreCase)) return AICoopPermissionMode.Deny;
                return AICoopPermissionMode.Ask;
            }
            return AICoopPermissionMode.Allow;
        }

        public void SetToolPermission(string key, AICoopPermissionMode mode)
        {
            if (toolPermissions == null) toolPermissions = new List<string>();
            toolPermissions.RemoveAll(entry => entry != null && entry.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));
            toolPermissions.Add(key + "=" + mode.ToString().ToLowerInvariant());
        }

        public string ToolPermissionsSummary()
        {
            return AICoopToolPermissions.Summary(this);
        }

        public override void ExposeData()
        {
            Scribe_Values.Look(ref tradeMode, "tradeMode", AICoopTradeMode.PlayerWindow);
            Scribe_Values.Look(ref allowSharedControl, "allowSharedControl", false);
            Scribe_Values.Look(ref controlMode, "controlMode", -1);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) ControlMode = ControlMode;
            Scribe_Values.Look(ref pauseDuringWorkAI, "pauseDuringWorkAI", false);
            Scribe_Values.Look(ref pauseWhileChatOpen, "pauseWhileChatOpen", false);
            Scribe_Values.Look(ref minWorkSecondsBetweenPauses, "minWorkSecondsBetweenPauses", 10);
            Scribe_Values.Look(ref showDetailedCommands, "showDetailedCommands", false);
            Scribe_Values.Look(ref decisionIntervalSeconds, "decisionIntervalSeconds", 30);
            Scribe_Values.Look(ref maxLogEntries, "maxLogEntries", 200);
            Scribe_Collections.Look(ref toolPermissions, "toolPermissions", LookMode.Value);
            decisionIntervalSeconds = Math.Max(0, Math.Min(120, decisionIntervalSeconds));
            maxLogEntries = Math.Max(10, Math.Min(2000, maxLogEntries));
            minWorkSecondsBetweenPauses = Math.Max(1, Math.Min(300, minWorkSecondsBetweenPauses));
            if (toolPermissions == null) toolPermissions = new List<string>();
        }
    }

    [StaticConstructorOnStartup]
    public static class AICoopBootstrap
    {
        static AICoopBootstrap()
        {
            AICoopAgentBridge.InstallExitHooks();
            if (Prefs.DevMode)
            {
                DebugSettings.godMode = true;
            }
            new Harmony("local.aicoopcompanion").PatchAll();
        }
    }
}
