namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_yang_feng_ol_Jiawei_Programs
{
    internal static bool IsJiaweiDamageCard(CardKind kind) => CardGame.Core.RuntimeSkills.RuntimeRules_Core_CommonRules.IsSlashCard(kind) || kind is CardKind.Duel or CardKind.FireAttack or CardKind.BarbarianAssault or CardKind.ArrowBarrage or CardKind.Lightning or CardKind.UnexpectedAssault;
}
