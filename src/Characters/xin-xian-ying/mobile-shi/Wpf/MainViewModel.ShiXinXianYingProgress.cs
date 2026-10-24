using CardGame.Core;
namespace CardGame.Wpf.ViewModels;
public sealed partial class MainViewModel
{
    private HumanSkillViewModel ApplyShiXinXianYingProgress(HumanSkillViewModel skill)
    {
        if (skill.ContentId is not ("mobile:shi-jiejie" or "mobile:shi-qingshi") ||
            _snapshot?.Players.SingleOrDefault(p => p.IsHuman)?.SkillRuntimeStates?.SingleOrDefault(s => s.SkillId == "mobile:shi-jiejie") is not { } runtime) return skill;
        var used = runtime.PublicRuleStates?.FirstOrDefault(s => s.StateId == "qingshi-round")?.Value ?? 0;
        if (skill.ContentId == "mobile:shi-qingshi") return skill with { StateText = "受到伤害后可发动；按实际阵营结算。同阵营各摸1张，选择自己摸2张；不同阵营由你选择弃置双方各1张。" };
        var mask = runtime.PublicRuleStates?.FirstOrDefault(s => s.StateId == "jiejie-active-suits")?.Value ?? 0;
        var suits = new[] { (1, "黑桃"), (2, "红桃"), (4, "梅花"), (8, "方块") }.Where(s => (mask & s.Item1) != 0).Select(s => s.Item2).ToArray();
        return skill with { ProgressText = $"清识 {used}/2", StateText = $"本轮通过诫节发动清识：{used}/2。每名角色每个实际出牌阶段可主动令你私下观看一次手牌。\n" +
            (suits.Length == 0 ? "当前没有诫节花色次数豁免。" : "本回合使用" + string.Join("、", suits) + "牌无次数限制。") };
    }
}
