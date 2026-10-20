namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidMoLuBu(ProgramSkillFrame f) => f.MoLuBu is { } r && f.InstructionIndex == 1 && r.Source.SkillId == f.SkillId && r.GameplayHash == f.GameplayHash &&
        (r.Operation == SkillProgramEffectOp.ResolveMoLuBuKuangmoDeath ? ProgramEventHistory<MoLuBuKuangmoEnteredEvent>().Any(e => e.Source == r.Source && e.GameplayHash == r.GameplayHash) : r.Source == MoLuBuSource(f)) &&
        ProgramEventHistory<MoLuBuStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == r.GameplayHash && e.Operation == r.Operation && e.WindowId == r.WindowId &&
            e.OriginalOwnerSeat == r.OriginalOwnerSeat && e.ActualTurn == r.ActualTurn && e.Reassign == r.Reassign) == 1;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 30800)]
    private void AssertMoLuBu(ProgramSkillFrame f)
    { if (f.MoLuBu is not null && !ValidMoLuBu(f)) throw new InvalidOperationException("OL Lu Bu lost its original immutable source and issued Qi/pair obligation."); }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 30800)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 30800)]
    private bool ContinueIssuedMoLuBu(ProgramSkillFrame f) => f.MoLuBu is not null && ValidMoLuBu(f) ||
        f.WindowContext is { } c && IsIssuedMoLuBuDeathCandidate(new(f.OwnerSeat, f.SkillId, f.TriggerId ?? "", f.SkillInstanceId, f.GameplayHash, 0, c.OccurrenceIndex), c);
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 30800)]
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 30800)]
    private bool MoLuBuMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation m) => f.MoLuBu is not null && ValidMoLuBu(f) && e is not null && MoLuBuComposition.Handles(e.Op) &&
        m.SubjectSeat == f.OwnerSeat && m.BeforeCount == 0 && m.CoverageResultBind is null;
    private bool MoLuBuFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.MoLuBu is not { } r || !ValidMoLuBu(f)) return false;
        if (RecoveryReplacementFrameRidesOn(child, f)) return true;
        if (child is HpChangedTriggerWindowFrame hp) return ProgramHpReturnMatches(f, hp);
        if (child is ProgramLifecycleTriggerWindowFrame state) return state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
        return child is CardsMovedTriggerWindowFrame moved && ProgramMovementReturnMatches(f, moved) && moved.Batch.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After &&
            ProgramEventHistory<MoLuBuMovementSignedEvent>().Count(e => e.FrameId == f.Id && e.CardId == m.CardId && e.Kind == m.CardKind && e.From == m.From && e.To == m.To && e.Reason == m.Reason && e.Sequence == m.Sequence) == 1);
    }
    private bool MoLuBuObserverSuffix(int index) => HasObserverSuffix(index, static (g, i) => g.IsMovementObserverEdge(i) || g.IsSameNameHandObserverEdge(i) || g.IsYanPuDyingExitEdge(i) ||
        g.TypedDamagePostEventEdge(g._resolutionStack[i - 1], g._resolutionStack[i]) || g._resolutionStack[i - 1] is ProgramSkillFrame f && g.MoLuBuFirstChild(f, g._resolutionStack[i]) ||
        NativeDrawAttemptRidesOn(g._resolutionStack[i], g._resolutionStack[i - 1]) || g.RecoveryReplacementFrameRidesOn(g._resolutionStack[i], g._resolutionStack[i - 1]),
        static (g, i, dying) => g.IsSuspendedOrPaidRescueObserverSuffix(i, dying));
    private ProgramSkillFrame? MoLuBuObserverRoot() => FindProgramObserverRoot(static (g, f, c) => g.MoLuBuFirstChild(f, c), static (g, i) => g.MoLuBuObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 30800)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 30800)]
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 30800)]
    private bool MoLuBuDamageObserver(long id) => MoLuBuObserverRoot() is { MoLuBu: { } r } root &&
        (r.WindowId == id || _resolutionStack.FindIndex(f => f.Id == id) > _resolutionStack.FindIndex(f => f.Id == root.Id));
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 30800)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 30800)]
    private bool MoLuBuDying() => ActiveDying is { } d && MoLuBuObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == d.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 30800)]
    private bool MoLuBuNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || MoLuBuObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 30800)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 30800)]
    private bool MoLuBuRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (!ValidMoLuBu(f)) return false;
        if (RecoveryReplacementFrameRidesOn(recovery, f)) return true;
        var parent = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return parent >= 0 && child > parent + 1 && MoLuBuFirstChild(f, _resolutionStack[parent + 1]) && MoLuBuObserverSuffix(parent) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 30800)]
    private bool AdvanceMoLuBuChildren() => _pendingDecision is null && MoLuBuObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
