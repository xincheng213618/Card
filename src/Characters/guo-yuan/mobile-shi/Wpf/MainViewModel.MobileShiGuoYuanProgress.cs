using CardGame.Core;
namespace CardGame.Wpf.ViewModels;
public sealed partial class MainViewModel
{
    private HumanSkillViewModel ApplyMobileShiGuoYuanProgress(HumanSkillViewModel skill)
    {
        if (skill.ContentId is not ("mobile:shi-qingdao" or "mobile:shi-xiugeng" or "mobile:shi-chenshe") ||
            _snapshot?.Players.SingleOrDefault(p => p.IsHuman)?.SkillRuntimeStates?.SingleOrDefault(s => s.SkillId == skill.ContentId) is not { } runtime) return skill;
        var states = runtime.PublicRuleStates ?? [];
        if (skill.ContentId == "mobile:shi-xiugeng")
        {
            var recorded = states.Where(s => s.StateId.StartsWith("xiugeng-seat-", StringComparison.Ordinal)).ToArray();
            var lines = recorded.Select(s => int.TryParse(s.StateId[13..], out var seat) ? $"{_snapshot.Players.Single(p => p.Seat == seat).Name}：记录{s.Value}张" : "").Where(s => s.Length > 0);
            var bonus = states.FirstOrDefault(s => s.StateId == "xiugeng-hand-limit")?.Value ?? 0;
            return skill with { ProgressText = $"记录 {recorded.Length}/2", StateText = string.Join("；", lines) +
                $"\n本人手牌上限累计 +{bonus}。摸牌阶段开始时一次比较：≤记录摸2，≥记录上限+1；相等两项都执行。新记录替换上次记录。" };
        }
        return skill with { ProgressText = skill.ContentId == "mobile:shi-chenshe" ? "待救援" : "待结算", StateText = skill.ContentId == "mobile:shi-chenshe"
            ? "其他角色濒死时，依次弃置本人、濒死角色、伤害来源各1张区域牌。三项都实际弃牌且花色相同，令其回复至体力上限，然后失去陈赦。缺少参与者或牌时，仍继续其余步骤。"
            : "其他人对你使用的伤害类牌完整结算后：受过此牌伤害可获得闪或弃置一名角色1牌；未受伤害可获得杀或无距离使用1张手牌。" };
    }
}
