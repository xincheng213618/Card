using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private HumanSkillViewModel ApplyMobileShiTianFengProgress(HumanSkillViewModel skill)
    {
        if (skill.ContentId is not ("mobile:shi-ganggeng" or "mobile:shi-sijian") ||
            _snapshot?.Players.SingleOrDefault(p => p.IsHuman)?.SkillRuntimeStates?.FirstOrDefault(s => s.SkillId == skill.ContentId) is not { } state)
            return skill;
        var rules = state.PublicRuleStates ?? [];
        if (skill.ContentId == "mobile:shi-ganggeng")
        {
            var phaseLimit = rules.FirstOrDefault(s => s.Kind == ProgramPublicRuleStateKind.ActivationLimit &&
                s.Scope == SkillUsageScope.Phase && s.StateId == "gift-at-least-two");
            var targets = rules.Where(s => s.StateId.StartsWith("ganggeng-target-", StringComparison.Ordinal)).Select(s =>
            {
                var seat = int.Parse(s.StateId.AsSpan("ganggeng-target-".Length), System.Globalization.CultureInfo.InvariantCulture);
                return _snapshot.Players.FirstOrDefault(p => p.Seat == seat)?.Name ?? $"座位{seat + 1}";
            }).ToArray();
            return skill with { ProgressText = phaseLimit is null ? skill.ProgressText : $"{phaseLimit.Used}/{phaseLimit.Value}",
                StateText = targets.Length == 0 ? "本阶段可交给一名其他角色至少两张手牌；结束时检查其手牌数是否全场最多（含并列）。" :
                "本回合结束时待结算：" + string.Join("、", targets) + "。\n手牌全场最多（含并列）则你摸一张，否则弃其区域内一张牌。" };
        }
        var used = rules.FirstOrDefault(s => s.StateId == "sijian")?.Used ?? 0;
        var backwaters = rules.FirstOrDefault(s => s.StateId == "sijian-backwater")?.Used ?? 0;
        var debts = rules.FirstOrDefault(s => s.StateId == "sijian-debts")?.Value ?? 0;
        return skill with { ProgressText = $"{used}/2", StateText =
            $"本回合已发动 {used}/2 次，剩余 {Math.Max(0, 2 - used)} 次。\n背水累计 {backwaters} 次；下次背水失去 {backwaters + 1} 点体力。\n待结算的下次用牌弃牌效果：{debts} 项。" };
    }
}
