using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private HumanSkillViewModel ApplyMobileShiWeiYanProgress(HumanSkillViewModel skill)
    {
        if (skill.ContentId is not ("mobile:shi-zhuangshi" or "mobile:shi-zhongao" or "mobile:shi-kuanggu" or "mobile:shi-kuanggu-upgraded") ||
            _snapshot?.Players.SingleOrDefault(p => p.IsHuman) is not { } owner) return skill;
        var state = owner.SkillRuntimeStates?.FirstOrDefault(s => s.SkillId == skill.ContentId);
        if (state is null) return skill;
        if (skill.ContentId == "mobile:shi-zhuangshi")
        {
            var distance = state.PublicRuleStates?.FirstOrDefault(s => s.StateId == "zhuangshi-discard");
            var quota = state.PublicRuleStates?.FirstOrDefault(s => s.StateId == "zhuangshi-hp");
            if (distance is null && quota is null) return skill with { StateText = "出牌阶段开始时，可选择弃牌、失去体力，或同时执行两项。" };
            var d = Math.Max(0, (distance?.Value ?? 0) - (distance?.Used ?? 0));
            var q = Math.Max(0, (quota?.Value ?? 0) - (quota?.Used ?? 0));
            return skill with { ProgressText = $"{d}／{q}", StateText = $"本阶段前牌额度：\n无距离且不可响应：剩余 {d}/{distance?.Value ?? 0} 张\n不计次数：剩余 {q}/{quota?.Value ?? 0} 张\n每次使用牌均消耗对应前牌额度。" };
        }
        if (skill.ContentId == "mobile:shi-zhongao")
        {
            var success = state.BooleanStates?.Any(s => s.StateId == "succeeded" && s.Value) == true;
            var failed = state.BooleanStates?.Any(s => s.StateId == "failed" && s.Value) == true;
            return skill with { ProgressText = success ? "成功" : failed ? "失败" : "进行中",
                StateText = success ? "使命成功：狂骨已升级，可选择背水。" : failed ? "使命失败：失去壮誓，获得困奋。" : "使命进行中：击杀一名角色升级狂骨；濒死或拒绝壮誓则使命失败。" };
        }
        return skill with { ProgressText = skill.ContentId == "mobile:shi-kuanggu-upgraded" ? "二级" : "一级" };
    }
}
