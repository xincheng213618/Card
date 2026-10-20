namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsMouZhenJiLuoshenMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        e?.Op == SkillProgramEffectOp.ResolveMouZhenJiLuoshen && f.MouZhenJiLuoshen is { Stage: MouZhenJiLuoshenStage.MovementChildren } &&
        p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null && ValidMouZhenJiLuoshen(f);
    private bool MouZhenJiLuoshenFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.MouZhenJiLuoshen is not { Stage: MouZhenJiLuoshenStage.MovementChildren } r || !ValidMouZhenJiLuoshen(f)) return false;
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } p && IsMouZhenJiLuoshenMovement(f, GetPausedPrivateOfferEffect(f), p))
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat &&
                moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
                moved.Batch.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After && _cardMovements.Contains(m));
        return child is ProgramLifecycleTriggerWindowFrame state && state.Window == SkillProgramTriggerWindow.SkillsChanged && state.ResumeProgramFrameId == f.Id && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private bool MouZhenJiLuoshenObserverEdge(int n) => IsMovementObserverEdge(n) || IsSameNameHandObserverEdge(n) || IsYanPuDyingExitEdge(n) ||
        TypedDamagePostEventEdge(_resolutionStack[n - 1], _resolutionStack[n]) || _resolutionStack[n - 1] is ProgramSkillFrame f && MouZhenJiLuoshenFirstChild(f, _resolutionStack[n]) ||
        NativeDrawAttemptRidesOn(_resolutionStack[n], _resolutionStack[n - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[n], _resolutionStack[n - 1]);
    private bool MouZhenJiLuoshenObserverSuffix(int index) => _resolutionStack[index + 1] is DyingFrame dying && index + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(index + 1, dying) ||
        HasObserverSuffix(index, static (g, n) => g.MouZhenJiLuoshenObserverEdge(n), static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d));
    private ProgramSkillFrame? MouZhenJiLuoshenObserverRoot() => FindProgramObserverRoot(static (g, f, c) => g.MouZhenJiLuoshenFirstChild(f, c), static (g, n) => g.MouZhenJiLuoshenObserverSuffix(n));
    private bool HasMouZhenJiLuoshenDamageObserver(long id) => MouZhenJiLuoshenObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool HasMouZhenJiLuoshenBeforeDamageObserver(long id) => MouZhenJiLuoshenObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool IsMouZhenJiLuoshenDying() => ActiveDying is { } dying && MouZhenJiLuoshenObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    private bool AllowsMouZhenJiLuoshenNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess || MouZhenJiLuoshenObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature &&
            target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    private bool IsMouZhenJiLuoshenRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return index >= 0 && child > index + 1 && MouZhenJiLuoshenFirstChild(f, _resolutionStack[index + 1]) && MouZhenJiLuoshenObserverSuffix(index) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 29900)]
    private bool ResumeMouZhenJiLuoshen(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouZhenJiLuoshen: not null } f || f.Id != id) return false;
        ContinueMouZhenJiLuoshen(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 29900)]
    private bool ReturnMouZhenJiLuoshenMovement(ProgramSkillFrame f) => f.PendingMovementContinuation is not null && ResumeMouZhenJiLuoshen(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 29900)]
    private bool AdvanceMouZhenJiLuoshenChildren() => _pendingDecision is null && MouZhenJiLuoshenObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 29900)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 29900)]
    private bool ContinueAcceptedMouZhenJiLuoshen(ProgramSkillFrame f) => f.MouZhenJiLuoshen is { StartingSeat: not null } && ValidMouZhenJiLuoshen(f);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 29900)]
    private void AssertMouZhenJiLuoshen(ProgramSkillFrame f)
    { if (f.MouZhenJiLuoshen is not null && !ValidMouZhenJiLuoshen(f)) throw new InvalidOperationException("Mou Luoshen lost its owning source, frozen roster or participant cursor."); }
}
