using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private HumanSkillViewModel ApplyMobileShiHeQiProgress(HumanSkillViewModel skill)
    {
        if (skill.ContentId is not ("mobile:shi-shanxi" or "mobile:shi-qizhou") || _snapshot?.Players.SingleOrDefault(p => p.IsHuman) is not { } human) return skill;
        var marked = human.Hand.Count(c => c.SkillMarkLabel?.Contains("应机", StringComparison.Ordinal) == true);
        var runtime = human.SkillRuntimeStates?.SingleOrDefault(s => s.SkillId == skill.ContentId);
        if (skill.ContentId == "mobile:shi-shanxi")
        {
            var used = runtime?.PublicRuleStates?.FirstOrDefault(s => s.StateId == "shanxi-used-targets")?.Value ?? 0;
            var remaining = runtime?.PublicRuleStates?.FirstOrDefault(s => s.StateId == "shanxi-remaining-targets")?.Value ?? 0;
            return skill with { ProgressText = $"应机 {marked} 张 · 已选 {used} 人", StateText = $"当前手牌中有 {marked} 张应机牌。\n本出牌阶段已对 {used} 名角色发动，当前仍可选择 {remaining} 名其他角色。每名角色限一次，将一张应机手牌当决斗使用。" };
        }
        var ready = runtime?.BooleanStates?.Any(s => s.StateId == "next-mark-draw" && s.Value) == true;
        return skill with { ProgressText = ready ? "下次标记 · 摸应机×2" : "下次奖励 · 未积累",
            StateText = $"{(ready ? "已积累奖励：下次成功标记一张手牌后，摸两张应机牌。" : "标记杀可积累下一次发动的两张应机牌奖励。新积累的奖励不会在本次立即兑现。")}\n摸牌阶段额外摸两张应机牌。每名角色的回合结束时，全场手牌中的应机牌置入弃牌堆。当前持有 {marked} 张。" };
    }
}
