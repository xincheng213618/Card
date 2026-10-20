namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_zhou_qun_ol_ZhouQun
{
    internal static CardConversionSource ZhouQunSource(ProgramSkillFrame f) => new(f.SkillId, CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramLifecycle.GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);

    internal static int ZhouQunNameLength(CardKind kind) => CardGame.Core.RuntimeSkills.RuntimeRules_Core_CommonRules.IsSlashCard(kind) ? 1 : kind == CardKind.BorrowedSword ? 2 : CardCatalog.Get(kind).DisplayName.Length;
}
