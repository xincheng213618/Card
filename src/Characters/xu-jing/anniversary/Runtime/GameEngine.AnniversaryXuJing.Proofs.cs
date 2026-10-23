namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool HasAnniversaryXuJingPrompt()
    {
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } p || _resolutionStack.LastOrDefault() is not ProgramSkillFrame { AnniversaryXuJing: { Stage: AnniversaryXuJingStage.GiftChoice or AnniversaryXuJingStage.CountChoice } } f ||
            f.Step != ResolutionFrameStep.AwaitingResponse || p.PlayerSeat != f.OwnerSeat || !AssistedChoicesEqual(p.Choices, AnniversaryXuJingChoices(f))) return false;
        RequireAnniversaryXuJing(f); return true;
    }
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 43100)]
    private void RequireAnniversaryXuJing(ProgramSkillFrame f)
    {
        if (f.AnniversaryXuJing is not { } r) return;
        if (r.Source != AnniversaryXuJingSource(f) || r.Hash != f.GameplayHash || f.InstructionIndex != 1 || r.ParentId != f.WindowContext?.ParentFrameId ||
            !Enum.IsDefined(r.Stage) || r.Mode is not ("opening" or "discard" or "damage" or "caixia") || r.RewardIndex is < 0 or > 2 ||
            r.Before < 0 || r.After < r.Before || r.After > _movementSequence || !IsValidPlayerSeat(r.UserSeat) ||
            ProgramEventHistory<AnniversaryXuJingStartedEvent>().Count(e => e == new AnniversaryXuJingStartedEvent(f.Id, r.Source, r.Hash, r.ParentId, r.Mode, r.CardId, r.UserSeat, r.DamageId)) != 1)
            throw new InvalidOperationException("Xu Jing lost his exact source, accepted trigger, original entity or native cursor.");
        if (r.Mode == "caixia")
        {
            if (f.SkillId != AnniversaryXuJingComposition.Caixia || f.WindowContext?.Window != SkillProgramTriggerWindow.AfterDamageApplied || r.CardId != 0 || r.Count < 0 || r.Count > Math.Min(5, _players.Count) ||
                r.Stage != AnniversaryXuJingStage.CountChoice && ProgramEventHistory<AnniversaryXuJingCaixiaIssuedEvent>().Count(e => e == new AnniversaryXuJingCaixiaIssuedEvent(f.Id, r.Source, r.Count)) != 1)
                throw new InvalidOperationException("Caixia lost its accepted damage or once-frozen requested amount.");
        }
        else if (f.SkillId != AnniversaryXuJingComposition.Shangyu || r.CardId <= 0 || AnniversaryXuJingMark(f.OwnerSeat, f.SkillInstanceId)?.CardId != r.CardId ||
                 r.Mode == "opening" && f.WindowContext?.Window != SkillProgramTriggerWindow.GameStarting ||
                 r.Mode == "discard" && (f.WindowContext?.MovementBatch is not { } batch || f.WindowContext.MovementIndex is not { } index || batch.Movements[index].CardId != r.CardId || batch.Movements[index].To != CardLocation.DiscardPile) ||
                 r.Mode == "damage" && (f.WindowContext?.DamageFrameId != r.DamageId || r.DamageId <= 0))
            throw new InvalidOperationException("Shangyu lost its original marked entity and actual damage/discard occurrence.");
        if (r.Mode is "opening" or "discard" && r.TargetSeat >= 0 && ProgramEventHistory<AnniversaryXuJingGiftIssuedEvent>().Count(e => e.FrameId == f.Id && e.Source == r.Source && e.CardId == r.CardId && e.TargetSeat == r.TargetSeat) != 1)
            throw new InvalidOperationException("Shangyu changed its once-issued recipient.");
        foreach (var draw in r.Draws)
            if (ProgramEventHistory<AnniversaryXuJingDrawReturnedEvent>().Count(e => e.FrameId == f.Id && e.AttemptId == draw.AttemptFrameId && e.RecipientSeat == draw.RecipientSeat && e.RequestedCount == draw.RequestedCount && e.ActualCount == draw.ActualDrawIds.Count && e.Before == draw.SequenceBefore && e.After == draw.SequenceAfter) != 1)
                throw new InvalidOperationException("Xu Jing lost a completed native draw invoice.");
    }
    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 43100)]
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 43100)]
    [ProgramMovementProof(ProgramMovementProofPhase.PrivatePayments, 43100)]
    private bool AnniversaryXuJingMovementProof(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending)
    {
        if (f.AnniversaryXuJing is not { Stage: AnniversaryXuJingStage.MovementChildren or AnniversaryXuJingStage.DrawChildren } r || pending != f.PendingMovementContinuation ||
            pending.SubjectSeat != f.OwnerSeat || pending.BeforeCount != 0 || pending.CoverageResultBind is not null ||
            effect?.Op != (r.Mode == "caixia" ? SkillProgramEffectOp.ResolveAnniversaryXuJingCaixia : SkillProgramEffectOp.ResolveAnniversaryXuJingShangyu)) return false;
        RequireAnniversaryXuJing(f);
        var moves = _cardMovements.Where(m => m.Sequence > r.Before && m.Sequence <= r.After).ToArray();
        if (r.Stage == AnniversaryXuJingStage.MovementChildren)
            return r.MovementSource is { } source && r.MovementDestination is { } destination &&
                ProgramEventHistory<AnniversaryXuJingMovedEvent>().Count(e => e == new AnniversaryXuJingMovedEvent(f.Id, r.CardId, source, destination, r.Before, r.After)) == 1 &&
                (source == destination ? r.Before == r.After && moves.Length == 0 : moves is [var movement] && movement.CardId == r.CardId && movement.From == source && movement.To == destination && movement.Reason == AnniversaryXuJingMoveReason);
        return r.Draws.LastOrDefault() is { } draw && draw.SequenceBefore == r.Before && draw.SequenceAfter == r.After && draw.RecipientSeat == r.TargetSeat &&
            draw.ActualDrawIds.All(id => moves.Count(m => m.CardId == id && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(r.TargetSeat) && m.Reason == AnniversaryXuJingDrawReason) == 1);
    }
    private bool AnniversaryXuJingFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.AnniversaryXuJing is not { } r) return false;
        RequireAnniversaryXuJing(f);
        if (child is NativeDrawAttemptFrame draw) return r.Stage == AnniversaryXuJingStage.Drawing && draw.Context.RecipientSeat == r.TargetSeat && draw.Context.RequestedCount == r.Count && draw.Context.ParentFrameId == f.Id;
        if (r.Stage is not (AnniversaryXuJingStage.MovementChildren or AnniversaryXuJingStage.DrawChildren)) return false;
        if (child is CardsMovedTriggerWindowFrame moved) return f.PendingMovementContinuation is { } pending && AnniversaryXuJingMovementProof(f, GetPausedPrivateOfferEffect(f), pending) && ProgramMovementReturnMatches(f, moved) &&
            moved.Batch.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After && _cardMovements.Contains(m));
        if (child is RecoveryReplacementFrame recovery) return RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Attempt.Completion.Producer == RecoveryAttemptProducer.Program &&
            recovery.Return.Continuation == (r.Stage == AnniversaryXuJingStage.MovementChildren ? PostEventContinuation.AwaitedProgramMovement : PostEventContinuation.Program);
        if (child is HpChangedTriggerWindowFrame hp) return hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id && hp.Continuation == (r.Stage == AnniversaryXuJingStage.MovementChildren ? PostEventContinuation.AwaitedProgramMovement : PostEventContinuation.Program);
        return child is ProgramLifecycleTriggerWindowFrame { Window: SkillProgramTriggerWindow.SkillsChanged } skills && skills.ResumeProgramFrameId == f.Id && skills.Continuation == ProgramLifecycleContinuation.ResumeParentProgram;
    }
    private ProgramSkillFrame? AnniversaryXuJingObserverRoot() => FindProgramObserverRoot(static (engine, frame, child) => engine.AnniversaryXuJingFirstChild(frame, child), static (engine, index) => engine.SameNameHandObserverSuffix(index));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 43100)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 43100)]
    private bool AnniversaryXuJingDamageObserver(long id) => AnniversaryXuJingObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 43100)] private bool AnniversaryXuJingBeforeDamageObserver(long id) => AnniversaryXuJingObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 43100)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 43100)]
    private bool AnniversaryXuJingDyingObserver() => ActiveDying is { } dying && AnniversaryXuJingObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 43100)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 43100)]
    private bool AnniversaryXuJingRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.AnniversaryXuJing is null) return false;
        var root = _resolutionStack.FindIndex(frame => frame.Id == f.Id); var child = _resolutionStack.FindIndex(frame => frame.Id == recovery.Id);
        return root >= 0 && child > root && AnniversaryXuJingFirstChild(f, _resolutionStack[root + 1]) &&
            (child == root + 1 || Enumerable.Range(root + 2, child - root - 1).All(i => SameNameHandObserverSuffix(i))) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 43100)] private bool AdvanceAnniversaryXuJingChildren() => _pendingDecision is null && AnniversaryXuJingObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
