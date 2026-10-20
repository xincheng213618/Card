namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_OwnTrickDrawTargets
{
    internal static int ActualTrickTargetCount(CardUseFrame use) => (use.CardKind == CardKind.BorrowedSword
        ? use.TargetSeats.Where((_, i) => i % 2 == 0) : use.TargetSeats).Distinct().Count();
}
