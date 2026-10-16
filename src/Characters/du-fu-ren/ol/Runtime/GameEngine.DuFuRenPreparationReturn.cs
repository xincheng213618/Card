namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool TryReturnDuFuRenPaidPreparation(ProgramLifecycleTriggerWindowFrame frame)
    {
        if (!HasDuFuRenShunshi || frame.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow ||
            frame.Continuation != ProgramLifecycleContinuation.NormalTurnStart || frame.OwnerSeat != _currentSeat ||
            frame.OwnerSeat != _turnProgression.OwnerSeat || _turnProgression.TurnNumber != _turnNumber ||
            !IsValidPlayerSeat(frame.OwnerSeat) || _winner == Winner.None && _players[frame.OwnerSeat].IsAlive ||
            frame.CandidateIndex != frame.Candidates.Count || _resolutionStack.Count != 0) return false;
        var paid = ProgramEventHistory<DuFuRenStartedEvent>().Where(start => start.WindowId == frame.Id &&
            start.Stage == DuFuRenStage.GiftChoice && start.Turn == _turnNumber && start.TurnOwner == frame.OwnerSeat &&
            start.Source.OwnerSeat == frame.OwnerSeat && start.DamageFrameId is null && start.BatchId is null &&
            frame.Candidates.Any(c => c.OwnerSeat == start.Source.OwnerSeat && c.SkillId == start.Source.SkillId &&
                c.BindingId == start.Source.BindingId && c.SkillInstanceId == start.Source.SkillInstanceId && c.GameplayHash == start.GameplayHash &&
                GetProgramTrigger(c).Effects is [{ Op: SkillProgramEffectOp.GiveForNextActualPhases }]) &&
            ProgramEventHistory<DuFuRenCompletedEvent>().Count(e => e.FrameId == start.FrameId && e.Applied) == 1 &&
            ProgramEventHistory<DuFuRenGiftIssuedEvent>().Count(g => g.FrameId == start.FrameId && g.Source == start.Source &&
                g.GameplayHash == start.GameplayHash && g.Turn == start.Turn && g.TurnOwner == start.TurnOwner &&
                g.From.OwnerSeat == frame.OwnerSeat && g.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment &&
                _cardMovements.Count(m => m.Sequence == g.MovementSequence && m.CardId == g.CardId && m.From == g.From &&
                    m.To == CardLocation.Hand(g.RecipientSeat) && m.Reason.Value == DuFuRenGiftReason) == 1) == 1).ToArray();
        if (paid.Length == 0) return false;
        if (ProgramEventHistory<TurnEndedEvent>().Any(e => e.TurnNumber == _turnNumber && e.ActorSeat == frame.OwnerSeat))
            throw new InvalidOperationException("A paid Shunshi preparation cannot finalize an already ended actual turn twice.");
        // The paid native subtree has fully returned and this exact preparation
        // window has been popped. No Draw or Ending is invented for the dead
        // actor. The existing finalizer owns expiry, deferred end work and the
        // next living actor when the game itself has not ended.
        ClearActualDiscardRecoveryPhase();
        if (_winner == Winner.None) FinalizeEndTurn(_players[frame.OwnerSeat]);
        return true;
    }
}
