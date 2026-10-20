namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidMouTianYu(ProgramSkillFrame f) => f.MouTianYu is { } r && f.InstructionIndex == 1 && r.Source == MouTianYuSource(f) &&
        r.GameplayHash == f.GameplayHash && f.WindowContext?.ParentFrameId == r.WindowId &&
        ProgramEventHistory<MouTianYuStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash &&
            e.WindowId == r.WindowId && e.UseId == r.UseId && e.ActionId == r.ActionId && e.TargetSeat == r.TargetSeat && e.TurnOrdinal == r.TurnOrdinal) == 1;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 30600)]
    private void AssertMouTianYu(ProgramSkillFrame f)
    { if (f.MouTianYu is not null && !ValidMouTianYu(f)) throw new InvalidOperationException("Tian Yu lost its owning source and accepted native opportunity."); }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 30600)]
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 30600)]
    private bool IsMouTianYuMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation m) =>
        f.MouTianYu is { Stage: MouTianYuStage.Children } && e is not null && MouTianYuComposition.Handles(e.Op) && ValidMouTianYu(f) &&
        m.SubjectSeat == f.OwnerSeat && m.BeforeCount == 0 && m.CoverageResultBind is null;
    private bool MouTianYuFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.MouTianYu is not { Stage: MouTianYuStage.Children } r || !ValidMouTianYu(f)) return false;
        if (RecoveryReplacementFrameRidesOn(child, f)) return true;
        if (child is HpChangedTriggerWindowFrame hp) return ProgramHpReturnMatches(f, hp);
        return child is CardsMovedTriggerWindowFrame moved && ProgramMovementReturnMatches(f, moved) && moved.Batch.Movements.All(m =>
            m.Sequence > r.Before && m.Sequence <= r.After && ProgramEventHistory<MouTianYuMovementSignedEvent>().Count(e => e.FrameId == f.Id &&
                e.CardId == m.CardId && e.Kind == m.CardKind && e.From == m.From && e.To == m.To && e.Reason == m.Reason && e.Sequence == m.Sequence) == 1) ||
            child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
                state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouTianYuObserverEdge(int index) => IsMovementObserverEdge(index) || IsSameNameHandObserverEdge(index) || IsYanPuDyingExitEdge(index) ||
        TypedDamagePostEventEdge(_resolutionStack[index - 1], _resolutionStack[index]) || NativeDrawAttemptRidesOn(_resolutionStack[index], _resolutionStack[index - 1]) ||
        RecoveryReplacementFrameRidesOn(_resolutionStack[index], _resolutionStack[index - 1]);
    private bool MouTianYuObserverSuffix(int index) => HasObserverSuffix(index, static (g, n) => g.MouTianYuObserverEdge(n),
        static (g, n, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(n, dying));
    private ProgramSkillFrame? MouTianYuObserverRoot() => FindProgramObserverRoot(static (g, f, c) => g.MouTianYuFirstChild(f, c), static (g, n) => g.MouTianYuObserverSuffix(n));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 30600)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 30600)]
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 30600)]
    private bool HasMouTianYuDamageObserver(long id) => MouTianYuObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 30600)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 30600)]
    private bool IsMouTianYuDying() => ActiveDying is { } dying && MouTianYuObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 30600)]
    private bool AllowsMouTianYuNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || sourceLess || MouTianYuObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 30600)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 30600)]
    private bool IsMouTianYuRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        var root = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        if (ValidMouTianYu(f) && RecoveryReplacementFrameRidesOn(recovery, f)) return true;
        return root >= 0 && child > root + 1 && MouTianYuFirstChild(f, _resolutionStack[root + 1]) && MouTianYuObserverSuffix(root) &&
            RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 30600)]
    private bool AdvanceMouTianYuChildren() => _pendingDecision is null && MouTianYuObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
