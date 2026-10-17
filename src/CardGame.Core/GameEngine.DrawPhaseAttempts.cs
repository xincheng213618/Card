namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TryBeginNormalDrawAttemptPhase(CharacterState owner, DelayedTurnEffects delayed,
        bool afterStartingWindow = false, bool replaced = false, int adjustment = 0, int? frozenBase = null)
    {
        if (!HasNativeDrawAttemptCapability || _resolutionStack.Count != 0 || _pendingDecision is not null ||
            !owner.IsAlive || owner.Seat != _currentSeat || _phase != TurnPhase.Draw ||
            delayed.HasFlag(DelayedTurnEffects.SkipDrawPhase) || _winner != Winner.None)
            return false;
        var frame = new DrawPhaseObligationFrame(++_resolutionSequence, owner.Seat, _turnNumber, false,
            (int)delayed, afterStartingWindow ? DrawPhaseObligationStage.Drawing : DrawPhaseObligationStage.Starting,
            NormalDrawReplaced: replaced, DrawAdjustment: adjustment, FrozenBaseDrawCount: frozenBase)
            { DiscardRecoveryPhase = CurrentActualDiscardRecoveryPhase() };
        PushRuntimeFrame(frame); AdvanceRuntimeFrame(frame.Id); return true;
    }

    private void BeginOwnedDrawAttempt(DrawPhaseObligationFrame frame, int requested)
    {
        if (!HasNativeDrawAttemptCapability || frame.Stage != DrawPhaseObligationStage.Drawing ||
            frame.NativeDraw is not null || requested < 0 || frame.NormalDrawReplaced)
            throw new InvalidOperationException("An actual draw phase cannot issue an unrelated draw request.");
        var reason = frame.IsExtra ? new CardMoveReason("program.extra-draw-phase.draw") : CardMoveReasons.Draw;
        ReplaceRuntimeTop(frame with { Stage = DrawPhaseObligationStage.RequestingDraw,
            NativeDraw = new NativeDrawProducerReceipt(0, requested, null, SkillProgramCardSetVisibility.Private, reason)
            { Recipients = [frame.OwnerSeat], ReturnKind = NativeDrawAttemptReturnKind.DrawPhase } });
    }

    // Like program producers, the real phase waits for its exact pre-attempt
    // return. Stolen replacement entities are never its actual DrawCount.
    private bool ContinueOwnedDrawAttempt(DrawPhaseObligationFrame frame)
    {
        var paid = frame.NativeDraw ?? throw new InvalidOperationException("An actual draw phase lost its request.");
        if (paid.ActiveAttemptId is not null && !paid.AttemptReturned)
            throw new InvalidOperationException("An actual draw phase resumed before its exact request child returned.");
        if (!paid.AttemptReturned && TryBeginNativeDrawAttempt(frame)) return true;
        var drawn = paid.Replacement is null && _players[frame.OwnerSeat].IsAlive && _winner == Winner.None
            ? DrawCards(_players[frame.OwnerSeat], paid.RequestedCount, true, paid.Reason)
            : Array.AsReadOnly(Array.Empty<int>());
        var active = (DrawPhaseObligationFrame)_resolutionStack.Last();
        ReplaceRuntimeTop(active with { NativeDraw = null, DrawCount = drawn.Count,
            Stage = DrawPhaseObligationStage.DrawChildren });
        return false;
    }

    private void AssertDrawPhaseAttemptProducer(DrawPhaseObligationFrame frame)
    {
        if (frame.NativeDraw is not { } paid)
        {
            if (frame.Stage == DrawPhaseObligationStage.RequestingDraw)
                throw new InvalidOperationException("A requesting Draw phase has no owning producer receipt.");
            return;
        }
        if (!HasNativeDrawAttemptCapability || frame.Stage != DrawPhaseObligationStage.RequestingDraw ||
            paid.ReturnKind != NativeDrawAttemptReturnKind.DrawPhase || paid.InstructionIndex != 0 ||
            paid.Operation is not null || paid.Recipients is not System.Collections.IList { IsReadOnly: true } ||
            paid.Recipients is not [var recipient] || recipient != frame.OwnerSeat || paid.RecipientIndex != 0 ||
            paid.RequestedCount < 0 || paid.ResultBind is not null || paid.RecipientApplied ||
            paid.Reason != (frame.IsExtra ? new CardMoveReason("program.extra-draw-phase.draw") : CardMoveReasons.Draw) ||
            paid.ActiveAttemptId is null && (paid.AttemptReturned || paid.Replacement is not null) ||
            paid.Replacement is not null && !paid.AttemptReturned)
            throw new InvalidOperationException("An actual Draw phase lost its exact requested count or return contract.");
    }
}
