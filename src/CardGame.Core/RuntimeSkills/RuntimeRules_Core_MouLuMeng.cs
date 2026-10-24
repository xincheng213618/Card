namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_MouLuMeng
{
    internal const string RoundDamageCardUsagePrefix = "card-name";

    internal static bool IsDamageCardKind(CardKind kind) => kind is
        CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.ColdSlash or
        CardKind.Duel or CardKind.BarbarianAssault or CardKind.ArrowBarrage or
        CardKind.FireAttack or CardKind.WumiaoFlood or CardKind.QizhengXiangsheng;

    internal static string GetRoundDamageCardUsageId(CardKind kind) =>
        $"{RoundDamageCardUsagePrefix}.{kind}";
}
