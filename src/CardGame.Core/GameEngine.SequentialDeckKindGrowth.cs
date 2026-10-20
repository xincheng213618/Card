namespace CardGame.Core;

public sealed partial class GameEngine
{
    private static CardConversionSource SequentialDeckKindGrowthSource(ProgramSkillFrame frame) =>
        new(frame.SkillId, GetProgramBindingId(frame), frame.OwnerSeat, frame.SkillInstanceId);
    private static string SequentialDeckKindGrowthReason(long frameId, int step) =>
        $"skill-program.sequential-deck-kind-growth.{frameId}.{step}.gain";

    private bool HasSequentialDeckKindGrowthParent(ProgramSkillFrame frame)
    {
        var index = _resolutionStack.FindIndex(item => item.Id == frame.Id);
        return index > 0 && frame.TriggerId is not null && frame.InstructionIndex == 1 &&
            frame.SelectedCardIds.Count == 0 && frame.SelectedTargetSeats.Count == 0 &&
            frame.WindowContext is { Window: SkillProgramTriggerWindow.TurnStartBeforeNormalFlow } context &&
            context.OwnerSeat == frame.OwnerSeat && context.SourceSeat == frame.OwnerSeat && context.TargetSeat == frame.OwnerSeat &&
            _resolutionStack[index - 1] is ProgramLifecycleTriggerWindowFrame parent && parent.Id == context.ParentFrameId &&
            parent.OwnerSeat == frame.OwnerSeat && parent.Window == context.Window &&
            parent.Continuation == ProgramLifecycleContinuation.NormalTurnStart &&
            parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count &&
            MountObserverCandidateMatches(frame, parent.Candidates[parent.CandidateIndex]) &&
            AcquireDeckKindSequentiallyAndGrowOnShortfallDescriptor.IsTrigger(GetProgramTrigger(frame));
    }

    private SkillProgramStepOutcome AcquireDeckKindSequentiallyAndGrowOnShortfall(ProgramSkillFrame supplied, SkillProgramEffect effect)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (_resolutionStack.LastOrDefault()?.Id != frame.Id || frame.SequentialDeckKindGrowth is not null ||
            !HasSequentialDeckKindGrowthParent(frame) || effect.Op != SkillProgramEffectOp.AcquireDeckKindSequentiallyAndGrowOnShortfall ||
            effect.OutputKind != CardKind.Duel || effect.NumberExpression != SkillProgramNumberExpression.OwnerMaxHp ||
            _turnNumber < 1 || _turnProgression.OwnerSeat != frame.OwnerSeat || _currentSeat != frame.OwnerSeat)
            throw new InvalidOperationException("Sequential deck-kind acquisition requires its exact own preparation candidate.");
        var owner = _players[frame.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive || !owner.HasHpValue ||
            !HasRuntimeSkillInstance(owner, frame.SkillId, frame.SkillInstanceId)) return SkillProgramStepOutcome.Continue;
        var receipt = new ProgramSequentialDeckKindGrowthReceipt(frame.Id, frame.InstructionIndex,
            SequentialDeckKindGrowthSource(frame), frame.GameplayHash, frame.WindowContext!.ParentFrameId,
            _turnNumber, effect.OutputKind.Value, Math.Max(0, owner.MaxHp));
        ReplaceRuntimeTop(frame = frame with { SequentialDeckKindGrowth = receipt });
        AdvanceEventRulesAndQueueFact(new SequentialDeckKindGrowthStartedEvent(frame.Id, receipt.Source,
            receipt.GameplayHash, receipt.PreparationWindowId, receipt.TurnNumber, receipt.OutputKind, receipt.RequestedCount));
        ResumeSequentialDeckKindGrowth(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 1244)]
    private bool ResumeSequentialDeckKindGrowth(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { SequentialDeckKindGrowth: not null } frame || frame.Id != frameId)
            return false;
        while (true)
        {
            frame = GetActiveProgramFrame(frameId);
            AssertSequentialDeckKindGrowth(frame);
            var receipt = frame.SequentialDeckKindGrowth!;
            if (receipt.Stage == SequentialDeckKindGrowthStage.GainChildren)
            {
                if (DrainSequentialDeckKindGrowthChildren(frame)) return true;
                ReplaceRuntimeTop(frame = frame with
                {
                    PendingMovementContinuation = null,
                    SequentialDeckKindGrowth = receipt = receipt with { Stage = SequentialDeckKindGrowthStage.Ready }
                });
            }
            if (_winner != Winner.None || !_players[frame.OwnerSeat].IsAlive)
            { FinishSequentialDeckKindGrowth(frame, cancelled: true); return true; }
            if (receipt.Steps.Count == receipt.RequestedCount)
            { FinishSequentialDeckKindGrowth(frame, cancelled: false); return true; }
            var failed = receipt.Steps.Where(step => step.ActualObtained == 0).Select(step => step.CardId).ToHashSet();
            var card = _cardZones.CardsAt(CardLocation.DrawPile).Reverse()
                .FirstOrDefault(item => item.Kind == receipt.OutputKind && !failed.Contains(item.Id));
            if (card is null)
            { FinishSequentialDeckKindGrowth(frame, cancelled: false); return true; }
            var before = (long)_movementSequence;
            var stepIndex = receipt.Steps.Count;
            ReplaceRuntimeTop(frame with
            {
                PendingMovementContinuation = new(frame.OwnerSeat, 0, null),
                SequentialDeckKindGrowth = receipt with { Stage = SequentialDeckKindGrowthStage.GainChildren }
            });
            MoveProgramCardsFromMultipleSources([card.Id], CardLocation.Hand(frame.OwnerSeat),
                new(SequentialDeckKindGrowthReason(frame.Id, stepIndex)), (batch, movements) =>
                {
                    var active = GetActiveProgramFrame(frameId);
                    var step = new SequentialDeckKindGrowthStepReceipt(stepIndex, card.Id, batch, before,
                        _movementSequence, movements.Count(move => move.CardId == card.Id &&
                            move.From == CardLocation.DrawPile && move.To == CardLocation.Hand(frame.OwnerSeat)))
                    { Movements = movements };
                    ReplaceRuntimeTop(active with
                    {
                        SequentialDeckKindGrowth = active.SequentialDeckKindGrowth! with
                        { Steps = active.SequentialDeckKindGrowth.Steps.Append(step).ToArray() }
                    });
                    AdvanceEventRulesAndQueueFact(new SequentialDeckKindGrowthStepPaidEvent(frameId, step));
                });
            // Drain this exact batch before observing the pile again. In the no-child
            // case iterate here rather than recursively re-entering the instruction.
        }
    }

    private bool DrainSequentialDeckKindGrowthChildren(ProgramSkillFrame frame) =>
        TryBeginQueuedRecoveryReplacement(frame.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(frame.Id, CharacterStateContinuation.Program) ||
        TryBeginHpChangedProgramWindow(frame.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(frame.Id) || TryBeginAdvancedSkillsChanged(frame.Id);

    private void FinishSequentialDeckKindGrowth(ProgramSkillFrame frame, bool cancelled)
    {
        var receipt = frame.SequentialDeckKindGrowth!;
        if (receipt.Stage != SequentialDeckKindGrowthStage.Ready || frame.PendingMovementContinuation is not null)
            throw new InvalidOperationException("Sequential deck-kind growth cannot finish before its issued gain children return.");
        var shortfall = receipt.RequestedCount - receipt.ActualObtained;
        ReplaceRuntimeTop(frame = frame with { SequentialDeckKindGrowth = receipt with
            { Stage = SequentialDeckKindGrowthStage.Finished, Shortfall = shortfall, Cancelled = cancelled } });
        if (!cancelled)
            AdvanceEventRulesAndQueueFact(new SequentialDeckKindGrowthGrantedEvent(frame.Id, receipt.Source, receipt.GameplayHash,
                receipt.PreparationWindowId, receipt.TurnNumber, receipt.OutputKind, receipt.RequestedCount, receipt.ActualObtained, shortfall));
        AdvanceEventRulesAndQueueFact(new SequentialDeckKindGrowthCompletedEvent(frame.Id, receipt.Steps.Count,
            receipt.ActualObtained, shortfall, cancelled));
        FinishProgramSkill(frame, true);
    }

    [ProgramContinuation(ProgramContinuationKind.Movement, 1244)]
    private bool ReturnSequentialDeckKindGrowthMovement(ProgramSkillFrame frame) =>
        frame.SequentialDeckKindGrowth is not null && ResumeSequentialDeckKindGrowth(frame.Id);

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 1244)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 1244)]
    private bool CanContinueSequentialDeckKindGrowth(ProgramSkillFrame frame) =>
        frame.SequentialDeckKindGrowth is { Steps.Count: > 0 } && ValidSequentialDeckKindGrowth(frame);

    [ProgramMovementProof(ProgramMovementProofPhase.EffectPayments, 490)]
    private bool IsSequentialDeckKindGrowthMovement(ProgramSkillFrame frame, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        effect?.Op == SkillProgramEffectOp.AcquireDeckKindSequentiallyAndGrowOnShortfall &&
        frame.SequentialDeckKindGrowth is { Stage: SequentialDeckKindGrowthStage.GainChildren, Steps.Count: > 0 } &&
        pending.SubjectSeat == frame.OwnerSeat && pending.BeforeCount == 0 && pending.CoverageResultBind is null &&
        ValidSequentialDeckKindGrowth(frame);

    private static bool SameSequentialDeckKindGrowthStep(SequentialDeckKindGrowthStepReceipt left, SequentialDeckKindGrowthStepReceipt right) =>
        left.Index == right.Index && left.CardId == right.CardId && left.BatchId == right.BatchId &&
        left.SequenceBefore == right.SequenceBefore && left.SequenceAfter == right.SequenceAfter &&
        left.ActualObtained == right.ActualObtained && left.Movements.SequenceEqual(right.Movements);

    private bool ValidSequentialDeckKindGrowthStep(long frameId, int owner, CardKind kind, SequentialDeckKindGrowthStepReceipt step)
    {
        if (step.Index < 0 || step.BatchId <= 0 || step.SequenceBefore < 0 || step.SequenceAfter < step.SequenceBefore ||
            step.SequenceAfter > _movementSequence || step.ActualObtained is < 0 or > 1 ||
            GetAdvancedCard(step.CardId).Kind != kind ||
            !step.Movements.SequenceEqual(_cardMovements.Where(move => move.Sequence > step.SequenceBefore && move.Sequence <= step.SequenceAfter)) ||
            step.Movements.Any(move => move.CardId != step.CardId || move.CardKind != kind || move.From != CardLocation.DrawPile ||
                move.Reason.Value != SequentialDeckKindGrowthReason(frameId, step.Index)) ||
            step.Movements.Count(move => move.To == CardLocation.Hand(owner)) != step.ActualObtained)
            return false;
        return ProgramEventHistory<SequentialDeckKindGrowthStepPaidEvent>()
            .Where(item => item.ProgramFrameId == frameId && item.Step.Index == step.Index).ToArray() is [var issued] &&
            SameSequentialDeckKindGrowthStep(step, issued.Step);
    }

    private bool ValidSequentialDeckKindGrowth(ProgramSkillFrame frame)
    {
        if (frame.SequentialDeckKindGrowth is not { } receipt || !HasSequentialDeckKindGrowthParent(frame) ||
            receipt.ProgramFrameId != frame.Id || receipt.InstructionIndex != frame.InstructionIndex ||
            receipt.Source != SequentialDeckKindGrowthSource(frame) || receipt.GameplayHash != frame.GameplayHash ||
            receipt.GameplayHash != _contentRegistry.GetSkill(frame.SkillId).Program?.GameplayHash ||
            receipt.PreparationWindowId != frame.WindowContext!.ParentFrameId || receipt.TurnNumber < 1 ||
            receipt.OutputKind != CardKind.Duel || receipt.RequestedCount < 0 || !Enum.IsDefined(receipt.Stage) ||
            receipt.Steps.Count > receipt.RequestedCount ||
            !receipt.Steps.Select(step => step.Index).SequenceEqual(Enumerable.Range(0, receipt.Steps.Count)) ||
            receipt.Steps.Any(step => !ValidSequentialDeckKindGrowthStep(frame.Id, frame.OwnerSeat, receipt.OutputKind, step)))
            return false;
        for (var index = 1; index < receipt.Steps.Count; index++)
            if (receipt.Steps[index].SequenceBefore < receipt.Steps[index - 1].SequenceAfter ||
                receipt.Steps.Take(index).Any(step => step.CardId == receipt.Steps[index].CardId && step.ActualObtained == 0)) return false;
        var starts = ProgramEventHistory<SequentialDeckKindGrowthStartedEvent>().Where(item => item.ProgramFrameId == frame.Id).ToArray();
        if (starts is not [var start] || start != new SequentialDeckKindGrowthStartedEvent(frame.Id, receipt.Source, receipt.GameplayHash,
                receipt.PreparationWindowId, receipt.TurnNumber, receipt.OutputKind, receipt.RequestedCount) ||
            ProgramEventHistory<SequentialDeckKindGrowthStepPaidEvent>().Count(item => item.ProgramFrameId == frame.Id) != receipt.Steps.Count ||
            ProgramEventHistory<ProgramBindingStartedEvent>().Count(item => item.FrameId == frame.Id && item.OwnerSeat == frame.OwnerSeat &&
                item.SkillId == frame.SkillId && item.BindingId == frame.TriggerId && item.SkillInstanceId == frame.SkillInstanceId &&
                item.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow) != 1) return false;
        var history = CompleteProgramEventHistory().ToArray();
        var startIndex = Array.FindIndex(history, item => ReferenceEquals(item, start));
        var turn = history.Take(startIndex).OfType<TurnStartedEvent>().LastOrDefault();
        if (startIndex < 0 || turn is null || turn.TurnNumber != receipt.TurnNumber || turn.ActorSeat != frame.OwnerSeat ||
            history.Take(startIndex).OfType<TurnEndedEvent>().Any(item => item.TurnNumber == turn.TurnNumber && item.ActorSeat == turn.ActorSeat)) return false;
        var growth = history.OfType<SequentialDeckKindGrowthGrantedEvent>().Where(item => item.ProgramFrameId == frame.Id).ToArray();
        var completed = history.OfType<SequentialDeckKindGrowthCompletedEvent>().Where(item => item.ProgramFrameId == frame.Id).ToArray();
        if (receipt.Stage != SequentialDeckKindGrowthStage.Finished)
            return receipt.Shortfall is null && !receipt.Cancelled && growth.Length == 0 && completed.Length == 0 &&
                (receipt.Stage == SequentialDeckKindGrowthStage.Ready ? frame.PendingMovementContinuation is null :
                    receipt.Steps.Count > 0 && frame.PendingMovementContinuation is { BeforeCount: 0, CoverageResultBind: null } pending &&
                    pending.SubjectSeat == frame.OwnerSeat);
        return frame.PendingMovementContinuation is null && receipt.Shortfall == receipt.RequestedCount - receipt.ActualObtained &&
            (receipt.Cancelled ? growth.Length == 0 : growth is [var granted] && ValidSequentialDeckKindGrowthGrant(granted)) &&
            completed is [var finished] && finished == new SequentialDeckKindGrowthCompletedEvent(frame.Id, receipt.Steps.Count,
                receipt.ActualObtained, receipt.Shortfall.Value, receipt.Cancelled);
    }

    private bool ValidSequentialDeckKindGrowthGrant(SequentialDeckKindGrowthGrantedEvent growth)
    {
        if (growth.ProgramFrameId <= 0 || !IsValidPlayerSeat(growth.Source.OwnerSeat) || growth.Amount < 0 ||
            growth.RequestedCount < 0 || growth.ActualObtained < 0 || growth.ActualObtained > growth.RequestedCount ||
            growth.Amount != growth.RequestedCount - growth.ActualObtained || growth.TurnNumber < 1 ||
            growth.PreparationWindowId <= 0 || growth.OutputKind != CardKind.Duel ||
            _contentRegistry.GetSkill(growth.Source.SkillId).Program is not { } program || program.GameplayHash != growth.GameplayHash ||
            program.Triggers.SingleOrDefault(item => item.Id == growth.Source.BindingId) is not { } trigger ||
            !AcquireDeckKindSequentiallyAndGrowOnShortfallDescriptor.IsTrigger(trigger)) return false;
        var starts = ProgramEventHistory<SequentialDeckKindGrowthStartedEvent>().Where(item => item.ProgramFrameId == growth.ProgramFrameId).ToArray();
        var steps = ProgramEventHistory<SequentialDeckKindGrowthStepPaidEvent>().Where(item => item.ProgramFrameId == growth.ProgramFrameId).ToArray();
        return starts is [var start] && start == new SequentialDeckKindGrowthStartedEvent(growth.ProgramFrameId, growth.Source,
            growth.GameplayHash, growth.PreparationWindowId, growth.TurnNumber, growth.OutputKind, growth.RequestedCount) &&
            ProgramEventHistory<ProgramBindingStartedEvent>().Count(item => item.FrameId == growth.ProgramFrameId &&
                item.OwnerSeat == growth.Source.OwnerSeat && item.SkillId == growth.Source.SkillId &&
                item.BindingId == growth.Source.BindingId && item.SkillInstanceId == growth.Source.SkillInstanceId &&
                item.Window == SkillProgramTriggerWindow.TurnStartBeforeNormalFlow) == 1 &&
            steps.Length <= growth.RequestedCount && steps.Select(item => item.Step.Index).SequenceEqual(Enumerable.Range(0, steps.Length)) &&
            steps.Select(item => item.Step.BatchId).Distinct().Count() == steps.Length &&
            steps.Sum(item => item.Step.ActualObtained) == growth.ActualObtained &&
            steps.All(item => ValidSequentialDeckKindGrowthStep(growth.ProgramFrameId, growth.Source.OwnerSeat, growth.OutputKind, item.Step));
    }

    // Issued permanent contributions belong to the character, not a currently enabled
    // skill instance. Queries are pure and never produce another grant or Draw.
    private int SequentialDeckKindGrowthBonus(int owner)
    {
        if (!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.AcquireDeckKindSequentiallyAndGrowOnShortfall)) return 0;
        var events = ProgramEventHistory<SequentialDeckKindGrowthGrantedEvent>().Where(item => item.Source.OwnerSeat == owner).ToArray();
        if (events.Select(item => item.ProgramFrameId).Distinct().Count() != events.Length || events.Any(item => !ValidSequentialDeckKindGrowthGrant(item)))
            throw new InvalidOperationException("Permanent deck-shortfall growth lost its once-issued acquisition ledger.");
        return events.Aggregate(0, (sum, item) => checked(sum + item.Amount));
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.BeforeSelectionState, 1244)]
    private void AssertSequentialDeckKindGrowth(ProgramSkillFrame frame)
    {
        if (frame.SequentialDeckKindGrowth is not null && !ValidSequentialDeckKindGrowth(frame))
            throw new InvalidOperationException("Sequential deck-kind acquisition lost its exact preparation, frozen amount or paid entity/child ledger.");
        if (frame.SequentialDeckKindGrowth is not { Stage: SequentialDeckKindGrowthStage.GainChildren } receipt) return;
        var index = _resolutionStack.FindIndex(item => item.Id == frame.Id);
        if (index + 1 < _resolutionStack.Count && _resolutionStack[index + 1] is CardsMovedTriggerWindowFrame child)
        {
            var step = receipt.Steps[^1];
            if (child.Batch.Id != step.BatchId || child.Batch.ParentFrameId != frame.Id ||
                child.Batch.AwaitingProgramFrameId != frame.Id || child.ResumeProgramFrameId is not null ||
                child.Batch.TurnNumber != receipt.TurnNumber || child.Batch.OriginOwnerSeat != frame.OwnerSeat ||
                child.Batch.OriginSkillId != frame.SkillId || child.Batch.OriginSkillInstanceId != frame.SkillInstanceId ||
                !child.Batch.Movements.SequenceEqual(step.Movements) || child.Batch.NativeDrawInvocation is not null)
                throw new InvalidOperationException("Sequential acquisition retained a movement window outside its exact paid entity batch.");
        }
    }

    [ProgramCapability]
    private sealed class SequentialDeckKindGrowthCapability(GameEngine engine) : ISequentialDeckKindGrowthHost
    {
        public SkillProgramStepOutcome AcquireDeckKindSequentiallyAndGrowOnShortfall(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.AcquireDeckKindSequentiallyAndGrowOnShortfall(frame, effect);
    }
}
