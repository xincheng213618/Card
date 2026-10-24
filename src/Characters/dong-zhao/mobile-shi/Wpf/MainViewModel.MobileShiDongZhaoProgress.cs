using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private HumanSkillViewModel ApplyMobileShiDongZhaoProgress(HumanSkillViewModel skill)
    {
        if (skill.ContentId == "mobile:shi-miaolue")
            return skill with { ProgressText = "受伤可选", StateText = "开局获得两张实体瞒天过海（不计手牌上限）。\n受到一次伤害后选择摸2张，或指定无中生有、过河拆桥、无懈可击的一种牌名，从牌堆或弃牌堆获得1张。" };
        if (skill.ContentId != "mobile:shi-yingjia" || _snapshot?.Players.SingleOrDefault(p => p.IsHuman)?.SkillRuntimeStates?
            .SingleOrDefault(s => s.SkillId == skill.ContentId) is not { } runtime) return skill;
        var used = runtime.PublicRuleStates?.FirstOrDefault(s => s.StateId == "same-tricks-ended")?.Used ?? 0;
        var count = runtime.PublicRuleStates?.FirstOrDefault(s => s.StateId == "same-name-tricks")?.Used ?? 0;
        return skill with { ProgressText = used > 0 ? "已用" : $"{count}/2", StateText = $"限定技：{(used > 0 ? "已发动" : "尚未发动")}\n本回合使用同名锦囊最多 {count}/2 张。达成后于任意角色回合结束弃置一张手牌，选择一人执行额外回合；该回合开始时其摸2张。" };
    }
}
