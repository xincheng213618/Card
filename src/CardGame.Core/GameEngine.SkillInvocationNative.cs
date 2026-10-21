namespace CardGame.Core;

public sealed record NativeFactionSkillInvocationCanceledEvent(long InvocationId, long ParentFrameId,
    long RequestOwnerFrameId, SkillInvocationReturnKind ReturnKind, int ActorSeat, string SkillKey,
    string SkillInstanceId, string OccurrenceKey) : IGameEvent;

public sealed partial class GameEngine
{
    private void ResumeNativeSkillInvocation(SkillInvocationFrame frame)
    {
        if (frame.TurnNumber != _turnNumber || frame.TurnOwnerSeat != _currentSeat ||
            frame.ParentFrameId != 0 && _resolutionStack.LastOrDefault()?.Id != frame.ParentFrameId)
            throw new InvalidOperationException("A skill invocation lost its original native turn or source frame.");
        switch (frame.ReturnKind)
        {
            case SkillInvocationReturnKind.RuntimeFrame:
                AdvanceRuntimeFrame(frame.ParentFrameId);
                return;
            case SkillInvocationReturnKind.SkipDiscard:
                if (frame.ParentFrameId != 0 || frame.ActorSeat != _currentSeat || _phase != TurnPhase.Discard ||
                    !ProgramEventHistory<ProgramCardPolicyResolvedEvent>().Any(e =>
                        e.OwnerSeat == frame.ActorSeat && e.SkillId == frame.SkillKey && e.Applied))
                    throw new InvalidOperationException("A discard policy invocation lost its accepted native choice.");
                if (_winner == Winner.None) EndTurn();
                return;
            case SkillInvocationReturnKind.FactionDefense:
                if (ActiveFactionDefense is not { } defense || defense.OwnerSeat != frame.ActorSeat ||
                    defense.Source.SkillId != frame.SkillKey || defense.Source.SkillInstanceId != frame.SkillInstanceId ||
                    defense.InvocationOccurrenceKey != frame.OccurrenceKey)
                    throw new InvalidOperationException("A faction defense invocation lost its exact provider request.");
                ValidateNativeFactionInvocationReturn(frame, defense.OwnerFrameId);
                if (!_players[frame.ActorSeat].IsAlive || _winner != Winner.None)
                {
                    RecordNativeFactionInvocationCancellation(frame, defense.OwnerFrameId);
                    CancelInvokedFactionDefense(defense);
                    return;
                }
                AdvanceFactionDefenseCandidate();
                return;
            case SkillInvocationReturnKind.FactionSlash:
                if (ActiveFactionCardRequest is not { AwaitingProviders: true } slash || slash.OwnerSeat != frame.ActorSeat ||
                    slash.PolicySource is not { } source || source.SkillId != frame.SkillKey || source.SkillInstanceId != frame.SkillInstanceId ||
                    slash.InvocationOccurrenceKey != frame.OccurrenceKey || slash.IsProgramSkillUse)
                    throw new InvalidOperationException("A faction Slash invocation lost its exact provider request.");
                ValidateNativeFactionInvocationReturn(frame, slash.OwnerFrameId);
                if (!_players[frame.ActorSeat].IsAlive || _winner != Winner.None)
                {
                    RecordNativeFactionInvocationCancellation(frame, slash.OwnerFrameId);
                    CancelInvokedFactionSlash(slash);
                    return;
                }
                AdvanceFactionSlashCandidate();
                return;
            case SkillInvocationReturnKind.NativeEquipment:
                ContinueNativeEquipmentInvocation(frame);
                return;
            default:
                throw new InvalidOperationException("The skill invocation has no native return contract.");
        }
    }
    private void ValidateNativeFactionInvocationReturn(SkillInvocationFrame frame, long requestOwnerId)
    {
        if (_pendingDecision is not null || _resolutionStack.LastOrDefault() is not { } parent || parent.Id != frame.ParentFrameId ||
            parent.Id != requestOwnerId && (parent is not ResponseWindowFrame response || response.ParentFrameId != requestOwnerId) ||
            parent.SkillInvocations?.Issued.Contains(InvocationSourceKey(frame.ActorSeat, frame.SkillKey, frame.OccurrenceKey), StringComparer.Ordinal) != true ||
            ProgramEventHistory<SkillInvocationStartedEvent>().Count(e => e == new SkillInvocationStartedEvent(frame.Id, frame.ParentFrameId,
                frame.ActorSeat, frame.SkillKey, frame.OccurrenceKey, frame.SkillInstanceId, frame.SkillName, frame.GameplayHash,
                frame.RoundNumber, frame.TurnNumber, frame.TurnOwnerSeat, frame.ReturnKind)) != 1 ||
            ProgramEventHistory<SkillInvocationCompletedEvent>().Count(e => e.FrameId == frame.Id && e.ParentFrameId == frame.ParentFrameId &&
                e.OccurrenceKey == frame.OccurrenceKey) != 1 ||
            ProgramEventHistory<NativeFactionSkillInvocationCanceledEvent>().Any(e => e.InvocationId == frame.Id))
            throw new InvalidOperationException("A native faction activation lost its original accepted source and exact completed typed return.");
    }
    private void RecordNativeFactionInvocationCancellation(SkillInvocationFrame frame, long requestOwnerId) =>
        AdvanceEventRulesAndQueueFact(new NativeFactionSkillInvocationCanceledEvent(frame.Id, frame.ParentFrameId, requestOwnerId,
            frame.ReturnKind, frame.ActorSeat, frame.SkillKey, frame.SkillInstanceId, frame.OccurrenceKey));
    private void CancelInvokedFactionDefense(FactionDefenseHandle defense)
    {
        var attack = defense.Attack;
        ActiveFactionDefense = null;
        AdvanceEventRulesAndQueueFact(new FactionDefenseResolvedEvent(defense.OwnerFrameId, defense.OwnerSeat, false, null, null));
        FinishCanceledNativeFactionResponse(attack);
    }
    private void CancelInvokedFactionSlash(FactionCardRequestHandle request)
    {
        var purpose = request.Purpose;
        var attack = request.ResponseAttack;
        var borrowed = request.BorrowedSword;
        var qinglong = request.QinglongCrescentBlade;
        ActiveFactionCardRequest = null;
        AdvanceEventRulesAndQueueFact(new FactionSlashResolvedEvent(request.ResolutionId, request.OwnerSeat, false, null, null, null,
            request.IsBorrowedSwordUse || request.IsQinglongCrescentBladeUse, request.TargetSeat));
        switch (purpose)
        {
            case FactionCardRequestPurpose.BorrowedSwordUse:
                // The native dead-weapon-owner branch also finishes without a
                // weapon transfer or a newly published owner Slash decision.
                CompleteBorrowedSwordWithoutSlash(borrowed ?? throw new InvalidOperationException("Canceled faction use lost Borrowed Sword."), transferWeapon: false);
                return;
            case FactionCardRequestPurpose.QinglongCrescentBladeUse:
            {
                var original = qinglong?.Attack ?? throw new InvalidOperationException("Canceled faction use lost Qinglong.");
                ActiveQinglongCrescentBlade = null;
                SetCardUseStep(original.ResolutionId, ResolutionFrameStep.ResolvingEffect);
                AdvanceEventRulesAndQueueFact(new QinglongCrescentBladeResolvedEvent(original.ResolutionId,
                    original.SourceSeat, original.TargetSeat, false, [], null));
                RestoreFactionRequestAfterQinglongChoice(original.ResolutionId);
                CompleteAttack(original);
                return;
            }
            case FactionCardRequestPurpose.DuelResponse:
            case FactionCardRequestPurpose.GroupResponse:
                FinishCanceledNativeFactionResponse(attack ?? throw new InvalidOperationException("Canceled faction response lost its attack."));
                return;
            default:
                throw new InvalidOperationException("No native cancellation contract exists for this faction request purpose.");
        }
    }
    private void FinishCanceledNativeFactionResponse(CardAttackHandle attack)
    {
        if (_resolutionStack.LastOrDefault() is not ResponseWindowFrame response || response.ParentFrameId != attack.ResolutionId ||
            !SameAttackOwner(ActiveCardAttack, attack))
            throw new InvalidOperationException("Canceled native faction response lost its original response host.");
        PopResponseWindow(attack.ResolutionId);
        SetResponseParentStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        ClearPendingDecision();
        // Native completion skips this unavailable target and retains any real
        // group/halberd cursor and typed card-use parent return.
        CompleteAttack(attack);
    }
}
