using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private static readonly (string StateId, string Effect)[] MobileShiWuyiProgressLevels =
    [
        ("shi-wuyi-one", "每轮一次视为使用杀或闪"),
        ("shi-wuyi-two", "使用基本牌无次数限制"),
        ("shi-wuyi-three", "使用基本牌无距离限制"),
        ("shi-wuyi-four", "手牌数恒定为 4")
    ];

    private HumanSkillViewModel ApplyMobileShiZhaoYunProgress(HumanSkillViewModel skill)
    {
        if (skill.ContentId is not ("mobile:shi-wuyi" or "mobile:shi-cuifeng") ||
            _snapshot?.Players.SingleOrDefault(player => player.IsHuman) is not { } human)
            return skill;

        var runtime = human.SkillRuntimeStates?.SingleOrDefault(state => state.SkillId == skill.ContentId);
        if (runtime is null) return skill;

        if (skill.ContentId == "mobile:shi-cuifeng")
        {
            var used = runtime.Usages.Where(usage => usage.Scope == SkillUsageScope.Round &&
                    usage.UsageId.StartsWith("cuifeng-turn-ending@", StringComparison.Ordinal))
                .Sum(usage => usage.Count);
            return skill with
            {
                ProgressText = $"{used}/2",
                StateText = $"本轮已发动 {used}/2 次 · 剩余 {Math.Max(0, 2 - used)} 次\n" +
                    "其他角色的回合结束时，可对该角色发动摧锋。"
            };
        }

        var flags = runtime.BooleanStates ?? [];
        var unlocked = MobileShiWuyiProgressLevels.Where(level => flags.Any(state =>
            state.StateId == level.StateId && state.Value)).ToArray();
        var next = MobileShiWuyiProgressLevels.FirstOrDefault(level => !flags.Any(state =>
            state.StateId == level.StateId && state.Value));
        var details = new List<string>
        {
            $"当前进度：{unlocked.Length}/4 种基本牌",
            unlocked.Length == 0 ? "已解锁效果：无" :
                $"已解锁效果：{string.Join("；", unlocked.Select(level => level.Effect))}",
            unlocked.Length == MobileShiWuyiProgressLevels.Length ? "全部效果已解锁" :
                $"下一解锁：累计 {Array.FindIndex(MobileShiWuyiProgressLevels, level => level.StateId == next.StateId) + 1} 种基本牌，{next.Effect}"
        };
        if (unlocked.Any(level => level.StateId == "shi-wuyi-one"))
        {
            var quota = runtime.PublicRuleStates?.FirstOrDefault(state =>
                state.Kind == ProgramPublicRuleStateKind.ActivationLimit && state.Scope == SkillUsageScope.Round);
            details.Add(quota is null ? "本轮视为杀／闪：次数状态待同步" :
                $"本轮视为杀／闪：{((quota.Used ?? 0) < quota.Value ? "可用" : "已用尽")}（已用 {quota.Used ?? 0}/{quota.Value} 次）");
        }
        else details.Add("本轮视为杀／闪：尚未解锁");

        if (skill.StateText.StartsWith("已选择", StringComparison.Ordinal)) details.Insert(0, skill.StateText);
        return skill with { ProgressText = $"{unlocked.Length}/4", StateText = string.Join("\n", details) };
    }
}
