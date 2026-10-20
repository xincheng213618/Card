namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static (string Id, string Name) ZhouQunWeather(Suit suit) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_zhou_qun_ol_ZhouQunWeather.ZhouQunWeather(suit);
    private bool TryReturnZhouQunPaidPreparation(ProgramLifecycleTriggerWindowFrame frame)
    {
        if (!HasZhouQunCapability || frame.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow || frame.Continuation != ProgramLifecycleContinuation.NormalTurnStart || frame.OwnerSeat != _currentSeat || frame.OwnerSeat != _turnProgression.OwnerSeat || frame.CandidateIndex != frame.Candidates.Count || _resolutionStack.Count != 0 || _winner == Winner.None && _players[frame.OwnerSeat].IsAlive) return false;
        if (!ProgramEventHistory<ZhouQunStartedEvent>().Any(e => e.WindowId == frame.Id && e.Source.OwnerSeat == frame.OwnerSeat && e.ActualTurn == _turnNumber && e.TurnOwner == frame.OwnerSeat && e.Op == SkillProgramEffectOp.ResolveChenshuoRepeatedReveal && frame.Candidates.Any(c => c.OwnerSeat == e.Source.OwnerSeat && c.SkillId == e.Source.SkillId && c.BindingId == e.Source.BindingId && c.SkillInstanceId == e.Source.SkillInstanceId && c.GameplayHash == e.GameplayHash) && ProgramEventHistory<ZhouQunCompletedEvent>().Count(done => done.FrameId == e.FrameId && done.Applied) == 1)) return false;
        if (ProgramEventHistory<TurnEndedEvent>().Any(e => e.TurnNumber == _turnNumber && e.ActorSeat == frame.OwnerSeat)) throw new InvalidOperationException("Chenshuo cannot finalize the same dead actual turn twice.");
        ClearActualDiscardRecoveryPhase(); if (_winner == Winner.None) FinalizeEndTurn(_players[frame.OwnerSeat]); return true;
    }
}
