using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private HumanSkillViewModel ApplyMobileShiChenDaoProgress(HumanSkillViewModel skill)
    {
        if (skill.ContentId is not ("mobile:shi-wanglie" or "mobile:shi-hongyi") ||
            _snapshot?.Players.SingleOrDefault(p => p.IsHuman)?.SkillRuntimeStates?.SingleOrDefault(s => s.SkillId == skill.ContentId) is not { } runtime) return skill;
        var states = runtime.PublicRuleStates ?? [];
        if (skill.ContentId == "mobile:shi-hongyi")
        {
            var count = states.FirstOrDefault(s => s.StateId == "hongyi-marks")?.Value ?? 0;
            var frozen = states.FirstOrDefault(s => s.StateId == "hongyi-frozen-x");
            var drew = states.FirstOrDefault(s => s.StateId == "hongyi-draw-first")?.Value == 1;
            return skill with { ProgressText = $"毅 {count}/4", StateText = $"当前“毅”标记：{count}/4。每次造成或受到伤害后获得1个。\n" +
                (frozen is null ? "准备阶段选择先摸牌或先清空；X在准备阶段固定。" : $"本回合 X={frozen.Value} 已固定；结束阶段" + (drew ? "清空现有毅标记。" : $"摸{frozen.Value}张牌。")) };
        }
        var stage = states.FirstOrDefault(s => s.StateId == "wanglie-phase")?.Value ?? 0;
        return skill with { ProgressText = stage switch { 2 => "仅限自身", 1 => "已选手牌", _ => "待选择" }, StateText = stage switch
        {
            2 => "所选牌已经结算结束；此出牌阶段不能对其他角色使用牌，仍可对自己使用牌或重铸。",
            1 => "所选手牌在此实际出牌阶段内使用无距离限制且不可被响应。其结算结束后，仅限制对其他角色使用牌。",
            _ => "出牌阶段开始时，可选择一张手牌。此效果只属于当次实际出牌阶段。"
        } };
    }
}
