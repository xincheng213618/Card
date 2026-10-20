namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 33100)]
    private void AssertMouWenChouOl(ProgramSkillFrame f)
    {
        if (f.MouWenChouOl is not { } r) return;
        if (!ValidMouWenChouOl(f) || r.Discarded < 0 || r.Discarded > r.Amount || r.Before > r.After || r.After > _movementSequence || r.NativeReturned && LifecycleCardUse(r.UseId) is not null)
            throw new InvalidOperationException("Mou Wen Chou lost its original source, paid native cursor or return invoice.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 33100)]
    private bool IsMouWenChouMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation movement) => f.MouWenChouOl is { Stage: MouWenChouOlStage.Children or MouWenChouOlStage.DiscardChildren } r && effect?.Op == r.Operation && movement == f.PendingMovementContinuation && movement.SubjectSeat == f.OwnerSeat && movement.BeforeCount == 0 && movement.CoverageResultBind is null && ValidMouWenChouOl(f);
    private bool MouWenChouLion(ProgramSkillFrame f, int seat, CardMoveReason? reason) => f.MouWenChouOl is { Stage: MouWenChouOlStage.DiscardChildren } r && r.TargetSeat == seat && reason?.Value == MouWenChouDiscardReason &&
        ProgramEventHistory<MouWenChouDiscardPaidEvent>().Any(e => e.FrameId == f.Id && e.OwnerSeat == seat && e.Before == r.Before && e.After == r.After && _cardMovements.Any(m => m.CardId == e.CardId && m.CardKind == CardKind.SilverLion && m.From == CardLocation.Equipment(seat) && m.Sequence > e.Before && m.Sequence <= e.After && m.Reason == reason));
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 33100)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 33100)]
    private bool IsMouWenChouRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery) => ValidMouWenChouOl(f) && f.PendingMovementContinuation is not null && RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.SourceSeat == recovery.Attempt.TargetSeat && MouWenChouLion(f, recovery.Attempt.TargetSeat, recovery.Attempt.Completion.MoveReason);
    private bool MouWenChouFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.MouWenChouOl is not { } r || !ValidMouWenChouOl(f)) return false;
        if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == MouWenChouOlStage.Drawing && f.NativeDraw is not null;
        if (child is RecoveryReplacementFrame recovery) return IsMouWenChouRecoveryParent(f, recovery);
        if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == hp.Change.TargetSeat && MouWenChouLion(f, hp.Change.TargetSeat, new(MouWenChouDiscardReason));
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } pending && IsMouWenChouMovement(f, GetPausedPrivateOfferEffect(f), pending))
        {
            if (IsMouWenChouNativeSettlementBatch(f, moved.Batch)) return moved.ResumeProgramFrameId == f.Id;
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.All(m => _cardMovements.Contains(m));
        }
        if (r.Stage == MouWenChouOlStage.NativeUse && child is CardUseFrame native) return native.Id == r.UseId && IsMouWenChouDuel(native.Id);
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id && (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram || state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouWenChouNativeEdge(int n)
    {
        if (_resolutionStack[n - 1] is CardUseFrame use && IsMouWenChouDuel(use.Id) && _resolutionStack[n] is ProgramCardTriggerWindowFrame window) return window.ParentFrameId == use.Id && window.Action.ActionId == use.Action!.ActionId;
        if (_resolutionStack[n - 1] is ProgramCardTriggerWindowFrame parent && LifecycleCardUse(parent.ParentFrameId) is { MouWenChouLunzhan: not null } native && IsMouWenChouDuel(native.Id) && _resolutionStack[n] is ProgramSkillFrame f && parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count)
        { var c = parent.Candidates[parent.CandidateIndex]; return MountObserverCandidateMatches(f, ToSharedCandidate(c)) && f.WindowContext == CreateCardActionProgramContext(parent, c); }
        return false;
    }
    private bool MouWenChouObserverEdge(int n) => IsMovementObserverEdge(n) || IsSameNameHandObserverEdge(n) || IsYanPuDyingExitEdge(n) || TypedDamagePostEventEdge(_resolutionStack[n - 1], _resolutionStack[n]) || MouWenChouNativeEdge(n) || _resolutionStack[n - 1] is ProgramSkillFrame f && MouWenChouFirstChild(f, _resolutionStack[n]) || NativeDrawAttemptRidesOn(_resolutionStack[n], _resolutionStack[n - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[n], _resolutionStack[n - 1]);
    private bool MouWenChouObserverSuffix(int n) => HasObserverSuffix(n, static (g, i) => g.MouWenChouObserverEdge(i), static (g, i, d) => g.IsSuspendedOrPaidRescueObserverSuffix(i, d));
    private ProgramSkillFrame? MouWenChouObserverRoot() => FindProgramObserverRoot(static (g, f, c) => g.MouWenChouFirstChild(f, c), static (g, n) => g.MouWenChouObserverSuffix(n));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 33100)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 33100)]
    private bool HasMouWenChouDamage(long id) => MouWenChouObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 33100)]
    private bool HasMouWenChouBeforeDamage(long id) => MouWenChouObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 33100)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 33100)]
    private bool IsMouWenChouDying() => ActiveDying is { } d && MouWenChouObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == d.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 33100)] [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 33100)]
    private bool AllowsMouWenChouNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || MouWenChouObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature && target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 33100)] private bool AdvanceMouWenChouChildren() => _pendingDecision is null && MouWenChouObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 33100)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 33100)]
    private bool ContinuePaidMouWenChou(ProgramSkillFrame f) => f.MouWenChouOl is { } r && ValidMouWenChouOl(f) && (r.Operation != SkillProgramEffectOp.UseMouWenChouLunzhan || r.Stage != MouWenChouOlStage.Targets) || f.MouWenChouOl is null && f.InstructionIndex is 0 or 1 && f.WindowContext is { } w && CanRunIssuedMouWenChou(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, 0), w);
}
