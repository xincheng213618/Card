namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 34800)]
    private bool MouLiuXieMovementReturn(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation p) =>
        effect?.Op == SkillProgramEffectOp.ResolveMouLiuXieZhanban && MouLiuXie.Valid(f) && f.MouLiuXie is { Stage: MouLiuXieStage.DiscardChildren or MouLiuXieStage.DrawChildren } &&
        p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool MouLiuXieFirstNativeChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!MouLiuXie.Valid(f) || f.MouLiuXie is not { } r) return false;
        if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == MouLiuXieStage.Drawing && f.NativeDraw is not null;
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } pending && MouLiuXieMovementReturn(f, GetPausedPrivateOfferEffect(f), pending))
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat &&
                moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
                moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After);
        if (r.Stage == MouLiuXieStage.Damage && f.AttackAttempt is { } attack && f.AttackReturn is not null &&
            attack.SourceSeat == f.OwnerSeat && attack.TargetSeat == r.Payer && !attack.SourceLess &&
            ProgramEventHistory<MouLiuXieDamageIssuedEvent>().Count(e => e.FrameId == f.Id && e.SourceSeat == f.OwnerSeat && e.TargetSeat == r.Payer) == 1)
            return DyingSuitsStructuralEdge(f, child);
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
                state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouLiuXieNativeEdge(int i) => IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) || IsYanPuDyingExitEdge(i) ||
        TypedDamagePostEventEdge(_resolutionStack[i - 1], _resolutionStack[i]) || _resolutionStack[i - 1] is ProgramSkillFrame f && MouLiuXieFirstNativeChild(f, _resolutionStack[i]) ||
        NativeDrawAttemptRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[i], _resolutionStack[i - 1]);
    private bool MouLiuXieNativeSuffix(int n) => _resolutionStack[n + 1] is DyingFrame d && n + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(n + 1, d) ||
        HasObserverSuffix(n, static (g, i) => g.MouLiuXieNativeEdge(i), static (g, i, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(i, dying));
    private ProgramSkillFrame? MouLiuXieNativeRoot() => FindProgramObserverRoot(static (g, f, c) => g.MouLiuXieFirstNativeChild(f, c), static (g, n) => g.MouLiuXieNativeSuffix(n));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 34800)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 34800)]
    private bool MouLiuXieDamageWindow(long id) => MouLiuXieNativeRoot() is { } f &&
        _resolutionStack.FindIndex(x => x.Id == id && x is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 34800)]
    private bool MouLiuXieBeforeDamage(long id) => MouLiuXieNativeRoot() is { } f &&
        _resolutionStack.FindIndex(x => x.Id == id && x is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 34800)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 34800)]
    private bool MouLiuXieDying() => ActiveDying is { } dying && MouLiuXieNativeRoot() is { } f && _resolutionStack.FindIndex(x => x.Id == dying.Id) > _resolutionStack.FindIndex(x => x.Id == f.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 34800)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 34800)]
    private bool MouLiuXieNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess) return false;
        if (MouLiuXie.Valid(f) && f.MouLiuXie is { Stage: MouLiuXieStage.Damage } r) return target == r.Payer && amount == 1 && source is null && nature is null;
        if (MouLiuXieNativeRoot() is not { } root || root.Id == f.Id) return false;
        var effect = GetPausedPrivateOfferEffect(f);
        return effect is { Op: SkillProgramEffectOp.Damage } && amount == effect.Amount && source == effect.ActorReference && nature == effect.DamageNature &&
            target == (effect.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, effect.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 34800)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 34800)]
    private bool MouLiuXieRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (MouLiuXieFirstNativeChild(f, recovery)) return true;
        var parent = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return parent >= 0 && child > parent + 1 && MouLiuXieFirstNativeChild(f, _resolutionStack[parent + 1]) && MouLiuXieNativeSuffix(parent) &&
            RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 34800)]
    private bool MouLiuXieAdvanceChildren() => _pendingDecision is null && MouLiuXieNativeRoot() is not null && TryAdvanceObserverSubtree(true);
}
