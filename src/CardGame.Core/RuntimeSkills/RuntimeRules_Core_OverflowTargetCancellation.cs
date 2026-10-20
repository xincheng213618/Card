namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_OverflowTargetCancellation
{
    internal static IReadOnlyList<int> OverflowPrimaryTargets(CardKind kind, IReadOnlyList<int> targets) =>
        kind == CardKind.BorrowedSword ? Array.AsReadOnly(targets.Where((_, index) => index % 2 == 0).ToArray()) : targets;

    internal static IReadOnlyList<int> OverflowRetainedTargets(CardKind kind, int owner, IReadOnlyList<int> targets) =>
        Array.AsReadOnly(kind == CardKind.BorrowedSword
            ? targets.Chunk(2).Where(pair => pair.Length == 2 && pair[0] == owner).SelectMany(pair => pair).ToArray()
            : targets.Where(target => target == owner).ToArray());
}
