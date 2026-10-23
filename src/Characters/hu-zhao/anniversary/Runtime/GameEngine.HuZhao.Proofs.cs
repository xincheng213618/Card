namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ValidHuZhaoMidu(ProgramSkillFrame f)
    {
        if (f.HuZhaoMidu is not { } r || f.SkillId != HuZhaoComposition.Midu || f.TriggerId is not null || f.ActivationId != "abolish-or-restore" ||
            f.InstructionIndex is < 1 or > 2 || r.Stage != HuZhaoMiduStage.Completed && f.InstructionIndex != 1 ||
            f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0 || r.Source != HuZhaoSource(f) || r.GameplayHash != f.GameplayHash ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash || r.Turn != _turnNumber || r.TurnOwner != _turnProgression.OwnerSeat ||
            r.TurnOwner != f.OwnerSeat || r.AvailableMask is < 0 or > 63 || r.SelectedMask is < 0 or > 63 || !Enum.IsDefined(r.Stage) ||
            r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            ProgramEventHistory<ProgramSkillStartedEvent>().Count(e => e.FrameId == f.Id && e.OwnerSeat == f.OwnerSeat && e.SkillId == f.SkillId && e.ActivationId == f.ActivationId) != 1 ||
            ProgramEventHistory<HuZhaoMiduStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var start] ||
            start != new HuZhaoMiduStartedEvent(f.Id, r.Source, r.GameplayHash, r.Turn, r.TurnOwner, r.AvailableMask)) return false;
        var payments = ProgramEventHistory<HuZhaoMiduPaidEvent>().Where(e => e.FrameId == f.Id).ToArray();
        var completed = ProgramEventHistory<HuZhaoMiduCompletedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (!r.Paid) return r.Stage is HuZhaoMiduStage.Areas or HuZhaoMiduStage.Recipient && !r.Restore && r.Recipient == -1 && r.Draw is null && payments.Length == 0 && completed.Length == 0 &&
            r.Before == 0 && r.After == 0 && (r.Stage != HuZhaoMiduStage.Areas || r.SelectedMask == 0) && (r.SelectedMask & r.AvailableMask) == r.SelectedMask;
        if (payments is not [var paid] || paid.Source != r.Source || paid.GameplayHash != r.GameplayHash || paid.SelectedMask != r.SelectedMask || paid.Restore != r.Restore ||
            paid.Recipient != r.Recipient || paid.Before < 0 || paid.After != paid.Before || paid.Before > r.Before || !IsValidPlayerSeat(r.Recipient) ||
            r.Stage is HuZhaoMiduStage.Areas or HuZhaoMiduStage.Recipient ||
            (r.Restore ? HuZhaoCount(r.SelectedMask) != 1 || (r.SelectedMask & r.AvailableMask) != 0 || r.Recipient != f.OwnerSeat || r.Draw is not null : (r.SelectedMask & r.AvailableMask) != r.SelectedMask)) return false;
        if (r.Stage == HuZhaoMiduStage.Completed)
            return completed is [var done] && done == new HuZhaoMiduCompletedEvent(f.Id, r.Restore, r.Recipient, r.Restore ? 0 : HuZhaoCount(r.SelectedMask)) &&
                f.ChoiceBindings.Count(b => b.Name == "midu-branch" && b.ChooserSeat == f.OwnerSeat && b.OptionId == (r.Restore ? "restore" : "abolish")) == 1;
        if (completed.Length != 0 || r.Restore && r.Stage != HuZhaoMiduStage.CostChildren) return false;
        if (r.Stage == HuZhaoMiduStage.Drawing) return !r.Restore && HuZhaoCount(r.SelectedMask) > 0 && r.Draw is null;
        return r.Stage == HuZhaoMiduStage.CostChildren ? r.Draw is null : r.Stage == HuZhaoMiduStage.DrawChildren && r.Draw is { } draw &&
            draw.RecipientSeat == r.Recipient && draw.RequestedCount == HuZhaoCount(r.SelectedMask) && draw.SequenceBefore == r.Before && draw.SequenceAfter == r.After;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 43000)]
    private void AssertHuZhaoMidu(ProgramSkillFrame f)
    {
        if (f.HuZhaoMidu is not { } r) return;
        if (!ValidHuZhaoMidu(f)) throw new InvalidOperationException("Midu lost its exact original activation, once-issued area payment, native draw or next-start grant cursor.");
        if (f.PendingMovementContinuation is { } pending && !HuZhaoMovement(f, GetPausedPrivateOfferEffect(f), pending))
            throw new InvalidOperationException("Midu lost its paid native movement continuation.");
        if (_resolutionStack.LastOrDefault()?.Id == f.Id && _pendingDecision is { } p && r.Stage is HuZhaoMiduStage.Areas or HuZhaoMiduStage.Recipient &&
            (p.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual(p.Choices, HuZhaoChoices(f)))) throw new InvalidOperationException("Midu's current owner menu changed.");
    }
    private bool HasAnniversaryHuZhaoPrompt() => _pendingDecision is { Kind: DecisionKind.ProgramTrigger } p &&
        _resolutionStack.LastOrDefault() is ProgramSkillFrame { HuZhaoMidu: { Stage: HuZhaoMiduStage.Areas or HuZhaoMiduStage.Recipient } } f &&
        f.Step == ResolutionFrameStep.AwaitingResponse && p.PlayerSeat == f.OwnerSeat && ValidHuZhaoMidu(f) && AssistedChoicesEqual(p.Choices, HuZhaoChoices(f));
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 43000)]
    private bool HuZhaoMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        f.HuZhaoMidu is { Paid: true, Stage: HuZhaoMiduStage.CostChildren or HuZhaoMiduStage.DrawChildren } && ValidHuZhaoMidu(f) &&
        e?.Op == SkillProgramEffectOp.ResolveAnniversaryHuZhaoMidu && f.PendingMovementContinuation == p && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 43000)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 43000)]
    private bool HuZhaoRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery) =>
        f.HuZhaoMidu is { Stage: HuZhaoMiduStage.CostChildren, Restore: false } r && ValidHuZhaoMidu(f) &&
        recovery.ParentFrameId == f.Id && recovery.Return == new RecoveryReplacementReturn(PostEventContinuation.Program, f.Id) &&
        recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.Completion.MoveReason == new CardMoveReason("equipment.slot-abolished") &&
        recovery.Attempt.SourceSeat == f.OwnerSeat && recovery.Attempt.TargetSeat == f.OwnerSeat && recovery.Attempt.Amount == 1 &&
        _cardMovements.Any(m => m.Sequence > r.Before && m.Sequence <= r.After && m.From == CardLocation.Equipment(f.OwnerSeat) && m.Reason.Value == "equipment.slot-abolished" &&
            GetAdvancedCard(m.CardId).Kind == CardKind.SilverLion);
    private bool HuZhaoFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.HuZhaoMidu is not { } r || !ValidHuZhaoMidu(f)) return false;
        if (child is NativeDrawAttemptFrame draw) return r.Stage == HuZhaoMiduStage.Drawing && draw.Context.ParentFrameId == f.Id &&
            draw.Context.RecipientSeat == r.Recipient && draw.Context.RequestedCount == HuZhaoCount(r.SelectedMask);
        if (child is CardsMovedTriggerWindowFrame moved) return f.PendingMovementContinuation is not null && moved.ResumeProgramFrameId == f.Id && moved.Batch.ParentFrameId == f.Id &&
            moved.Batch.OriginOwnerSeat == f.OwnerSeat && moved.Batch.OriginSkillId == f.SkillId && moved.Batch.OriginSkillInstanceId == f.SkillInstanceId && moved.Batch.Movements.Count > 0 &&
            moved.Batch.Movements.All(m => _cardMovements.Contains(m) && m.Sequence > r.Before && m.Sequence <= r.After &&
                (m.Reason == CardMoveReasons.Reshuffle || r.Stage == HuZhaoMiduStage.DrawChildren && m.Reason.Value == HuZhaoDrawReason && m.From == CardLocation.DrawPile ||
                 r.Stage == HuZhaoMiduStage.CostChildren && (m.Reason.Value == "equipment.slot-abolished" && m.From == CardLocation.Equipment(f.OwnerSeat) ||
                    m.Reason.Value == HuZhaoJudgmentReason && m.From == CardLocation.Judgment(f.OwnerSeat))));
        if (child is RecoveryReplacementFrame recovery) return HuZhaoRecoveryParent(f, recovery);
        if (child is HpChangedTriggerWindowFrame hp) return r.Stage == HuZhaoMiduStage.CostChildren && hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id &&
            hp.Continuation == PostEventContinuation.Program && hp.Change.Kind == HpChangeKind.Recovery && hp.Change.TargetSeat == f.OwnerSeat && hp.Change.Amount == 1;
        return child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills && skills.ResumeProgramFrameId == f.Id &&
            skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram && r.Paid;
    }
    private ProgramSkillFrame? HuZhaoObserverRoot() => FindProgramObserverRoot(static (e, f, c) => e.HuZhaoFirstChild(f, c), static (e, index) => e.SameNameHandObserverSuffix(index));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 43000)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 43000)]
    private bool HuZhaoDamageObserver(long id) => HuZhaoObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 43000)]
    private bool HuZhaoBeforeDamageObserver(long id) => HuZhaoObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 43000)] private bool HuZhaoDyingObserver() => ActiveDying is { } dying && HuZhaoObserverRoot() is { } root &&
        _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 43000)] private bool AdvanceHuZhaoSubtree() =>
        _pendingDecision is null && HuZhaoObserverRoot() is not null && TryAdvanceObserverSubtree(advanceRecoveryReplacement: true);
    [ProgramNestedDamageProof(ProgramNestedDamageProofPhase.AfterFactionDebts, 43000)]
    private bool HuZhaoNestedDamage(ProgramSkillFrame f, int target, int amount, ProgramParticipantReference? source, DamageNature? nature, bool sourceLess)
    {
        if (sourceLess || f.AttackAttempt is not null || _resolutionStack.LastOrDefault()?.Id != f.Id || HuZhaoObserverRoot() is not { } root || root.Id == f.Id) return false;
        var e = GetPausedPrivateOfferEffect(f);
        return e is { Op: SkillProgramEffectOp.Damage } && e.Amount == amount && e.ActorReference == source && e.DamageNature == nature &&
            target == (e.TargetReference is { } p ? ResolveProgramParticipant(f, p) : ResolveProgramEffectTarget(f, e.Target));
    }
}
