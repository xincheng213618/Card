using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private HumanSkillViewModel ApplyMobileShiChenZhiProgress(HumanSkillViewModel skill)
    {
        if (skill.ContentId is not ("mobile:shi-quanchong" or "mobile:shi-renxing") ||
            _snapshot?.Players.SingleOrDefault(p => p.IsHuman)?.SkillRuntimeStates?.SingleOrDefault(s => s.SkillId == skill.ContentId) is not { } runtime) return skill;
        var quanchong = skill.ContentId == "mobile:shi-quanchong";
        var binding = quanchong ? "quanchong-ending" : "renxing-first-discard";
        var limit = quanchong ? 1 : 2;
        var used = runtime.PublicRuleStates?.FirstOrDefault(s => s.StateId == binding && s.Scope == SkillUsageScope.Round)?.Used ??
            runtime.Usages.Where(u => u.Scope == SkillUsageScope.Round && u.UsageId.StartsWith(binding + "@", StringComparison.Ordinal)).Sum(u => u.Count);
        var firstUsed = runtime.PublicRuleStates?.FirstOrDefault(s => s.StateId == "turn-first-discard")?.Used == 1;
        return skill with { ProgressText = $"{used}/{limit}", StateText = $"本轮已发动 {used}/{limit} 次 · 剩余 {Math.Max(0, limit - used)} 次\n" +
            (quanchong ? "结束阶段弃置所有手牌和装备，获得额外回合；若体力不为唯一最大，额外回合开始时失去1点体力。" :
                (firstUsed ? "本回合首次弃牌阶段外弃牌机会已出现。" : "等待本回合全场首次弃牌阶段外弃牌。") + "\n发动后选择各摸一张，或弃置本回合未使用或打出过杀的角色一张牌。") };
    }
}
