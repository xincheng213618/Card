using CardGame.Core;
namespace CardGame.Wpf.ViewModels;
public sealed partial class MainViewModel
{
    private HumanSkillViewModel ApplyMobileShiHuangZuProgress(HumanSkillViewModel skill)
    {
        if (skill.ContentId == "mobile:shi-chizhang") return skill with { ProgressText = "伤害牌无距离", StateText = "手牌中除闪电外的伤害牌指定目标后，可多选手牌弃置；此牌结算期间禁止其他角色以相同颜色的牌响应。" };
        if (skill.ContentId != "mobile:shi-duanyang" || _snapshot?.Players.SingleOrDefault(p => p.IsHuman)?.SkillRuntimeStates?.SingleOrDefault(s => s.SkillId == skill.ContentId) is not { } runtime) return skill;
        var states = runtime.PublicRuleStates ?? []; var used = states.FirstOrDefault(s => s.StateId == "store-non-use-slash")?.Used ?? 0;
        var stored = states.FirstOrDefault(s => s.StateId == "stored-current-phase")?.Value ?? 0;
        return skill with { ProgressText = $"{used}/1 · 储杀{stored}", StateText = $"本回合储存 {used}/1 次 · 当前待用【杀】{stored}张。\n仅手牌非使用进入弃牌堆时可发动；储存原牌在本实际阶段结束时使用，不计次数。命中后可重铸受伤者至多2张手牌、装备或判定牌，再自己摸4张。" };
    }
}
