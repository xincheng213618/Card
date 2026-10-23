namespace CardGame.Core;
public sealed partial class GameEngine
{
    private bool HasAnniversaryLiuYongPrompt()
    {
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } p || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryLiuYong: { Stage: AnniversaryLiuYongStage.Gift or AnniversaryLiuYongStage.UseChoice } } f ||
            f.Step != ResolutionFrameStep.AwaitingResponse || p.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual(p.Choices, AnniversaryLiuYongChoices(f))) return false;
        RequireAnniversaryLiuYong(f); return true;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 43400)] private void RequireAnniversaryLiuYong(ProgramSkillFrame f)
    {
        if (f.AnniversaryLiuYong is not { } r) return;
        if (r.Source != AnniversaryLiuYongSource(f) || r.Hash != f.GameplayHash || f.InstructionIndex != 1 || r.ParentId != (f.WindowContext?.ParentFrameId ?? 0) || r.Turn != _turnNumber ||
            !Enum.IsDefined(r.Stage) || r.Mode is not ("gift" or "damage" or "transition") || r.Before < 0 || r.After < r.Before || r.After > _movementSequence ||
            ProgramEventHistory<AnniversaryLiuYongStartedEvent>().Count(e => e == new AnniversaryLiuYongStartedEvent(f.Id, r.Source, r.Hash, r.ParentId, r.Turn, r.PhaseInstance, r.Mode)) != 1)
            throw new InvalidOperationException("Liu Yong lost the original skill instance, accepted phase/damage/movement parent or once-issued cursor.");
        if (r.Mode == "gift")
        {
            if (f.SkillId != AnniversaryLiuYongComposition.Zhuning || f.TriggerId is not null || f.ActivationId != "gift-and-use" || r.PhaseInstance != _cardUseDebitPhaseInstanceId || f.OwnerSeat != _currentSeat ||
                f.SelectedTargetSeats is not [var recipient] || recipient != r.Recipient || recipient == f.OwnerSeat || r.Cards.Count != r.Cards.Distinct().Count() ||
                r.GiftPaid && (r.From.Count != r.Cards.Count || ProgramEventHistory<AnniversaryLiuYongGiftPaidEvent>().Count(e => e == new AnniversaryLiuYongGiftPaidEvent(f.Id, r.Source, recipient, r.Cards.Count,
                    r.Stage == AnniversaryLiuYongStage.GiftChildren ? r.Before : e.Before, r.Stage == AnniversaryLiuYongStage.GiftChildren ? r.After : e.After)) != 1))
                throw new InvalidOperationException("Zhuning changed its original other receiver or private real gift invoice.");
            if (r.Stage is AnniversaryLiuYongStage.NativeUse or AnniversaryLiuYongStage.NativeReturned && (r.Plan is null || r.UseId <= 0 || r.ActionId <= 0 || ProgramEventHistory<AnniversaryLiuYongNativeIssuedEvent>().Count(e => e == new AnniversaryLiuYongNativeIssuedEvent(f.Id, r.UseId, r.ActionId, r.Plan.Kind)) != 1))
                throw new InvalidOperationException("Zhuning lost its actual native issuance.");
        }
        else if (f.SkillId != AnniversaryLiuYongComposition.Fengxiang || r.Mode == "damage" && f.WindowContext?.Window != SkillProgramTriggerWindow.AfterDamageApplied ||
            r.Mode == "transition" && (f.WindowContext?.MovementBatch is not { } batch || ProgramEventHistory<AnniversaryLiuYongLeaderChangedEvent>().Count(e => e.BatchId == batch.Id && e.Source.OwnerSeat == f.OwnerSeat && e.Source.SkillInstanceId == f.SkillInstanceId && e.Hash == f.GameplayHash) != 1))
            throw new InvalidOperationException("Fengxiang lost the actual incoming damage or frozen unique-Xi-leader change.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 43400)] [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 43400)] [ProgramMovementProof(ProgramMovementProofPhase.PrivatePayments, 43400)]
    private bool AnniversaryLiuYongMovementProof(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending)
    {
        if (f.AnniversaryLiuYong is not { Stage: AnniversaryLiuYongStage.GiftChildren or AnniversaryLiuYongStage.DrawChildren } r || pending != f.PendingMovementContinuation || pending.SubjectSeat != f.OwnerSeat || pending.BeforeCount != 0 || pending.CoverageResultBind is not null ||
            effect?.Op != (r.Mode == "gift" ? SkillProgramEffectOp.ResolveAnniversaryLiuYongZhuning : SkillProgramEffectOp.ResolveAnniversaryLiuYongFengxiang)) return false;
        RequireAnniversaryLiuYong(f); var moves = _cardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= r.After).ToArray();
        if (r.Stage == AnniversaryLiuYongStage.DrawChildren) return r.Draw is { } draw && draw.SequenceBefore == r.Before && draw.SequenceAfter == r.After && draw.RecipientSeat == f.OwnerSeat && draw.RequestedCount == 1 &&
            ProgramEventHistory<AnniversaryLiuYongDrawReturnedEvent>().Count(e => e == new AnniversaryLiuYongDrawReturnedEvent(f.Id, draw.AttemptFrameId, r.Before, r.After, draw.ActualDrawIds.Count)) == 1 &&
            draw.ActualDrawIds.All(id => moves.Count(m => m.CardId == id && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason == AnniversaryLiuYongDrawReason) == 1);
        return r.GiftPaid && r.Cards.Count == r.From.Count && r.Cards.Select((id, index) => (id, from: r.From[index])).All(x => moves.Count(m => m.CardId == x.id && m.From == x.from && m.To == CardLocation.Hand(r.Recipient) && m.Reason == AnniversaryLiuYongGiftReason) == 1);
    }
    private bool AnniversaryLiuYongFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.AnniversaryLiuYong is not { } r) return false; RequireAnniversaryLiuYong(f);
        if (child is CardUseFrame use) return r.Stage == AnniversaryLiuYongStage.NativeUse && (IsAnniversaryLiuYongSlashUse(use) || IsAnniversaryLiuYongVirtualUse(use));
        if (child is NativeDrawAttemptFrame draw) return r.Stage == AnniversaryLiuYongStage.Drawing && draw.Context.ParentFrameId == f.Id && draw.Context.RecipientSeat == f.OwnerSeat && draw.Context.RequestedCount == 1;
        if (child is CardsMovedTriggerWindowFrame moved) return f.PendingMovementContinuation is { } pending && AnniversaryLiuYongMovementProof(f, GetPausedPrivateOfferEffect(f), pending) && ProgramMovementReturnMatches(f, moved) && moved.Batch.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After && _cardMovements.Contains(m));
        if (child is HappyQiaoHealthChoiceFrame health) return r.Stage == AnniversaryLiuYongStage.HealthChildren && HappyQiaoHealthChoiceRidesOn(health, f);
        if (child is RecoveryReplacementFrame recovery) return RecoveryReplacementFrameRidesOn(recovery, f) &&
            (r.Stage == AnniversaryLiuYongStage.HealthChildren && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.Program && recovery.Attempt.SourceSeat == f.OwnerSeat && recovery.Attempt.TargetSeat == r.Recipient && recovery.Attempt.Amount == 1 && recovery.Return.Continuation == PostEventContinuation.Program ||
             r.Stage == AnniversaryLiuYongStage.GiftChildren && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.SilverLion && recovery.Attempt.SourceSeat == f.OwnerSeat && recovery.Attempt.TargetSeat == f.OwnerSeat && recovery.Attempt.Completion.MoveReason == AnniversaryLiuYongGiftReason && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement &&
                _cardMovements.Any(m => m.Sequence > r.Before && m.Sequence <= r.After && m.From == CardLocation.Equipment(f.OwnerSeat) && m.Reason == AnniversaryLiuYongGiftReason && GetAdvancedCard(m.CardId).Kind == CardKind.SilverLion));
        if (child is HpChangedTriggerWindowFrame hp) return hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id && hp.Change.Kind == HpChangeKind.Recovery &&
            (r.Stage == AnniversaryLiuYongStage.HealthChildren && hp.Change.TargetSeat == r.Recipient && hp.Continuation == PostEventContinuation.Program || r.Stage == AnniversaryLiuYongStage.GiftChildren && hp.Change.TargetSeat == f.OwnerSeat && hp.Continuation == PostEventContinuation.AwaitedProgramMovement);
        return child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills && skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? AnniversaryLiuYongObserverRoot() => FindProgramObserverRoot(static (e, f, child) => e.AnniversaryLiuYongFirstChild(f, child), static (e, index) => e.SameNameHandObserverSuffix(index));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 43400)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 43400)] private bool AnniversaryLiuYongDamageObserver(long id) => AnniversaryLiuYongObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 43400)] private bool AnniversaryLiuYongBeforeDamageObserver(long id) => AnniversaryLiuYongObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 43400)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 43400)] private bool AnniversaryLiuYongDyingObserver() => ActiveDying is { } dying && AnniversaryLiuYongObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 43400)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 43400)]
    private bool AnniversaryLiuYongRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.AnniversaryLiuYong is null) return false; var parent = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return parent >= 0 && child > parent && AnniversaryLiuYongFirstChild(f, _resolutionStack[parent + 1]) && (child == parent + 1 || Enumerable.Range(parent + 2, child - parent - 1).All(i => SameNameHandObserverSuffix(i))) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 43400)] private bool AdvanceAnniversaryLiuYongChildren() => _pendingDecision is null && AnniversaryLiuYongObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
