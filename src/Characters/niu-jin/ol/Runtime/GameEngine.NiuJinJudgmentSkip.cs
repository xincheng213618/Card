namespace CardGame.Core;

public sealed record NiuJinJudgmentSkipConsumedEvent(
    int OwnerSeat, string SkillInstanceId, int ActualTurnNumber, bool AlreadySkipped) : IGameEvent;

public sealed partial class GameEngine
{
    [ProgramRuntimeHook(ProgramRuntimePhase.BeforeJudgmentPhase, 10)]
    private bool ConsumeNiuJinJudgmentSkip()
    {
        // This is an already issued next-phase obligation. A later source loss
        // cannot defer it until that skill is acquired again. A face-down whole
        // turn skips this boundary, so the debt waits for the next real turn.
        var debts = _programBooleanStates.Where(pair => pair.Value && pair.Key.OwnerSeat == _currentSeat &&
            pair.Key.SkillId == "ol:cuorui" && pair.Key.StateId == "skip-next-judgment")
            .Select(pair => pair.Key).ToArray();
        foreach (var debt in debts)
        {
            var alreadySkipped = _pendingTurnDelayedEffects.HasFlag(DelayedTurnEffects.SkipJudgmentPhase);
            _pendingTurnDelayedEffects |= DelayedTurnEffects.SkipJudgmentPhase;
            SetProgramBooleanState(debt.OwnerSeat, debt.SkillId, debt.SkillInstanceId, debt.StateId, false);
            AdvanceEventRulesAndQueueFact(new NiuJinJudgmentSkipConsumedEvent(
                debt.OwnerSeat, debt.SkillInstanceId, _turnNumber, alreadySkipped));
        }
        // The native turn transition still owns progression and publication.
        return false;
    }
}
