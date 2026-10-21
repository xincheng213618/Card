namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string PingluObtainReason = "skill-program.le-zhou-yu.pinglu.obtain";
    private bool HasLeZhouYuPingluCapability => _contentRegistry.ProgramDependencies
        .HasActivationOperation(SkillProgramEffectOp.ResolvePingluInRangeRandomHands);
    private static CardConversionSource PingluSource(ProgramSkillFrame frame) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_zhou_yu_le_LeZhouYuPinglu.PingluSource(frame);

    [ProgramCapability]
    private sealed class LeZhouYuPingluCapability(GameEngine engine) : ILeZhouYuPingluHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame) => engine.BeginPinglu(frame); }

    private ActualDiscardRecoveryPhaseKey? PingluPlay(int actor)
    {
        var phase = CurrentActualDiscardRecoveryPhase();
        return _setupComplete && _winner == Winner.None && _phase == TurnPhase.Play && _currentSeat == actor &&
            _players[actor].IsAlive && phase is { Kind: ActualDiscardRecoveryPhaseKind.Play } &&
            phase.ActorSeat == actor && IsRecordedActualDiscardRecoveryPhase(phase) ? phase : null;
    }
    private int[] PingluTargets(int actor) => _players.Where(player => player.IsAlive && player.Seat != actor &&
        GetCombatDistance(actor, player.Seat) <= GetAttackRange(actor))
        .OrderBy(player => RelativeTableOrder(actor, player.Seat)).Select(player => player.Seat).ToArray();

    // One invocation's original Hand residency blocks this phase. A later gain of
    // the same entity is a new residency, and a new real Play has a different token.
    private bool HasPingluCustody(int owner, ActualDiscardRecoveryPhaseKey phase)
    {
        var starts = ProgramEventHistory<PingluStartedEvent>().Where(e => e.Source.OwnerSeat == owner && e.Phase == phase)
            .Select(e => e.FrameId).ToHashSet();
        return ProgramEventHistory<PingluEntityObtainedEvent>().Any(e => starts.Contains(e.FrameId) &&
            e.To == CardLocation.Hand(owner) && _cardZones.GetLocation(e.CardId) == CardLocation.Hand(owner) &&
            !_cardMovements.Any(m => m.CardId == e.CardId && m.Sequence > e.Sequence &&
                m.From == CardLocation.Hand(owner) && m.To != CardLocation.Hand(owner)));
    }
    [ProgramActivationLegality(ProgramActivationLegalityPhase.BeforeCardCount, 28820,
        SkillProgramEffectOp.ResolvePingluInRangeRandomHands)]
    private bool CanActivatePinglu(ProgramActivationLegalityContext gate) =>
        PingluPlay(gate.OwnerSeat) is { } phase && !HasPingluCustody(gate.OwnerSeat, phase) &&
        PingluTargets(gate.OwnerSeat).Any(seat => GetHand(_players[seat]).Count > 0);

    private SkillProgramStepOutcome BeginPinglu(ProgramSkillFrame supplied)
    {
        var frame = GetActiveProgramFrame(supplied.Id);
        if (frame.Pinglu is not null) { ContinuePinglu(frame.Id); return SkillProgramStepOutcome.AwaitChild; }
        if (frame.SkillId != "le:pinglu" || frame.ActivationId != "all-in-range-random-hand" || frame.TriggerId is not null ||
            frame.WindowContext is not null || frame.InstructionIndex != 1 || frame.SelectedCardIds.Count != 0 ||
            frame.SelectedTargetSeats.Count != 0 || PingluPlay(frame.OwnerSeat) is not { } phase ||
            !HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) ||
            HasPingluCustody(frame.OwnerSeat, phase))
            throw new InvalidOperationException("Pinglu requires its real current Play, eligible source and released original Hand cohort.");
        var targets = PingluTargets(frame.OwnerSeat);
        if (!targets.Any(seat => GetHand(_players[seat]).Count > 0))
            throw new InvalidOperationException("Pinglu cannot issue an empty random obtain.");
        var samples = new List<PingluTargetSample>(targets.Length);
        foreach (var target in targets)
        {
            var hand = GetHand(_players[target]); var beforeRandom = _random.State;
            var card = hand.Count > 0 ? hand[_random.Next(hand.Count)] : null;
            samples.Add(new(samples.Count, target, hand.Count, card?.Id, card?.Kind,
                card is null ? null : CardLocation.Hand(target), beforeRandom, _random.State));
        }
        var before = _movementSequence;
        var receipt = new PingluReceipt(PingluSource(frame), frame.GameplayHash, phase, frame.InstructionIndex,
            _turnNumber, _turnProgression.OwnerSeat, before)
        { Stage = PingluStage.ObtainChildren, Targets = samples, Before = before, After = before };
        ReplaceRuntimeTop(frame = frame with { Pinglu = receipt, PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new PingluStartedEvent(frame.Id, receipt.Source, receipt.GameplayHash,
            phase, receipt.ActualTurn, receipt.TurnOwner, targets.Length, before));
        foreach (var sample in receipt.Targets) AdvanceEventRulesAndQueueFact(new PingluTargetSampledEvent(frame.Id,
            sample.Index, sample.TargetSeat, sample.HandCount, sample.CardId, sample.PrintedKind, sample.From,
            sample.RandomBefore, sample.RandomAfter, before));
        var selected = receipt.Targets.Where(s => s.CardId is not null).ToArray();
        MoveProgramCardsFromMultipleSources(selected.Select(s => s.CardId!.Value).ToArray(),
            CardLocation.Hand(frame.OwnerSeat), new(PingluObtainReason), (batchId, moves) =>
            {
                var current = GetActiveProgramFrame(frame.Id); var after = _movementSequence;
                ReplaceRuntimeTop(current with { Pinglu = current.Pinglu! with { BatchId = batchId, After = after,
                    ActualCount = moves.Count(m => m.To == CardLocation.Hand(frame.OwnerSeat)) } });
                for (var index = 0; index < moves.Count; index++)
                {
                    var move = moves[index]; var sample = selected.Single(s => s.CardId == move.CardId);
                    AdvanceEventRulesAndQueueFact(new PingluEntityObtainedEvent(frame.Id, index, sample.TargetSeat,
                        move.CardId, move.CardKind, move.From, move.To, move.Sequence, batchId));
                }
                AdvanceEventRulesAndQueueFact(new PingluObtainPaidEvent(frame.Id, batchId, moves.Count,
                    moves.Count(m => m.To == CardLocation.Hand(frame.OwnerSeat)), before, after));
            }, disclosure: CardMovementDisclosure.FaceDown);
        ContinuePinglu(frame.Id); return SkillProgramStepOutcome.AwaitChild;
    }
    private void ContinuePinglu(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Pinglu: { } receipt } frame ||
            frame.Id != id || _pendingDecision is not null) return;
        AssertPinglu(frame);
        if (TryBeginQueuedRecoveryReplacement(id, PostEventContinuation.AwaitedProgramMovement) ||
            TryBeginCharacterStateProgramWindow(id, CharacterStateContinuation.Program) || TryBeginAdvancedSkillsChanged(id) ||
            TryBeginHpChangedProgramWindow(id, PostEventContinuation.AwaitedProgramMovement) || TryBeginCardsMovedProgramWindow(id)) return;
        ReplaceRuntimeTop(frame = GetActiveProgramFrame(id) with { PendingMovementContinuation = null });
        AdvanceEventRulesAndQueueFact(new PingluCompletedEvent(id, receipt.ActualCount));
        FinishProgramSkill(frame, true);
    }
    [ProgramContinuation(ProgramContinuationKind.Resume, 28820)]
    private bool ResumePinglu(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Pinglu: not null } frame || frame.Id != id) return false;
        ContinuePinglu(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 28820)]
    private bool ReturnPingluMovement(ProgramSkillFrame frame)
    { if (frame.Pinglu is null) return false; ContinuePinglu(frame.Id); return true; }
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 28820)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 28820)]
    private bool CanContinueIssuedPinglu(ProgramSkillFrame frame) => ValidPingluStarted(frame) && frame.Pinglu is { ActualCount: >= 0 };
}
