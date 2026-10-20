namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool MouZhangXiuOlParentMatches(ProgramSkillFrame f)
    {
        if (f.WindowContext is not { } w) return false; var n = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (n < 1 || _resolutionStack[n - 1].Id != w.ParentFrameId) return false;
        return _resolutionStack[n - 1] switch
        {
            ProgramLifecycleTriggerWindowFrame p => p.Window == w.Window && p.CandidateIndex >= 0 && p.CandidateIndex < p.Candidates.Count && MountObserverCandidateMatches(f, p.Candidates[p.CandidateIndex]),
            PlayPhaseStartingBoundaryFrame p => w.Window == SkillProgramTriggerWindow.PlayPhaseStarting && p.ItemIndex >= 0 && p.ItemIndex < p.Items.Count && p.Items[p.ItemIndex].Candidate is { } c && MountObserverCandidateMatches(f, c),
            TurnEndingBoundaryFrame p => w.Window == SkillProgramTriggerWindow.TurnEnding && p.ItemIndex >= 0 && p.ItemIndex < p.Items.Count && p.Items[p.ItemIndex].Candidate is { } c && MountObserverCandidateMatches(f, c),
            ProgramCardTriggerWindowFrame p => p.CandidateIndex >= 0 && p.CandidateIndex < p.Candidates.Count && MountObserverCandidateMatches(f, ToSharedCandidate(p.Candidates[p.CandidateIndex])) && p.ParentFrameId == w.CardUse?.ParentCardUseFrameId && p.Action.ActionId == w.CardUse.CardActionId,
            _ => false
        };
    }
    private bool ValidMouZhangXiuOl(ProgramSkillFrame f)
    {
        if (f.MouZhangXiuOl is not { } r || r.Source != MouZhangXiuOlSource(f) || r.GameplayHash != f.GameplayHash || f.ActivationId != f.TriggerId || f.InstructionIndex != 1 || f.WindowContext is not { } w ||
            !Enum.IsDefined(r.Stage) || !MouZhangXiuOlComposition.Handles(r.Operation) || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash ||
            GetPausedPrivateOfferEffect(f).Op != r.Operation || !MouZhangXiuOlParentMatches(f) || ProgramEventHistory<MouZhangXiuOlStartedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.GameplayHash == f.GameplayHash && e.Operation == r.Operation) != 1) return false;
        if (r.Operation is SkillProgramEffectOp.ResolveMouZhangXiuQiulie or SkillProgramEffectOp.UseMouZhangXiuQiulieSlash)
        {
            if (f.SkillId != MouZhangXiuOlComposition.Qiulie || r.Phase is not { } p || !IsRecordedActualDiscardRecoveryPhase(p) || p.ActorSeat != f.OwnerSeat || p.ActualTurnOwnerSeat != f.OwnerSeat ||
                p.Kind != MouZhangXiuOlComposition.Phase(w.Window)) return false;
            if (r.Stage == MouZhangXiuOlStage.Target) return r.Operation == SkillProgramEffectOp.ResolveMouZhangXiuQiulie && r.TargetSeat == -1 && r.PaidCardId is null;
            if (!IsValidPlayerSeat(r.TargetSeat) || r.TargetSeat == f.OwnerSeat || ProgramEventHistory<MouZhangXiuOlHuntChosenEvent>().Count(e => e.Source.OwnerSeat == f.OwnerSeat && e.TargetSeat == r.TargetSeat && e.ActualTurn == p.TurnNumber) != 1 ||
                ProgramEventHistory<MouZhangXiuOlPhaseOfferedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.Phase == p && e.TargetSeat == r.TargetSeat) != 1) return false;
            if (r.Stage == MouZhangXiuOlStage.Cost) return r.PaidCardId is null && r.UseId == 0;
            if (!MouZhangXiuOlPaymentMatches(f)) return false;
            if (r.Stage == MouZhangXiuOlStage.CostChildren || r.Stage == MouZhangXiuOlStage.Issuing) return r.UseId == 0 && !r.NativeReturned;
            return r.Stage is MouZhangXiuOlStage.NativeUse or MouZhangXiuOlStage.Children && r.UseId > 0 && r.ActionId > 0 &&
                ProgramEventHistory<MouZhangXiuOlQiulieIssuedEvent>().Count(e => e.Origin.FrameId == f.Id && e.Origin.Source == r.Source && e.Origin.GameplayHash == f.GameplayHash && e.Origin.UseId == r.UseId && e.Origin.ActionId == r.ActionId && e.Origin.TargetSeat == r.TargetSeat && e.Origin.Phase == p) == 1;
        }
        if (w.CardUse?.ParentCardUseFrameId != r.UseId || w.CardUse.CardActionId != r.ActionId || LifecycleCardUse(r.UseId) is not { Action: { } a } use || a.ActionId != r.ActionId) return false;
        if (r.Operation == SkillProgramEffectOp.DefendMouZhangXiuQiulieSlash)
            return f.SkillId == MouZhangXiuOlComposition.Qiulie && w.Window == SkillProgramTriggerWindow.CardUseBeforeTargetEffects && use.MouZhangXiuOlQiulie is { } origin && origin.TargetSeat == r.TargetSeat &&
                origin.Source.OwnerSeat == f.OwnerSeat && origin.Source.SkillInstanceId == f.SkillInstanceId && origin.GameplayHash == f.GameplayHash && IsMouZhangXiuOlSlash(use) &&
                (r.Stage == MouZhangXiuOlStage.Defense && r.PaidCardId is null || r.Stage == MouZhangXiuOlStage.DefenseChildren && MouZhangXiuOlPaymentMatches(f));
        if (f.SkillId != MouZhangXiuOlComposition.Zhuijiao || MouZhangXiuOlEntry(use, new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, 0)) is not { } entry) return false;
        if (r.Operation == SkillProgramEffectOp.ResolveMouZhangXiuZhuijiao)
            return w.Window == SkillProgramTriggerWindow.CardUseCommitted && (r.Stage == MouZhangXiuOlStage.Drawing && r.Draw is null || r.Stage == MouZhangXiuOlStage.DrawChildren && r.Draw is { RequestedCount: 1 } d &&
                d.RecipientSeat == f.OwnerSeat && r.Before == d.SequenceBefore && r.After == d.SequenceAfter && ProgramEventHistory<MouZhangXiuOlDrawReturnedEvent>().Count(e => e.FrameId == f.Id && e.UseId == use.Id && e.Requested == 1 && e.Actual == d.ActualDrawIds.Count && e.Replacement == d.ReplacementGainedIds.Count) == 1);
        return r.Operation == SkillProgramEffectOp.CompleteMouZhangXiuZhuijiao && w.Window == SkillProgramTriggerWindow.CardUseCompleted && entry.Boosted && entry.DrawReturned && entry.PenaltyIssued &&
            !MouZhangXiuOlUseDamaged(use.Id, null) && (r.Stage == MouZhangXiuOlStage.Penalty && r.PaidCardId is null || r.Stage == MouZhangXiuOlStage.PenaltyChildren && MouZhangXiuOlPaymentMatches(f));
    }
    private bool MouZhangXiuOlPaymentMatches(ProgramSkillFrame f)
    {
        var r = f.MouZhangXiuOl!; if (r.PaidCardId is not { } card || r.Before < 0 || r.After < r.Before || r.After > _movementSequence) return false;
        var defense = r.Operation == SkillProgramEffectOp.DefendMouZhangXiuQiulieSlash; var payer = defense ? r.TargetSeat : f.OwnerSeat;
        var reason = defense ? MouZhangXiuOlDefenseReason : r.Operation == SkillProgramEffectOp.CompleteMouZhangXiuZhuijiao ? MouZhangXiuOlPenaltyReason : MouZhangXiuOlCostReason;
        return ProgramEventHistory<MouZhangXiuOlDiscardPaidEvent>().Count(e => e.FrameId == f.Id && e.PayerSeat == payer && e.CardId == card && e.Before == r.Before && e.After == r.After && e.Defense == defense) == 1 &&
            _cardMovements.Count(m => m.CardId == card && m.From.OwnerSeat == payer && m.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment && m.Sequence > r.Before && m.Sequence <= r.After && m.Reason.Value == reason) == 1;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 33000)]
    private void AssertMouZhangXiuOl(ProgramSkillFrame f)
    { if (f.MouZhangXiuOl is not null && !ValidMouZhangXiuOl(f)) throw new InvalidOperationException("Mou Zhang Xiu lost its exact phase, source, native card parent or paid entity receipt."); }
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 33000)]
    private bool IsMouZhangXiuOlMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation movement) => f.MouZhangXiuOl is { Stage: MouZhangXiuOlStage.CostChildren or MouZhangXiuOlStage.DefenseChildren or MouZhangXiuOlStage.PenaltyChildren or MouZhangXiuOlStage.DrawChildren or MouZhangXiuOlStage.Children } r &&
        effect?.Op == r.Operation && movement == f.PendingMovementContinuation && movement.SubjectSeat == f.OwnerSeat && movement.BeforeCount == 0 && movement.CoverageResultBind is null && ValidMouZhangXiuOl(f);
    private bool MouZhangXiuOlLionPayment(ProgramSkillFrame f, int seat, CardMoveReason? reason) => f.MouZhangXiuOl is { PaidCardId: { } card } r &&
        r.Stage is MouZhangXiuOlStage.CostChildren or MouZhangXiuOlStage.DefenseChildren or MouZhangXiuOlStage.PenaltyChildren &&
        seat == (r.Operation == SkillProgramEffectOp.DefendMouZhangXiuQiulieSlash ? r.TargetSeat : f.OwnerSeat) &&
        _cardMovements.Any(m => m.CardId == card && m.CardKind == CardKind.SilverLion && m.From == CardLocation.Equipment(seat) && m.Sequence > r.Before && m.Sequence <= r.After && m.Reason == reason);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 33000)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 33000)]
    private bool IsMouZhangXiuOlRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery) => ValidMouZhangXiuOl(f) && f.PendingMovementContinuation is not null && RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement &&
        recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.SourceSeat == recovery.Attempt.TargetSeat && MouZhangXiuOlLionPayment(f, recovery.Attempt.TargetSeat, recovery.Attempt.Completion.MoveReason);
    private bool MouZhangXiuOlFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.MouZhangXiuOl is not { } r || !ValidMouZhangXiuOl(f)) return false;
        if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == MouZhangXiuOlStage.Drawing && f.NativeDraw is not null;
        if (child is RecoveryReplacementFrame recovery) return IsMouZhangXiuOlRecoveryParent(f, recovery);
        if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement && hp.Change.Kind == HpChangeKind.Recovery &&
            hp.Change.SourceSeat == hp.Change.TargetSeat && MouZhangXiuOlLionPayment(f, hp.Change.TargetSeat, new(r.Operation == SkillProgramEffectOp.DefendMouZhangXiuQiulieSlash ? MouZhangXiuOlDefenseReason : r.Operation == SkillProgramEffectOp.CompleteMouZhangXiuZhuijiao ? MouZhangXiuOlPenaltyReason : MouZhangXiuOlCostReason));
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } pending && IsMouZhangXiuOlMovement(f, GetPausedPrivateOfferEffect(f), pending)) return ProgramMovementReturnMatches(f, moved) &&
            moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After);
        if (r.Stage == MouZhangXiuOlStage.NativeUse && child is CardUseFrame native) return IsMouZhangXiuOlSlash(native);
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id && (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram || state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange &&
            ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouZhangXiuOlNativeEdge(int n)
    {
        if (_resolutionStack[n - 1] is CardUseFrame { MouZhangXiuOlQiulie: not null } use && IsMouZhangXiuOlSlash(use) && _resolutionStack[n] is ProgramCardTriggerWindowFrame window) return window.ParentFrameId == use.Id && window.Action.ActionId == use.Action!.ActionId;
        if (_resolutionStack[n - 1] is ProgramCardTriggerWindowFrame parent && LifecycleCardUse(parent.ParentFrameId) is { MouZhangXiuOlQiulie: not null } native && IsMouZhangXiuOlSlash(native) && _resolutionStack[n] is ProgramSkillFrame f && parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count)
        { var c = parent.Candidates[parent.CandidateIndex]; return MountObserverCandidateMatches(f, ToSharedCandidate(c)) && f.WindowContext == CreateCardActionProgramContext(parent, c); }
        return false;
    }
    private bool MouZhangXiuOlObserverEdge(int n) => IsMovementObserverEdge(n) || IsSameNameHandObserverEdge(n) || IsYanPuDyingExitEdge(n) || TypedDamagePostEventEdge(_resolutionStack[n - 1], _resolutionStack[n]) || MouZhangXiuOlNativeEdge(n) ||
        _resolutionStack[n - 1] is ProgramSkillFrame f && MouZhangXiuOlFirstChild(f, _resolutionStack[n]) || NativeDrawAttemptRidesOn(_resolutionStack[n], _resolutionStack[n - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[n], _resolutionStack[n - 1]);
    private bool MouZhangXiuOlObserverSuffix(int n) => HasObserverSuffix(n, static (g, i) => g.MouZhangXiuOlObserverEdge(i), static (g, i, d) => g.IsSuspendedOrPaidRescueObserverSuffix(i, d));
    private ProgramSkillFrame? MouZhangXiuOlObserverRoot() => FindProgramObserverRoot(static (g, f, c) => g.MouZhangXiuOlFirstChild(f, c), static (g, n) => g.MouZhangXiuOlObserverSuffix(n));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 33000)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 33000)]
    private bool HasMouZhangXiuOlDamage(long id) => MouZhangXiuOlObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 33000)]
    private bool HasMouZhangXiuOlBeforeDamage(long id) => MouZhangXiuOlObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 33000)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 33000)]
    private bool IsMouZhangXiuOlDying() => ActiveDying is { } d && MouZhangXiuOlObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == d.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 33000)] [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 33000)]
    private bool AllowsMouZhangXiuOlNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || MouZhangXiuOlObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature && target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 33000)] private bool AdvanceMouZhangXiuOlChildren() => _pendingDecision is null && MouZhangXiuOlObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 33000)] [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 33000)]
    private bool ContinuePaidMouZhangXiuOl(ProgramSkillFrame f) => f.MouZhangXiuOl is { } r && ValidMouZhangXiuOl(f) && (r.PaidCardId is not null || r.Operation is SkillProgramEffectOp.ResolveMouZhangXiuZhuijiao or SkillProgramEffectOp.CompleteMouZhangXiuZhuijiao or SkillProgramEffectOp.DefendMouZhangXiuQiulieSlash) ||
        f.MouZhangXiuOl is null && f.InstructionIndex is 0 or 1 && f.WindowContext is { } w && CanRunIssuedMouZhangXiuOl(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, 0), w);
}
