namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool ZhangHengOriginal(ProgramSkillFrame f)
    {
        if (f.AnniversaryZhangHeng is not { } r || f.WindowContext is not { Window: SkillProgramTriggerWindow.PlayPhaseStarting } w || w.ParentFrameId != r.ParentId ||
            w.OwnerSeat != f.OwnerSeat || w.SourceSeat != f.OwnerSeat || w.TargetSeat != f.OwnerSeat ||
            _resolutionStack.SingleOrDefault(p => p.Id == r.ParentId) is not PlayPhaseStartingBoundaryFrame parent || parent.OwnerSeat != f.OwnerSeat ||
            parent.ItemIndex < 0 || parent.ItemIndex >= parent.Items.Count || parent.Items[parent.ItemIndex].Candidate is not { } candidate) return false;
        return MountObserverCandidateMatches(f, candidate) && w.OccurrenceIndex == candidate.OccurrenceIndex;
    }
    private bool ValidZhangHeng(ProgramSkillFrame f)
    {
        if (f.AnniversaryZhangHeng is not { } r || f.SkillId != AnniversaryZhangHengComposition.Dangzai || f.TriggerId != "take-delayed-subset" || f.ActivationId != f.TriggerId ||
            f.InstructionIndex != 1 || r.Source != ZhangHengSource(f) || r.Hash != f.GameplayHash || r.Turn != _turnNumber ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || !ZhangHengOriginal(f) ||
            GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.ResolveAnniversaryZhangHengDangzai || r.Stage is not ("donor" or "cards" or "children") ||
            r.Before < 0 || r.After < r.Before || r.After > _movementSequence || r.Cards.Distinct().Count() != r.Cards.Count ||
            ProgramEventHistory<AnniversaryZhangHengStartedEvent>().Count(e => e == new AnniversaryZhangHengStartedEvent(f.Id, r.Source, r.Hash, r.ParentId, r.Turn)) != 1) return false;
        if (r.Stage == "donor") return r.Donor == -1 && r.Cards.Count == 0 && f.PendingMovementContinuation is null;
        if (!IsValidPlayerSeat(r.Donor) || r.Donor == f.OwnerSeat || ProgramEventHistory<AnniversaryZhangHengDonorEvent>().Count(e => e == new AnniversaryZhangHengDonorEvent(f.Id, r.Donor)) != 1) return false;
        if (r.Stage == "cards") return f.PendingMovementContinuation is null && r.Cards.All(id => _cardZones.GetLocation(id) == CardLocation.Judgment(r.Donor));
        return r.Cards.Count > 0 && f.PendingMovementContinuation is { BeforeCount: 0, CoverageResultBind: null } p && p.SubjectSeat == f.OwnerSeat &&
            ProgramEventHistory<AnniversaryZhangHengMovedEvent>().Count(e => e == new AnniversaryZhangHengMovedEvent(f.Id, r.Donor, r.Cards.Count, r.Before, r.After)) == 1 &&
            r.Cards.All(id => _cardMovements.Count(m => m.Sequence > r.Before && m.Sequence <= r.After && m.CardId == id &&
                m.From == CardLocation.Judgment(r.Donor) && m.To == CardLocation.Judgment(f.OwnerSeat) && m.Reason == ZhangHengMoveReason) == 1);
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 49600)] private void AssertZhangHeng(ProgramSkillFrame f)
    { if (f.AnniversaryZhangHeng is not null && !ValidZhangHeng(f)) throw new InvalidOperationException("Dangzai lost its original Play boundary, legal physical subset or once-paid native transfer."); }
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 49600)]
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 49600)]
    [ProgramMovementProof(ProgramMovementProofPhase.PrivatePayments, 49600)]
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 49600)]
    private bool ZhangHengMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => f.AnniversaryZhangHeng is { Stage: "children" } &&
        e?.Op == SkillProgramEffectOp.ResolveAnniversaryZhangHengDangzai && p == f.PendingMovementContinuation && ValidZhangHeng(f);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 49600)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 49600)]
    private bool RetainZhangHeng(ProgramSkillFrame f) => f.AnniversaryZhangHeng is { Stage: "children" } && ValidZhangHeng(f);
    private bool ZhangHengFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.AnniversaryZhangHeng is not { Stage: "children" } r || !ValidZhangHeng(f)) return false;
        if (child is CardsMovedTriggerWindowFrame moved) return f.PendingMovementContinuation is { } p && ZhangHengMovement(f, GetPausedPrivateOfferEffect(f), p) && ProgramMovementReturnMatches(f, moved) &&
            moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId &&
            moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After && r.Cards.Contains(m.CardId) && m.Reason == ZhangHengMoveReason);
        if (child is RecoveryReplacementFrame recovery) return recovery.ParentFrameId == f.Id && recovery.Return.ResumeFrameId == f.Id &&
            recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && RecoveryReplacementFrameRidesOn(recovery, f);
        if (child is HpChangedTriggerWindowFrame hp) return hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && state.CharacterStateContinuation == CharacterStateContinuation.Program);
    }
    private bool ZhangHengObserverEdge(int i) => IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) || IsYanPuDyingExitEdge(i) || TypedDamagePostEventEdge(_resolutionStack[i - 1], _resolutionStack[i]) ||
        _resolutionStack[i - 1] is ProgramSkillFrame f && ZhangHengFirstChild(f, _resolutionStack[i]) || NativeDrawAttemptRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[i], _resolutionStack[i - 1]);
    private ProgramSkillFrame? ZhangHengObserverRoot() => FindProgramObserverRoot(static (e, f, c) => e.ZhangHengFirstChild(f, c),
        static (e, i) => e.HasObserverSuffix(i, static (g, n) => g.ZhangHengObserverEdge(n), static (g, n, d) => g.IsSuspendedOrPaidRescueObserverSuffix(n, d)));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 49600)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 49600)]
    private bool ZhangHengDamageObserver(long id) => ZhangHengObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 49600)] private bool ZhangHengBeforeDamageObserver(long id) => ZhangHengObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 49600)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 49600)]
    private bool ZhangHengDying() => ActiveDying is { } d && ZhangHengObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == d.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 49600)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 49600)]
    private bool ZhangHengRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.AnniversaryZhangHeng is null) return false;
        var i = _resolutionStack.FindIndex(x => x.Id == f.Id); var j = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return i >= 0 && j > i && ZhangHengFirstChild(f, _resolutionStack[i + 1]) && (j == i + 1 || SameNameHandObserverSuffix(i)) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[j - 1]);
    }
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 49600)] [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 49600)]
    private bool ZhangHengNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || ZhangHengObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 49600)] private bool AdvanceZhangHengChildren() => _pendingDecision is null && ZhangHengObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
