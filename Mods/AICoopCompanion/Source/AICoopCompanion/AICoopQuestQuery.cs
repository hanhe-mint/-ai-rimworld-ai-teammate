using System;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace AICoopCompanion
{
    internal static class AICoopQuestQuery
    {
        // Inspection only: never accept, signal, select rewards or modify a quest.
        internal static void Execute(string[] parts)
        {
            var component = AICoopGameComponent.Current;
            if (Find.QuestManager == null) throw new InvalidOperationException("当前没有任务管理器。");
            var quests = Find.QuestManager.QuestsListForReading
                .Where(q => q != null && !q.hidden && !q.hiddenInUI).OrderBy(q => q.id).ToList();
            if (parts[0].Equals("QUEST_LIST", StringComparison.OrdinalIgnoreCase))
            {
                int offset = 0;
                if (parts.Length > 2 || (parts.Length == 2 && (!int.TryParse(parts[1], out offset) || offset < 0)))
                    throw new ArgumentException("格式：QUEST_LIST [offset非负整数]");
                var page = quests.Skip(offset).Take(30).ToList();
                var result = new StringBuilder("OK QUEST_LIST readonly=1 total=" + quests.Count + " next=" +
                    (offset < quests.Count - page.Count ? (offset + page.Count).ToString() : "done"));
                foreach (var quest in page) result.AppendLine().Append(Header(quest));
                component.AddCommandResult(result.ToString());
                return;
            }
            int id;
            if (parts.Length != 2 || !int.TryParse(parts[1], out id) || id < 0)
                throw new ArgumentException("格式：QUEST_DETAIL 任务ID");
            var target = quests.FirstOrDefault(q => q.id == id);
            if (target == null) throw new ArgumentException("找不到可见任务 ID=" + id);
            var detail = new StringBuilder("OK QUEST_DETAIL readonly=1\n" + Header(target));
            detail.AppendLine().AppendLine("任务内容：").AppendLine(target.description.ToString());
            int group = 0;
            foreach (var choicePart in target.PartsListForReading.OfType<QuestPart_Choice>())
            {
                group++;
                detail.AppendLine("奖励组选项 " + group + "（仅查看，已选择=" + choicePart.choiceUsed + "）：");
                for (int i = 0; i < choicePart.choices.Count; i++)
                {
                    detail.AppendLine("选项 " + (i + 1) + "：");
                    foreach (var reward in choicePart.choices[i].rewards)
                    {
                        try { detail.AppendLine(reward.GetDescription(default(RewardsGeneratorParams))); }
                        catch (Exception ex) { detail.AppendLine("奖励说明读取失败：" + reward.GetType().Name + " " + ex.Message); }
                    }
                }
            }
            if (group == 0) detail.AppendLine("没有可选奖励组；固定奖励请参阅任务内容及补充说明。");
            foreach (var part in target.PartsListForReading)
            {
                try
                {
                    string text = part.DescriptionPart;
                    if (!text.NullOrEmpty()) detail.AppendLine(text);
                }
                catch (Exception ex) { detail.AppendLine("补充说明读取失败：" + part.GetType().Name + " " + ex.Message); }
            }
            component.AddCommandResult(detail.ToString().TrimEnd());
        }

        private static string Header(Quest quest)
        {
            return "QUEST id=" + quest.id + " name=" + (quest.name ?? "").Replace("\n", " ").Replace("\r", " ") +
                " state=" + quest.State + " stars=" + quest.challengeRating + " points=" + quest.points +
                " historical=" + quest.Historical + " expiresTicks=" + quest.TicksUntilExpiry;
        }
    }
}
