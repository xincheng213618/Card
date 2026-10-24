using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private HumanSkillViewModel ApplyMobileShiHuanJieProgress(HumanSkillViewModel skill)
    {
        if (skill.ContentId == "mobile:shi-gongmou") return skill with { ProgressText = "准备阶段换手牌",
            StateText = "准备阶段选择一名其他角色，交换全部手牌；交换及其移动结算完成后，本回合你获得奇策、对方获得看破。回合结束时临时技能失效。" };
        if (skill.ContentId != "mobile:shi-zhengshuo" || _snapshot?.Players.SingleOrDefault(p => p.IsHuman)?.SkillRuntimeStates?
                .SingleOrDefault(s => s.SkillId == skill.ContentId) is not { } runtime) return skill;
        var used = runtime.PublicRuleStates?.FirstOrDefault(s => s.StateId == "reset-all-hands" && s.Scope == SkillUsageScope.Game)?.Used ??
            runtime.Usages.Where(u => u.Scope == SkillUsageScope.Game && u.UsageId == "reset-all-hands").Sum(u => u.Count);
        return skill with { ProgressText = used == 0 ? "限定技 · 可发动" : "限定技 · 已使用",
            StateText = $"本局已发动 {used}/1 次。\n全体角色先弃置全部手牌，随后将弃牌堆并回摸牌堆并洗牌，最后所有存活角色依次各摸四张牌。装备区及判定区不参与弃牌。" };
    }
}
