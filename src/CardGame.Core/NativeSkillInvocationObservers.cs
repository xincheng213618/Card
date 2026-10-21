namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed record NativeFactionSkillSuspensionProjection(ResolutionFrame Parent,
        SkillInvocationFrame Invocation, int ParentIndex);

    private NativeFactionSkillSuspensionProjection? FindNativeFactionSkillInvocation(long requestOwnerId, int ownerSeat,
        FactionResponsePolicySource source, string occurrenceKey, SkillInvocationReturnKind returnKind)
    {
        for (var index = 0; index + 1 < _resolutionStack.Count; index++)
        {
            var parent = _resolutionStack[index];
            var sameOwner = parent.Id == requestOwnerId || parent is ResponseWindowFrame response &&
                response.ParentFrameId == requestOwnerId && index > 0 && _resolutionStack[index - 1].Id == requestOwnerId &&
                _resolutionStack[index - 1].Step == ResolutionFrameStep.AwaitingResponse;
            if (!sameOwner ||
                _resolutionStack[index + 1] is not SkillInvocationFrame f || f.ParentFrameId != parent.Id ||
                f.ActorSeat != ownerSeat || f.SkillKey != source.SkillId || f.SkillInstanceId != source.SkillInstanceId ||
                f.OccurrenceKey != occurrenceKey || f.ReturnKind != returnKind || !SkillInvocationStructuralEdge(parent, f) ||
                ProgramEventHistory<SkillInvocationStartedEvent>().Count(e => e == new SkillInvocationStartedEvent(f.Id, parent.Id,
                    ownerSeat, source.SkillId, occurrenceKey, source.SkillInstanceId, f.SkillName, f.GameplayHash,
                    f.RoundNumber, f.TurnNumber, f.TurnOwnerSeat, returnKind)) != 1 ||
                ProgramEventHistory<SkillInvocationCompletedEvent>().Any(e => e.FrameId == f.Id) ||
                !HasObserverSuffix(index, static (engine, child) => engine.NativeEquipmentObserverEdge(child),
                    static (engine, child, dying) => engine.IsSuspendedOrPaidRescueObserverSuffix(child, dying))) continue;
            return new(parent, f, index);
        }
        return null;
    }
    private bool HasFactionDefenseSkillInvocation(FactionDefenseHandle defense)
    {
        if (defense.Source.RequiredKind != CardKind.Dodge || defense.CandidateIndex < 0 || defense.CandidateIndex >= defense.CandidateSeats.Count ||
            !SameContinuationOwner(ActiveFactionDefense, defense) ||
            FindNativeFactionSkillInvocation(defense.OwnerFrameId, defense.OwnerSeat, defense.Source,
                defense.InvocationOccurrenceKey, SkillInvocationReturnKind.FactionDefense) is not { } projection ||
            projection.Parent is not ResponseWindowFrame { Step: ResolutionFrameStep.AwaitingResponse } response ||
            response.ParentFrameId != defense.OwnerFrameId || response.ResponderSeat != defense.OwnerSeat ||
            response.RequiredCardKind != CardKind.Dodge || response.IncomingCard != defense.Attack.EffectiveCardKind ||
            defense.OwnerSeat != defense.Attack.TargetSeat || !defense.Attack.FactionDefenseAttempted ||
            !SameAttackOwner(ActiveCardAttack, defense.Attack)) return false;
        return ProgramEventHistory<FactionDefenseRequestedEvent>().LastOrDefault(e => e.ResolutionId == defense.OwnerFrameId) is { } issued &&
            issued.OwnerSeat == defense.OwnerSeat && issued.SkillId == defense.Source.SkillId && issued.CandidateSeats.SequenceEqual(defense.CandidateSeats);
    }
    private bool HasFactionSlashSkillInvocation(FactionCardRequestHandle request)
    {
        if (request.IsProgramSkillUse || request.PolicySource is not { RequiredKind: CardKind.Slash } source ||
            source.FactionId != request.ProviderFactionId || request.RequiredKind != CardKind.Slash || !request.AwaitingProviders ||
            request.AwaitingZhuqueFanChoice || request.ActiveAttack is not null || request.CandidateIndex < 0 ||
            request.CandidateIndex >= request.CandidateSeats.Count || !SameContinuationOwner(ActiveFactionCardRequest, request) ||
            FindNativeFactionSkillInvocation(request.OwnerFrameId, request.OwnerSeat, source,
                request.InvocationOccurrenceKey, SkillInvocationReturnKind.FactionSlash) is not { } projection ||
            !NativeFactionSlashInvocationHostMatches(request, projection.Parent)) return false;
        return ProgramEventHistory<FactionSlashRequestedEvent>().LastOrDefault(e => e.ResolutionId == request.OwnerFrameId) is { } issued &&
            issued.OwnerSeat == request.OwnerSeat && issued.SkillId == source.SkillId && issued.CandidateSeats.SequenceEqual(request.CandidateSeats) &&
            issued.IsActiveUse == (request.IsBorrowedSwordUse || request.IsQinglongCrescentBladeUse) && issued.TargetSeat == request.TargetSeat;
    }
    private bool NativeFactionSlashInvocationHostMatches(FactionCardRequestHandle request, ResolutionFrame parent)
    {
        if (request.IsQinglongCrescentBladeUse)
            return parent is CardUseFrame { Step: ResolutionFrameStep.AwaitingResponse } use && use.Id == request.OwnerFrameId &&
                request.QinglongCrescentBlade is { } qinglong && qinglong.OwnerFrameId == use.Id && qinglong.FactionSlashAttempted &&
                SameContinuationOwner(ActiveQinglongCrescentBlade, qinglong) && SameAttackOwner(ActiveCardAttack, qinglong.Attack) &&
                qinglong.Attack.SourceSeat == request.OwnerSeat && qinglong.Attack.TargetSeat == request.TargetSeat;
        if (parent is not ResponseWindowFrame { Step: ResolutionFrameStep.AwaitingResponse } response ||
            response.ParentFrameId != request.OwnerFrameId || response.ResponderSeat != request.OwnerSeat || response.RequiredCardKind != CardKind.Slash)
            return false;
        if (request.IsBorrowedSwordUse)
            return response.IncomingCard == CardKind.BorrowedSword && request.BorrowedSword is { } borrowed &&
                borrowed.ResolutionId == request.OwnerFrameId && SameContinuationOwner(ActiveBorrowedSword, borrowed) &&
                !borrowed.AwaitingSlashChoice && borrowed.FactionSlashAttempted && borrowed.ActiveAttack is null &&
                borrowed.WeaponOwnerSeat == request.OwnerSeat && borrowed.SlashTargetSeat == request.TargetSeat;
        if (request.ResponseAttack is not { } attack || !SameAttackOwner(ActiveCardAttack, attack) ||
            attack.ResolutionId != request.OwnerFrameId || response.IncomingCard != attack.EffectiveCardKind) return false;
        return request.Purpose switch
        {
            FactionCardRequestPurpose.DuelResponse => ActiveDuel is { FactionSlashAttempted: true } duel &&
                SameAttackOwner(duel.Attack, attack) && duel.ResponderSeat == request.OwnerSeat && response.SourceSeat == duel.OpponentSeat &&
                response.IncomingCard == CardKind.Duel,
            FactionCardRequestPurpose.GroupResponse => ActiveGroupCard is { Effect: GroupCardEffect.ResponseAttack, RequiredCardKind: CardKind.Slash } group &&
                SameAttackOwner(group.CurrentAttack, attack) && attack.FactionSlashAttempted && request.OwnerSeat == attack.TargetSeat &&
                response.SourceSeat == group.SourceSeat,
            _ => false
        };
    }
    private bool HasNativeFactionSkillInvocation(long attackId) => ActiveCardAttack?.ResolutionId == attackId &&
        (ActiveFactionDefense is { } defense && defense.OwnerFrameId == attackId && HasFactionDefenseSkillInvocation(defense) ||
         ActiveFactionCardRequest is { } request &&
         (request.ResponseAttack?.ResolutionId == attackId || request.QinglongCrescentBlade?.OwnerFrameId == attackId) &&
         HasFactionSlashSkillInvocation(request));
}
