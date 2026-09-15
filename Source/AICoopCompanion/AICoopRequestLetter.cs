using System.Collections.Generic;
using Verse;

namespace AICoopCompanion
{
    public sealed class AICoopRequestLetter : ChoiceLetter
    {
        private bool replied;
        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref replied, "replied", false);
        }

        public override IEnumerable<DiaOption> Choices
        {
            get
            {
                if (replied) { yield return Option_Close; yield break; }
                foreach (string response in new[] { "好的，现在就去做", "知道了，一会再做", "不想做/禁止做" })
                {
                    string choice = response;
                    yield return new DiaOption(choice)
                    {
                        resolveTree = true,
                        action = delegate
                        {
                            if (replied) return;
                            replied = true;
                            string reply = "针对请求「" + Text.ToString() + "」回复：" + choice;
                            AICoopGameComponent.Current.AddWorkMessage("玩家", reply);
                            AICoopGameComponent.Current.AddChatMessage("玩家", reply);
                            AICoopAgentBridge.QueueAgentTrigger("player_message", "玩家说：" + reply);
                            Find.LetterStack.RemoveLetter(this);
                        }
                    };
                }
            }
        }
    }
}
