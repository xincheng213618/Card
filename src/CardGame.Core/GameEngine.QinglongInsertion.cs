namespace CardGame.Core;

public sealed partial class GameEngine
{
    // A supplied Slash may itself be dodged. Keep its previous request on its
    // owner while allowing the new Qinglong choice to ask for another supply.
    private void SuspendFactionRequestForQinglongChoice(CardAttackHandle attack)
    {
        if (ActiveFactionCardRequest is not { AwaitingProviders: false, ActiveAttack: { } supplied } request ||
            !SameAttackOwner(supplied, attack)) return;
        UpdateCardContinuations(attack.ResolutionId, state => state with
        { QinglongCrescentBlade = state.QinglongCrescentBlade! with { SuspendedFactionRequestOwnerId = request.OwnerFrameId } });
        ActiveFactionCardRequest = null;
    }

    private void RestoreFactionRequestAfterQinglongChoice(long ownerId)
    {
        if (GetCardContinuations(ownerId).QinglongCrescentBlade is not { SuspendedFactionRequestOwnerId: { } request } saved) return;
        UpdateCardContinuations(ownerId, state => state with
        { QinglongCrescentBlade = saved with { SuspendedFactionRequestOwnerId = null } });
        ActiveFactionCardRequest = new(this, request);
    }

    // The dodged target and the remaining target cursor stay on their original
    // use. No finished event, cleanup, or completed-use skill runs before the
    // inserted Slash and all of its own children have returned.
    private void SuspendContinuationForQinglongFollowup(long outerResolutionId,
        FactionCardRequestHandle? followupRequest = null)
    {
        if (ActiveCardAttack?.ResolutionId != outerResolutionId ||
            _resolutionStack.LastOrDefault() is not CardUseFrame original || original.Id != outerResolutionId ||
            original.Continuations.QinglongFollowup is { Active: true })
            throw new InvalidOperationException("A Qinglong insertion requires its exact unfinished dodged Slash.");

        var request = ActiveFactionCardRequest;
        var savedRequest = original.Continuations.QinglongCrescentBlade?.SuspendedFactionRequestOwnerId ??
            (request is not null && !SameContinuationOwner(request, followupRequest) ? request.OwnerFrameId : (long?)null);
        var suspension = new QinglongFollowupState(outerResolutionId, null,
            ActiveFangtianHalberd?.OwnerFrameId, EngineStatus.Running,
            BorrowedSwordOwnerId: ActiveBorrowedSword?.OwnerFrameId, FactionRequestOwnerId: savedRequest);
        UpdateCardContinuations(outerResolutionId, state => state with { QinglongFollowup = suspension,
            QinglongCrescentBlade = state.QinglongCrescentBlade is { } choice ? choice with { SuspendedFactionRequestOwnerId = null } : null });
        ActiveCardAttack = null;
        ActiveFangtianHalberd = null;
        ActiveBorrowedSword = null;
        if (request is not null && !SameContinuationOwner(request, followupRequest)) ActiveFactionCardRequest = null;
        ClearPendingDecision();
        _status = EngineStatus.Running;
    }

    private void BindQinglongInsertedUse(CardUseFrame child)
    {
        var index = _resolutionStack.FindIndex(frame => frame.Id == child.Id);
        if (index < 1 || _resolutionStack[index - 1] is not CardUseFrame original ||
            original.Continuations.QinglongFollowup is not { Active: true, FollowupUseId: null } suspension)
            return;
        if (original.Action is not { } action || child.Action?.ParentActionId != action.ActionId ||
            child.SourceSeat != original.SourceSeat || !IsSlashCard(child.CardKind) ||
            original.CardAttack is not { } attack || !child.Action.TargetSeats.SequenceEqual([attack.TargetSeat]))
            throw new InvalidOperationException("A Qinglong inserted use changed its parent, actor, or exact dodging target.");
        UpdateCardContinuations(original.Id, state => state with
        { QinglongFollowup = suspension with { FollowupUseId = child.Id } });
    }

    private bool RestoreContinuationAfterQinglongFollowup(long completedOwnerId)
    {
        if (_resolutionStack.LastOrDefault() is not CardUseFrame original ||
            original.Continuations.QinglongFollowup is not { Active: true } suspension ||
            suspension.FollowupUseId != completedOwnerId)
            return false;

        // Schedule the parent, rather than recursively finishing a whole chain
        // inside this child's native return. One engine step retires one parent.
        UpdateCardContinuations(original.Id, state => state with
        { QinglongFollowup = suspension with { ChildCompleted = true } });
        ClearPendingDecision();
        _status = EngineStatus.Running;
        return true;
    }

    [ProgramRuntimeHook(ProgramRuntimePhase.BeforeDeclaration, 20)]
    private bool TryResumeQinglongInsertion()
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not CardUseFrame original ||
            original.Continuations.QinglongFollowup is not { Active: true, ChildCompleted: true } suspension)
            return false;
        UpdateCardContinuations(original.Id, state => state with
        { QinglongFollowup = suspension with { Active = false } });
        if (suspension.FangtianOwnerId is { } fangtian) ActiveFangtianHalberd = new(this, fangtian);
        if (suspension.BorrowedSwordOwnerId is { } borrowed) ActiveBorrowedSword = new(this, borrowed);
        if (suspension.FactionRequestOwnerId is { } request) ActiveFactionCardRequest = new(this, request);
        var attack = new CardAttackHandle(this, original.Id);
        ActiveCardAttack = attack;
        CompleteAttack(attack);
        AdvanceRulesAndPublishState();
        return true;
    }

    private HashSet<int> QinglongAncestorProcessing(long attackId)
    {
        var index = _resolutionStack.FindIndex(frame => frame.Id == attackId);
        return _resolutionStack.Take(Math.Max(0, index)).OfType<CardUseFrame>()
            .Where(use => use.Continuations.QinglongFollowup is { Active: true })
            .SelectMany(use => use.PhysicalCardIds ?? [use.CardId]).Where(id => id != 0).ToHashSet();
    }

    private void AssertQinglongInsertions()
    {
        foreach (var original in _resolutionStack.OfType<CardUseFrame>())
        {
            if (original.Continuations.QinglongFollowup is not { Active: true } suspension) continue;
            if (original.Step == ResolutionFrameStep.Completed || original.CardAttack is not { Active: false } attack ||
                !IsSlashCard(original.CardKind) || suspension.NextAttackOwnerId != original.Id ||
                attack.SuccessfulDodgeResponses != attack.RequiredDodgeResponses || suspension.Decision is not null ||
                suspension.FollowupUseId is not { } childId)
                throw new InvalidOperationException("A Qinglong insertion lost its unfinished dodged parent.");
            var child = _resolutionStack.OfType<CardUseFrame>().SingleOrDefault(use => use.Id == childId);
            var paidSettlement = HasBoundaryLiuFengQinglongSettlementRide(original, childId);
            var matchingChild = suspension.ChildCompleted
                ? child is null && (_resolutionStack.LastOrDefault()?.Id == original.Id || paidSettlement)
                : child is null ? paidSettlement : child.Action is { } childAction && childAction.ParentActionId == original.Action?.ActionId &&
                  child.SourceSeat == original.SourceSeat && IsSlashCard(child.CardKind) &&
                  childAction.TargetSeats.SequenceEqual([attack.TargetSeat]);
            if (!matchingChild)
                throw new InvalidOperationException("A Qinglong insertion returned through a different child use.");
            if (CompleteProgramEventHistory().OfType<CardUseFinishedEvent>().Any(fact => fact.ResolutionId == original.Id))
                throw new InvalidOperationException("A suspended Qinglong parent finished before its inserted Slash.");
        }
    }
}
