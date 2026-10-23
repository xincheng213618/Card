namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool ZhangHuanParent(ProgramSkillFrame f)
    {
        if (f.SkillId != AnniversaryZhangHuanComposition.Shuguo || f.TriggerId != "actual-turn-discard-uses" || f.ActivationId != f.TriggerId ||
            f.WindowContext is not { Window: SkillProgramTriggerWindow.AfterTurnEnded } w || w.OwnerSeat != f.OwnerSeat ||
            _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash) return false;
        var index = _resolutionStack.FindIndex(x => x.Id == f.Id);
        return index > 0 && _resolutionStack[index - 1] is DeferredTurnEndFrame parent && parent.Id == w.ParentFrameId &&
            parent.TurnNumber == _turnNumber && parent.OwnerSeat == _turnProgression.OwnerSeat && IsActualAfterTurnEndedParent(parent) &&
            parent.AfterTurnEnded?.CurrentChild?.FrameId == f.Id && MatchesAfterTurnEndedChild(parent, f, w);
    }
    private bool ValidZhangHuan(ProgramSkillFrame f) => f.AnniversaryZhangHuan is { } r && f.InstructionIndex == 1 && ZhangHuanParent(f) && r.Source == ZhangHuanSource(f) && r.Hash == f.GameplayHash &&
        r.Turn == _turnNumber && r.TurnOwner == _turnProgression.OwnerSeat && r.ParentId == f.WindowContext?.ParentFrameId && Enum.IsDefined(r.Stage) &&
        r.Before >= 0 && r.After >= r.Before && r.After <= _movementSequence && r.Used.All(id => id > 0) && r.Used.Distinct().Count() == r.Used.Count &&
        ProgramEventHistory<AnniversaryZhangHuanStartedEvent>().Count(e => e == new AnniversaryZhangHuanStartedEvent(f.Id, r.Source, r.Hash, r.Turn, r.TurnOwner, r.ParentId)) == 1;
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 46300)] private void RequireZhangHuan(ProgramSkillFrame f)
    {
        if (f.AnniversaryZhangHuan is not { } r) return;
        if (!ValidZhangHuan(f) || r.Used.Any(id => ProgramEventHistory<AnniversaryZhangHuanUseReturnedEvent>().Count(e => e.FrameId == f.Id && e.CardId == id) != 1))
            throw new InvalidOperationException("Shuguo lost its original ending candidate, source/hash, actual turn or one-return-per-used-entity ledger.");
        if (r.Native is { } n && (n.FrameId != f.Id || n.Instruction != f.InstructionIndex || n.Source != r.Source || n.Hash != r.Hash || n.Turn != r.Turn || n.Actor != f.OwnerSeat ||
            n.Discard.CardId <= 0 || n.Action.CardId != n.Discard.CardId || !ZhangHuanDiscards().Contains(n.Discard)))
            throw new InvalidOperationException("Shuguo cannot substitute an arbitrary movement or another discarded material for its issued original entity.");
        if (r.Stage is AnniversaryZhangHuanStage.NativeUse or AnniversaryZhangHuanStage.NativeTail && (r.Native is not { UseId: > 0, ActionId: > 0 } issued ||
            ProgramEventHistory<AnniversaryZhangHuanUseIssuedEvent>().Count(e => e == new AnniversaryZhangHuanUseIssuedEvent(f.Id, issued.UseId, issued.ActionId, issued.Discard.CardId, issued.Discard.Sequence, issued.Discard.ExecutorSeat)) != 1 ||
            r.Returned && ProgramEventHistory<AnniversaryZhangHuanUseReturnedEvent>().Count(e => e == new AnniversaryZhangHuanUseReturnedEvent(f.Id, issued.UseId, issued.ActionId, issued.Discard.CardId)) != 1))
            throw new InvalidOperationException("Shuguo lost its exact accepted and wholly completed native use.");
        if (r.Stage is AnniversaryZhangHuanStage.Drawing or AnniversaryZhangHuanStage.DrawChildren && (r.RequestedDraw is < 1 or > 5 || r.RequestedDraw != Math.Min(5, Math.Max(0, r.TotalDiscards - r.Used.Count)) ||
            ProgramEventHistory<AnniversaryZhangHuanDrawIssuedEvent>().Count(e => e.FrameId == f.Id && e.Discards == r.TotalDiscards && e.Used == r.Used.Count && !e.UniqueMaximum && e.Requested == r.RequestedDraw) != 1))
            throw new InvalidOperationException("Shuguo lost its once-issued native draw count or current five-card cap.");
        if (r.Draw is { } d && (d.RecipientSeat != f.OwnerSeat || d.RequestedCount != r.RequestedDraw ||
            ProgramEventHistory<AnniversaryZhangHuanDrawReturnedEvent>().Count(e => e == new AnniversaryZhangHuanDrawReturnedEvent(f.Id, d.RequestedCount, d.ActualDrawIds.Count, d.SequenceBefore, d.SequenceAfter)) != 1))
            throw new InvalidOperationException("Shuguo cannot replace its native draw outcome or return it twice.");
    }
    private bool HasAnniversaryZhangHuanPrompt() => _pendingDecision is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p &&
        _resolutionStack.LastOrDefault() is ProgramSkillFrame { AnniversaryZhangHuan.Stage: AnniversaryZhangHuanStage.Choice, Step: ResolutionFrameStep.AwaitingResponse } f &&
        p.PlayerSeat == f.OwnerSeat && p.TargetSeat == f.OwnerSeat && ValidZhangHuan(f) && AssistedChoicesEqual(p.Choices, ZhangHuanChoices(f));
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 46300)]
    [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 46300)]
    [ProgramMovementProof(ProgramMovementProofPhase.PrivatePayments, 46300)]
    private bool ZhangHuanMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending)
    {
        if (f.AnniversaryZhangHuan is not { } r || effect?.Op != SkillProgramEffectOp.ResolveAnniversaryZhangHuanShuguo || pending != f.PendingMovementContinuation ||
            pending.SubjectSeat != f.OwnerSeat || pending.BeforeCount != 0 || pending.CoverageResultBind is not null || !ValidZhangHuan(f)) return false;
        if (r.Stage == AnniversaryZhangHuanStage.DrawChildren) return r.Draw is { } d && d.RecipientSeat == f.OwnerSeat && d.RequestedCount == r.RequestedDraw &&
            ProgramEventHistory<AnniversaryZhangHuanDrawReturnedEvent>().Count(e => e == new AnniversaryZhangHuanDrawReturnedEvent(f.Id, d.RequestedCount, d.ActualDrawIds.Count, d.SequenceBefore, d.SequenceAfter)) == 1;
        return r.Stage == AnniversaryZhangHuanStage.NativeTail && r.Returned && _pendingCardsMovedBatches.Where(b => r.NativeBatches.Contains(b.Id)).All(b => IsAnniversaryZhangHuanSettlementBatch(f, b));
    }
    private bool ZhangHuanFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (!ValidZhangHuan(f) || f.AnniversaryZhangHuan is not { } r) return false;
        if (child is CardUseFrame use) return r.Stage == AnniversaryZhangHuanStage.NativeUse && IsZhangHuanNative(use);
        if (child is NativeDrawAttemptFrame draw) return r.Stage == AnniversaryZhangHuanStage.Drawing && draw.Context.ParentFrameId == f.Id && draw.Context.RecipientSeat == f.OwnerSeat && draw.Context.RequestedCount == r.RequestedDraw && NativeDrawAttemptRidesOn(draw, f);
        if (child is CardsMovedTriggerWindowFrame moved) return f.PendingMovementContinuation is { } p && ZhangHuanMovement(f, GetPausedPrivateOfferEffect(f), p) && ProgramMovementReturnMatches(f, moved) &&
            moved.Batch.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After && _cardMovements.Contains(m));
        if (child is RecoveryReplacementFrame recovery) return recovery.ParentFrameId == f.Id && recovery.Return.ResumeFrameId == f.Id && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement;
        if (child is HpChangedTriggerWindowFrame hp) return hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id &&
            (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram ||
             state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && state.CharacterStateContinuation == CharacterStateContinuation.Program);
    }
    private ProgramSkillFrame? ZhangHuanObserverRoot() => FindProgramObserverRoot(static (e, f, c) => e.ZhangHuanFirstChild(f, c), static (e, i) => e.ZhangHuanObserverSuffix(i));
    private bool ZhangHuanObserverSuffix(int i) => HasObserverSuffix(i, static (e, n) => e.IsSameNameHandObserverEdge(n),
        static (e, n, dying) => e.IsSuspendedOrPaidRescueObserverSuffix(n, dying) || e.TieredRoundZeroDyingRescueRide(n, dying) || e.DrawFundedDistinctBasicDyingRescueRide(n, dying) || e.HuanpeiDyingRescueRide(n, dying));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 46300)]
    [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 46300)]
    private bool ZhangHuanDamageObserver(long id) => ZhangHuanObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 46300)]
    private bool ZhangHuanBeforeDamage(long id) => ZhangHuanObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 46300)]
    [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 46300)]
    private bool ZhangHuanDying() => ActiveDying is { } dying && ZhangHuanObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 46300)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 46300)]
    private bool RetainZhangHuan(ProgramSkillFrame f) => f.AnniversaryZhangHuan is not null && ValidZhangHuan(f);
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 46300)]
    private bool AdvanceZhangHuanChildren() => _pendingDecision is null && ZhangHuanObserverRoot() is not null && TryAdvanceObserverSubtree(true);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 46300)]
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 46300)]
    private bool ZhangHuanRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.AnniversaryZhangHuan is null) return false;
        var parent = _resolutionStack.FindIndex(x => x.Id == f.Id); var child = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return parent >= 0 && child > parent && ZhangHuanFirstChild(f, _resolutionStack[parent + 1]) &&
            (child == parent + 1 || ZhangHuanObserverSuffix(parent)) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[child - 1]);
    }
}
