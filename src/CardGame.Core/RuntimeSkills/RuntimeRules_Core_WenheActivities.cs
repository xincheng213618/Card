namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_WenheActivities
{
    internal static string WenheRuleName(WenheRuleKind rule) => rule switch { WenheRuleKind.Chaos => "乱武", WenheRuleKind.Bounty => "重赏",
        WenheRuleKind.LastStand => "破釜沉舟", WenheRuleKind.Equipment => "横刀跃马", WenheRuleKind.Sweep => "横扫千军",
        WenheRuleKind.Famine => "饿莩载道", WenheRuleKind.Poison => "宴安鸩毒", _ => throw new ArgumentOutOfRangeException(nameof(rule)) };
}
