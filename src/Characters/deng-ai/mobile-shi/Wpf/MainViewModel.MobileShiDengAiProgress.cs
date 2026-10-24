using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private HumanSkillViewModel ApplyMobileShiDengAiProgress(HumanSkillViewModel skill)
    {
        if (skill.ContentId is not ("mobile:shi-tuntian" or "mobile:shi-zaoxian" or "mobile:shi-jixi")) return skill;
        var states = _snapshot?.Players.SingleOrDefault(p => p.IsHuman)?.SkillRuntimeStates?.SingleOrDefault(s => s.SkillId == skill.ContentId)?.PublicRuleStates ?? [];
        if (skill.ContentId == "mobile:shi-tuntian")
        {
            var charge = Math.Max(0, states.FirstOrDefault(s => s.StateId == "charge")?.Value ?? 0);
            var capacity = Math.Max(0, states.FirstOrDefault(s => s.StateId == "capacity")?.Value ?? 0);
            return skill with
            {
                ProgressText = $"蓄力 {charge}/{capacity}",
                StateText = "每失去一张非伤害牌获得1点蓄力，至当前上限。每个出牌阶段一次，消耗任意点并令至多等量角色随机各获红桃牌；任一角色回合开始时蓄满，摸1张且上限+1。"
            };
        }
        if (skill.ContentId == "mobile:shi-jixi")
        {
            var targets = Math.Max(0, states.FirstOrDefault(s => s.StateId == "eligible-targets")?.Value ?? 0);
            return skill with
            {
                ProgressText = $"本回合可选目标 {targets}名",
                StateText = "任一角色回合结束时，可弃置当前角色1张牌，对本回合成为过你牌目标的任意名其他合法角色使用同一张无距离顺手牵羊。目标人数不受蓄力上限限制。"
            };
        }
        return skill with { StateText = "一次消耗蓄力达3/5/7点，独立从弃牌堆获得无中生有、无懈可击、五谷丰登各1张；没有对应实体则该项不获得。" };
    }
}
