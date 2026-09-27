using RimWorld;
using Verse;

namespace AICoopCompanion
{
    internal static class AICoopSkillRules
    {
        public static bool CheckPriority(Pawn pawn, WorkTypeDef work, int priority, out string reason)
        {
            reason = string.Empty;
            if (pawn == null || work == null || priority == 0 || priority > 2) return true;
            SkillDef skill = RelevantSkill(work);
            int minimum = MinimumSkill(work);
            if (skill == null || minimum <= 0) return true;
            int level = SkillLevel(pawn, skill);
            if (level >= minimum) return true;
            reason = pawn.LabelShort + " 的 " + skill.defName + " 技能为 " + level + "，不能将 " + work.defName + " 优先级设为 " + priority + "（至少需要 " + minimum + "）。";
            return false;
        }

        public static bool MeetsMinimum(Pawn pawn, WorkTypeDef work, int minimum)
        {
            if (minimum <= 0 || work == null) return true;
            SkillDef skill = RelevantSkill(work);
            return skill == null || SkillLevel(pawn, skill) >= minimum;
        }

        public static bool CheckDirectJob(Pawn pawn, WorkTypeDef work, out string reason)
        {
            reason = string.Empty;
            if (work == null) return true;
            SkillDef skill = RelevantSkill(work);
            int minimum = MinimumSkill(work);
            if (skill == null || minimum <= 0) return true;
            int level = SkillLevel(pawn, skill);
            if (level >= minimum) return true;
            reason = pawn.LabelShort + " 的 " + skill.defName + " 技能为 " + level + "，不足以执行 " + work.defName + " 直接任务（至少需要 " + minimum + "）。";
            return false;
        }

        public static bool CanEquip(Pawn pawn, Thing item, out string reason)
        {
            reason = string.Empty;
            if (pawn == null || item == null || item.def == null || !item.def.IsWeapon) return true;
            SkillDef skill = item.def.IsRangedWeapon ? GetSkill("Shooting") : GetSkill("Melee");
            int minimum = 3;
            int level = SkillLevel(pawn, skill);
            if (level >= minimum) return true;
            reason = pawn.LabelShort + " 的 " + skill.defName + " 技能为 " + level + "，不适合装备 " + item.LabelShort + "（至少需要 " + minimum + "）。";
            return false;
        }

        public static SkillDef RelevantSkill(WorkTypeDef work)
        {
            if (work == null) return null;
            string name = work.defName;
            switch (name)
            {
                case "Doctor": return GetSkill("Medicine");
                case "Growing": return GetSkill("Plants");
                case "Cooking": return GetSkill("Cooking");
                case "Construction": return GetSkill("Construction");
                case "Mining": return GetSkill("Mining");
                case "Crafting":
                case "Smithing":
                case "Tailoring": return GetSkill("Crafting");
                case "Research": return GetSkill("Intellectual");
                case "Artistic": return GetSkill("Artistic");
                case "Handling": return GetSkill("Animals");
                case "Shooting": return GetSkill("Shooting");
                case "Melee": return GetSkill("Melee");
                case "Warden": return GetSkill("Social");
                default:
                    return work.relevantSkills == null || work.relevantSkills.Count == 0 ? null : work.relevantSkills[0];
            }
        }

        public static int MinimumSkill(WorkTypeDef work)
        {
            if (work == null || RelevantSkill(work) == null) return 0;
            switch (work.defName)
            {
                case "Doctor":
                case "Research": return 5;
                default: return 3;
            }
        }

        public static int SkillLevel(Pawn pawn, SkillDef skill)
        {
            if (pawn == null || skill == null || pawn.skills == null) return 0;
            SkillRecord record = pawn.skills.GetSkill(skill);
            return record == null ? 0 : record.Level;
        }

        private static SkillDef GetSkill(string defName)
        {
            return DefDatabase<SkillDef>.GetNamedSilentFail(defName);
        }
    }
}
