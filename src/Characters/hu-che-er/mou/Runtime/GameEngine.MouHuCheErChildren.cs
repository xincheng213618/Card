namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 35800)] private bool MouHuCheErMovement(ProgramSkillFrame f, SkillProgramEffect? op, ProgramMovementContinuation p) =>
        f.MouHuCheEr is not null && op?.Op == SkillProgramEffectOp.ResolveMouHuCheErKongwu && ValidMouHuCheEr(f) && p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool MouHuCheErFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidMouHuCheEr(f) || f.MouHuCheEr is not { } r) return false;
        if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == MouHuCheErStage.Drawing && f.NativeDraw is not null;
        if (child is CardUseFrame use) return r.Stage == MouHuCheErStage.NativeSlash && use.MouHuCheErSlash is { } o && o.FrameId == f.Id && use.Id == r.UseId && IsIssuedMouHuCheErUse(use);
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } p && MouHuCheErMovement(f, GetPausedPrivateOfferEffect(f), p))
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
                moved.Batch.Movements.All(m => _cardMovements.Contains(m) && (r.Stage == MouHuCheErStage.SlashChildren || m.Sequence > r.Before && m.Sequence <= r.After));
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram || state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouHuCheErEdge(int i) => IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) || IsYanPuDyingExitEdge(i) || TypedDamagePostEventEdge(_resolutionStack[i - 1], _resolutionStack[i]) ||
        _resolutionStack[i - 1] is ProgramSkillFrame f && MouHuCheErFirstChild(f, _resolutionStack[i]) || NativeDrawAttemptRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) ||
        DyingSuitsStructuralEdge(_resolutionStack[i - 1], _resolutionStack[i]);
    private bool MouHuCheErSuffix(int n) => _resolutionStack[n + 1] is DyingFrame d && n + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(n + 1, d) || HasObserverSuffix(n, static (g,i) => g.MouHuCheErEdge(i), static (g,i,dying) => g.IsSuspendedOrPaidRescueObserverSuffix(i,dying));
    private ProgramSkillFrame? MouHuCheErRoot() => FindProgramObserverRoot(static (g,f,c) => g.MouHuCheErFirstChild(f,c), static (g,n) => g.MouHuCheErSuffix(n));
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 35800)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 35800)] private bool MouHuCheErDying() => ActiveDying is { } d && MouHuCheErRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == d.Id) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 35800)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 35800)] private bool MouHuCheErDamageWindow(long id) => MouHuCheErRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 35800)] private bool MouHuCheErBeforeDamage(long id) => MouHuCheErRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 35800)] [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 35800)]
    private bool MouHuCheErNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess || MouHuCheErRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature && target == (e.TargetReference is { } p ? ResolveProgramParticipant(f,p) : ResolveProgramEffectTarget(f,e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 35800)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 35800)] private bool MouHuCheErRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        var n = _resolutionStack.FindIndex(x => x.Id == f.Id); var c = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return n >= 0 && c > n + 1 && MouHuCheErFirstChild(f, _resolutionStack[n + 1]) && MouHuCheErSuffix(n) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[c - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 35800)] private bool MouHuCheErAdvanceChildren() => _pendingDecision is null && MouHuCheErRoot() is not null && TryAdvanceObserverSubtree(true);
}
