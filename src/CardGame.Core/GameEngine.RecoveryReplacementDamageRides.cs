namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool RecoveryReplacementFrameRidesOn(ResolutionFrame ride, ResolutionFrame beneath) =>
        ride is RecoveryReplacementFrame recovery && recovery.ParentFrameId == beneath.Id &&
        recovery.Return.ResumeFrameId == beneath.Id && recovery.Return.Continuation switch
        {
            PostEventContinuation.Program or PostEventContinuation.AwaitedProgramMovement => beneath is ProgramSkillFrame,
            PostEventContinuation.FriendDuelResponse => beneath is FriendDuelResponseFrame,
            PostEventContinuation.GuanduCardEffect => beneath is CardUseFrame guandu && IsGuanduCardEffectRecoveryReturn(guandu, recovery),
            PostEventContinuation.WumiaoFlood => beneath is CardUseFrame flood && IsWumiaoFloodRecoveryReturn(flood, recovery),
            PostEventContinuation.DragonBoatCardEffect => beneath is CardUseFrame dragon && IsDragonBoatCardEffectRecoveryReturn(dragon, recovery),
            PostEventContinuation.CardUse or PostEventContinuation.GroupRecovery or
                PostEventContinuation.VirtualBasicCardUse or PostEventContinuation.RecoveryProducer => beneath is CardUseFrame,
            PostEventContinuation.DenglouCardUse => beneath is CardUseFrame { DenglouNative: not null, DenglouCostDrained: false },
            PostEventContinuation.GuoSiCardUse => beneath is CardUseFrame { GuoSiNative: not null, GuoSiCostDrained: false },
            PostEventContinuation.RecoveryPaidCardUse => beneath is CardUseFrame { RecoveryPaidContinuation: not null },
            PostEventContinuation.FactionRequestCost => beneath.PaidFactionRequestCostRecovery is not null,
            PostEventContinuation.RecoveryReplacement => beneath is RecoveryReplacementFrame,
            _ => false
        };

    private static bool PaidCardUseHpFrameRidesOn(ResolutionFrame ride, ResolutionFrame beneath) =>
        beneath is CardUseFrame { RecoveryPaidContinuation.Kind: RecoveryPaidCardUseKind.DodgeCompletion or RecoveryPaidCardUseKind.CommittedSlash } use &&
        ride is HpChangedTriggerWindowFrame hp && hp.Change.ParentFrameId == use.Id &&
        hp.ResumeFrameId == use.Id && hp.Continuation == PostEventContinuation.RecoveryPaidCardUse;

    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 10, group: 20)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 110)]
    private bool IsPaidCardUseProgramDying() => ActiveCardAttack is { } attack &&
        LifecycleCardUse(attack.ResolutionId)?.RecoveryPaidContinuation?.Kind is RecoveryPaidCardUseKind.DodgeCompletion or RecoveryPaidCardUseKind.CommittedSlash &&
        IsRecoveryReplacementProgramDying(attack.ResolutionId);

    private bool RecoveryReplacementDamageObserverRidesOn(int index)
    {
        if (!DamageObserverRidesOn(_resolutionStack[index], _resolutionStack[index - 1])) return false;
        // Only a contiguous chain of exact observer returns may borrow the recovery
        // producer's damage cursor. An unrelated HP or movement observer still stops it.
        for (var parentIndex = index - 1; parentIndex >= 1; parentIndex--)
        {
            var frame = _resolutionStack[parentIndex];
            var parent = _resolutionStack[parentIndex - 1];
            if (RecoveryReplacementFrameRidesOn(frame, parent) || PaidCardUseHpFrameRidesOn(frame, parent)) return true;
            if (!DamageFrameRidesOn(frame, parent) && !DamageObserverRidesOn(frame, parent) &&
                !PileEquipmentFrameRidesOn(frame, parent) && !RandomEquipmentFrameRidesOn(frame, parent)) return false;
        }
        return false;
    }

    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 330, group: 10)]
    private bool MatchRecoveryReplacementProgramDying() => IsRecoveryReplacementProgramDying();

    private bool IsRecoveryReplacementProgramDying(long? requiredPaidUseId = null)
    {
        var paymentCursor = ProjectTypedCommittedSlashPaymentCursor();
        if (paymentCursor.IsMalformed || paymentCursor.Owner is { } typedOwner &&
            (requiredPaidUseId is null || requiredPaidUseId == typedOwner.Id)) return false;
        if (ActiveDying is not { ResumesProgramSkill: true } dying ||
            _resolutionStack.OfType<DyingFrame>().SingleOrDefault(f => f.Id == dying.FrameId) is not { } child ||
            child.ParentFrameId != dying.ParentFrameId)
            return false;
        var effectiveTop = DamageCursorEffectiveTop(includeNestedObservers: true);
        var topMatches = effectiveTop is DyingFrame topDying && topDying.Id == child.Id ||
            effectiveTop is ProgramSkillFrame { WindowContext: { } response } &&
            response.ParentFrameId == child.Id && response.Window is
                SkillProgramTriggerWindow.DyingResponse or SkillProgramTriggerWindow.SelfDyingResponse;
        if (!topMatches) return false;
        var parentIndex = _resolutionStack.FindLastIndex(f => f.Id == child.ParentFrameId);
        if (parentIndex < 1 || _resolutionStack[parentIndex] is not ProgramSkillFrame) return false;
        var hasRecovery = false;
        for (var index = parentIndex; index >= 0; index--)
        {
            var frame = _resolutionStack[index];
            if (hasRecovery && (requiredPaidUseId is { } paidUseId
                ? frame is CardUseFrame { RecoveryPaidContinuation: not null } paid && paid.Id == paidUseId
                : ActiveDamageTrigger is { } damage && frame is ProgramSkillFrame { WindowContext: { } context } &&
                  context.ParentFrameId == damage.Id && context.Window is
                      SkillProgramTriggerWindow.DamageAppliedBeforeDying or SkillProgramTriggerWindow.AfterDamageApplied))
                return true;
            if (index == 0) return false;
            var parent = _resolutionStack[index - 1];
            if (RecoveryReplacementFrameRidesOn(frame, parent) || PaidCardUseHpFrameRidesOn(frame, parent))
            { hasRecovery = true; continue; }
            if (parent is CardUseFrame paymentOwner && frame is CardsMovedTriggerWindowFrame movement &&
                IsPaidCardUseMovementReturn(paymentOwner, movement))
            { hasRecovery = true; continue; }
            if (!DamageFrameRidesOn(frame, parent) && !DamageObserverRidesOn(frame, parent) &&
                !PileEquipmentFrameRidesOn(frame, parent) && !RandomEquipmentFrameRidesOn(frame, parent)) return false;
        }
        return false;
    }
}
