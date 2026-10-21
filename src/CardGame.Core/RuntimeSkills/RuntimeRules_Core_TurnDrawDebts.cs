namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_TurnDrawDebts
{
    // This new opt-in classification deliberately does not change the older
    // damage-card predicates used by other content.
    internal static bool IsTurnDebtDamageCard(CardKind kind) => kind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or
        CardKind.Duel or CardKind.BarbarianAssault or CardKind.ArrowBarrage or CardKind.FireAttack or CardKind.Lightning or CardKind.QizhengXiangsheng;
}
