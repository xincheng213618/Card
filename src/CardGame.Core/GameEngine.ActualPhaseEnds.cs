namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool TracksActualPhaseEnds => HasLishiCapability || HasMouSimaShi || HasMouHuCheEr || IsGuanduActivity || HasOlLuShi || _contentRegistry.ProgramDependencies
        .HasTriggerOperation(SkillProgramEffectOp.ResolveQingyaNextActualPhaseUse) || _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveNishouIssuedCurrentPhaseSwap) ||
        _contentRegistry.ProgramDependencies.HasActivationOperation(SkillProgramEffectOp.ResolveAnniversaryWuFuJuekai) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMobileShiHuangZuPhaseUse) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.ResolveMobileShiRunweiPhaseEnd);

    private void ConfirmActualDiscardPhaseExecution()
    {
        if (TracksActualPhaseEnds && CurrentActualDiscardRecoveryPhase() is { Kind: ActualDiscardRecoveryPhaseKind.Discard } phase &&
            !CompleteProgramEventHistory().OfType<ActualDiscardPhaseExecutionConfirmedEvent>().Any(e => e.Phase == phase))
            AdvanceEventRulesAndQueueFact(new ActualDiscardPhaseExecutionConfirmedEvent(phase));
    }

    private bool TryBeginActualPhaseEnd(ActualDiscardRecoveryPhaseKind kind, ActualPhaseEndContinuation continuation,
        int delayedEffects = 0, bool afterNormalDrawProgramsCompleted = false,
        bool drawPhaseEndedProgramsCompleted = false, bool actualDrawCompletionCompleted = false,
        long? parentFrameId = null)
    {
        if (!TracksActualPhaseEnds || CurrentActualDiscardRecoveryPhase() is not { } phase || phase.Kind != kind ||
            phase.TurnNumber != _turnNumber || phase.ActualTurnOwnerSeat != _turnProgression.OwnerSeat ||
            CompleteProgramEventHistory().OfType<ActualPhaseEndedEvent>().Any(e => e.Phase == phase) ||
            kind == ActualDiscardRecoveryPhaseKind.Discard &&
                !CompleteProgramEventHistory().OfType<ActualDiscardPhaseExecutionConfirmedEvent>().Any(e => e.Phase == phase)) return false;
        if (_pendingDecision is not null || (parentFrameId is null ? _resolutionStack.Count != 0 :
                _resolutionStack.LastOrDefault() is not DrawPhaseObligationFrame draw || draw.Id != parentFrameId ||
                draw.Stage != DrawPhaseObligationStage.ConsumeMarker || draw.Skipped || draw.DiscardRecoveryPhase != phase))
            throw new InvalidOperationException("An actual phase end lost its clean native producer boundary.");
        var inherited = Array.AsReadOnly(_pendingCardsMovedBatches.Where(b => b.TurnNumber == _turnNumber &&
            b.AwaitingProgramFrameId is null && (b.ParentFrameId is null || b.ParentFrameId == parentFrameId ||
                !_resolutionStack.Any(f => f.Id == b.ParentFrameId)))
            .Select(b => b.Id).ToArray());
        var frame = new ActualPhaseEndFrame(++_resolutionSequence, phase, continuation,
            Array.AsReadOnly(Array.Empty<ActualPhaseEndItem>()), inherited,
            DelayedEffects: delayedEffects, AfterNormalDrawProgramsCompleted: afterNormalDrawProgramsCompleted,
            DrawPhaseEndedProgramsCompleted: drawPhaseEndedProgramsCompleted,
            ActualDrawCompletionCompleted: actualDrawCompletionCompleted, ParentFrameId: parentFrameId);
        PushRuntimeFrame(frame); AdvanceRuntimeFrame(frame.Id); return true;
    }

    [NativeFrameAutomaticStep(NativeFrameAutomaticPhase.AfterOpening, 160, NativeFrameAutomaticPending.RequireNoneAfterFrame, NativeFrameAutomaticPublish.AfterAdvance)]
    [NativeFrameRoute(typeof(ActualPhaseEndFrame), 370)]
    private void ContinueActualPhaseEnd(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ActualPhaseEndFrame frame || frame.Id != frameId ||
            frame.ActiveWindowFrameId is not null || _pendingDecision is not null)
            throw new InvalidOperationException("An actual phase end cannot advance before its exact child returns.");
        if (!frame.ItemsCaptured)
        {
            if (TryBeginCardsMovedProgramWindow(frame.Id)) return;
            AdvanceEventRulesAndQueueFact(new ActualPhaseEndingEvent(frame.Id, frame.Phase));
            var items = Array.AsReadOnly(CollectQingyaPhaseEndCandidates(frame.Phase).Concat(CollectNishouPhaseEndCandidates(frame.Phase)).Concat(CollectMouSimaShiPhaseEndCandidates(frame.Phase)).Concat(CollectShiHzPhaseEndCandidates(frame.Phase)).Concat(CollectShiLysPhaseEndCandidates(frame.Phase)).ToArray());
            ReplaceRuntimeTop(frame with { Items = items, ItemsCaptured = true });
            frame = (ActualPhaseEndFrame)_resolutionStack.Last();
        }
        var withLateNishou = AppendNishouLatePhaseItems(frame);
        if (!ReferenceEquals(withLateNishou, frame)) ReplaceRuntimeTop(frame = withLateNishou);
        if (frame.ItemIndex >= frame.Items.Count)
        {
            AdvanceEventRulesAndQueueFact(new ActualPhaseEndedEvent(frame.Id, frame.Phase));
            PopResolutionFrame(frame.Id, ResolutionFrameKind.ActualPhaseEnd);
            ReturnActualPhaseEnd(frame); return;
        }
        var item = frame.Items[frame.ItemIndex];
        var context = new ActualPhaseEndContext(frame.Phase, item.EntitlementId, frame.Id);
        var window = new ProgramLifecycleTriggerWindowFrame(++_resolutionSequence, frame.Phase.ActorSeat,
            SkillProgramTriggerWindow.ActualPhaseEnded, Array.AsReadOnly(new[] { item.Candidate }),
            ProgramLifecycleContinuation.ResumeActualPhaseEnd, CaptureProgramTriggerFacts(_players[item.Candidate.OwnerSeat]))
        { ActualPhaseEnd = context };
        ReplaceRuntimeTop(frame with { ActiveWindowFrameId = window.Id });
        PushRuntimeFrame(window); AdvanceRuntimeFrame(window.Id);
    }

    private bool IsExactActualPhaseEndCandidate(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context)
    {
        if (context.Window != SkillProgramTriggerWindow.ActualPhaseEnded || context.ActualPhaseEnd is not { } issued ||
            _resolutionStack.OfType<ProgramLifecycleTriggerWindowFrame>().LastOrDefault() is not { } window ||
            window.Id != context.ParentFrameId || window.Window != context.Window || window.ActualPhaseEnd != issued ||
            window.Continuation != ProgramLifecycleContinuation.ResumeActualPhaseEnd || window.Candidates.Count != 1 ||
            window.CandidateIndex != 0 || window.Candidates[0] != candidate || context.OwnerSeat != candidate.OwnerSeat ||
            _resolutionStack.OfType<ActualPhaseEndFrame>().LastOrDefault() is not { } parent ||
            parent.Id != issued.BoundaryFrameId || parent.ActiveWindowFrameId != window.Id || parent.Phase != issued.Phase ||
            !parent.ItemsCaptured || parent.ItemIndex < 0 || parent.ItemIndex >= parent.Items.Count ||
            parent.Items[parent.ItemIndex] != new ActualPhaseEndItem(candidate, issued.EntitlementId) ||
            parent.Phase.TurnNumber != _turnNumber || parent.Phase.ActualTurnOwnerSeat != _turnProgression.OwnerSeat) return false;
        var wi = _resolutionStack.FindIndex(f => f.Id == window.Id);
        return wi > 0 && _resolutionStack[wi - 1].Id == parent.Id;
    }

    private void ReturnActualPhaseEndWindow(ProgramLifecycleTriggerWindowFrame window)
    {
        if (_resolutionStack.LastOrDefault() is not ActualPhaseEndFrame parent ||
            parent.ActiveWindowFrameId != window.Id || window.ActualPhaseEnd is not { } context ||
            context.BoundaryFrameId != parent.Id || context.Phase != parent.Phase ||
            window.CandidateIndex != 1 || window.Candidates.Count != 1 ||
            parent.ItemIndex >= parent.Items.Count || parent.Items[parent.ItemIndex] != new ActualPhaseEndItem(window.Candidates[0], context.EntitlementId))
            throw new InvalidOperationException("An actual phase item lost its exact mother-frame return.");
        AdvanceEventRulesAndQueueFact(new ActualPhaseEndItemResolvedEvent(parent.Id, parent.Phase,
            context.EntitlementId, window.Candidates[0].OwnerSeat));
        ReplaceRuntimeTop(parent with { ItemIndex = parent.ItemIndex + 1, ActiveWindowFrameId = null });
        AdvanceRuntimeFrame(parent.Id);
    }

    [ProgramWindowReturn(SkillProgramTriggerWindow.ActualPhaseEnded)]
    private void ReturnActualPhaseEndedBinding(ProgramSkillFrame frame, bool completed)
    {
        var context = frame.WindowContext ?? throw new InvalidOperationException("Actual phase binding lost its context.");
        if (_resolutionStack.LastOrDefault() is not ProgramLifecycleTriggerWindowFrame parent ||
            parent.Id != context.ParentFrameId || parent.CandidateIndex != 0 || parent.Candidates.Count != 1 ||
            !MountObserverCandidateMatches(frame, parent.Candidates[0]) ||
            !IsExactActualPhaseEndCandidate(parent.Candidates[0], context))
            throw new InvalidOperationException("An actual phase binding lost its current issued item.");
        AdvanceProgramLifecycleCursor(parent); AdvanceRuntimeFrame(parent.Id);
    }

    private void ReturnActualPhaseEnd(ActualPhaseEndFrame frame)
    {
        if (frame.Continuation != ActualPhaseEndContinuation.OwnedDraw &&
            (_winner != Winner.None || !_players[frame.Phase.ActorSeat].IsAlive))
        {
            if (frame.Continuation == ActualPhaseEndContinuation.Play)
            { CompletePlayPhaseAfterProgramWindow(); return; }
            if (_programPhaseSchedule is not null && ResumeScheduledProgramPhase()) return;
            if (_winner != Winner.None) { AdvanceRulesAndPublishState(); return; }
            // Death during the issued phase-ending use finishes the actual turn;
            // it does not manufacture a Draw, Play or Ending phase for its corpse.
            ClearActualDiscardRecoveryPhase();
            FinalizeEndTurn(_players[frame.Phase.ActualTurnOwnerSeat]); return;
        }
        switch (frame.Continuation)
        {
            case ActualPhaseEndContinuation.Preparation:
                BeginTurnStartAfterProgramLifecycle(_players[frame.Phase.ActorSeat]); break;
            case ActualPhaseEndContinuation.Judgment:
                CompleteTurnStart(_players[frame.Phase.ActorSeat], (DelayedTurnEffects)frame.DelayedEffects); break;
            case ActualPhaseEndContinuation.Draw:
                CompleteTurnStartAfterDraw(_players[frame.Phase.ActorSeat], (DelayedTurnEffects)frame.DelayedEffects,
                    frame.AfterNormalDrawProgramsCompleted, frame.DrawPhaseEndedProgramsCompleted, frame.ActualDrawCompletionCompleted); break;
            case ActualPhaseEndContinuation.Play:
                CompletePlayPhaseAfterProgramWindow(); break;
            case ActualPhaseEndContinuation.Discard:
                EndTurn(); break;
            case ActualPhaseEndContinuation.Ending:
                FinalizeEndTurn(_players[frame.Phase.ActorSeat]); break;
            case ActualPhaseEndContinuation.OwnedDraw:
                if (_resolutionStack.LastOrDefault() is not DrawPhaseObligationFrame draw || draw.Id != frame.ParentFrameId ||
                    draw.DiscardRecoveryPhase != frame.Phase || draw.Stage != DrawPhaseObligationStage.ConsumeMarker)
                    throw new InvalidOperationException("Actual Draw end lost its owning phase producer.");
                AdvanceRuntimeFrame(draw.Id); break;
            default: throw new InvalidOperationException("Unknown actual phase continuation.");
        }
    }

    private bool HasActualPhaseEndBoundaryFrame()
    {
        if (_resolutionStack.FirstOrDefault() is not (ActualPhaseEndFrame or DrawPhaseObligationFrame)) return false;
        var found = false;
        foreach (var frame in _resolutionStack.OfType<ActualPhaseEndFrame>())
        {
            found = true;
            var index = _resolutionStack.FindIndex(f => f.Id == frame.Id);
            if (!TracksActualPhaseEnds || !IsRecordedActualDiscardRecoveryPhase(frame.Phase) ||
                frame.Phase.TurnNumber != _turnNumber || frame.Phase.ActualTurnOwnerSeat != _turnProgression.OwnerSeat ||
                frame.Items is not System.Collections.IList { IsReadOnly: true } ||
                frame.InheritedMovementBatchIds is not System.Collections.IList { IsReadOnly: true } ||
                frame.Items.Select(i => i.EntitlementId).Distinct().Count() != frame.Items.Count ||
                frame.ItemIndex < 0 || frame.ItemIndex > frame.Items.Count ||
                !frame.ItemsCaptured && (frame.Items.Count != 0 || frame.ItemIndex != 0 || frame.ActiveWindowFrameId is not null) ||
                frame.ItemsCaptured && CompleteProgramEventHistory().OfType<ActualPhaseEndingEvent>().Count(e => e.BoundaryFrameId == frame.Id && e.Phase == frame.Phase) != 1 ||
                (frame.Continuation == ActualPhaseEndContinuation.OwnedDraw) != (frame.ParentFrameId is not null) ||
                (frame.ParentFrameId is null ? index != 0 : index < 1 || _resolutionStack[index - 1] is not DrawPhaseObligationFrame draw ||
                    draw.Id != frame.ParentFrameId || draw.Stage != DrawPhaseObligationStage.ConsumeMarker || draw.DiscardRecoveryPhase != frame.Phase))
                throw new InvalidOperationException("An actual phase end lost its exact phase, frozen items or native parent.");
            if (frame.ActiveWindowFrameId is { } id && (frame.ItemIndex >= frame.Items.Count || index + 1 >= _resolutionStack.Count ||
                _resolutionStack[index + 1] is not ProgramLifecycleTriggerWindowFrame child || child.Id != id ||
                child.ActualPhaseEnd != new ActualPhaseEndContext(frame.Phase, frame.Items[frame.ItemIndex].EntitlementId, frame.Id)))
                throw new InvalidOperationException("An actual phase end lost its exact current lifecycle child.");
            if (frame.ActiveWindowFrameId is null && index + 1 < _resolutionStack.Count &&
                (frame.ItemsCaptured || _resolutionStack[index + 1] is not CardsMovedTriggerWindowFrame movement ||
                 movement.ResumeActualPhaseEndFrameId != frame.Id || !frame.InheritedMovementBatchIds.Contains(movement.Batch.Id) ||
                 movement.Batch.AwaitingProgramFrameId is not null))
                throw new InvalidOperationException("An actual phase end retained an unissued native child.");
        }
        return found;
    }
}
