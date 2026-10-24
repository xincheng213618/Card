using CardGame.Core;
namespace CardGame.Wpf.ViewModels;
public sealed partial class MainViewModel
{
    private HumanSkillViewModel ApplyMobileShiLuYuShengProgress(HumanSkillViewModel skill)
    {
        if (skill.ContentId is not ("mobile:shi-runwei" or "mobile:shi-shuanghuai") ||
            _snapshot?.Players.SingleOrDefault(p => p.IsHuman)?.SkillRuntimeStates?.SingleOrDefault(s => s.SkillId == skill.ContentId) is not { } runtime) return skill;
        var states = runtime.PublicRuleStates ?? [];
        int Value(string id) => states.FirstOrDefault(s => s.StateId == id)?.Value ?? 0;
        if (skill.ContentId == "mobile:shi-runwei")
        {
            var required = Value("runwei-required"); var progress = Value("runwei-progress"); var refunded = Value("runwei-refunded") != 0;
            var repeatUsed = refunded && runtime.Usages.Any(u => u.Scope == SkillUsageScope.Phase && u.UsageId == "reveal-grant" && u.Count > 0);
            return skill with { ProgressText = repeatUsed ? "重发已使用" : refunded ? "重发已解锁" : required == 0 ? "待获得牌" : $"失去 {progress}/{required}",
                StateText = (repeatUsed ? "本实际出牌阶段唯一一次重新发动机会已使用。" :
                    refunded ? "本实际出牌阶段唯一一次重新发动机会已解锁；重发须选择本回合未通过润微获得牌的角色。" :
                    required == 0 ? "先展示牌并选择受益角色；X按其实际获得的张数固定。" : $"本阶段 X={required}，已失去 {progress} 张；达到X后可再发动一次。") +
                    $"\n本阶段结束时，你自己的手牌中有 {Value("runwei-own-hand")} 张润微收益牌须弃置；其他受益者保留其牌。" };
        }
        var last = Value("shuanghuai-last-target") - 1; var used = Value("shuanghuai-turn-used");
        var name = last < 0 ? "尚未发动" : _snapshot.Players.SingleOrDefault(p => p.Seat == last)?.Name ?? $"座位{last + 1}";
        return skill with { ProgressText = $"本回合 {used}/1", StateText = $"上次霜怀目标：{name}。\n与其距离1以内的其他角色受到伤害时，可防止伤害或令其获得弃牌堆中的实体桃。相同目标双方各摸1张，不同目标失去1点体力。" };
    }
}
