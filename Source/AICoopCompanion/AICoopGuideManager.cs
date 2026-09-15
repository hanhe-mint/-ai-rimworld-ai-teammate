using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Verse;

namespace AICoopCompanion
{
    internal static class AICoopGuideManager
    {
        private sealed class GuideFile
        {
            public string Name;
            public List<GuideTask> Tasks = new List<GuideTask>();
        }

        private sealed class GuideTask
        {
            public string Id;
            public string Line;
            public string Reference;
        }

        private static readonly object Gate = new object();
        private static readonly List<GuideFile> GuideFiles = new List<GuideFile>();
        private static bool initialized;
        private static string loadError = string.Empty;

        public static string BuildPromptSection()
        {
            lock (Gate)
            {
                EnsureInitialized();
                if (!loadError.NullOrEmpty()) return "GUIDE_STATUS unavailable reason=" + loadError;

                AICoopGameComponent component = AICoopGameComponent.Current;
                if (component == null) return "GUIDE_STATUS unavailable reason=no_game";
                if (component.GuideCompleted) return "GUIDE_STATUS completed";

                GuideFile guide;
                GuideTask task;
                if (!TryGetCurrent(component, out guide, out task)) return "GUIDE_STATUS unavailable reason=no_current_milestone";

                StringBuilder section = new StringBuilder();
                section.AppendLine("GUIDE_STATE file=" + guide.Name + " milestone=" + task.Id + " status=active");
                section.AppendLine("GUIDE_REFERENCE " + task.Reference);
                section.AppendLine("GUIDE_MILESTONE " + task.Line);
                return section.ToString();
            }
        }

        public static bool CompleteCurrentTask(string taskId)
        {
            lock (Gate)
            {
                EnsureInitialized();
                AICoopGameComponent component = AICoopGameComponent.Current;
                GuideFile guide;
                GuideTask task;
                if (component == null || component.GuideCompleted || !TryGetCurrent(component, out guide, out task))
                {
                    Log("[攻略] 攻略已完成或未能读取当前里程碑，忽略进展确认。");
                    return false;
                }
                if (!string.Equals(task.Id, taskId, StringComparison.Ordinal))
                {
                    Log("[攻略] 进展确认里程碑不匹配：当前为 " + task.Id + "，收到 " + taskId + "。");
                    return false;
                }

                string completedId = task.Id;
                int nextFileIndex = component.GuideFileIndex;
                int nextTaskIndex = component.GuideTaskIndex + 1;
                if (nextTaskIndex >= guide.Tasks.Count)
                {
                    nextFileIndex++;
                    nextTaskIndex = 0;
                }
                bool completed = nextFileIndex >= GuideFiles.Count;
                component.SetGuideProgress(nextFileIndex, nextTaskIndex, false, completed);

                if (completed)
                {
                    Log("[攻略] 主线里程碑 " + completedId + " 已记录，全部攻略阶段均已完成。");
                }
                else
                {
                    GuideFile nextGuide;
                    GuideTask nextTask;
                    TryGetCurrent(component, out nextGuide, out nextTask);
                    Log("[攻略] 主线里程碑 " + completedId + " 已记录，下一里程碑为 " + nextGuide.Name + " / " + nextTask.Id + "。");
                    AICoopAgentBridge.QueueAgentTrigger("guide_advanced",
                        "前一里程碑 " + completedId + " 已完成。下一阶段只读取并推进这一条：" + nextTask.Line);
                }
                component.AddCommandResult("OK guide_milestone_completed=" + completedId);
                return true;
            }
        }

        private static void EnsureInitialized()
        {
            if (initialized) return;
            initialized = true;
            try
            {
                string guideDirectory = Path.Combine(ModRootDirectory(), "Guides");
                if (!Directory.Exists(guideDirectory))
                {
                    loadError = "guide_directory_missing";
                    return;
                }
                string taskDirectory = Path.Combine(guideDirectory, "Tasks");
                if (Directory.Exists(taskDirectory))
                {
                    foreach (string stageDirectory in Directory.GetDirectories(taskDirectory)
                        .OrderBy(Path.GetFileName, StringComparer.Ordinal))
                    {
                        GuideFile guide = new GuideFile { Name = Path.GetFileName(stageDirectory) };
                        foreach (string path in Directory.GetFiles(stageDirectory, "task_*.txt")
                            .OrderBy(Path.GetFileName, StringComparer.Ordinal))
                        {
                            AddTasks(guide, path, "Tasks/" + Path.GetFileName(stageDirectory) + "/" + Path.GetFileName(path));
                        }
                        if (guide.Tasks.Count > 0) GuideFiles.Add(guide);
                    }
                }
                else
                {
                    foreach (string path in Directory.GetFiles(guideDirectory, "*.txt").OrderBy(Path.GetFileName, StringComparer.Ordinal))
                    {
                        GuideFile guide = new GuideFile { Name = Path.GetFileName(path) };
                        AddTasks(guide, path, Path.GetFileName(path));
                        if (guide.Tasks.Count > 0) GuideFiles.Add(guide);
                    }
                }
                if (GuideFiles.Count == 0) loadError = "guide_tasks_missing";
            }
            catch (Exception ex)
            {
                loadError = ex.GetType().Name;
            }
        }

        private static void AddTasks(GuideFile guide, string path, string reference)
        {
            foreach (string rawLine in File.ReadAllLines(path, Encoding.UTF8))
            {
                string line = rawLine.Trim();
                if (!line.StartsWith("TASK ", StringComparison.OrdinalIgnoreCase)) continue;
                string remaining = line.Substring(5).Trim();
                int separator = remaining.IndexOf(' ');
                if (separator <= 0) continue;
                guide.Tasks.Add(new GuideTask
                {
                    Id = remaining.Substring(0, separator),
                    Line = line,
                    Reference = reference
                });
            }
        }

        private static bool TryGetCurrent(AICoopGameComponent component, out GuideFile guide, out GuideTask task)
        {
            guide = null;
            task = null;
            if (component == null || GuideFiles.Count == 0) return false;
            if (component.GuideFileIndex < 0 || component.GuideFileIndex >= GuideFiles.Count)
            {
                component.SetGuideProgress(0, 0, false, false);
            }
            guide = GuideFiles[component.GuideFileIndex];
            if (component.GuideTaskIndex < 0 || component.GuideTaskIndex >= guide.Tasks.Count)
            {
                component.SetGuideProgress(component.GuideFileIndex, 0, false, false);
            }
            task = guide.Tasks[component.GuideTaskIndex];
            return true;
        }

        private static string ModRootDirectory()
        {
            return AICoopMod.Instance == null || AICoopMod.Instance.Content == null ? string.Empty : AICoopMod.Instance.Content.RootDir;
        }

        private static void Log(string message)
        {
            AICoopGameComponent component = AICoopGameComponent.Current;
            if (component != null) component.AddLog(message);
        }
    }
}
