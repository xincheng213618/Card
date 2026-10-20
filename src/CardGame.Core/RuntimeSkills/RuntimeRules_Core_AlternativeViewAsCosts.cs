namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_AlternativeViewAsCosts
{
    internal static CardKind ProgramBasicCardName(CardKind kind) => CardGame.Core.RuntimeSkills.RuntimeRules_Core_CommonRules.IsSlashCard(kind) ? CardKind.Slash : kind;
}
