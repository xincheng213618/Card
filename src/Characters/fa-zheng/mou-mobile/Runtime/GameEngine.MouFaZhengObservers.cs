namespace CardGame.Core;
public sealed record MouFaZhengRosterEvent(long FrameId, int Index, long MarkId) : IGameEvent;
public sealed partial class GameEngine
{
    private bool ValidMouFaZheng(ProgramSkillFrame f)
    {
        if (f.MouFaZheng is not { } r || r.Source != MouFaZhengSource(f) || r.GameplayHash != f.GameplayHash || f.InstructionIndex != 1 ||
            !MouFaZhengComposition.Handles(r.Op) || GetPausedPrivateOfferEffect(f)?.Op != r.Op || !Enum.IsDefined(r.Stage) ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash || r.Index < 0 || r.Index > r.Marks.Count || r.Before < 0 || r.Before > r.After || r.After > _movementSequence ||
            ProgramEventHistory<MouFaZhengStartedEvent>().Count(e => e == new MouFaZhengStartedEvent(f.Id, r.Source, r.GameplayHash, r.Op, r.ParentId)) != 1) return false;
        if (r.Op == SkillProgramEffectOp.IssueMouFaZhengXuanhuo)
        {
            if (f.SkillId != MouFaZhengComposition.Xuanhuo || f.TriggerId is not null || f.ActivationId != "give-xuan" || f.WindowContext is not null ||
                r.Phase is not { Kind: ActualDiscardRecoveryPhaseKind.Play } phase || phase.ActorSeat != f.OwnerSeat || !IsRecordedActualDiscardRecoveryPhase(phase) ||
                ProgramEventHistory<MouFaZhengActivationPhaseEvent>().Count(e => e == new MouFaZhengActivationPhaseEvent(f.Id, f.OwnerSeat, phase)) != 1) return false;
        }
        else
        {
            if (f.ActivationId != f.TriggerId || f.WindowContext is not { } context || context.ParentFrameId != r.ParentId) return false;
            var index = _resolutionStack.FindIndex(p => p.Id == f.Id);
            if (index < 1 || _resolutionStack[index - 1].Id != r.ParentId) return false;
            if (r.Op == SkillProgramEffectOp.ResolveMouFaZhengEnyuan)
            {
                if (f.SkillId != MouFaZhengComposition.Enyuan || f.TriggerId != "settle-xuan" || context.Window != SkillProgramTriggerWindow.TurnStartBeforeNormalFlow ||
                    _resolutionStack[index - 1] is not ProgramLifecycleTriggerWindowFrame parent || parent.Window != context.Window ||
                    parent.CandidateIndex < 0 || parent.CandidateIndex >= parent.Candidates.Count || !MountObserverCandidateMatches(f, parent.Candidates[parent.CandidateIndex]) ||
                    !r.Marks.Select((mark, n) => new MouFaZhengRosterEvent(f.Id, n, mark)).SequenceEqual(ProgramEventHistory<MouFaZhengRosterEvent>().Where(e => e.FrameId == f.Id))) return false;
            }
            else if (f.SkillId != MouFaZhengComposition.Xuanhuo || f.TriggerId != "marked-outside-draw-gain" ||
                MouFaZhengQualification(new(f.OwnerSeat, f.SkillId, GetProgramBindingId(f), f.SkillInstanceId, f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex), context) is not { } q ||
                q.MarkId != r.MarkId || q.TargetSeat != r.Target) return false;
        }
        if (r.Stage == MouFaZhengStage.MovementChildren)
        {
            var reason = r.MovementPurpose == "gift" ? MouFaZhengGiftReason : r.MovementPurpose == "take" ? MouFaZhengTakeReason : r.MovementPurpose == "repay" ? MouFaZhengRepayReason : null;
            if (reason is null || !r.Accepted || r.PaidCardId <= 0 || r.After <= r.Before || !ProgramEventHistory<MouFaZhengMovementPaidEvent>().Any(e => e.FrameId == f.Id &&
                e.Purpose == r.MovementPurpose && e.MarkId == r.MarkId && e.Sequence > r.Before && e.Sequence <= r.After && _cardMovements.Any(m => m.CardId == r.PaidCardId &&
                    m.Sequence == e.Sequence && m.From.OwnerSeat == e.SourceSeat && m.Reason.Value == reason && (m.To == CardLocation.Hand(e.TargetSeat) || m.To == CardLocation.OutsideGame)))) return false;
        }
        return true;
    }
    private bool DrainMouFaZhengChildren(ProgramSkillFrame f) => TryBeginQueuedRecoveryReplacement(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(f.Id, CharacterStateContinuation.Program) || TryBeginHpChangedProgramWindow(f.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(f.Id) || TryBeginAdvancedSkillsChanged(f.Id);
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 31800)]
    private bool IsMouFaZhengMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) => e is not null && MouFaZhengComposition.Handles(e.Op) && ValidMouFaZheng(f) &&
        f.MouFaZheng is { Accepted: true, Stage: MouFaZhengStage.MovementChildren or MouFaZhengStage.LosingHp or MouFaZhengStage.LossChildren or MouFaZhengStage.RecoveryChildren } &&
        p == f.PendingMovementContinuation && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private bool IsMouFaZhengSilverLionChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.MouFaZheng is not { Stage: MouFaZhengStage.MovementChildren } r || !ValidMouFaZheng(f) ||
            !_cardMovements.Any(m => m.CardId == r.PaidCardId && m.CardKind == CardKind.SilverLion && m.From == CardLocation.Equipment(f.OwnerSeat) && m.Sequence > r.Before && m.Sequence <= r.After)) return false;
        var reason = r.MovementPurpose == "gift" ? MouFaZhengGiftReason : MouFaZhengRepayReason;
        if (child is RecoveryReplacementFrame recovery) return RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement &&
            recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason?.Value == reason &&
            recovery.Attempt.Amount == 1 && recovery.Attempt.SourceSeat == f.OwnerSeat && recovery.Attempt.TargetSeat == f.OwnerSeat;
        return child is HpChangedTriggerWindowFrame hp && hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement &&
            hp.Change.Kind == HpChangeKind.Recovery && hp.Change.SourceSeat == f.OwnerSeat && hp.Change.TargetSeat == f.OwnerSeat && ProgramEventHistory<SilverLionRemovedRecoveryEvent>().Any(e =>
                e.ResolutionId > f.Id && e.ResolutionId < hp.Change.Id && e.PlayerSeat == f.OwnerSeat && e.Reason.Value == reason && e.RecoveredAmount == hp.Change.Amount && e.RemainingHp == hp.Change.HpAfter);
    }
    private bool MouFaZhengFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidMouFaZheng(f) || f.MouFaZheng is not { } r || !r.Accepted) return false;
        if (IsMouFaZhengSilverLionChild(f, child)) return true;
        if (child is RecoveryReplacementFrame recovery) return IsMouFaZhengDirectRecovery(f, recovery);
        if (child is DyingFrame dying) return r.Stage == MouFaZhengStage.LossChildren && r.LossId > 0 && r.LossHpAfter == 0 &&
            dying.ParentFrameId == f.Id && dying.VictimSeat == r.Target && dying.KillerSeat is null && dying.Continuation == DyingContinuationKind.ProgramSkill;
        if (child is IdentityAppearanceFrame identity) return r.Stage == MouFaZhengStage.LosingHp && identity.Receipt.Entry == IdentityAppearanceEntry.ProgramHpLoss &&
            identity.Receipt.ParentFrameId == f.Id && identity.Receipt.OwnerSeat == r.Target && identity.Receipt.RequestedDeduction == 1 && identity.Receipt.OriginalInstructionIndex == f.InstructionIndex;
        if (child is HpChangedTriggerWindowFrame hp)
            return r.Stage == MouFaZhengStage.LossChildren && ProgramHpReturnMatches(f, hp) && hp.Change.Id == r.LossId && hp.Change.Kind == HpChangeKind.Loss && hp.Change.TargetSeat == r.Target ||
                r.Stage == MouFaZhengStage.RecoveryChildren && ProgramRecoveryReceiptMatches(f, hp, CompleteProgramEventHistory().ToArray()) &&
                hp.Change.SourceSeat == f.OwnerSeat && hp.Change.TargetSeat == f.OwnerSeat && hp.Change.HpBefore == r.RecoveryHpBefore;
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is { } p && IsMouFaZhengMovement(f, GetPausedPrivateOfferEffect(f), p))
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.ParentFrameId == f.Id && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId &&
                moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 && moved.Batch.Movements.All(m => _cardMovements.Contains(m) &&
                    (m.Sequence > r.Before && m.Sequence <= r.After || r.Stage == MouFaZhengStage.RecoveryChildren));
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool IsMouFaZhengDirectRecovery(ProgramSkillFrame f, RecoveryReplacementFrame recovery) => f.MouFaZheng is { Stage: MouFaZhengStage.RecoveryChildren } r && ValidMouFaZheng(f) &&
        recovery.ParentFrameId == f.Id && recovery.Attempt.SourceSeat == f.OwnerSeat && recovery.Attempt.TargetSeat == f.OwnerSeat && recovery.Attempt.HpBefore == r.RecoveryHpBefore &&
        ProgramRecoveryReplacementReceiptMatches(f, recovery, CompleteProgramEventHistory().ToArray());
    private bool MouFaZhengObserverEdge(int n) => IsMovementObserverEdge(n) || IsSameNameHandObserverEdge(n) || IsYanPuDyingExitEdge(n) ||
        TypedDamagePostEventEdge(_resolutionStack[n - 1], _resolutionStack[n]) || _resolutionStack[n - 1] is ProgramSkillFrame f && MouFaZhengFirstChild(f, _resolutionStack[n]) ||
        NativeDrawAttemptRidesOn(_resolutionStack[n], _resolutionStack[n - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[n], _resolutionStack[n - 1]);
    private bool MouFaZhengObserverSuffix(int n) => _resolutionStack[n + 1] is DyingFrame dying && n + 2 < _resolutionStack.Count && IsSuspendedOrPaidRescueObserverSuffix(n + 1, dying) ||
        HasObserverSuffix(n, static (g, i) => g.MouFaZhengObserverEdge(i), static (g, i, d) => g.IsSuspendedOrPaidRescueObserverSuffix(i, d));
    private ProgramSkillFrame? MouFaZhengObserverRoot() => FindProgramObserverRoot(static (g, f, c) => g.MouFaZhengFirstChild(f, c), static (g, i) => g.MouFaZhengObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 31800)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 31800)]
    private bool HasMouFaZhengDamageObserver(long id) => MouFaZhengObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 31800)]
    private bool HasMouFaZhengBeforeDamageObserver(long id) => MouFaZhengObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 31800)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 31800)]
    private bool IsMouFaZhengDying() => ActiveDying is { } dying && MouFaZhengObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 31800)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 31800)]
    private bool AllowsMouFaZhengNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || MouFaZhengObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f); return e is { Op: SkillProgramEffectOp.Damage } && amount == e.Amount && source == e.ActorReference && nature == e.DamageNature &&
            target == (e.TargetReference is { } participant ? ResolveProgramParticipant(f, participant) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 31800)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 31800)]
    private bool IsMouFaZhengRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (IsMouFaZhengSilverLionChild(f, recovery) || IsMouFaZhengDirectRecovery(f, recovery)) return true;
        var n = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return n >= 0 && child > n + 1 && MouFaZhengFirstChild(f, _resolutionStack[n + 1]) && MouFaZhengObserverSuffix(n) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 31800)]
    private bool ResumeMouFaZheng(long id)
    { if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { MouFaZheng: not null } f || f.Id != id) return false; ContinueMouFaZheng(id); return true; }
    [ProgramContinuation(ProgramContinuationKind.Movement, 31800)]
    private bool ReturnMouFaZhengMovement(ProgramSkillFrame f) => f.PendingMovementContinuation is not null && f.MouFaZheng is not null && ResumeMouFaZheng(f.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 31800)]
    private bool AdvanceMouFaZhengChildren() => _pendingDecision is null && MouFaZhengObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 31800)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 31800)]
    private bool ContinueAcceptedMouFaZheng(ProgramSkillFrame f) => f.MouFaZheng is { Accepted: true } && ValidMouFaZheng(f);
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 31800)]
    private void AssertMouFaZheng(ProgramSkillFrame f)
    { if (f.MouFaZheng is not null && !ValidMouFaZheng(f)) throw new InvalidOperationException("Mou Fa Zheng lost the exact issuer, mark, paid entity or native return."); }
}
