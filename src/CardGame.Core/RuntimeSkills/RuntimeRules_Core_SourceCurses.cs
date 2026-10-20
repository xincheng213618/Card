namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_SourceCurses
{
    internal static bool SameSourceCurse(SourceCurseDeposit a, SourceCurseDeposit b) =>
        (a with { ActualDrawCount = 0 }) == (b with { ActualDrawCount = 0 });
}
