namespace CardGame.Core;
public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 46100)]
    private void RequireXunXu(ProgramSkillFrame f)
    {
        if (f.AnniversaryXunXu is not { } r) return;
        if (f.InstructionIndex != 1 || f.TriggerId is null || f.ActivationId != f.TriggerId || f.WindowContext is not { } w || w.ParentFrameId != r.ParentId || r.Source != XunXuSource(f) || r.Hash != f.GameplayHash ||
            r.Mode is not ("claim" or "hp" or "boqia") || r.Stage is not ("claim" or "hp" or "target" or "discard" or "drawing" or "children" or "next-draw" or "native-children" or "finish") ||
            r.Before < 0 || r.After < r.Before || r.After > _movementSequence || !IsValidPlayerSeat(r.Target) || r.Count < 0 || r.Cards.Distinct().Count() != r.Cards.Count || r.Cards.Any(id => id <= 0) ||
            r.Recipients.Distinct().Count() != r.Recipients.Count || r.Recipients.Any(s => !IsValidPlayerSeat(s)) || r.Cursor < 0 || r.Cursor > r.Recipients.Count ||
            ProgramEventHistory<AnniversaryXunXuStartedEvent>().Count(e => e == new AnniversaryXunXuStartedEvent(f.Id, r.Source, r.Hash, r.ParentId, r.Mode, r.ActionId)) != 1)
            throw new InvalidOperationException("Xun Xu lost its original trigger, source, immutable result or native child cursor.");
        if (r.Mode == "claim" && (f.SkillId != AnniversaryXunXuComposition.Diyin || f.TriggerId != "next-discard" || w is not { Window: SkillProgramTriggerWindow.DiscardPileReceived, MovementBatch: { } batch } ||
            !ProgramEventHistory<AnniversaryXunXuClaimEvent>().Any(e => e.Source == r.Source && e.BatchId == batch.Id && e.ThroughSeventh > 0 && ProgramEventHistory<AnniversaryXunXuLossEvent>().Any(l => l.Source == e.Source && l.Occurrence >= e.ThroughSeventh * 7 && l.SequenceAfter <= e.SequenceBefore))))
            throw new InvalidOperationException("Diyin needs the true seventh loss before its distinct next-discard batch.");
        if (r.Mode == "hp" && (f.SkillId != AnniversaryXunXuComposition.Diyin || f.TriggerId != "same-hp" || w.Window != SkillProgramTriggerWindow.AfterDamageApplied || w.TargetSeat != f.OwnerSeat || w.Amount <= 0))
            throw new InvalidOperationException("Diyin's equality draw must belong to its original real post-damage target.");
        if (r.Mode == "boqia" && (f.SkillId != AnniversaryXunXuComposition.Boqia || f.TriggerId != "completed-use" || w.Window != SkillProgramTriggerWindow.CardUseCompleted || w.CardUse?.CardActionId != r.ActionId ||
            !ProgramEventHistory<AnniversaryXunXuUseEvent>().Any(e => e.ActionId == r.ActionId && e.Source.OwnerSeat == f.OwnerSeat && e.Source.SkillInstanceId == f.SkillInstanceId && e.Reward)))
            throw new InvalidOperationException("Boqia's adjustment must retain its truly completed three-material Use and qualified material result.");
        if (r.NativeFinishBatches.Select(b => b.Id).Distinct().Count() != r.NativeFinishBatches.Count ||
            r.NativeFinishBatches.Count > 0 && (r.Mode != "boqia" || !HasXunXuNativeFinishOwner(f, out var native) ||
                r.NativeFinishBatches.Any(b => !IsXunXuFinishedMaterialBatch(native!, b) || ProgramEventHistory<AnniversaryXunXuNativeBatchEvent>().Count(e =>
                    e == new AnniversaryXunXuNativeBatchEvent(f.Id, r.NativeUseId, r.ActionId, b.Id, b.Movements.Min(m => m.Sequence) - 1, b.Movements.Max(m => m.Sequence))) != 1)))
            throw new InvalidOperationException("Boqia lost its original native Use, frozen physical finish batches or accepted completion ancestor.");
        for (var i = 0; i < r.Draws.Count; i++)
        {
            var draw = r.Draws[i];
            if (draw.SequenceBefore < 0 || draw.SequenceAfter < draw.SequenceBefore || draw.SequenceAfter > _movementSequence ||
                ProgramEventHistory<AnniversaryXunXuDrawEvent>().Count(e => e == new AnniversaryXunXuDrawEvent(f.Id, i, draw.RecipientSeat, draw.RequestedCount, draw.ActualDrawIds.Count, draw.SequenceBefore, draw.SequenceAfter, draw.AttemptFrameId)) != 1 ||
                draw.ActualDrawIds.Any(id => id <= 0 || _cardMovements.Count(m => m.CardId == id && m.Sequence > draw.SequenceBefore && m.Sequence <= draw.SequenceAfter && m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(draw.RecipientSeat) && m.Reason == XunXuDrawReason) != 1))
                throw new InvalidOperationException("Xun Xu cannot replace a native draw receipt with obtained or twice-paid materials.");
        }
    }
    private bool HasXunXuNativeFinishOwner(ProgramSkillFrame f, out CardUseFrame? use)
    {
        use = null;
        if (f.AnniversaryXunXu is not { Mode: "boqia", NativeUseId: > 0 } r) return false;
        var i = _resolutionStack.FindIndex(x => x.Id == f.Id);
        if (i < 2 || _resolutionStack[i - 1] is not ProgramCardTriggerWindowFrame window || window.Id != r.ParentId ||
            window.Action.ActionId != r.ActionId || window.Action.Type != CardActionType.Use || window.CompletedResponseReturn is not null ||
            _resolutionStack[i - 2] is not CardUseFrame { Step: ResolutionFrameStep.Completed, Action: { } action } original || original.Id != r.NativeUseId ||
            window.ParentFrameId != original.Id || action.ActionId != r.ActionId || action.ActorSeat != f.OwnerSeat || action.ProviderSeat != f.OwnerSeat ||
            action.PhysicalCards.Count != 3 || !action.PhysicalCards.Select(c => c.CardId).SequenceEqual(original.PhysicalCardIds ?? []) ||
            action.ConversionChain.SingleOrDefault(s => s.SkillId == AnniversaryXunXuComposition.Boqia) is not { } source || source.OwnerSeat != f.OwnerSeat || source.SkillInstanceId != f.SkillInstanceId ||
            ProgramEventHistory<AnniversaryXunXuUseEvent>().Count(e => e.Source == source && e.ActionId == action.ActionId && e.Reward) != 1 ||
            ProgramEventHistory<CardUseFinishedEvent>().Count(e => e.ResolutionId == original.Id) != 1) return false;
        use = original; return true;
    }
    private bool IsXunXuFinishedMaterialBatch(CardUseFrame use, CardMovementBatchContext b) => use.Action is { Type: CardActionType.Use } a &&
        b.ParentFrameId == use.Id && b.AwaitingProgramFrameId is null && b.Movements.Count > 0 && b.TurnNumber == _turnNumber &&
        b.Movements.All(m => m.Sequence > 0 && _cardMovements.Contains(m) && m.From == CardLocation.Processing && m.To == CardLocation.DiscardPile &&
            m.Reason == CardMoveReasons.UseFinished && a.PhysicalCards.Any(c => c.CardId == m.CardId && c.CardKind == m.CardKind)) &&
        b.Movements.Select(m => m.CardId).Distinct().Count() == b.Movements.Count;
    private bool IsAnniversaryXunXuSettlementBatch(ProgramSkillFrame f, CardMovementBatchContext b) => f.AnniversaryXunXu is { Stage: "native-children" } r &&
        HasXunXuNativeFinishOwner(f, out var use) && IsXunXuFinishedMaterialBatch(use!, b) && r.NativeFinishBatches.Any(frozen => frozen.Id == b.Id &&
            frozen.ParentFrameId == b.ParentFrameId && frozen.AwaitingProgramFrameId == b.AwaitingProgramFrameId && frozen.Movements.SequenceEqual(b.Movements) &&
            frozen.SourceCounts.SequenceEqual(b.SourceCounts) && (frozen.DestinationCounts ?? []).SequenceEqual(b.DestinationCounts ?? [])) &&
        ProgramEventHistory<AnniversaryXunXuNativeBatchEvent>().Count(e => e.FrameId == f.Id && e.UseId == use!.Id && e.ActionId == r.ActionId && e.BatchId == b.Id &&
            e.Before == b.Movements.Min(m => m.Sequence) - 1 && e.After == b.Movements.Max(m => m.Sequence)) == 1;
    private bool HasAnniversaryXunXuPrompt() => _pendingDecision is { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p &&
        _resolutionStack.LastOrDefault() is ProgramSkillFrame { AnniversaryXunXu: { Stage: "target" or "discard" } r, Step: ResolutionFrameStep.AwaitingResponse } f &&
        p.PlayerSeat == (r.Stage == "discard" ? r.Target : f.OwnerSeat) && p.TargetSeat == r.Target && AssistedChoicesEqual(p.Choices, XunXuChoices(f));
    [ProgramMovementProof(ProgramMovementProofPhase.NativeCost, 46100)] [ProgramMovementProof(ProgramMovementProofPhase.BoundEquipment, 46100)]
    [ProgramMovementProof(ProgramMovementProofPhase.PrivatePayments, 46100)] [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 46100)]
    private bool XunXuMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending)
    {
        if (f.AnniversaryXunXu is not { Stage: "children" or "native-children" } r || effect?.Op is not (SkillProgramEffectOp.ResolveAnniversaryXunXuDiyin or SkillProgramEffectOp.ResolveAnniversaryXunXuBoqiaCompletion) ||
            pending != f.PendingMovementContinuation || pending.SubjectSeat != f.OwnerSeat || pending.BeforeCount != 0 || pending.CoverageResultBind is not null) return false;
        RequireXunXu(f);
        if (r.Stage == "native-children") return r.NativeFinishBatches.Count > 0 && HasXunXuNativeFinishOwner(f, out _);
        if (r.Draws.LastOrDefault() is { } d && d.SequenceBefore == r.Before && d.SequenceAfter == r.After) return true;
        var mode = r.Mode == "claim" ? "claim" : "discard";
        if (mode == "claim")
        {
            if (r.Gain is not { } gain || gain.Reason != XunXuGainReason || gain.Movements.Any(m => m.From != CardLocation.DiscardPile)) return false;
            ValidateHappyWeiYanGainRequestSettlement(gain, f.Id, r.Cards, CardLocation.Hand(r.Target), r.Before);
        }
        return ProgramEventHistory<AnniversaryXunXuMovementEvent>().Count(e => e == new AnniversaryXunXuMovementEvent(f.Id, mode, r.Target, r.Cards.Count, r.Before, r.After)) == 1 &&
            r.Cards.All(id => _cardMovements.Count(m => m.CardId == id && m.Sequence > r.Before && m.Sequence <= r.After &&
                (mode == "claim" ? m.From == CardLocation.DiscardPile && (m.To == CardLocation.Hand(r.Target) || m.To == CardLocation.OutsideGame || IsHappyWeiYanRedirectedGain(m, CardLocation.Hand(r.Target))) && m.Reason == XunXuGainReason :
                    (m.From == CardLocation.Hand(r.Target) || GetProgramDiscardSource(m) == CardLocation.Hand(r.Target)) && (m.To == CardLocation.DiscardPile || m.To == CardLocation.OutsideGame && IsEphemeralCardDestroyedMaterial(m)) && m.Reason == XunXuDiscardReason)) == 1);
    }
    private bool XunXuFirstChild(ProgramSkillFrame f, ResolutionFrame child)
    {
        if (f.AnniversaryXunXu is not { } r) return false;
        RequireXunXu(f);
        if (child is HappyQiaoHealthChoiceFrame qiao) return r.Stage == "hp" && HappyQiaoHealthChoiceRidesOn(qiao, f);
        if (child is NativeDrawAttemptFrame draw) return r.Stage == "drawing" && draw.Context.ParentFrameId == f.Id && draw.Context.RecipientSeat == r.Target && draw.Context.RequestedCount == r.Count && NativeDrawAttemptRidesOn(draw, f);
        if (child is CardsMovedTriggerWindowFrame settled && r.Stage == "native-children") return settled.Id == settled.Batch.Id && settled.ResumeProgramFrameId == f.Id && !HasAlternateProgramMovementReturn(settled) && IsAnniversaryXunXuSettlementBatch(f, settled.Batch);
        if (child is CardsMovedTriggerWindowFrame moved) return f.PendingMovementContinuation is { } p && XunXuMovement(f, GetPausedPrivateOfferEffect(f), p) && ProgramMovementReturnMatches(f, moved) && moved.Batch.Movements.All(m => m.Sequence > r.Before && m.Sequence <= r.After && _cardMovements.Contains(m));
        if (child is RecoveryReplacementFrame recovery) return f.PendingMovementContinuation is not null && RecoveryReplacementFrameRidesOn(recovery, f) && recovery.Return.Continuation == PostEventContinuation.AwaitedProgramMovement;
        if (child is HpChangedTriggerWindowFrame hp) return f.PendingMovementContinuation is not null && hp.ResumeFrameId == f.Id && hp.Change.ParentFrameId == f.Id && hp.Continuation == PostEventContinuation.AwaitedProgramMovement;
        return child is ProgramLifecycleTriggerWindowFrame state && state.ResumeProgramFrameId == f.Id && (state.Window == SkillProgramTriggerWindow.SkillsChanged && state.Continuation == ProgramLifecycleContinuation.ResumeParentProgram || state.Continuation == ProgramLifecycleContinuation.ResumeCharacterStateChange && state.CharacterStateContinuation == CharacterStateContinuation.Program);
    }
    private ProgramSkillFrame? XunXuObserverRoot() => FindProgramObserverRoot(static (e, f, child) => e.XunXuFirstChild(f, child), static (e, i) => e.SameNameHandObserverSuffix(i));
    [ProgramObserverProof(ProgramObserverProofPhase.PaidDamage, 46100)] [ProgramObserverProof(ProgramObserverProofPhase.NestedDamage, 46100)]
    private bool XunXuDamageObserver(long id) => XunXuObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is DamageTriggerWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramObserverProof(ProgramObserverProofPhase.BeforeDamage, 46100)] private bool XunXuBeforeDamageObserver(long id) => XunXuObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == id && f is BeforeDamageProgramWindowFrame) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramDyingProof(ProgramDyingProofPhase.Invariant, 46100)] [ProgramDyingProof(ProgramDyingProofPhase.Runtime, 46100)] private bool XunXuDyingObserver() => ActiveDying is { } dying && XunXuObserverRoot() is { } root && _resolutionStack.FindIndex(f => f.Id == dying.Id) > _resolutionStack.FindIndex(f => f.Id == root.Id);
    [ProgramRecoveryParent(ProgramRecoveryParentPhase.RecoveryCursor, 46100)] [ProgramRecoveryParent(ProgramRecoveryParentPhase.DamageCursor, 46100)]
    private bool XunXuRecoveryParent(ProgramSkillFrame f, RecoveryReplacementFrame recovery)
    {
        if (f.AnniversaryXunXu is null) return false;
        var i = _resolutionStack.FindIndex(x => x.Id == f.Id); var j = _resolutionStack.FindIndex(x => x.Id == recovery.Id);
        return i >= 0 && j > i && XunXuFirstChild(f, _resolutionStack[i + 1]) && (j == i + 1 || SameNameHandObserverSuffix(i)) && RecoveryReplacementFrameRidesOn(recovery, _resolutionStack[j - 1]);
    }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterResponseHealth, 46100)] private bool AdvanceXunXuChildren() => _pendingDecision is null && XunXuObserverRoot() is not null && TryAdvanceObserverSubtree(true);
}
