namespace CardGame.Core;

public sealed partial class GameEngine
{
    private CardUseFrame? LifecycleCardUse(long frameId) =>
        _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(frame => frame.Id == frameId);

    private void UpdateLifecycleCardUse(long frameId, Func<CardUseFrame, CardUseFrame> update)
    {
        var frame = LifecycleCardUse(frameId) ?? throw new InvalidOperationException("The card-use lifecycle owner is missing.");
        ReplaceRuntimeFrame(frameId, update(frame));
    }

    private bool TryMarkProgramUseAccepted(long frameId)
    {
        var frame = LifecycleCardUse(frameId) ?? throw new InvalidOperationException("The card-use lifecycle owner is missing.");
        if (frame.ProgramUseAccepted) return false;
        ReplaceRuntimeFrame(frameId, frame with { ProgramUseAccepted = true });
        return true;
    }

    private bool TryMarkProgramUseCommitted(long frameId)
    {
        var frame = LifecycleCardUse(frameId) ?? throw new InvalidOperationException("The card-use lifecycle owner is missing.");
        if (frame.ProgramUseCommitted) return false;
        ReplaceRuntimeFrame(frameId, frame with { ProgramUseCommitted = true });
        return true;
    }

    private bool TryMarkFinalizedTrickProgramsStarted(long frameId)
    {
        var frame = LifecycleCardUse(frameId) ?? throw new InvalidOperationException("The card-use lifecycle owner is missing.");
        if (frame.FinalizedTrickProgramsStarted) return false;
        ReplaceRuntimeFrame(frameId, frame with { FinalizedTrickProgramsStarted = true });
        return true;
    }

    private bool TryMarkFinalizedSimpleProgramsStarted(long frameId)
    {
        var frame = LifecycleCardUse(frameId) ?? throw new InvalidOperationException("The card-use lifecycle owner is missing.");
        if (frame.FinalizedSimpleProgramsStarted) return false;
        ReplaceRuntimeFrame(frameId, frame with { FinalizedSimpleProgramsStarted = true });
        return true;
    }

    private bool TryMarkTargetsAdjusted(long frameId)
    {
        var frame = LifecycleCardUse(frameId) ?? throw new InvalidOperationException("The card-use lifecycle owner is missing.");
        if (frame.TargetsAdjusted) return false;
        ReplaceRuntimeFrame(frameId, frame with { TargetsAdjusted = true });
        return true;
    }

    private bool TryMarkUnlimitedUse(long frameId)
    {
        var frame = LifecycleCardUse(frameId) ?? throw new InvalidOperationException("The card-use lifecycle owner is missing.");
        if (frame.UnlimitedUse) return false;
        ReplaceRuntimeFrame(frameId, frame with { UnlimitedUse = true });
        return true;
    }

    private bool TryMarkYingboUnrespondable(long frameId)
    {
        var frame = LifecycleCardUse(frameId) ?? throw new InvalidOperationException("The card-use lifecycle owner is missing.");
        if (frame.YingboUnrespondable) return false;
        ReplaceRuntimeFrame(frameId, frame with { YingboUnrespondable = true });
        return true;
    }

    private bool TryMarkYingboRepeated(long frameId)
    {
        var frame = LifecycleCardUse(frameId) ?? throw new InvalidOperationException("The card-use lifecycle owner is missing.");
        if (frame.YingboRepeated) return false;
        ReplaceRuntimeFrame(frameId, frame with { YingboRepeated = true });
        return true;
    }

    private bool TryBeginCommittedCardUse(long frameId, ProgramCardContinuation continuation,
        ProgramTrickContinuation? trick = null, ProgramSimpleCardContinuation? simple = null)
    {
        var frame = _resolutionStack.OfType<CardUseFrame>().Single(item => item.Id == frameId);
        if (frame.Action is not { } action || !TryMarkProgramUseCommitted(frameId)) return false;
        return TryBeginProgramCardWindow(null, action, SkillProgramTriggerWindow.CardUseCommitted,
            action.TargetSeats, continuation, trickContinuation: trick, simpleContinuation: simple);
    }

    private void ContinueCommittedTrickUse(ProgramCardTriggerWindowFrame frame)
    {
        var continuation = frame.TrickContinuation ??
            throw new InvalidOperationException("The committed trick lost its continuation.");
        var card = GetTrickRepresentation(frame.ParentFrameId, continuation.EffectCardId, requireProcessing: true);
        var action = _resolutionStack.OfType<CardUseFrame>().Single(use => use.Id == frame.ParentFrameId).Action!;
        BeginJizhiOrNullificationWindow(frame.ParentFrameId, card, action.ActorSeat,
            action.TargetSeats, continuation.ActionKind, continuation.TargetCardId,
            continuation.RequiredCardKind, action.EffectiveKind);
    }

    private void BeginSimpleCardUse(long frameId, ProgramSimpleCardContinuation continuation)
    {
        var use = _resolutionStack.OfType<CardUseFrame>().Single(frame => frame.Id == frameId);
        if (TryPauseRecoveryPaidCardUse(frameId, new(RecoveryPaidCardUseKind.Simple, use.SourceSeat, continuation.CardId, Simple: continuation))) return;
        if ((LifecycleCardUse(frameId)?.TargetsAdjusted == true) && use.TargetSeats.Count > 1 &&
            continuation.Effect is SimpleCardUseEffect.Recovery or SimpleCardUseEffect.Alcohol)
            UpdateLifecycleCardUse(frameId, frame => frame with { AdjustedSimpleContinuation = continuation with
                { RecoveryPolicySources = continuation.RecoveryPolicySources is { } policies ? Array.AsReadOnly(policies.ToArray()) : null } });
        if (!TryBeginCommittedCardUse(frameId, ProgramCardContinuation.CommittedSimpleCard,
                simple: continuation))
            ContinueSimpleCardUse(frameId, continuation);
    }

    private void ContinueSimpleCardUse(long frameId, ProgramSimpleCardContinuation continuation)
    {
        if (TryBeginFinalizedSimpleCardPrograms(frameId, continuation)) return;
        var frame = _resolutionStack.OfType<CardUseFrame>().Single(item => item.Id == frameId);
        if (ContinueDyingSuitsPeach(frameId, continuation)) return;
        if (ContinueTieredRoundZeroSimpleUse(frameId, continuation)) return;
        if (ContinueVirtualBasicEffect(frameId, continuation)) return;
        var card = _cardZones.CardsAt(_cardZones.GetLocation(continuation.CardId)).Single(item => item.Id == continuation.CardId);
        var source = _players[frame.SourceSeat];
        SetCardUseStep(frameId, ResolutionFrameStep.ResolvingEffect);
        if (!source.IsAlive)
        {
            FinishCardUse(frameId, card, frame.CardKind);
            return;
        }
        switch (continuation.Effect)
        {
            case SimpleCardUseEffect.EquipmentPlacement:
                CompleteEquipmentUse(source, card, frameId);
                break;
            case SimpleCardUseEffect.Equipment:
                FinishCardUse(frameId, card);
                break;
            case SimpleCardUseEffect.Alcohol:
                CompleteAlcoholUse(source, card, frameId);
                break;
            case SimpleCardUseEffect.Recovery:
                CompleteRecoveryCardUse(source, _players[frame.TargetSeats[frame.TargetIndex]], card,
                    frameId, frame.CardKind, continuation.RecoveryAmount,
                    continuation.RecoveryPolicySources ?? []);
                break;
            default:
                throw new InvalidOperationException("Unsupported simple card effect.");
        }
    }
}
