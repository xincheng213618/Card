namespace CardGame.Core;

/// <summary>Generic post-movement program host. It contains no skill ids.</summary>
public sealed partial class GameEngine
{
    private readonly bool _hasGainPhaseQualificationCapability;

    private CardMovementTiming? CaptureMovementTiming() => !(_hasGainPhaseQualificationCapability || TracksRedOwnedLoss ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.GiveAfterBatchGain)) && !HasOtherActualBasicDiscardCapability ? null :
        _programPhaseSchedule is { Phase: TurnPhase.Draw } scheduled
            ? new(_turnProgression.OwnerSeat, TurnPhase.Draw, scheduled.Frame.OwnerSeat)
            : new(_turnProgression.OwnerSeat, _phase, _currentSeat);

    private static bool IsGainPhaseQualified(SkillProgramTrigger trigger, int ownerSeat, CardMovementTiming? timing) =>
        trigger.GainPhaseQualification is null || timing is not null &&
        (timing.Phase != TurnPhase.Draw || timing.PhaseActorSeat != ownerSeat);

    private readonly Stack<long> _suppressedCardMovedEventBatchIds = new();

    private sealed class CardMovementBatchBuilder(
        long id,
        long? parentFrameId,
        long? parentBatchId,
        long? awaitingProgramFrameId,
        int turnNumber,
        IReadOnlyDictionary<CardLocation, int> sourceCountsBefore,
        IReadOnlyDictionary<CardLocation, int> destinationCountsBefore,
        ProgramSkillFrame? originProgram,
        Action? finalize = null) : IDisposable
    {
        private bool _completed;

        public void Dispose()
        {
            if (_completed) return;
            _completed = true;
            finalize?.Invoke();
        }

        public long Id { get; } = id;
        public long? ParentFrameId { get; } = parentFrameId;
        public long? ParentBatchId { get; } = parentBatchId;
        public long? AwaitingProgramFrameId { get; } = awaitingProgramFrameId;
        public int TurnNumber { get; } = turnNumber;
        public IReadOnlyDictionary<CardLocation, int> SourceCountsBefore { get; } = sourceCountsBefore;
        public IReadOnlyDictionary<CardLocation, int> DestinationCountsBefore { get; } = destinationCountsBefore;
        public ProgramSkillFrame? OriginProgram { get; } = originProgram;
        public CardMovementTiming? MovementTiming { get; init; }
        public ActualDiscardRecoveryPhaseKey? DiscardRecoveryPhase { get; init; }
    }

    private CardMovementBatchBuilder BeginCardMovementBatch(IEnumerable<CardLocation> sourceLocations, IEnumerable<CardLocation> destinationLocations)
    {
        var sources = sourceLocations.Distinct().ToDictionary(
            location => location,
            location => _cardZones.Count(location));
        var batch = new CardMovementBatchBuilder(
            ++_resolutionSequence,
            _resolutionStack.LastOrDefault()?.Id,
            _activeCardMovementBatchIds.TryPeek(out var parentBatchId) ? parentBatchId : null,
            _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault() is
                { } awaited && IsAwaitingProgramMovement(awaited)
                    ? awaited.Id : null,
            _turnNumber,
            sources,
            destinationLocations.Distinct().ToDictionary(location => location, location => _cardZones.Count(location)),
            _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault()) { MovementTiming = CaptureMovementTiming(), DiscardRecoveryPhase = CurrentActualDiscardRecoveryPhase() };
        _activeCardMovementBatchIds.Push(batch.Id);
        return batch;
    }
    // Scope for callers that perform their own per-move fact emission: the
    // movements still join one atomic batch and its completion facts, but the
    // per-move CardMoved events stay suppressed until the scope disposes.
    private CardMovementBatchBuilder BeginCardMovementBatch(bool emitCardMovedEvents = true)
    {
        var batch = new CardMovementBatchBuilder(
            ++_resolutionSequence,
            _resolutionStack.LastOrDefault()?.Id,
            _activeCardMovementBatchIds.TryPeek(out var parentBatchId) ? parentBatchId : null,
            _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault() is
                { } awaited && IsAwaitingProgramMovement(awaited)
                    ? awaited.Id : null,
            _turnNumber,
            new Dictionary<CardLocation, int>(),
            new Dictionary<CardLocation, int>(),
            _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault()) { MovementTiming = CaptureMovementTiming(), DiscardRecoveryPhase = CurrentActualDiscardRecoveryPhase() };
        _activeCardMovementBatchIds.Push(batch.Id);
        if (!emitCardMovedEvents) _suppressedCardMovedEventBatchIds.Push(batch.Id);
        return batch;
    }

    private void CompleteScopedCardMovementBatch(CardMovementBatchBuilder batch)
    {
        if (_suppressedCardMovedEventBatchIds.TryPop(out var suppressed) || true)
        {
            if (_activeCardMovementBatchIds.TryPeek(out var active) && active == batch.Id)
                _activeCardMovementBatchIds.Pop();
        }
        CompleteCardMovementBatch(batch, Array.Empty<CardMovementRecord>(), committed: true);
    }

    private void CompleteCardMovementBatch(
        CardMovementBatchBuilder batch,
        IReadOnlyList<CardMovementRecord> movements,
        bool committed)
    {
        if (!_activeCardMovementBatchIds.TryPop(out var activeBatchId) || activeBatchId != batch.Id)
            throw new InvalidOperationException("The atomic card-movement batch nesting changed unexpectedly.");
        if (!committed || movements.Count == 0 || !_setupComplete ||
            _winner != Winner.None || _status == EngineStatus.Completed)
            return;
        var sourceCounts = batch.SourceCountsBefore
            .OrderBy(item => item.Key.Zone)
            .ThenBy(item => item.Key.OwnerSeat)
            .Select(item => new CardMovementSourceCount(
                item.Key,
                item.Value,
                _cardZones.Count(item.Key)))
            .ToArray();
        CaptureActualDiscardRecoveryBatch(batch.Id, batch.DiscardRecoveryPhase, movements);
        CaptureNeighborDiscardOpportunity(batch.Id, batch.TurnNumber, movements);
        CaptureTurnDiscardSuitFact(batch.TurnNumber,movements);
        CaptureTurnRedDiscardCount(batch.TurnNumber,movements);
        CaptureActionDiscardFact(batch.Id,batch.TurnNumber,movements);
        CaptureFirstGameDomainCrossings(batch.Id,batch.TurnNumber,movements);
        _pendingCardsMovedBatches.Add(new CardMovementBatchContext(
            batch.Id,
            batch.ParentFrameId,
            batch.ParentBatchId,
            batch.TurnNumber,
            Array.AsReadOnly(movements.ToArray()),
            Array.AsReadOnly(sourceCounts),
            batch.AwaitingProgramFrameId,
            batch.DestinationCountsBefore.OrderBy(item => item.Key.Zone).ThenBy(item => item.Key.OwnerSeat)
                .Select(item => new CardMovementSourceCount(item.Key, item.Value, _cardZones.Count(item.Key))).ToArray(),
            batch.OriginProgram?.SkillId, batch.OriginProgram?.SkillInstanceId, batch.OriginProgram?.OwnerSeat)
            { MovementTiming = batch.MovementTiming, DiscardRecoveryPhase = batch.DiscardRecoveryPhase });
    }

    private bool HasCardsMovedProgramBoundaryFrame()
    {
        if (_resolutionStack.FirstOrDefault() is not CardsMovedTriggerWindowFrame frame)
            return false;
        if (frame.CandidateIndex < 0 || frame.CandidateIndex >= frame.Candidates.Count ||
            frame.Batch.Id != frame.Id || frame.Batch.Movements.Count == 0)
            throw new InvalidOperationException("The cards-moved boundary lost its batch or candidate cursor.");
        if (_resolutionStack.Count == 1 && frame.Step == ResolutionFrameStep.AwaitingResponse &&
            _pendingDecision?.Kind != DecisionKind.ProgramTrigger)
            throw new InvalidOperationException("The cards-moved boundary is waiting without its program prompt.");
        return true;
    }

    private bool TryBeginCardsMovedProgramWindow(long? instructionFrameId = null, int? subjectSeat = null)
    {
        var factionRequestCost = _resolutionStack.LastOrDefault() is { PaidFactionRequestCostRecovery: not null } paidRequest &&
            paidRequest.Id == instructionFrameId ? paidRequest : null;
        var awaitingFrame = factionRequestCost is null && _resolutionStack.LastOrDefault() is ProgramSkillFrame program &&
            (IsAwaitingProgramMovement(program) || program.Id == instructionFrameId) ? program : null;
        var declaration = _resolutionStack.LastOrDefault() is CardDeclarationFrame declared &&
            declared.Id == instructionFrameId && declared.Stage is CardDeclarationStage.Paying or CardDeclarationStage.Cleaning ? declared : null;
        var recoveryReplacement = _resolutionStack.LastOrDefault() is RecoveryReplacementFrame recovery && recovery.Id == instructionFrameId ? recovery : null;
        var equipmentRecast = _resolutionStack.LastOrDefault() is EquipmentRecastFrame recast && recast.Id == instructionFrameId ? recast : null;
        var drawPhase = _resolutionStack.LastOrDefault() is DrawPhaseObligationFrame draw && draw.Id == instructionFrameId ? draw : null;
        var colorFireAttack = _resolutionStack.LastOrDefault() is CardUseFrame { ColorFireAttack.PaidCardId: not null } fire && fire.Id == instructionFrameId ? fire : null;
        var counterspellPayment = _resolutionStack.LastOrDefault() is NullificationWindowFrame { CounterspellPayment: not null } counter && counter.Id == instructionFrameId ? counter : null;
        var historicalEnding = _resolutionStack.LastOrDefault() is CardUseFrame { EndingHistoricalUseReturn: not null, EndingHistoricalCostDrained: false } history && history.Id == instructionFrameId ? history : null;
        var roundPileAlcohol = _resolutionStack.LastOrDefault() is CardUseFrame { RoundPileAlcoholReturn: not null, RoundPileAlcoholCostDrained: false } wine && wine.Id == instructionFrameId ? wine : null;
        bool Eligible(CardMovementBatchContext batch) => historicalEnding is not null ? batch.ParentFrameId == historicalEnding.Id && batch.AwaitingProgramFrameId is null : roundPileAlcohol is not null ? batch.ParentFrameId == roundPileAlcohol.Id && batch.AwaitingProgramFrameId is null : counterspellPayment is not null ? batch.ParentFrameId == counterspellPayment.Id && batch.AwaitingProgramFrameId is null : colorFireAttack is not null ? batch.ParentFrameId == colorFireAttack.Id && batch.AwaitingProgramFrameId is null : drawPhase is not null ? batch.ParentFrameId == drawPhase.Id || drawPhase.InheritedMovementBatchIds?.Contains(batch.Id) == true : factionRequestCost is not null ? batch.ParentFrameId == factionRequestCost.Id : equipmentRecast is not null ? batch.ParentFrameId == equipmentRecast.Id : recoveryReplacement is not null ? batch.ParentFrameId == recoveryReplacement.Id : declaration is not null ? batch.ParentFrameId == declaration.Id : awaitingFrame is null
            ? batch.AwaitingProgramFrameId is null
            : batch.AwaitingProgramFrameId == awaitingFrame.Id ||
              batch.AwaitingProgramFrameId is null && batch.ParentFrameId == awaitingFrame.Id;
        if (_pendingDecision is not null ||
            (_resolutionStack.Count != 0 && awaitingFrame is null && declaration is null && recoveryReplacement is null && equipmentRecast is null && factionRequestCost is null && drawPhase is null && colorFireAttack is null && counterspellPayment is null && roundPileAlcohol is null && historicalEnding is null) ||
            _winner != Winner.None || _status == EngineStatus.Completed)
            return false;

        // Equipment removal may have completed a nested recovery before this movement finished.
        if (awaitingFrame is not null &&
            IsAwaitingProgramMovement(awaitingFrame) &&
            TryBeginHpChangedProgramWindow(awaitingFrame.Id, PostEventContinuation.AwaitedProgramMovement)) return true;

        while (_pendingCardsMovedBatches.Any(Eligible))
        {
            var batch = _pendingCardsMovedBatches.Where(Eligible).OrderBy(item => item.Id).First();
            _pendingCardsMovedBatches.Remove(batch);
            var window = CreateCardsMovedProgramWindow(batch,
                awaitingFrame is not null && !IsAwaitingProgramMovement(awaitingFrame) ? awaitingFrame.Id : null);
            if (window is null) continue;
            if (recoveryReplacement is not null) window = window with { ResumeRecoveryReplacementFrameId = recoveryReplacement.Id };
            if (equipmentRecast is not null) window = window with { ResumeEquipmentRecastFrameId = equipmentRecast.Id };
            if (drawPhase is not null) window = window with { ResumeDrawPhaseObligationFrameId = drawPhase.Id };
            if (colorFireAttack is not null) window = window with { ResumeColorFireAttackFrameId = colorFireAttack.Id };
            if (counterspellPayment is not null) window = window with { ResumeCounterspellPaymentFrameId = counterspellPayment.Id };
            if (historicalEnding is not null) window = window with { ResumeHistoricalEndingUseFrameId = historicalEnding.Id };
            if (roundPileAlcohol is not null) window = window with { ResumeRoundPileAlcoholUseFrameId = roundPileAlcohol.Id };
            if (factionRequestCost is not null) window = window with { ResumeFactionRequestCostFrameId = factionRequestCost.Id, ResumeProgramFrameId = null };
            if (declaration is not null)
            {
                window = window with { ResumeDeclarationFrameId = declaration.Id };
                ReplaceRuntimeTop(declaration with { ActiveChildFrameId = window.Id });
            }
            if (awaitingFrame?.SelectedCardPayment is { } payment &&
                awaitingFrame.SelectedCardPaymentResult is null)
            {
                if (payment.ActiveChildFrameId is not null)
                    throw new InvalidOperationException("A selected-card payment already has an active child frame.");
                ReplaceRuntimeTop(awaitingFrame with
                {
                    SelectedCardPayment = payment with { ActiveChildFrameId = window.Id }
                });
            }
            PushRuntimeFrame(window);
            AdvanceRuntimeTop<CardsMovedTriggerWindowFrame>();
            return true;
        }
        return false;
    }

    private IReadOnlyList<ProgramTriggerCandidate> CollectCardsMovedProgramCandidates(
        CardMovementBatchContext batch)
    {
        var candidates = new List<ProgramTriggerCandidate>();
        foreach (var window in new[] { SkillProgramTriggerWindow.CardsMoved, SkillProgramTriggerWindow.CardsGained })
        foreach (var (count, discardOriginOnly) in ProgramMovementSourceCounts(batch, window))
        {
            var ownerSeat = count.Location.OwnerSeat!.Value;
            if (!IsValidPlayerSeat(ownerSeat)) continue;
            foreach (var candidate in CollectProgramTriggerCandidates(_players[ownerSeat], window))
            {
                var trigger = GetProgramTrigger(candidate);
                if (IsNeighborDiscardTopTrigger(trigger) || IsThirdPartyHandGainTrigger(trigger) ||
                    IsOwnerSourceHandGainTrigger(trigger)) continue;
                if (!IsGainPhaseQualified(trigger, ownerSeat, batch.MovementTiming)) continue;
                if (discardOriginOnly && !trigger.MovementDiscardOnly) continue;
                if (!(window == SkillProgramTriggerWindow.CardsMoved ? trigger.SourceZones : trigger.DestinationZones).Contains(count.Location.Zone)) continue;
                if (trigger.MovementOccurrence == SkillProgramMovementOccurrence.PerOwnerBatch)
                {
                    if (window != SkillProgramTriggerWindow.CardsMoved || candidates.Any(item =>
                        item.OwnerSeat == candidate.OwnerSeat && item.SkillId == candidate.SkillId &&
                        item.BindingId == candidate.BindingId && item.SkillInstanceId == candidate.SkillInstanceId)) continue;
                    var matching = MatchingOwnerBatchMovementIndexes(batch, candidate, trigger);
                    var ownerBatchFacts = CaptureCardsMovedTriggerFacts(_players[ownerSeat], matching.Length,
                        new CardMovementSourceCount(count.Location,
                            ProgramMovementSourceCounts(batch, window).Where(item => (!item.DiscardOriginOnly || trigger.MovementDiscardOnly) && item.Count.Location.OwnerSeat == ownerSeat && trigger.SourceZones.Contains(item.Count.Location.Zone)).Sum(item => item.Count.CountBefore),
                            ProgramMovementSourceCounts(batch, window).Where(item => (!item.DiscardOriginOnly || trigger.MovementDiscardOnly) && item.Count.Location.OwnerSeat == ownerSeat && trigger.SourceZones.Contains(item.Count.Location.Zone)).Sum(item => item.Count.CountAfter)), window);
                    ownerBatchFacts = CaptureOwnerBatchEquipmentLoss(ownerBatchFacts, batch, candidate, trigger, matching);
                    if (matching.Length > 0 && trigger.Condition.Evaluate(ownerBatchFacts, candidate.SkillId, candidate.SkillInstanceId))
                        candidates.Add(candidate);
                    continue;
                }
                if (trigger.MovementOccurrence == SkillProgramMovementOccurrence.PerSourceOwner)
                {
                    candidates.AddRange(CollectSourceOwnerGainCandidates(batch, candidate, trigger, count));
                    continue;
                }
                var indexes = MatchingMovementIndexes(batch, candidate, trigger, count.Location);
                if (indexes.Length == 0) continue;
                var facts = CaptureCardsMovedTriggerFacts(_players[ownerSeat], indexes.Length, count, window, batch.MovementTiming);
                if (!trigger.Condition.Evaluate(facts, candidate.SkillId, candidate.SkillInstanceId)) continue;
                // A judgment still in flight (for example while another skill replaces its
                // card) owns its judgment zone churn; starting a second judgment from that
                // movement would collide with the pending one and self-feedback the same
                // judgment, so such triggers wait for a settled zone instead.
                if (ActiveJudgment is not null &&
                    trigger.Effects.Any(effect => effect.Op == SkillProgramEffectOp.StartJudgment)) continue;
                var occurrences = trigger.MovementOccurrence == SkillProgramMovementOccurrence.PerBatch ? [0] : indexes;
                candidates.AddRange(occurrences.Select(index => candidate with { OccurrenceIndex = index }));
            }
        }
        candidates.AddRange(CollectNeighborDiscardCandidates(batch));
        candidates.AddRange(CollectThirdPartyHandGainCandidates(batch));
        candidates.AddRange(CollectOwnerSourceHandGainCandidates(batch));
        return candidates
            .OrderBy(candidate => (candidate.OwnerSeat - _currentSeat + _players.Count) % _players.Count)
            .ThenByDescending(candidate => candidate.Priority)
            .ThenBy(candidate => candidate.SkillId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.BindingId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.SkillInstanceId, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.OccurrenceIndex)
            .ToArray();
    }

    private IReadOnlyList<ProgramTriggerCandidate> CollectDiscardPileReceivedCandidates(
        CardMovementBatchContext batch)
    {
        var additions = new List<ProgramTriggerCandidate>();
        for (var seat = 0; seat < _players.Count; seat++)
        {
            if (!_players[seat].IsAlive) continue;
            foreach (var candidate in CollectProgramTriggerCandidates(
                         _players[seat], SkillProgramTriggerWindow.DiscardPileReceived))
            {
                var trigger = _contentRegistry!.GetSkill(candidate.SkillId).Program!.Triggers
                    .Single(item => item.Id == candidate.BindingId);
                var indexes = MatchingDiscardPileIndexes(batch, candidate, trigger);
                if (indexes.Length == 0) continue;
                var occurrences = trigger.MovementOccurrence == SkillProgramMovementOccurrence.PerBatch
                    ? new[] { IsOtherActualBasicDiscardTrigger(trigger) ? indexes[0] : 0 }
                    : indexes;
                additions.AddRange(occurrences.Select(index => candidate with { OccurrenceIndex = index }));
            }
        }
        return additions;
    }

    private int[] MatchingDiscardPileIndexes(CardMovementBatchContext batch,
        ProgramTriggerCandidate candidate, SkillProgramTrigger trigger)
    {
        if (IsOtherActualBasicDiscardTrigger(trigger)) return MatchingOtherActualBasicDiscardIndexes(batch, candidate);
        if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.RestoreActualDiscardBatch))
            return MatchingActualDiscardRecoveryIndexes(batch, candidate);
        if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ClaimDiscardedEntityWithProvenance))
            return MatchingProvenanceDiscardIndexes(batch, candidate, trigger);
        if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.StoreAdjacentDiscardedSlash))
            return MatchingAdjacentDiscardIndexes(batch, candidate);
        if(trigger.Effects.Any(e=>e.Op==SkillProgramEffectOp.RewardDiscardedActionColor))
        {
            var action=CompleteProgramEventHistory().OfType<ActionCardsDiscardedEvent>().LastOrDefault(e=>e.BatchId==batch.Id);
            return action?.ActorSeat==candidate.OwnerSeat ? batch.Movements.Select((m,i)=>(m,i)).Where(x=>x.m.To==CardLocation.DiscardPile).Select(x=>x.i).ToArray():[];
        }
        if (trigger.IgnoreOwnSkillMovements && batch.OriginOwnerSeat == candidate.OwnerSeat &&
            batch.OriginSkillId == candidate.SkillId &&
            batch.OriginSkillInstanceId == candidate.SkillInstanceId) return [];
        var discardPile = _cardZones.CardsAt(CardLocation.DiscardPile).ToDictionary(card => card.Id);
        return batch.Movements.Select((movement, index) => (movement, index))
            .Where(item => item.movement.To == CardLocation.DiscardPile &&
                (trigger.MovementDiscardOnly ? GetProgramDiscardSource(item.movement) : item.movement.From) is { } discardSource &&
                discardSource.OwnerSeat is { } source &&
                (trigger.DiscardOwnerScope == SkillProgramDiscardOwnerScope.Own ? source == candidate.OwnerSeat : source != candidate.OwnerSeat) &&
                (trigger.SourceZones.Count == 0 || trigger.SourceZones.Contains(discardSource.Zone)) &&
                item.movement.From != item.movement.To &&
                (trigger.MovementReasons.Count == 0 ||
                    trigger.MovementReasons.Contains(item.movement.Reason.Value)) &&
                !trigger.ExcludedMovementReasons.Contains(item.movement.Reason.Value) &&
                (trigger.Suits.Count == 0 ||
                    discardPile.TryGetValue(item.movement.CardId, out var card) && trigger.Suits.Contains(card.Suit)) &&
                (trigger.CardCategories.Count == 0 || discardPile.TryGetValue(item.movement.CardId, out var filteredCard) &&
                    trigger.CardCategories.Any(category => MatchesSkillProgramCardCategory(filteredCard.Kind, category))) &&
                (trigger.CardKinds.Count == 0 || discardPile.TryGetValue(item.movement.CardId, out var kindCard) &&
                    trigger.CardKinds.Contains(kindCard.Kind)))
            .Select(item => item.index).ToArray();
    }

    private void ClaimProgramMovedCards(ProgramSkillFrame frame)
    {
        var active = GetActiveProgramFrame(frame.Id);
        if (active.WindowContext is not
            {
                Window: SkillProgramTriggerWindow.DiscardPileReceived,
                MovementBatch: { } batch,
                MovementIndex: { } index
            } || index < 0 || index >= batch.Movements.Count)
            throw new InvalidOperationException("The moved-card claim lost its discard window.");
        var movement = batch.Movements[index];
        if (movement.To != CardLocation.DiscardPile ||
            movement.From.OwnerSeat is not { } source || source == active.OwnerSeat)
            throw new InvalidOperationException(
                "A moved-card claim must answer another player's discard into the discard pile.");
        var owner = _players[active.OwnerSeat];
        if (!owner.IsAlive || _cardZones.GetLocation(movement.CardId) != CardLocation.DiscardPile)
            return;
        var card = _cardZones.CardsAt(CardLocation.DiscardPile).Single(item => item.Id == movement.CardId);
        MoveCard(card, CardLocation.DiscardPile, CardLocation.Hand(active.OwnerSeat),
            new CardMoveReason($"skill-program.{active.SkillId}.{SkillProgramEffectOp.ClaimMovedCards}"));
        AdvanceEventRulesAndQueueFact(new ProgramMovedCardsClaimedEvent(
            active.Id, active.SkillId, active.TriggerId!, active.OwnerSeat, source, card.Id));
        AddLog("SkillTriggered",
            $"{owner.Name} 发动【{_contentRegistry!.GetSkill(active.SkillId).Name}】，获得 {card.DisplayName}。",
            active.OwnerSeat, source);
    }

    private int[] MatchingMovementIndexes(CardMovementBatchContext batch, ProgramTriggerCandidate candidate,
        SkillProgramTrigger trigger, CardLocation location)
    {
        if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.GiveAfterBatchGain))
            return MatchingActualGainGiftIndexes(batch, candidate.OwnerSeat, location);
        if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.RevealRedLossAndDraw))
            return MatchingRedOwnerLossIndexes(batch, candidate.OwnerSeat, location);
        if(trigger.Effects.Any(e=>e.Op==SkillProgramEffectOp.RewardDiscardedActionColor))
        {
            var action=CompleteProgramEventHistory().OfType<ActionCardsDiscardedEvent>().LastOrDefault(e=>e.BatchId==batch.Id);
            return action?.ActorSeat==candidate.OwnerSeat ? batch.Movements.Select((m,i)=>(m,i)).Where(x=>x.m.To==CardLocation.DiscardPile).Select(x=>x.i).ToArray():[];
        }
        if (trigger.IgnoreOwnSkillMovements && batch.OriginOwnerSeat == candidate.OwnerSeat &&
            batch.OriginSkillId == candidate.SkillId &&
            batch.OriginSkillInstanceId == candidate.SkillInstanceId) return [];
        return batch.Movements.Select((movement, index) => (movement, index))
            .Where(item => (trigger.Window == SkillProgramTriggerWindow.CardsGained ? item.movement.To : trigger.MovementDiscardOnly ? GetProgramDiscardSource(item.movement) : item.movement.From) == location &&
                item.movement.From != item.movement.To &&
                (trigger.MovementReasons.Count == 0 || trigger.MovementReasons.Contains(item.movement.Reason.Value)) &&
                !trigger.ExcludedMovementReasons.Contains(item.movement.Reason.Value))
            .Where(item => trigger.MovementOccurrence != SkillProgramMovementOccurrence.PerSourceOwner ||
                item.movement.From.OwnerSeat == batch.Movements[candidate.OccurrenceIndex].From.OwnerSeat)
            .Select(item => item.index).ToArray();
    }

    private int[] MatchingOwnerBatchMovementIndexes(CardMovementBatchContext batch,
        ProgramTriggerCandidate candidate, SkillProgramTrigger trigger) =>
        ProgramMovementSourceCounts(batch, SkillProgramTriggerWindow.CardsMoved)
            .Where(item => (!item.DiscardOriginOnly || trigger.MovementDiscardOnly) && item.Count.Location.OwnerSeat == candidate.OwnerSeat &&
                trigger.SourceZones.Contains(item.Count.Location.Zone))
            .SelectMany(item => MatchingMovementIndexes(batch, candidate, trigger, item.Count.Location))
            .Distinct().Order().ToArray();

    private SkillProgramTriggerFacts CaptureCardsMovedTriggerFacts(
        CharacterState owner,
        int movedCardCount,
        CardMovementSourceCount sourceCount,
        SkillProgramTriggerWindow window, CardMovementTiming? timing = null)
    {
        var facts = CaptureProgramTriggerFacts(owner);
        return facts with
        {
            MovementTiming = timing,
            MovedCardCount = movedCardCount,
            SourceZoneCountBefore = window == SkillProgramTriggerWindow.CardsMoved ? sourceCount.CountBefore : 0,
            SourceZoneCountAfter = window == SkillProgramTriggerWindow.CardsMoved ? sourceCount.CountAfter : 0,
            DestinationZoneCountBefore = window == SkillProgramTriggerWindow.CardsGained ? sourceCount.CountBefore : 0,
            DestinationZoneCountAfter = window == SkillProgramTriggerWindow.CardsGained ? sourceCount.CountAfter : 0
        };
    }

    private ProgramSkillWindowContext CreateCardsMovedProgramContext(
        CardsMovedTriggerWindowFrame frame,
        ProgramTriggerCandidate candidate)
    {
        if (frame.Contexts is { } contexts) return contexts[frame.CandidateIndex];
        var trigger = GetProgramTrigger(candidate);
        if (trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.RestoreActualDiscardBatch)) return CreateActualDiscardRecoveryContext(frame, candidate);
        if (trigger.Window == SkillProgramTriggerWindow.FirstGameDomainCrossing) return CreateFirstDomainContext(frame,candidate);
        if (IsNeighborDiscardTopTrigger(trigger)) return CreateNeighborDiscardContext(frame, candidate);
        if (trigger.Window == SkillProgramTriggerWindow.DiscardPileReceived)
        {
            var movement = frame.Batch.Movements[candidate.OccurrenceIndex];
            return new ProgramSkillWindowContext(
                trigger.Window,
                frame.Id,
                candidate.OwnerSeat,
                SourceSeat: trigger.Effects.Any(e => e.Op == SkillProgramEffectOp.ClaimDiscardedEntityWithProvenance) ? DiscardedProvenanceOrigin(movement)?.SourceSeat : (trigger.MovementDiscardOnly ? GetProgramDiscardSource(movement) : movement.From)?.OwnerSeat,
                OccurrenceIndex: candidate.OccurrenceIndex,
                Facts: CaptureCardsMovedTriggerFacts(_players[candidate.OwnerSeat], 1,
                    new CardMovementSourceCount(CardLocation.DiscardPile, 0, 0), trigger.Window),
                MovementBatch: frame.Batch,
                MovementIndex: candidate.OccurrenceIndex);
        }
        if (trigger.MovementOccurrence == SkillProgramMovementOccurrence.PerOwnerBatch)
        {
            var counts = ProgramMovementSourceCounts(frame.Batch, trigger.Window)
                .Where(item => (!item.DiscardOriginOnly || trigger.MovementDiscardOnly) && item.Count.Location.OwnerSeat == candidate.OwnerSeat &&
                    trigger.SourceZones.Contains(item.Count.Location.Zone)).Select(item => item.Count).ToArray();
            var matching = MatchingOwnerBatchMovementIndexes(frame.Batch, candidate, trigger);
            return new ProgramSkillWindowContext(trigger.Window, frame.Id, candidate.OwnerSeat,
                SourceSeat: candidate.OwnerSeat, TargetSeat: candidate.OwnerSeat,
                Facts: CaptureOwnerBatchEquipmentLoss(CaptureCardsMovedTriggerFacts(_players[candidate.OwnerSeat], matching.Length,
                    new CardMovementSourceCount(counts[0].Location, counts.Sum(item => item.CountBefore), counts.Sum(item => item.CountAfter)), trigger.Window),
                    frame.Batch, candidate, trigger, matching),
                MovementBatch: frame.Batch);
        }
        if (trigger.MovementOccurrence is SkillProgramMovementOccurrence.PerThirdPartyHandGain
            or SkillProgramMovementOccurrence.PerOwnerSourceHandGain)
        {
            var movement = frame.Batch.Movements[candidate.OccurrenceIndex];
            var destinationCount = ProgramMovementSourceCounts(frame.Batch, trigger.Window)
                .FirstOrDefault(item => item.Count.Location == movement.To).Count is { } matched ? matched
                : new CardMovementSourceCount(movement.To, 0, 1);
            return new ProgramSkillWindowContext(
                trigger.Window,
                frame.Id,
                candidate.OwnerSeat,
                SourceSeat: movement.From.OwnerSeat,
                TargetSeat: movement.To.OwnerSeat,
                OccurrenceIndex: candidate.OccurrenceIndex,
                Facts: CaptureCardsMovedTriggerFacts(_players[candidate.OwnerSeat], 1,
                    destinationCount, trigger.Window),
                MovementBatch: frame.Batch,
                MovementIndex: candidate.OccurrenceIndex);
        }
        var gained = trigger.Window == SkillProgramTriggerWindow.CardsGained;
        var location = new CardLocation((gained ? trigger.DestinationZones : trigger.SourceZones).Single(), candidate.OwnerSeat);
        var count = ProgramMovementSourceCounts(frame.Batch, trigger.Window)
            .Single(item => item.Count.Location == location && (!item.DiscardOriginOnly || trigger.MovementDiscardOnly)).Count;
        var matchingIndexes = MatchingMovementIndexes(frame.Batch, candidate, trigger, location);
        return new ProgramSkillWindowContext(
            trigger.Window,
            frame.Id,
            candidate.OwnerSeat,
            SourceSeat: trigger.MovementOccurrence == SkillProgramMovementOccurrence.PerSourceOwner
                ? frame.Batch.Movements[candidate.OccurrenceIndex].From.OwnerSeat
                : gained ? frame.Batch.OriginOwnerSeat : candidate.OwnerSeat,
            TargetSeat: candidate.OwnerSeat,
            OccurrenceIndex: candidate.OccurrenceIndex,
            Facts: CaptureCardsMovedTriggerFacts(
                _players[candidate.OwnerSeat], matchingIndexes.Length, count, trigger.Window, frame.Batch.MovementTiming),
            MovementBatch: frame.Batch,
            MovementIndex: trigger.MovementOccurrence == SkillProgramMovementOccurrence.PerCard
                ? candidate.OccurrenceIndex
                : null);
    }

    private void ContinueCardsMovedProgramWindowCore()
    {
        while (_resolutionStack.LastOrDefault() is CardsMovedTriggerWindowFrame frame)
        {
            if (frame.CandidateIndex >= frame.Candidates.Count)
            {
                PopResolutionFrame(frame.Id, ResolutionFrameKind.CardsMovedTriggerWindow);
                if (frame.DeferredTurnEndReturn is not null)
                {
                    ReturnDeferredTurnEndPrelude(frame);
                    return;
                }
                if (_resolutionStack.LastOrDefault() is ProgramSkillFrame
                    { SelectedCardPayment: { } payment, SelectedCardPaymentResult: null } parent)
                {
                    if (payment.ActiveChildFrameId != frame.Id)
                        throw new InvalidOperationException("A selected-card payment lost its movement child frame.");
                    ReplaceRuntimeTop(parent with
                    {
                        SelectedCardPayment = payment with
                        {
                            ActiveChildFrameId = null,
                            LastCompletedChildFrameId = frame.Id
                        }
                    });
                }
                if (frame.ResumeDeclarationFrameId is { } declarationId)
                {
                    if (_resolutionStack.LastOrDefault() is not CardDeclarationFrame declarationParent || declarationParent.Id != declarationId || declarationParent.ActiveChildFrameId != frame.Id)
                        throw new InvalidOperationException("A declaration payment lost its exact movement child.");
                    ReplaceRuntimeTop(declarationParent with { ActiveChildFrameId = null });
                    AdvanceRuntimeFrame(declarationId); return;
                }
                if (frame.ResumeRecoveryReplacementFrameId is { } recoveryId)
                { AdvanceRuntimeFrame(recoveryId); return; }
                if (frame.ResumeDrawPhaseObligationFrameId is { } drawId)
                {
                    if (_resolutionStack.LastOrDefault() is not DrawPhaseObligationFrame drawParent || drawParent.Id != drawId ||
                        frame.Batch.ParentFrameId != drawId && drawParent.InheritedMovementBatchIds?.Contains(frame.Batch.Id) != true)
                        throw new InvalidOperationException("An actual draw lost its exact gain movement return.");
                    AdvanceRuntimeFrame(drawId); return;
                }
                if (frame.ResumeHistoricalEndingUseFrameId is { } historyId)
                {
                    if (_resolutionStack.LastOrDefault() is not CardUseFrame { EndingHistoricalUseReturn: not null, EndingHistoricalCostDrained: false } historyParent || historyParent.Id != historyId || frame.Batch.ParentFrameId != historyId || frame.Batch.AwaitingProgramFrameId is not null)
                        throw new InvalidOperationException("Historical Ending lost its exact one-Hand movement return.");
                    ContinueHistoricalEndingPayment(historyId); return;
                }
                if (frame.ResumeRoundPileAlcoholUseFrameId is { } wineId)
                {
                    if (_resolutionStack.LastOrDefault() is not CardUseFrame { RoundPileAlcoholReturn: not null, RoundPileAlcoholCostDrained: false } wineParent ||
                        wineParent.Id != wineId || frame.Batch.ParentFrameId != wineId || frame.Batch.AwaitingProgramFrameId is not null)
                        throw new InvalidOperationException("Pile Alcohol lost its exact cost movement return.");
                    ContinueRoundPileAlcoholUse(wineId); return;
                }
                if (frame.ResumeCounterspellPaymentFrameId is { } counterId)
                {
                    if (_resolutionStack.LastOrDefault() is not NullificationWindowFrame { CounterspellPayment: not null } counterParent ||
                        counterParent.Id != counterId || frame.Batch.ParentFrameId != counterId || frame.Batch.AwaitingProgramFrameId is not null)
                        throw new InvalidOperationException("Counterspell lost its exact payment movement return.");
                    ContinuePolicyCounterspellPayment(counterId); return;
                }
                if (frame.ResumeColorFireAttackFrameId is { } fireId)
                {
                    if (_resolutionStack.LastOrDefault() is not CardUseFrame { ColorFireAttack.PaidCardId: not null } fireParent ||
                        fireParent.Id != fireId || frame.Batch.ParentFrameId != fireId || frame.Batch.AwaitingProgramFrameId is not null)
                        throw new InvalidOperationException("Color Fire Attack lost its exact movement return.");
                    ContinueColorFireAttackPayment(fireId); return;
                }
                if (frame.ResumeEquipmentRecastFrameId is { } recastId)
                {
                    if (_resolutionStack.LastOrDefault() is not EquipmentRecastFrame recastParent ||
                        recastParent.Id != recastId || frame.Batch.ParentFrameId != recastId)
                        throw new InvalidOperationException("Equipment recast lost its exact movement return.");
                    AdvanceRuntimeFrame(recastId); return;
                }
                if (frame.ResumeProgramFrameId is { } resume)
                {
                    AdvanceRuntimeProgram(resume);
                    return;
                }
                if (frame.ResumeFactionRequestCostFrameId is { } requestCostId)
                {
                    if (_resolutionStack.LastOrDefault() is not { PaidFactionRequestCostRecovery: not null } requestProducer ||
                        requestProducer.Id != requestCostId || frame.Batch.ParentFrameId != requestCostId)
                        throw new InvalidOperationException("Faction request cost lost its exact movement return.");
                    ContinuePaidFactionRequestCost(requestCostId); return;
                }
                if (!TryBeginCardsMovedProgramWindow() &&
                    _resolutionStack.LastOrDefault() is ProgramSkillFrame
                        { } awaited && IsAwaitingProgramMovement(awaited))
                    ReturnRuntimeProgramMovement(awaited.Id);
                return;
            }
            var candidate = frame.Candidates[frame.CandidateIndex];
            var context = CreateCardsMovedProgramContext(frame, candidate);
            if (!CanRunProgramTrigger(candidate, context))
            {
                AdvanceCardsMovedProgramCandidate(frame, candidate, activated: false, completed: false);
                continue;
            }
            var trigger = _contentRegistry!.GetSkill(candidate.SkillId).Program!.Triggers
                .Single(item => item.Id == candidate.BindingId);
            if (trigger.Optional)
            {
                ReplaceRuntimeTop(frame with { Step = ResolutionFrameStep.AwaitingResponse });
                ExposeProgramTriggerDecision(candidate, context);
                return;
            }
            BeginProgramBinding(candidate, context);
            return;
        }
    }

    private void AdvanceCardsMovedProgramCandidate(
        CardsMovedTriggerWindowFrame frame,
        ProgramTriggerCandidate candidate,
        bool activated,
        bool completed)
    {
        AdvanceEventRulesAndQueueFact(new ProgramBindingResolvedEvent(
            frame.Id, candidate.SkillId, candidate.BindingId, candidate.SkillInstanceId,
            candidate.OwnerSeat, CreateCardsMovedProgramContext(frame, candidate).Window, activated, completed));
        AdvanceCardsMovedProgramCursor(frame);
    }

    private void AdvanceCardsMovedProgramCursor(CardsMovedTriggerWindowFrame frame)
    {
        if (_resolutionStack.LastOrDefault() is not CardsMovedTriggerWindowFrame current ||
            current.Id != frame.Id || current.CandidateIndex != frame.CandidateIndex)
            throw new InvalidOperationException("The cards-moved trigger cursor is no longer current.");
        ReplaceRuntimeTop(current with
        {
            CandidateIndex = current.CandidateIndex + 1,
            Step = ResolutionFrameStep.ResolvingEffect
        });
    }
}
