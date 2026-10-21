namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string LeZhouFeiXianshuGiftReason = "skill-program.le-zhou-fei.xianshu.gift";
    private const string LeZhouFeiXianshuDrawReason = "skill-program.le-zhou-fei.xianshu.draw";
    private static CardConversionSource LeZhouFeiXianshuSource(ProgramSkillFrame frame) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_zhou_fei_le_LeZhouFeiXianshu.LeZhouFeiXianshuSource(frame);

    [ProgramCapability]
    private sealed class LeZhouFeiXianshuCapability(GameEngine engine) :
        ILeZhouFeiXianshuHost, ILeZhouFeiXianshuSelectionQuery
    {
        public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame) => engine.BeginLeZhouFeiXianshu(frame);
        public bool CanGive(int ownerSeat, Card card) => engine.HasLingkongHandEntityMark(ownerSeat, card.Id);
    }

    private ActualDiscardRecoveryPhaseKey? LeZhouFeiXianshuPlay(int ownerSeat)
    {
        var phase = CurrentActualDiscardRecoveryPhase();
        return _setupComplete && _winner == Winner.None && _phase == TurnPhase.Play && _currentSeat == ownerSeat &&
            _players[ownerSeat].IsAlive && phase is { Kind: ActualDiscardRecoveryPhaseKind.Play } &&
            phase.ActorSeat == ownerSeat && IsRecordedActualDiscardRecoveryPhase(phase) ? phase : null;
    }

    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 28320,
        SkillProgramEffectOp.ResolveXianshuGiftDrawAndHealth)]
    private bool CanActivateLeZhouFeiXianshu(ProgramActivationLegalityContext gate) =>
        LeZhouFeiXianshuPlay(gate.OwnerSeat) is not null &&
        GetHand(gate.Owner).Any(card => HasLingkongHandEntityMark(gate.OwnerSeat, card.Id)) &&
        _players.Any(player => player.IsAlive && player.Seat != gate.OwnerSeat);

    private SkillProgramStepOutcome BeginLeZhouFeiXianshu(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.LeZhouFeiXianshu is not null)
        { ContinueLeZhouFeiXianshu(frame.Id); return SkillProgramStepOutcome.AwaitChild; }
        if (frame.SkillId != "le:xianshu" || frame.ActivationId != "marked-hand-gift" || frame.TriggerId is not null ||
            frame.WindowContext is not null || frame.InstructionIndex != 1 ||
            frame.SelectedCardIds is not [var cardId] || frame.SelectedTargetSeats is not [var targetSeat] ||
            LeZhouFeiXianshuPlay(frame.OwnerSeat) is not { } phase || targetSeat == frame.OwnerSeat ||
            !IsValidPlayerSeat(targetSeat) || !_players[targetSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) ||
            !HasLingkongHandEntityMark(frame.OwnerSeat, cardId))
            throw new InvalidOperationException("Xianshu needs its current owner's exact marked Hand gift and other living recipient.");
        var card = GetHand(_players[frame.OwnerSeat]).Single(c => c.Id == cardId);
        var before = _movementSequence;
        var receipt = new LeZhouFeiXianshuReceipt(LeZhouFeiXianshuSource(frame), frame.GameplayHash,
            phase, frame.InstructionIndex, _turnNumber, _turnProgression.OwnerSeat, card.Id, card.Kind,
            EffectiveSuit(_players[frame.OwnerSeat], card), targetSeat, before)
        { Stage = LeZhouFeiXianshuStage.GiftChildren, Before = before, After = before };
        ReplaceRuntimeTop(frame = frame with { LeZhouFeiXianshu = receipt,
            PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new XianshuGiftStartedEvent(frame.Id, receipt.Source, receipt.GameplayHash,
            phase, receipt.ActualTurn, receipt.TurnOwner, card.Id, card.Kind, receipt.GiftSuit, targetSeat, before));
        MoveProgramCardsFromMultipleSources([card.Id], CardLocation.Hand(targetSeat), new(LeZhouFeiXianshuGiftReason), NativeGiveCompletionRecords(frame.Id,frame.OwnerSeat,targetSeat,[card.Id],_movementSequence,(batchId, records) =>
            {
                var paid = records.Single();
                var current = GetActiveProgramFrame(frame.Id);
                ReplaceRuntimeTop(current with { LeZhouFeiXianshu = current.LeZhouFeiXianshu! with
                    { GiftBatchId = batchId, GiftSequence = paid.Sequence, After = paid.Sequence } });
                AdvanceEventRulesAndQueueFact(new XianshuGiftPaidEvent(frame.Id, batchId, paid.CardId,
                    paid.From, paid.To, paid.Sequence));
            }));
        ContinueLeZhouFeiXianshu(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private bool DrainLeZhouFeiXianshu(ProgramSkillFrame frame) =>
        TryBeginQueuedRecoveryReplacement(frame.Id, PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCharacterStateProgramWindow(frame.Id, CharacterStateContinuation.Program) ||
        TryBeginAdvancedSkillsChanged(frame.Id) ||
        TryBeginHpChangedProgramWindow(frame.Id, frame.LeZhouFeiXianshu!.HealthAction == XianshuHealthAction.LoseHp
            ? PostEventContinuation.Program : PostEventContinuation.AwaitedProgramMovement) ||
        TryBeginCardsMovedProgramWindow(frame.Id);

    private void ContinueLeZhouFeiXianshu(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { LeZhouFeiXianshu: { } receipt } frame ||
            frame.Id != frameId || _pendingDecision is not null) return;
        AssertLeZhouFeiXianshu(frame);
        if (frame.NativeDraw is not null || receipt.Stage == LeZhouFeiXianshuStage.Drawing) return;
        if (receipt.Stage == LeZhouFeiXianshuStage.HealthIssuing)
        {
            if (!_players[receipt.TargetSeat].IsAlive || _winner != Winner.None)
            { CompleteLeZhouFeiXianshu(frame); return; }
            new ProgramSkillHost(this).LoseHp(frame.Id, frame.SkillId, receipt.TargetSeat, 1);
            if (_resolutionStack.LastOrDefault()?.Id != frameId) return;
            frame = GetActiveProgramFrame(frameId); receipt = frame.LeZhouFeiXianshu!;
            if (receipt.Stage == LeZhouFeiXianshuStage.HealthIssuing)
                throw new InvalidOperationException("Xianshu's native HP loss returned without applying or preventing its one deduction.");
        }
        if (DrainLeZhouFeiXianshu(frame)) return;
        frame = GetActiveProgramFrame(frameId); receipt = frame.LeZhouFeiXianshu!;
        ReplaceRuntimeTop(frame = frame with { PendingMovementContinuation = null });
        if (_winner != Winner.None) { CompleteLeZhouFeiXianshu(frame); return; }
        if (receipt.Stage == LeZhouFeiXianshuStage.GiftChildren)
        {
            var owner = _players[frame.OwnerSeat]; var target = _players[receipt.TargetSeat];
            var count = owner.HasHpValue && target.HasHpValue
                ? (int)Math.Min(5L, Math.Abs((long)owner.Hp - target.Hp)) : 0;
            ReplaceRuntimeTop(frame = frame with { LeZhouFeiXianshu = receipt with
                { Stage = LeZhouFeiXianshuStage.Drawing, RequestedDraw = count, Before = _movementSequence, After = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new XianshuDrawRequestedEvent(frameId, owner.HasHpValue,
                owner.HasHpValue ? owner.Hp : 0, target.HasHpValue, target.HasHpValue ? target.Hp : 0, count));
            BeginOperationProgramDraw(frame, frame.OwnerSeat, count, new(LeZhouFeiXianshuDrawReason));
            return;
        }
        if (receipt.Stage == LeZhouFeiXianshuStage.DrawChildren)
        {
            var owner = _players[frame.OwnerSeat]; var target = _players[receipt.TargetSeat];
            var action = XianshuHealthAction.None;
            if (target.IsAlive && owner.HasHpValue && target.HasHpValue)
            {
                if (IsRedSuit(receipt.GiftSuit) && target.Hp <= owner.Hp) action = XianshuHealthAction.Recover;
                else if ((receipt.GiftSuit is Suit.Spade or Suit.Club) && target.Hp >= owner.Hp) action = XianshuHealthAction.LoseHp;
            }
            ReplaceRuntimeTop(frame = frame with { LeZhouFeiXianshu = receipt with
                { HealthAction = action, Stage = action == XianshuHealthAction.LoseHp
                    ? LeZhouFeiXianshuStage.HealthIssuing : LeZhouFeiXianshuStage.HealthChildren,
                    Before = _movementSequence, After = _movementSequence },
                PendingMovementContinuation = new(receipt.TargetSeat, 0, null) });
            AdvanceEventRulesAndQueueFact(new XianshuHealthEvaluatedEvent(frameId, receipt.GiftSuit,
                owner.HasHpValue, owner.HasHpValue ? owner.Hp : 0, target.HasHpValue,
                target.HasHpValue ? target.Hp : 0, target.IsAlive, action));
            if (action == XianshuHealthAction.Recover)
                new ProgramSkillHost(this).Recover(frameId, frame.OwnerSeat, receipt.TargetSeat, 1, null, null);
            ContinueLeZhouFeiXianshu(frameId);
            return;
        }
        if (receipt.Stage == LeZhouFeiXianshuStage.HealthChildren)
        { CompleteLeZhouFeiXianshu(frame); return; }
        throw new InvalidOperationException("Xianshu resumed outside its exact paid native stage.");
    }

    [ProgramNativeDrawReturn(SkillProgramEffectOp.ResolveXianshuGiftDrawAndHealth)]
    private void ReturnLeZhouFeiXianshuDraw(ProgramSkillFrame supplied, NativeDrawOutcome outcome)
    {
        var frame = GetActiveProgramFrame(supplied.Id); var receipt = frame.LeZhouFeiXianshu!;
        if (receipt.Stage != LeZhouFeiXianshuStage.Drawing || frame.NativeDraw is not null ||
            outcome.RecipientSeat != frame.OwnerSeat || outcome.RequestedCount != receipt.RequestedDraw ||
            outcome.SequenceBefore < receipt.Before || outcome.SequenceAfter < outcome.SequenceBefore ||
            outcome.SequenceAfter > _movementSequence || receipt.DrawOutcome is not null)
            throw new InvalidOperationException("Xianshu's native draw returned to another recipient, amount or already-consumed stage.");
        ReplaceRuntimeTop(frame = frame with { LeZhouFeiXianshu = receipt with
            { Stage = LeZhouFeiXianshuStage.DrawChildren, DrawOutcome = outcome,
                Before = outcome.SequenceBefore, After = outcome.SequenceAfter },
            PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new XianshuDrawReturnedEvent(frame.Id, outcome.RecipientSeat,
            outcome.RequestedCount, outcome.ActualDrawIds.Count, outcome.ReplacementGainedIds.Count,
            outcome.SequenceBefore, outcome.SequenceAfter, outcome.AttemptFrameId));
        ContinueLeZhouFeiXianshu(frame.Id);
    }

    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 28320)]
    private void ObserveLeZhouFeiXianshuHealth(IGameEvent fact)
    {
        if (fact is ProgramSkillHpLostEvent lost) CaptureLeZhouFeiXianshuHpLoss(lost);
        else if (fact is IdentityHpDeductionPreventedEvent prevented) CaptureLeZhouFeiXianshuPreventedHpLoss(prevented);
    }

    private void CaptureLeZhouFeiXianshuHpLoss(ProgramSkillHpLostEvent fact)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { LeZhouFeiXianshu:
                { Stage: LeZhouFeiXianshuStage.HealthIssuing, HealthAction: XianshuHealthAction.LoseHp } receipt } frame ||
            fact.FrameId != frame.Id || fact.SkillId != frame.SkillId || fact.TargetSeat != receipt.TargetSeat ||
            receipt.HpLossCaptured) return;
        ReplaceRuntimeTop(frame with { LeZhouFeiXianshu = receipt with
            { Stage = LeZhouFeiXianshuStage.HealthChildren, HpLossCaptured = true } });
        AdvanceEventRulesAndQueueFact(new XianshuHpLossAppliedEvent(frame.Id, fact.TargetSeat,
            fact.Amount, fact.RemainingHp, false));
    }

    private void CaptureLeZhouFeiXianshuPreventedHpLoss(IdentityHpDeductionPreventedEvent fact)
    {
        if (_resolutionStack.LastOrDefault() is not IdentityAppearanceFrame appearance ||
            appearance.Id != fact.AppearanceFrameId || appearance.Receipt is not
                { Entry: IdentityAppearanceEntry.ProgramHpLoss, ParentFrameId: { } parent } native ||
            _resolutionStack.SingleOrDefault(f => f.Id == parent) is not ProgramSkillFrame
                { LeZhouFeiXianshu: { Stage: LeZhouFeiXianshuStage.HealthIssuing,
                    HealthAction: XianshuHealthAction.LoseHp } receipt } frame ||
            native.OwnerSeat != receipt.TargetSeat || native.OriginalInstructionIndex != frame.InstructionIndex ||
            fact.RequestedDeduction != 1 || receipt.HpLossCaptured) return;
        ReplaceRuntimeFrame(frame.Id, frame with { LeZhouFeiXianshu = receipt with
            { Stage = LeZhouFeiXianshuStage.HealthChildren, HpLossCaptured = true, AppearancePrevented = true } });
        AdvanceEventRulesAndQueueFact(new XianshuHpLossAppliedEvent(frame.Id, receipt.TargetSeat,
            0, _players[receipt.TargetSeat].Hp, true));
    }

    private void CompleteLeZhouFeiXianshu(ProgramSkillFrame frame)
    {
        var receipt = frame.LeZhouFeiXianshu!;
        AdvanceEventRulesAndQueueFact(new XianshuCompletedEvent(frame.Id, receipt.CardId,
            receipt.TargetSeat, receipt.HealthAction ?? XianshuHealthAction.None));
        ReplaceRuntimeTop(frame = frame with { PendingMovementContinuation = null });
        FinishProgramSkill(frame, true);
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 28320)]
    private bool ResumeLeZhouFeiXianshu(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { LeZhouFeiXianshu: not null } frame || frame.Id != id) return false;
        if (frame.NativeDraw is null) ContinueLeZhouFeiXianshu(id);
        return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 28320)]
    private bool ReturnLeZhouFeiXianshuMovement(ProgramSkillFrame frame)
    { if (frame.LeZhouFeiXianshu is null) return false; ContinueLeZhouFeiXianshu(frame.Id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 28320)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 28320)]
    private bool ContinuePaidLeZhouFeiXianshu(ProgramSkillFrame frame) => HasPaidLeZhouFeiXianshuSelection(frame);
}
