namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_yan_jun_ol_Guanchao_Programs
{
    internal static bool MatchesGuanchaoPattern(IReadOnlyList<int?> ranks, bool ascending)
    {
        int? previous = null;
        foreach (var rank in ranks)
        {
            if (rank is not { } value) return false;
            if (previous is { } before && (ascending ? before >= value : before <= value)) return false;
            previous = value;
        }
        return true;
    }
}
