namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidMouHxBojue(ProgramSkillFrame f)
    {
        if (f.MouHxBojue is not { } r || f.SkillId != MouHuaXiongOlComposition.Bojue || f.ActivationId != "simultaneous-choice" || f.TriggerId is not null ||
            f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats is not [var peer] || peer != r.Peer || peer == f.OwnerSeat ||
            !IsValidPlayerSeat(peer) || r.Source != MouHxBojueSource(f) || r.GameplayHash != f.GameplayHash || r.Turn != _turnNumber ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash || GetPausedPrivateOfferEffect(f)?.Op != SkillProgramEffectOp.ResolveMouHuaXiongBojue ||
            !Enum.IsDefined(r.Stage) || r.Cursor is < 0 or > 2 || r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            ProgramEventHistory<MouHxBojueStartedEvent>().Count(e => e == new MouHxBojueStartedEvent(f.Id, r.Source, r.GameplayHash, r.Turn, peer, r.InitialHandSum)) != 1) return false;
        if (r.Votes.Count > 2 || r.Votes.Where((v, i) => v.Seat != (i == 0 ? f.OwnerSeat : peer)).Any()) return false;
        if (r.Stage == MouHxBojueStage.Commit) return r.Cursor == 0 && r.Delta is null && f.PendingMovementContinuation is null;
        if (r.Votes.Count != 2) return false;
        var revealed = ProgramEventHistory<MouHxBojueVoteRevealedEvent>().Where(e => e.FrameId == f.Id).OrderBy(e => e.Index).ToArray();
        if (revealed.Length != 2 || revealed.Where((e, i) => e.Index != i || e.Seat != r.Votes[i].Seat || e.Draw != r.Votes[i].Draw).Any()) return false;
        if (r.Delta is { } delta && ProgramEventHistory<MouHxBojueDeltaEvent>().Count(e => e.FrameId == f.Id && e.Before == r.InitialHandSum && e.AbsoluteChange == delta && Math.Abs(e.After - e.Before) == delta) != 1) return false;
        if (r.Stage is MouHxBojueStage.Children or MouHxBojueStage.NativeChildren)
        {
            if (f.PendingMovementContinuation is not { BeforeCount: 0, CoverageResultBind: null } p || p.SubjectSeat != f.OwnerSeat) return false;
            if (r.Stage == MouHxBojueStage.NativeChildren) return r.Returned && r.UseId > 0 && LifecycleCardUse(r.UseId) is null &&
                ProgramEventHistory<MouHxBojueSlashReturnedEvent>().Count(e => e.FrameId == f.Id && e.Cursor == r.Cursor && e.UseId == r.UseId && e.ActionId == r.ActionId) == 1;
            if (r.DrawOutcome is { } o) return r.Delta is null && r.PaidCard is null && o.RecipientSeat == MouHxBojueActor(f) && o.RequestedCount == 1 &&
                r.Before == o.SequenceBefore && r.After == o.SequenceAfter && ProgramEventHistory<MouHxBojueDrawReturnedEvent>().Count(e => e.FrameId == f.Id && e.Cursor == r.Cursor &&
                    e.Recipient == o.RecipientSeat && e.Actual == o.ActualDrawIds.Count && e.Replacement == o.ReplacementGainedIds.Count && e.AttemptId == o.AttemptFrameId) == 1;
            return r.PaidCard is { } id && r.PaidBy == MouHxBojueActor(f) && ProgramEventHistory<MouHxBojueDiscardedEvent>().Count(e =>
                e.FrameId == f.Id && e.Cursor == r.Cursor && e.CardId == id && e.Executor == r.PaidBy && e.Followup == (r.Delta is not null) &&
                e.Sequence > r.Before && e.Sequence <= r.After && _cardMovements.Any(m => m.Sequence == e.Sequence && m.CardId == id && m.From == e.From && m.To == e.To && m.Reason.Value == MouHxBojueDiscardReason)) == 1;
        }
        return f.PendingMovementContinuation is null;
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 33300)]
    private void AssertMouHxBojue(ProgramSkillFrame f)
    {
        if (f.MouHxBojue is null) return;
        if (!ValidMouHxBojue(f)) throw new InvalidOperationException("Bojue lost its original two commitments, native payment, or original activation cursor.");
        if (_resolutionStack.LastOrDefault()?.Id == f.Id && _pendingDecision is { } p &&
            f.MouHxBojue.Stage is MouHxBojueStage.Commit or MouHxBojueStage.Discard or MouHxBojueStage.OtherDiscard &&
            (p.PlayerSeat != MouHxBojueChooser(f) || !p.IsPrivate || !AssistedChoicesEqual(p.Choices, MouHxBojueChoices(f))))
            throw new InvalidOperationException("Bojue's private committed option or hidden native card slot was changed.");
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 33300)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 33300)]
    private bool ContinuePaidMouHxBojue(ProgramSkillFrame f) => f.MouHxBojue is { Votes.Count: 2 } && ValidMouHxBojue(f);

    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 33300)]
    private bool IsMouHxBojueMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        f.MouHxBojue is { Stage: MouHxBojueStage.Children or MouHxBojueStage.NativeChildren } &&
        effect?.Op == SkillProgramEffectOp.ResolveMouHuaXiongBojue && pending == f.PendingMovementContinuation &&
        pending.SubjectSeat == f.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null && ValidMouHxBojue(f);

    private bool MouHxBojueLionPayment(ProgramSkillFrame f, int seat, CardMoveReason? reason) => f.MouHxBojue is { Stage: MouHxBojueStage.Children, PaidCard: { } id } r &&
        _cardMovements.Any(m => m.CardId == id && m.CardKind == CardKind.SilverLion && m.From == CardLocation.Equipment(seat) &&
            m.Sequence > r.Before && m.Sequence <= r.After && m.Reason == reason);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 33300)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 33300)]
    private bool IsMouHxBojueRecovery(ProgramSkillFrame f, RecoveryReplacementFrame recovery) => ValidMouHxBojue(f) &&
        f.PendingMovementContinuation is not null && RecoveryReplacementFrameRidesOn(recovery, f) &&
        recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion &&
        recovery.Attempt.SourceSeat == recovery.Attempt.TargetSeat && MouHxBojueLionPayment(f, recovery.Attempt.TargetSeat, recovery.Attempt.Completion.MoveReason);

    private bool MouHxBojueFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.MouHxBojue is not { } r || !ValidMouHxBojue(f)) return false;
        if (child is CardUseFrame native) return r.Stage == MouHxBojueStage.NativeUse && IsMouHxBojueUse(native);
        if (NativeDrawAttemptRidesOn(child, f)) return r.Stage == MouHxBojueStage.Drawing && f.NativeDraw is not null;
        if (child is RecoveryReplacementFrame recovery) return IsMouHxBojueRecovery(f, recovery);
        if (child is CardsMovedTriggerWindowFrame moved && f.PendingMovementContinuation is not null)
            return ProgramMovementReturnMatches(f, moved) && moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId &&
                moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.All(m => _cardMovements.Contains(m) &&
                    (r.Stage == MouHxBojueStage.NativeChildren ? moved.Batch.ParentFrameId == r.UseId : m.Sequence > r.Before && m.Sequence <= r.After));
        if (child is HpChangedTriggerWindowFrame hp) return hp.Change.ParentFrameId == f.Id && hp.ResumeFrameId == f.Id &&
            hp.Continuation == PostEventContinuation.AwaitedProgramMovement && hp.Change.Kind == HpChangeKind.Recovery &&
            hp.Change.SourceSeat == hp.Change.TargetSeat && MouHxBojueLionPayment(f, hp.Change.TargetSeat, new(MouHxBojueDiscardReason));
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && ProgramEventHistory<CharacterStateChangedEvent>().Any(e => e.Change.Id == state.Id && e.Change.ParentFrameId == f.Id));
    }
    private bool MouHxBojueNativeEdge(int i)
    {
        if (_resolutionStack[i - 1] is CardUseFrame { MouHxBojueSlash: not null } use && IsMouHxBojueUse(use) && _resolutionStack[i] is ProgramCardTriggerWindowFrame w)
            return w.ParentFrameId == use.Id && w.Action.ActionId == use.Action!.ActionId;
        if (_resolutionStack[i - 1] is ProgramCardTriggerWindowFrame parent && LifecycleCardUse(parent.ParentFrameId) is { MouHxBojueSlash: not null } native &&
            IsMouHxBojueUse(native) && _resolutionStack[i] is ProgramSkillFrame f && parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count)
        {
            var c = parent.Candidates[parent.CandidateIndex];
            return MountObserverCandidateMatches(f, ToSharedCandidate(c)) && f.WindowContext == CreateCardActionProgramContext(parent, c);
        }
        return false;
    }
    private bool MouHxBojueObserverEdge(int i) => IsMovementObserverEdge(i) || IsSameNameHandObserverEdge(i) || IsYanPuDyingExitEdge(i) ||
        TypedDamagePostEventEdge(_resolutionStack[i - 1], _resolutionStack[i]) || MouHxBojueNativeEdge(i) ||
        _resolutionStack[i - 1] is ProgramSkillFrame f && MouHxBojueFirstChild(f, _resolutionStack[i]) ||
        NativeDrawAttemptRidesOn(_resolutionStack[i], _resolutionStack[i - 1]) || RecoveryReplacementFrameRidesOn(_resolutionStack[i], _resolutionStack[i - 1]);
    private ProgramSkillFrame? MouHxBojueObserverRoot() => FindProgramObserverRoot(static (g, f, child) => g.MouHxBojueFirstChild(f, child),
        static (g, i) => g.HasObserverSuffix(i, static (e, n) => e.MouHxBojueObserverEdge(n), static (e, n, d) => e.IsSuspendedOrPaidRescueObserverSuffix(n, d)));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 33300)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 33300)]
    private bool IsMouHxBojueDamageObserver(long id) => MouHxBojueObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 33300)]
    private bool IsMouHxBojueBeforeDamageObserver(long id) => MouHxBojueObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 33300)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 33300)]
    private bool IsMouHxBojueDying() => ActiveDying is { } d && MouHxBojueObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == d.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.BeforeFactionDebts, 33300)]
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 33300)]
    private bool AllowsMouHxBojueNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || MouHxBojueObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 33300)]
    private bool AdvanceMouHxBojueChildren() => _pendingDecision is null && MouHxBojueObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
