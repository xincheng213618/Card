namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 33400)]
    private void AssertMouDengAiOl(ProgramSkillFrame f)
    {
        if (f.MouDengAiOl is not { } r) return;
        if (!ValidMouDengAiOl(f) || r.Before > r.After || r.After > _movementSequence || r.Distance < 0 || r.HandCards.Distinct().Count() != r.HandCards.Count || r.GuCards.Distinct().Count() != r.GuCards.Count ||
            r.NativeReturned && LifecycleCardUse(r.UseId) is not null || r.Stage is MouDengAiOlStage.CostChildren or MouDengAiOlStage.Material or MouDengAiOlStage.Snatch or MouDengAiOlStage.Issuing or MouDengAiOlStage.NativeUse && !r.Paid)
            throw new InvalidOperationException("Mou Deng Ai lost its source, original paid cursor or native return.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 33400)]
    private bool IsMouDengAiMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation m) => f.MouDengAiOl is { Stage: MouDengAiOlStage.Children or MouDengAiOlStage.CostChildren } r && ValidMouDengAiOl(f) &&
        effect?.Op == r.Operation && m == f.PendingMovementContinuation && m.SubjectSeat == f.OwnerSeat && m.BeforeCount == 0 && m.CoverageResultBind is null;
    private bool MouDengAiFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.MouDengAiOl is not { } r || !ValidMouDengAiOl(f)) return false;
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } m && IsMouDengAiMovement(f, GetPausedPrivateOfferEffect(f), m))
            return IsMouDengAiNativeSettlementBatch(f, moved.Batch) && moved.ResumeProgramFrameId == f.Id ||
                ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.All(x => _cardMovements.Contains(x));
        if (child is HpChangedTriggerWindowFrame hp) return r.Paid && hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement && hp.Change.Kind == HpChangeKind.MaximumHp && hp.Change.TargetSeat == f.OwnerSeat;
        if (child is CardUseFrame use) return r.Stage == MouDengAiOlStage.NativeUse && use.Id == r.UseId && IsMouDengAiSnatch(use.Id);
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id && (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
            state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouDengAiNativeEdge(int n)
    {
        if (_resolutionStack[n - 1] is CardUseFrame use && IsMouDengAiSnatch(use.Id) && _resolutionStack[n] is ProgramCardTriggerWindowFrame window) return window.ParentFrameId == use.Id && window.Action.ActionId == use.Action!.ActionId;
        if (_resolutionStack[n - 1] is ProgramCardTriggerWindowFrame parent && LifecycleCardUse(parent.ParentFrameId) is { MouDengAiJiewan: not null } native && IsMouDengAiSnatch(native.Id) && _resolutionStack[n] is ProgramSkillFrame f && parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count)
        { var c = parent.Candidates[parent.CandidateIndex]; return MountObserverCandidateMatches(f, ToSharedCandidate(c)) && f.WindowContext == CreateCardActionProgramContext(parent, c); }
        return false;
    }
    private bool MouDengAiObserverEdge(int n) => IsMovementObserverEdge(n) || IsSameNameHandObserverEdge(n) || IsYanPuDyingExitEdge(n) || TypedDamagePostEventEdge(_resolutionStack[n - 1], _resolutionStack[n]) || MouDengAiNativeEdge(n) ||
        _resolutionStack[n - 1] is ProgramSkillFrame f && MouDengAiFirstChild(f, _resolutionStack[n]) || NativeDrawAttemptRidesOn(_resolutionStack[n], _resolutionStack[n - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[n], _resolutionStack[n - 1]);
    private bool MouDengAiObserverSuffix(int n) => HasObserverSuffix(n, static (g, i) => g.MouDengAiObserverEdge(i), static (g, i, d) => g.IsSuspendedOrPaidRescueObserverSuffix(i, d));
    private ProgramSkillFrame? MouDengAiObserverRoot() => FindProgramObserverRoot(static (g, f, c) => g.MouDengAiFirstChild(f, c), static (g, n) => g.MouDengAiObserverSuffix(n));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 33400)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 33400)]
    private bool HasMouDengAiDamage(long id) => MouDengAiObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 33400)]
    private bool HasMouDengAiBeforeDamage(long id) => MouDengAiObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 33400)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 33400)]
    private bool IsMouDengAiDying() => ActiveDying is { } d && MouDengAiObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == d.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 33400)] [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 33400)]
    private bool AllowsMouDengAiNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || MouDengAiObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature && target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 33400)] private bool AdvanceMouDengAiChildren() => _pendingDecision is null && MouDengAiObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 33400)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 33400)]
    private bool ContinuePaidMouDengAi(ProgramSkillFrame f) => f.MouDengAiOl is { Paid: true } && ValidMouDengAiOl(f);
}
