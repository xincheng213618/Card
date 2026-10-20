namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_zhu_jun_ol_ZhuJun
{
    internal static int ZhuJunCardNameLength(CardKind kind) => CardGame.Core.RuntimeSkills.RuntimeRules_Core_CommonRules.IsSlashCard(kind) ? 1 : kind == CardKind.BorrowedSword ? 2 : CardCatalog.Get(kind).DisplayName.Length;

    // Delayed Lightning is not an ordinary damage trick. Borrowed Sword's
    // subsequent Slash is a separate card; Unexpected Assault itself deals damage.
    internal static bool IsZhuJunDamageCard(CardKind kind) => CardGame.Core.RuntimeSkills.RuntimeRules_Core_CommonRules.IsSlashCard(kind) || kind is CardKind.Duel or CardKind.FireAttack or CardKind.BarbarianAssault or CardKind.ArrowBarrage or CardKind.UnexpectedAssault;
}
