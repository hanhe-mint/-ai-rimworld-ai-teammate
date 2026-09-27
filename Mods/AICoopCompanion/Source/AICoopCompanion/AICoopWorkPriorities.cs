using System;
using System.Collections.Generic;

namespace AICoopCompanion
{
    internal static class AICoopWorkPriorities
    {
        // Protocol slots are permanent; never derive them from the UI or naturalPriority.
        internal static readonly string[] Names = {
            "Firefighter", "Patient", "Doctor", "PatientBedRest", "BasicWorker", "Warden",
            "Handling", "Cooking", "Hunting", "Construction", "Growing", "Mining",
            "PlantCutting", "Smithing", "Tailoring", "Art", "Crafting", "Hauling", "Cleaning", "Research"
        };

        internal static bool TryParse(string input, out Dictionary<string, int> priorities, out string error)
        {
            priorities = new Dictionary<string, int>(StringComparer.Ordinal);
            error = null;
            string[] values = input.Trim('(', ')').Split(',');
            bool named = input.Contains("=");
            if (!named && values.Length != Names.Length)
            { error = "数字模式必须提供固定的20项；Mod新增工作使用工作DefName=优先级，不按界面顺序。"; return false; }
            for (int i = 0; i < values.Length; i++)
            {
                string[] pair = named ? values[i].Split('=') : new[] { Names[i], values[i] };
                int priority;
                if (pair.Length != 2 || string.IsNullOrWhiteSpace(pair[0]) ||
                    !int.TryParse(pair[1], out priority) || priority < 0 || priority > 4 || priorities.ContainsKey(pair[0]))
                { error = "无效或重复的工作优先级：" + values[i]; return false; }
                priorities.Add(pair[0], priority);
            }
            return true;
        }
    }
}
