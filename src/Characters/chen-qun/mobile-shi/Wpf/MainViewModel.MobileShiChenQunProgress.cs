using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private HumanSkillViewModel ApplyMobileShiChenQunProgress(HumanSkillViewModel skill)
    {
        if (skill.ContentId is not ("mobile:shi-dingpin" or "mobile:shi-faen") ||
            _snapshot?.Players.SingleOrDefault(player => player.IsHuman)?.SkillRuntimeStates?
                .SingleOrDefault(state => state.SkillId == skill.ContentId) is not { } runtime) return skill;
        var dingpin = skill.ContentId == "mobile:shi-dingpin";
        var scope = dingpin ? SkillUsageScope.Round : SkillUsageScope.Turn;
        var binding = dingpin ? "maximum-loss-ending" : "faen-use";
        var limit = dingpin ? 1 : 2;
        var projected = runtime.PublicRuleStates?.FirstOrDefault(state => state.StateId == binding && state.Scope == scope);
        var used = projected?.Used ?? runtime.Usages.Where(usage => usage.Scope == scope &&
            usage.UsageId.StartsWith(binding + "@", StringComparison.Ordinal)).Sum(usage => usage.Count);
        return skill with { ProgressText = $"{used}/{limit}", StateText =
            $"{(dingpin ? "本轮" : "本回合")}已发动 {used}/{limit} 次 · 剩余 {Math.Max(0, limit - used)} 次\n" +
            (dingpin ? "每个回合结束时，可令本回合失牌最多的角色摸两张牌。" : "上一张牌由你使用时，可令当前使用者摸一张或弃一张，再令本回合下一使用者执行另一项。") };
    }
}
