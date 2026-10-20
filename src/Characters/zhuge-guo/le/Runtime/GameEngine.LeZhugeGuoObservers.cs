namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 30200)]
    private bool IsLeChengyanMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        e?.Op == SkillProgramEffectOp.ResolveLeZhugeGuoChengyan && f.LeChengyan is { Stage: LeChengyanStage.RevealChildren or LeChengyanStage.SecondDrawChildren } &&
        p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null && ValidLeChengyan(f);
    private bool LeChengyanFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.LeChengyan is not { Stage: LeChengyanStage.RevealChildren or LeChengyanStage.SecondDrawChildren } r || !ValidLeChengyan(f)) return false;
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } p && IsLeChengyanMovement(f, GetPausedPrivateOfferEffect(f), p))
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat &&
                moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
                moved.Batch.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After && _cardMovements.Contains(m));
        return child is ProgramLifecycleTriggerWindowFrame state && state.Window == SkillProgramTriggerWindow.SkillsChanged && state.ResumeProgramFrameId == f.Id && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private bool LeChengyanObserverEdge(int n) => IsMovementObserverEdge(n) || IsSameNameHandObserverEdge(n) || IsYanPuDyingExitEdge(n) ||
        TypedDamagePostEventEdge(_resolutionStack[n - 1], _resolutionStack[n]) || _resolutionStack[n - 1] is ProgramSkillFrame f && LeChengyanFirstChild(f, _resolutionStack[n]) ||
        NativeDrawAttemptRidesOn(_resolutionStack[n], _resolutionStack[n - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[n], _resolutionStack[n - 1]);
    private bool LeChengyanObserverSuffix(int index) => _resolutionStack[index + 1] is DyingFrame dying && index + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(index + 1, dying) ||
        HasObserverSuffix(index, static (g, n) => g.LeChengyanObserverEdge(n), static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d));
    private ProgramSkillFrame? LeChengyanObserverRoot() => FindProgramObserverRoot(static (g, f, c) => g.LeChengyanFirstChild(f, c), static (g, n) => g.LeChengyanObserverSuffix(n));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 30200)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 30200)]
    private bool HasLeChengyanDamageObserver(long id) => LeChengyanObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 30200)]
    private bool HasLeChengyanBeforeDamageObserver(long id) => LeChengyanObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 30200)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 30200)]
    private bool IsLeChengyanDying() => ActiveDying is { } dying && LeChengyanObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 30200)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 30200)]
    private bool AllowsLeChengyanNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess || LeChengyanObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature &&
            target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 30200)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 30200)]
    private bool IsLeChengyanRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return index >= 0 && child > index + 1 && LeChengyanFirstChild(f, _resolutionStack[index + 1]) && LeChengyanObserverSuffix(index) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 30200)]
    private bool ResumeLeChengyan(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { LeChengyan: not null } f || f.Id != id) return false;
        ContinueLeChengyan(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 30200)]
    private bool ReturnLeChengyanMovement(ProgramSkillFrame f) => f.PendingMovementContinuation is not null && ResumeLeChengyan(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 30200)]
    private bool AdvanceLeChengyanChildren() => _pendingDecision is null && LeChengyanObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 30200)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 30200)]
    private bool ContinueAcceptedLeChengyan(ProgramSkillFrame f) => f.LeChengyan is not null && ValidLeChengyan(f);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 30200)]
    private void AssertLeChengyan(ProgramSkillFrame f)
    { if (f.LeChengyan is not null && !ValidLeChengyan(f)) throw new InvalidOperationException("Chengyan lost its original Use or paid draw cursor."); }
}
