using CardGame.Core;
namespace CardGame.Wpf.ViewModels;
public sealed partial class MainViewModel
{
    private HumanSkillViewModel ApplyMobileShiSunChenProgress(HumanSkillViewModel skill)
    {
        if (skill.ContentId == "mobile:shi-lulian") return skill with { StateText = "完整使用手牌后检查当前同类别手牌；分别连环目标、摸1张。两条件都满足时，完成效果后强制对非最低体力角色火伤1（可以选自己）。" };
        if (skill.ContentId != "mobile:shi-nigu" || _snapshot is null) return skill;
        var states = _snapshot.Players.SingleOrDefault(p => p.IsHuman)?.SkillRuntimeStates?.SingleOrDefault(s => s.SkillId == skill.ContentId)?.PublicRuleStates ?? [];
        var remaining = states.Where(s => s.StateId == "nigu-damage-remaining").Sum(s => Math.Max(0, s.Value - (s.Used ?? 0)));
        return skill with { ProgressText = $"本回合加伤余{remaining}次", StateText = "每个出牌阶段一次：多选不同花色的手牌或装备牌，可零张；全部私下承诺后交牌。未交人数变为本回合下X次实际伤害各+1的额度。" };
    }
}
