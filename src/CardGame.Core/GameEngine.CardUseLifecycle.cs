namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TryBeginCommittedCardUse(long frameId, ProgramCardContinuation continuation,
        ProgramTrickContinuation? trick = null, ProgramSimpleCardContinuation? simple = null)
    {
        var frame = _resolutionStack.OfType<CardUseFrame>().Single(item => item.Id == frameId);
        if (frame.Action is not { } action || !_committedProgramUses.Add(frameId)) return false;
        return TryBeginProgramCardWindow(null, action, SkillProgramTriggerWindow.CardUseCommitted,
            action.TargetSeats, continuation, trickContinuation: trick, simpleContinuation: simple);
    }

    private void ContinueCommittedTrickUse(ProgramCardTriggerWindowFrame frame)
    {
        var continuation = frame.TrickContinuation ??
            throw new InvalidOperationException("The committed trick lost its continuation.");
        var card = _cardZones.CardsAt(CardLocation.Processing)
            .Single(item => item.Id == continuation.EffectCardId);
        var action = _resolutionStack.OfType<CardUseFrame>().Single(use => use.Id == frame.ParentFrameId).Action!;
        BeginJizhiOrNullificationWindow(frame.ParentFrameId, card, action.ActorSeat,
            action.TargetSeats, continuation.ActionKind, continuation.TargetCardId,
            continuation.RequiredCardKind, action.EffectiveKind);
    }

    private void BeginSimpleCardUse(long frameId, ProgramSimpleCardContinuation continuation)
    {
        var use = _resolutionStack.OfType<CardUseFrame>().Single(frame => frame.Id == frameId);
        if (_adjustedTargetCardUses.Contains(frameId) && use.TargetSeats.Count > 1 &&
            continuation.Effect is SimpleCardUseEffect.Recovery or SimpleCardUseEffect.Alcohol)
            _adjustedSimpleCardContinuations[frameId] = continuation;
        if (!TryBeginCommittedCardUse(frameId, ProgramCardContinuation.CommittedSimpleCard,
                simple: continuation))
            ContinueSimpleCardUse(frameId, continuation);
    }

    private void ContinueSimpleCardUse(long frameId, ProgramSimpleCardContinuation continuation)
    {
        var frame = _resolutionStack.OfType<CardUseFrame>().Single(item => item.Id == frameId);
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
