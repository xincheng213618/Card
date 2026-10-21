namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 37000)] private bool MouChenLinMovement(ProgramSkillFrame f, SkillProgramEffect? op, ProgramMovementContinuation p) =>
        (f.MouChenLin is not null && ValidMouChenLin(f) && op?.Op is SkillProgramEffectOp.ResolveMouChenLinZhuanwen or SkillProgramEffectOp.ResolveMouChenLinYaozuo || f.MouChenLinInvitation is { Stage: MouChenLinInvitationStage.MovementChildren } && MouChenLinRules.ValidInvitation(f) && op?.Op == SkillProgramEffectOp.ResolveMouChenLinYaozuo) && p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool MouChenLinFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.MouChenLin is null && f.MouChenLinInvitation is { Stage: MouChenLinInvitationStage.MovementChildren } invitation && MouChenLinRules.ValidInvitation(f))
        {
            if (child is CardsMovedTriggerWindowFrame gift && f.PendingMovementContinuation is { } paid && MouChenLinMovement(f, GetPausedPrivateOfferEffect(f), paid)) return ProgramMovementReturnMatches(f, gift) && gift.Batch.ParentFrameId == f.Id && gift.Batch.OriginOwnerSeat == f.OwnerSeat && gift.Batch.OriginSkillId == f.SkillId && gift.Batch.OriginSkillInstanceId == f.SkillInstanceId && gift.Batch.Movements.All(m => _cardMovements.Contains(m) && m.CardId == invitation.PendingCard?.Id && m.Sequence > invitation.Before && m.Sequence <= invitation.After);
            if (invitation.PendingCard is { Kind: CardKind.SilverLion, From.Zone: CardZoneKind.Equipment } giftLion && giftLion.From.OwnerSeat is { } giftHolder && _cardMovements.Any(m => m.CardId == giftLion.Id && m.Sequence > invitation.Before && m.Sequence <= invitation.After))
            {
                if (child is RecoveryReplacementFrame rr) return RecoveryReplacementFrameRidesOn(rr, f) && rr.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && rr.Attempt.TargetSeat == giftHolder && rr.Attempt.SourceSeat == giftHolder;
                if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.TargetSeat == giftHolder;
            }
            return child is ProgramLifecycleTriggerWindowFrame giftState && giftState.ResumeProgramFrameId == f.Id && giftState.Window == SkillProgramTriggerWindow.SkillsChanged && giftState.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
        }
        if (!ValidMouChenLin(f) || f.MouChenLin is not { } r) return false;
        if (child is CardUseFrame use) return MouChenLinUseRidesOn(f, use);
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } p && MouChenLinMovement(f, GetPausedPrivateOfferEffect(f), p))
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
                moved.Batch.Movements.All(m => _cardMovements.Contains(m) && (r.Stage == MouChenLinStage.UseChildren || m.Sequence > r.Before && m.Sequence <= r.After));
        var lion = r.Cards.FirstOrDefault(c => c.From.Zone == CardZoneKind.Equipment && c.Kind == CardKind.SilverLion && _cardMovements.Any(m => m.CardId == c.Id && m.From == c.From && m.Sequence > r.Before && m.Sequence <= r.After));
        if (lion?.From.OwnerSeat is { } holder)
        {
            if (child is RecoveryReplacementFrame rr) return RecoveryReplacementFrameRidesOn(rr, f) && rr.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && rr.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && rr.Attempt.SourceSeat == holder && rr.Attempt.TargetSeat == holder;
            if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.TargetSeat == holder;
        }
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram || state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouChenLinEdge(int i) => IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) || IsYanPuDyingExitEdge(i) || TypedDamagePostEventEdge(_resolutionStack[i - 1], _resolutionStack[i]) ||
        _resolutionStack[i - 1] is ProgramSkillFrame f && MouChenLinFirstChild(f, _resolutionStack[i]) || NativeDrawAttemptRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) ||
        DyingSuitsStructuralEdge(_resolutionStack[i - 1], _resolutionStack[i]);
    private bool MouChenLinSuffix(int n) => _resolutionStack[n + 1] is DyingFrame d && n + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(n + 1, d) || HasObserverSuffix(n, static (g,i) => g.MouChenLinEdge(i), static (g,i,dying) => g.IsSuspendedOrPaidRescueObserverSuffix(i,dying));
    private ProgramSkillFrame? MouChenLinRoot() => FindProgramObserverRoot(static (g,f,c) => g.MouChenLinFirstChild(f,c), static (g,n) => g.MouChenLinSuffix(n));
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 37000)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 37000)] private bool MouChenLinDying() => ActiveDying is { } d && MouChenLinRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 37000)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 37000)] private bool MouChenLinDamageWindow(long id) => MouChenLinRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 37000)] private bool MouChenLinBeforeDamage(long id) => MouChenLinRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 37000)] [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 37000)]
    private bool MouChenLinNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess || MouChenLinRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature && target == (e.TargetReference is { } p ? ResolveProgramParticipant(f,p) : ResolveProgramEffectTarget(f,e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 37000)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 37000)] private bool MouChenLinRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (MouChenLinFirstChild(f, recovery)) return true;
        var n = _resolutionStack.FindIndex(x => x.Id == f.Id); var c = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return n >= 0 && c > n + 1 && MouChenLinFirstChild(f, _resolutionStack[n + 1]) && MouChenLinSuffix(n) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[c - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 37000)] private bool MouChenLinAdvanceChildren() => _pendingDecision is null && MouChenLinRoot() is not null && TryAdvanceObserverSubtree(true);
}
