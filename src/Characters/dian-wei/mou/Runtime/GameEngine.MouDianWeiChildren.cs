namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 35300)]
    private bool MouDianWeiMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) =>
        f.MouDianWei is { } r && effect?.Op == r.Operation && MouDianWei.Valid(f) && p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool MouDianWeiFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!MouDianWei.Valid(f) || f.MouDianWei is not { } r) return false;
        if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == MouDianWeiStage.Drawing && f.NativeDraw is not null;
        if (child is PindianFrame contest)
            return r.Stage == MouDianWeiStage.Pindian && contest.Id == r.ContestId && contest.ParentFrameId == f.Id && contest.SourceSeat == f.OwnerSeat && contest.OpponentSeat == r.Opponent && contest.ProgramResultBind == MouDianWeiRuntime.Bind(r);
        if (child is CardUseFrame use) return r.Stage == MouDianWeiStage.Slash && IsMouDianWeiSlashUse(use.Id);
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } p && MouDianWeiMovement(f, GetPausedPrivateOfferEffect(f), p))
        {
            var movement = moved.Batch.Movements;
            if (r.Stage == MouDianWeiStage.PindianChildren)
                return moved.ResumeProgramFrameId == f.Id && movement.Count > 0 && movement.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After && m.Reason == CardMoveReasons.PindianFinish);
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
                movement.Count > 0 && movement.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After);
        }
        if (r.Stage == MouDianWeiStage.RecoveryChildren)
        {
            if (child is RecoveryReplacementFrame rr)
                return RecoveryReplacementFrameRidesOn(rr, f) && rr.Attempt.Completion == new RecoveryAttemptCompletion(RecoveryAttemptProducer.ProgramMaximum, 1) && rr.Attempt.SourceSeat == f.OwnerSeat && rr.Attempt.TargetSeat == f.OwnerSeat && rr.Attempt.Amount == r.Requested;
            if (child is HpChangedTriggerWindowFrame hp)
                return hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == f.OwnerSeat && hp.Change.TargetSeat == f.OwnerSeat &&
                    f.RecoveryReceipt is { InstructionIndex: 1 } receipt && hp.Change.Amount == receipt.Recovered && hp.Change.HpBefore == r.HpBefore && hp.Change.HpAfter == r.HpBefore + receipt.Recovered;
        }
        if (r.Stage == MouDianWeiStage.LossChildren && child is HpChangedTriggerWindowFrame loss)
            return loss.ResumeFrameId == f.Id && loss.Change.ParentFrameId == f.Id && loss.Change.Kind == HpChangeKind.Loss && loss.Change.TargetSeat == f.OwnerSeat &&
                loss.Change.HpBefore == r.HpBefore && loss.Change.Amount == r.ActualLoss && loss.Change.HpAfter == r.HpBefore - r.ActualLoss && loss.Change.HpAfter >= 1 &&
                ProgramEventHistory<ProgramSkillHpLostEvent>().Any(e => e.FrameId == f.Id && e.Amount == r.ActualLoss && e.TargetSeat == f.OwnerSeat);
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram || state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
                ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouDianWeiEdge(int i) => IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) || IsYanPuDyingExitEdge(i) ||
        TypedDamagePostEventEdge(_resolutionStack[i - 1], _resolutionStack[i]) || _resolutionStack[i - 1] is ProgramSkillFrame f && MouDianWeiFirstChild(f, _resolutionStack[i]) ||
        NativeDrawAttemptRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[i], _resolutionStack[i - 1]);
    private bool MouDianWeiSuffix(int n) => _resolutionStack[n + 1] is DyingFrame d && n + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(n + 1, d) ||
        HasObserverSuffix(n, static (g, i) => g.MouDianWeiEdge(i), static (g, i, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(i, dying));
    private ProgramSkillFrame? MouDianWeiRoot() => FindProgramObserverRoot(static (g, f, c) => g.MouDianWeiFirstChild(f, c), static (g, n) => g.MouDianWeiSuffix(n));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 35300)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 35300)]
    private bool MouDianWeiDamageWindow(long id) => MouDianWeiRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 35300)]
    private bool MouDianWeiBeforeDamage(long id) => MouDianWeiRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 35300)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 35300)]
    private bool MouDianWeiDying() => ActiveDying is { } d && MouDianWeiRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 35300)] [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 35300)]
    private bool MouDianWeiNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess || MouDianWeiRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature && target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 35300)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 35300)]
    private bool MouDianWeiRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (MouDianWeiFirstChild(f, recovery)) return true;
        var n = _resolutionStack.FindIndex(x => x.Id == f.Id); var c = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return n >= 0 && c > n + 1 && MouDianWeiFirstChild(f, _resolutionStack[n + 1]) && MouDianWeiSuffix(n) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[c - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 35300)]
    private bool MouDianWeiAdvanceChildren() => _pendingDecision is null && MouDianWeiRoot() is not null && TryAdvanceObserverSubtree(true);
}
