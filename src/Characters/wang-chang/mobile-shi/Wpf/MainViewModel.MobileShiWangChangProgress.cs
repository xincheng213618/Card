using CardGame.Core;

namespace CardGame.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    private HumanSkillViewModel ApplyMobileShiWangChangProgress(HumanSkillViewModel skill)
    {
        if (skill.ContentId is not ("mobile:shi-kaiji" or "mobile:shi-shepan") ||
            _snapshot?.Players.SingleOrDefault(p => p.IsHuman)?.SkillRuntimeStates?.SingleOrDefault(s => s.SkillId == skill.ContentId) is not { } runtime) return skill;
        var states = runtime.PublicRuleStates ?? [];
        if (skill.ContentId == "mobile:shi-kaiji")
        {
            var maximum = states.FirstOrDefault(s => s.StateId == "kaiji-maximum")?.Value ?? 1;
            return skill with { ProgressText = $"至多{maximum}人", StateText = $"当前 X={maximum} · 按进入过濒死且仍存活的角色计数，至少为1。\n准备阶段选择至多X人各摸1张；其中任意人获得非基本牌后，你额外摸1张。" };
        }
        var used = states.FirstOrDefault(s => s.StateId == "shepan-adjust")?.Used ?? 0;
        var names = states.Where(s => s.StateId.StartsWith("shepan-name-", StringComparison.Ordinal)).Select(s => s.StateId[12..] switch
        { "Slash" => "杀", "Duel" => "决斗", "FireAttack" => "火攻", "BarbarianAssault" => "南蛮入侵", "ArrowBarrage" => "万箭齐发",
          "WumiaoFlood" => "水淹七军", "QizhengXiangsheng" => "奇正相生", var other => other }).ToArray();
        return skill with { ProgressText = $"{used}/1", StateText = $"本回合已发动 {used}/1 次 · 剩余 {Math.Max(0, 1 - used)} 次\n本轮已用牌名：{(names.Length == 0 ? "无" : string.Join("、", names))}。同名牌本轮不能再次发动；火杀、雷杀均计为杀。" };
    }
}
