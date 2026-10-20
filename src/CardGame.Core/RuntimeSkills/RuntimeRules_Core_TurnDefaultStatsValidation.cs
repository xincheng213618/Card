namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_TurnDefaultStatsValidation
{
    internal static bool TurnDefaultStatDamageRequestMatches(IReadOnlyList<IGameEvent> history, long damageId,
        int source, int target, int amount, DamageNature nature, bool sourceLess)
    {
        if (damageId <= 0 || amount <= 0) return false;
        var requests = history.OfType<DamageRequestedEvent>().Where(e => e.ResolutionId == damageId).ToArray();
        return requests is [var request] && request.SourceSeat == source && request.TargetSeat == target &&
            request.Amount == amount && request.Nature == nature && request.SourceLess == sourceLess;
    }
}
