using CardGame.Core;
namespace CardGame.Wpf.ViewModels;
public sealed partial class MainViewModel
{
    private HumanSkillViewModel ApplyMobileShiXiaoQiaoProgress(HumanSkillViewModel skill)
    {
        if (skill.ContentId is not ("mobile:shi-heyun" or "mobile:shi-yinhui") || _snapshot is null) return skill;
        var owner = _snapshot.Players.SingleOrDefault(p => p.Seat == _snapshot.HumanSeat);
        var states = owner?.SkillRuntimeStates?.FirstOrDefault(s => s.SkillId == skill.ContentId)?.PublicRuleStates ?? [];
        if (skill.ContentId == "mobile:shi-heyun") return skill with { ProgressText = $"本阶段 {states.FirstOrDefault(s => s.StateId == "heyun-uses")?.Used ?? 0}/2",
            StateText = "选择与你有相同技能的角色，可以选自己；再选择自己失去的一个技能，令该角色摸两张牌。可以失去合韵或音洄，已经支付的摸牌继续。" };
        var ids = states.Where(s => s.StateId.StartsWith("yinhui-skill:", StringComparison.Ordinal)).Select(s => s.StateId["yinhui-skill:".Length..]).ToHashSet(StringComparer.Ordinal);
        var names = (owner?.Skills ?? []).Concat(owner?.SecondarySkills ?? []).Where(s => s.ContentId is { } id && ids.Contains(id)).Select(s => s.Name).Distinct().ToArray();
        return skill with { ProgressText = names.Length == 0 ? "待借韵" : "借韵 " + names.Length,
            StateText = names.Length == 0 ? "每轮开始可以清除此前音洄授予的来源，再获得一名其他角色当前拥有的一个技能。相同技能的独立来源会保留。"
                : "当前音洄所得：" + string.Join("、", names) + "。下次发动音洄仅清除此来源；失去音洄本身不会提前清除已获得技能。" };
    }
}
