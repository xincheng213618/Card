using System.Globalization;
namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ActualHandGainDrawReason = "program.actual-hand-gain.draw";
    private const string ForeignHandCleanupReason = "program.foreign-turn-hand-gain.cleanup";
    private const string SameCategoryDeckGiftReason = "program.same-category-deck.gift";
    private bool HasActualHandGainCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DrawAfterActualOwnHandGain) ||
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DiscardForeignTurnHandGains);
    private bool ActualHandGainTurnOpen(int owner) => _turnNumber > 0 && _turnProgression.OwnerSeat == owner &&
        _phase is not (TurnPhase.NotStarted or TurnPhase.Finished) && !CompleteProgramEventHistory().OfType<TurnEndedEvent>()
            .Any(e => e.TurnNumber == _turnNumber && e.ActorSeat == owner);
    private CardLocation ActualHandGainOriginalSource(CardMovementRecord first, CardMovementBatchContext? batch = null)
    {
        var from = first.From; var sequence = first.Sequence;
        while (from == CardLocation.Processing)
        {
            var prior = batch?.Movements.Where(m => m.CardId == first.CardId && m.Sequence < sequence).MaxBy(m => m.Sequence);
            var ledger = _cardMovements.LastOrDefault(m => m.CardId == first.CardId && m.Sequence < sequence);
            if (ledger is not null && (prior is null || ledger.Sequence > prior.Sequence)) prior = ledger;
            if (prior?.To != CardLocation.Processing) break;
            from = prior.From; sequence = prior.Sequence;
        }
        return from;
    }
    private int[] MatchingActualHandGainIndexes(CardMovementBatchContext batch, int owner, CardLocation location) =>
        batch.Movements.Select((m, i) => (m, i)).GroupBy(x => x.m.CardId)
            .Where(g => g.Any(x => x.m.To == CardLocation.Hand(owner) && ActualHandGainOriginalSource(x.m, batch) != CardLocation.Hand(owner)) &&
                g.OrderBy(x => x.m.Sequence).Last().m.To == CardLocation.Hand(owner))
            .Select(g => g.OrderBy(x => x.m.Sequence).Last()).Where(x => x.m.To == location).Select(x => x.i).Order().ToArray();
    private ActualHandGainEntity[] ActualHandGainEntities(CardMovementBatchContext batch, int owner) =>
        MatchingActualHandGainIndexes(batch, owner, CardLocation.Hand(owner)).Select(i =>
        {
            var acquisition = batch.Movements.Where(m => m.CardId == batch.Movements[i].CardId && m.To == CardLocation.Hand(owner) &&
                ActualHandGainOriginalSource(m, batch) != CardLocation.Hand(owner)).OrderByDescending(m => m.Sequence).First();
            return new ActualHandGainEntity(acquisition.CardId, acquisition.Sequence);
        }).ToArray();
    // This records original issuance, before queued loss/gain observers move the entities again.
    private void CaptureForeignTurnActualHandGains(CardMovementBatchContext batch)
    {
        if (!HasActualHandGainCapability || batch.MovementTiming is not { } timing ||
            !ActualHandGainTurnOpen(timing.ActualTurnOwnerSeat)) return;
        foreach (var owner in _players.Where(p => p.IsAlive))
        {
            var gains = ActualHandGainEntities(batch, owner.Seat);
            if (gains.Length == 0) continue;
            if (owner.Seat == timing.ActualTurnOwnerSeat)
            {
                foreach (var c in CollectProgramTriggerCandidates(owner, SkillProgramTriggerWindow.CardsGained)
                    .Where(c => GetProgramTrigger(c).Effects.Any(e => e.Op == SkillProgramEffectOp.DrawAfterActualOwnHandGain)))
                    AdvanceEventRulesAndQueueFact(new ActualOwnTurnHandGainsRecordedEvent(new(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId),
                        c.GameplayHash, batch.TurnNumber, timing.ActualTurnOwnerSeat, batch.Id, gains));
                continue;
            }
            foreach (var c in CollectProgramTriggerCandidates(owner, SkillProgramTriggerWindow.AfterTurnEnded)
                .Where(c => GetProgramTrigger(c).Effects.Any(e => e.Op == SkillProgramEffectOp.DiscardForeignTurnHandGains)))
                AdvanceEventRulesAndQueueFact(new ForeignTurnHandGainsRecordedEvent(new(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId),
                    c.GameplayHash, batch.TurnNumber, timing.ActualTurnOwnerSeat, batch.Id, gains));
        }
    }
    private bool HasRecordedActualOwnHandGain(ProgramTriggerCandidate c, CardMovementBatchContext batch) =>
        CompleteProgramEventHistory().OfType<ActualOwnTurnHandGainsRecordedEvent>().Any(e =>
            e.Source == new CardConversionSource(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId) && e.GameplayHash == c.GameplayHash &&
            e.BatchId == batch.Id && e.ActualTurn == batch.TurnNumber && e.ActualTurnOwner == c.OwnerSeat &&
            e.Gains.SequenceEqual(ActualHandGainEntities(batch, c.OwnerSeat)));
    private ActualHandGainEntity[] ForeignHandGainRoster(int owner, string skill, string hash, int turn, int actualOwner) =>
        CompleteProgramEventHistory().OfType<ForeignTurnHandGainsRecordedEvent>()
            .Where(e => e.Source.OwnerSeat == owner && e.Source.SkillId == skill && e.GameplayHash == hash &&
                e.ActualTurn == turn && e.ActualTurnOwner == actualOwner).SelectMany(e => e.Gains)
            .GroupBy(e => e.CardId).Select(g => g.OrderByDescending(e => e.GainSequence).First()).OrderBy(e => e.GainSequence).ToArray();
    private int[] CurrentForeignHandGainIds(int owner, IReadOnlyList<ActualHandGainEntity> roster) => roster
        .Where(g => _cardZones.GetLocation(g.CardId) == CardLocation.Hand(owner) &&
            _cardMovements.LastOrDefault(m => m.CardId == g.CardId && m.To == CardLocation.Hand(owner) &&
                ActualHandGainOriginalSource(m) != CardLocation.Hand(owner))?.Sequence == g.GainSequence)
        .Select(g => g.CardId).ToArray();
    private static CardConversionSource ActualHandGainSource(ProgramSkillFrame f) => new(f.SkillId, f.TriggerId!, f.OwnerSeat, f.SkillInstanceId);
    private bool IsActualHandGainOwnDrawBatch(CardMovementBatchContext batch, int owner, string skill, string hash)
    {
        if (batch.OriginOwnerSeat != owner || batch.OriginSkillId != skill || batch.OriginSkillInstanceId is not { } instance ||
            batch.ParentFrameId is not { } parentId || batch.AwaitingProgramFrameId != parentId ||
            _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.Id == parentId) is not { } producer ||
            producer.ActualHandGain is not { Stage: ActualHandGainProgramStage.MovementChildren, Issued: true,
                Operation: SkillProgramEffectOp.DrawAfterActualOwnHandGain } paid || producer.PendingMovementContinuation is not { } pending ||
            !IsActualHandGainMovement(producer, ActualHandGainPausedEffect(producer), pending) ||
            paid.Source != new CardConversionSource(skill, producer.TriggerId!, owner, instance) || paid.Source != ActualHandGainSource(producer) ||
            paid.GameplayHash != hash || producer.GameplayHash != hash || paid.InstructionIndex != producer.InstructionIndex ||
            producer.WindowContext is not { Window: SkillProgramTriggerWindow.CardsGained, MovementBatch: { } original } context ||
            context.ParentFrameId != paid.OriginalParentId || original.Id != paid.OriginalBatchId ||
            _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().LastOrDefault(w => w.Id == paid.OriginalParentId) is not { } originalParent ||
            originalParent.Batch.Id != original.Id || !originalParent.Batch.Movements.SequenceEqual(original.Movements) ||
            originalParent.CandidateIndex < 0 || originalParent.CandidateIndex >= originalParent.Candidates.Count ||
            !MountObserverCandidateMatches(producer, originalParent.Candidates[originalParent.CandidateIndex]) ||
            !HasRecordedActualOwnHandGain(originalParent.Candidates[originalParent.CandidateIndex], originalParent.Batch) ||
            !paid.Gains.SequenceEqual(ActualHandGainEntities(originalParent.Batch, owner))) return false;
        var facts = CompleteProgramEventHistory().OfType<ActualHandGainDrawPaidEvent>().Where(e => e.FrameId == producer.Id).ToArray();
        if (facts is not [var fact] || fact.Source != paid.Source || fact.GameplayHash != hash || fact.OriginalBatchId != original.Id ||
            fact.ActualTurn != paid.ActualTurn || fact.ActualTurnOwner != owner || fact.Before != paid.Before || fact.After != paid.After ||
            fact.ActualCount != paid.PaidIds.Count || fact.ActualCount != 1 || batch.TurnNumber != paid.ActualTurn ||
            batch.Movements.Any(m => !_cardMovements.Contains(m) || m.Sequence <= paid.Before || m.Sequence > paid.After)) return false;
        var gains = MatchingActualHandGainIndexes(batch, owner, CardLocation.Hand(owner)).Select(i => batch.Movements[i]).ToArray();
        return gains.Length > 0 && gains.All(m => m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(owner) &&
            m.Reason.Value == ActualHandGainDrawReason && paid.PaidIds.Contains(m.CardId)) &&
            _cardMovements.Where(m => m.Sequence > paid.Before && m.Sequence <= paid.After && m.Reason.Value == ActualHandGainDrawReason &&
                m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(owner)).Select(m => m.CardId).SequenceEqual(paid.PaidIds);
    }
    private static SkillProgramCardCategory ActualHandGainCategory(CardKind kind) =>
        new[] { SkillProgramCardCategory.Basic, SkillProgramCardCategory.Trick, SkillProgramCardCategory.Equipment }
            .Single(c => MatchesSkillProgramCardCategory(kind, c));
    private bool SameCategoryGiftUsed(int owner, string skill, int turn, SkillProgramCardCategory category) =>
        CompleteProgramEventHistory().OfType<SameCategoryDeckGiftIssuedEvent>()
            .Any(e => e.Source.OwnerSeat == owner && e.Source.SkillId == skill && e.ActualTurn == turn && e.Category == category);
    private bool ExactActualHandGainUse(ProgramTriggerCandidate c, ProgramSkillWindowContext context,
        out ProgramCardTriggerWindowFrame parent, out CardUseFrame use)
    {
        parent = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault(w => w.Id == context.ParentFrameId)!;
        use = parent is null ? null! : LifecycleCardUse(parent.ParentFrameId)!;
        var useId = use?.Id;
        var effectiveKind = parent?.Action.EffectiveKind;
        return context.Window == SkillProgramTriggerWindow.CardUseCompleted && parent is not null &&
            parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count && ToSharedCandidate(parent.Candidates[parent.CandidateIndex]) == c &&
            parent.Action.Type == CardActionType.Use && parent.Action.ActorSeat == c.OwnerSeat && use is { Step: ResolutionFrameStep.Completed } &&
            use.Action?.ActionId == parent.Action.ActionId && use.SourceSeat == c.OwnerSeat &&
            context.CardUse is { } card && card.ParentCardUseFrameId == use.Id && card.CardActionId == parent.Action.ActionId &&
            card.ActorSeat == c.OwnerSeat && card.EffectiveKind == parent.Action.EffectiveKind &&
            HasExactAcceptedActualHandGainUse(use, parent.Action) &&
            CompleteProgramEventHistory().OfType<CardUseFinishedEvent>().Count(e => e.ResolutionId == useId && e.CardKind == effectiveKind) == 1;
    }
    private bool CanRunActualHandGainTrigger(ProgramTriggerCandidate c, SkillProgramTrigger t, ProgramSkillWindowContext context)
    {
        var op = t.Effects.FirstOrDefault()?.Op;
        if (op is null || !ActualHandGainAndCategoryGiftComposition.IsOperation(op.Value)) return true;
        if (_winner != Winner.None || _status == EngineStatus.Completed) return false;
        if (op == SkillProgramEffectOp.DrawAfterActualOwnHandGain)
            return ExactGainGiftMovementParent(c, context, out var moved) && moved.Batch.TurnNumber == _turnNumber &&
                moved.Batch.MovementTiming is { } timing && timing.ActualTurnOwnerSeat == c.OwnerSeat &&
                timing.Phase is not (TurnPhase.NotStarted or TurnPhase.Finished) && HasRecordedActualOwnHandGain(c, moved.Batch) &&
                !IsActualHandGainOwnDrawBatch(moved.Batch, c.OwnerSeat, c.SkillId, c.GameplayHash) &&
                MatchingActualHandGainIndexes(moved.Batch, c.OwnerSeat, CardLocation.Hand(c.OwnerSeat)).Length > 0 &&
                !CompleteProgramEventHistory().OfType<ActualHandGainDrawPaidEvent>().Any(e => e.Source.OwnerSeat == c.OwnerSeat &&
                    e.Source.SkillId == c.SkillId && e.OriginalBatchId == moved.Batch.Id);
        if (op == SkillProgramEffectOp.DiscardForeignTurnHandGains)
            return _resolutionStack.OfType<DeferredTurnEndFrame>().LastOrDefault(w => w.Id == context.ParentFrameId) is { } ended &&
                IsActualAfterTurnEndedParent(ended) && AfterTurnEndedCandidate(ended) == c && ended.OwnerSeat != c.OwnerSeat &&
                CurrentForeignHandGainIds(c.OwnerSeat, ForeignHandGainRoster(c.OwnerSeat, c.SkillId, c.GameplayHash, ended.TurnNumber, ended.OwnerSeat)).Length > 0 &&
                !CompleteProgramEventHistory().OfType<ForeignTurnHandGainsCleanupPaidEvent>().Any(e => e.Source.OwnerSeat == c.OwnerSeat &&
                    e.Source.SkillId == c.SkillId && e.ActualTurn == ended.TurnNumber);
        return ActualHandGainTurnOpen(c.OwnerSeat) && ExactActualHandGainUse(c, context, out var parent, out _) &&
            !SameCategoryGiftUsed(c.OwnerSeat, c.SkillId, _turnNumber, ActualHandGainCategory(parent.Action.EffectiveKind)) &&
            _players.Any(p => p.IsAlive && p.Seat != c.OwnerSeat);
    }
    private SkillProgramStepOutcome ExecuteActualHandGain(ProgramSkillFrame supplied, SkillProgramEffect effect)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.ActualHandGain is not null) throw new InvalidOperationException("An actual hand gain cannot be issued twice.");
        var context = f.WindowContext ?? throw new InvalidOperationException("Actual hand gains require their original native window.");
        ProgramTriggerCandidate c; int actualOwner; int actualTurn; long batchId = 0, useId = 0, actionId = 0;
        ActualHandGainEntity[] gains = []; SkillProgramCardCategory? category = null;
        if (effect.Op == SkillProgramEffectOp.DrawAfterActualOwnHandGain)
        {
            var moved = _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Id == context.ParentFrameId);
            c = moved.Candidates[moved.CandidateIndex]; actualOwner = moved.Batch.MovementTiming!.ActualTurnOwnerSeat;
            actualTurn = moved.Batch.TurnNumber; batchId = moved.Batch.Id;
            gains = ActualHandGainEntities(moved.Batch, f.OwnerSeat);
        }
        else if (effect.Op == SkillProgramEffectOp.DiscardForeignTurnHandGains)
        {
            var ended = _resolutionStack.OfType<DeferredTurnEndFrame>().Single(w => w.Id == context.ParentFrameId);
            c = AfterTurnEndedCandidate(ended); actualOwner = ended.OwnerSeat; actualTurn = ended.TurnNumber;
            gains = ForeignHandGainRoster(f.OwnerSeat, f.SkillId, f.GameplayHash, actualTurn, actualOwner);
        }
        else
        {
            var parent = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().Single(w => w.Id == context.ParentFrameId);
            c = ToSharedCandidate(parent.Candidates[parent.CandidateIndex]); actualOwner = _turnProgression.OwnerSeat; actualTurn = _turnNumber;
            useId = parent.ParentFrameId; actionId = parent.Action.ActionId; category = ActualHandGainCategory(parent.Action.EffectiveKind);
        }
        if (!MountObserverCandidateMatches(f, c) || !CanRunActualHandGainTrigger(c, GetProgramTrigger(f), context))
            throw new InvalidOperationException("Actual hand gains lost the exact original candidate, source or qualification.");
        ReplaceRuntimeTop(f = f with { ActualHandGain = new()
        { InstructionIndex = f.InstructionIndex, Operation = effect.Op, Source = ActualHandGainSource(f), GameplayHash = f.GameplayHash,
          OriginalParentId = context.ParentFrameId, ActualTurn = actualTurn, ActualTurnOwner = actualOwner, OriginalBatchId = batchId,
          OriginalCardUseFrameId = useId, OriginalActionId = actionId, Category = category, Gains = gains, Stage = ActualHandGainProgramStage.Choosing } });
        if (effect.Op == SkillProgramEffectOp.GiveSameCategoryFromDeck) { PublishActualHandGainChoice(f); return SkillProgramStepOutcome.AwaitChoice; }
        PayActualHandGain(f); return SkillProgramStepOutcome.AwaitChild;
    }
    private IReadOnlyList<PromptChoice> ActualHandGainChoices(ProgramSkillFrame f)
    {
        // Availability and identity of matching draw-pile cards never determine a player's choices.
        var choices = _players.Where(p => p.IsAlive && p.Seat != f.OwnerSeat).OrderBy(p => p.Seat).Select(p =>
            new PromptChoice(new($"same-category-deck.{f.Id}.{p.Seat}"), $"令 {p.Name} 获得牌堆中一张类型相同的牌。", [], [p.Seat],
                ActualHandGainParameters(f, "give"))).ToList();
        choices.Add(new(new($"same-category-deck.{f.Id}.decline"), "不发动应援。", [], [], ActualHandGainParameters(f, "decline")));
        return Array.AsReadOnly(choices.ToArray());
    }
    private static Dictionary<string, string> ActualHandGainParameters(ProgramSkillFrame f, string branch) => new()
    { ["program-action"] = "actual-hand-gain-category", ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch };
    private void PublishActualHandGainChoice(ProgramSkillFrame f)
    {
        var choices = ActualHandGainChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat, "应援：可令一名其他角色获得牌堆中一张类型相同的牌。",
            [], choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = f.OwnerSeat, Choices = choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private void ResolveActualHandGainChoice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Category gift has no owning frame.");
        AssertActualHandGain(f); var r = f.ActualHandGain!;
        if (r.Operation != SkillProgramEffectOp.GiveSameCategoryFromDeck || r.Stage != ActualHandGainProgramStage.Choosing ||
            _pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p || p.PlayerSeat != f.OwnerSeat ||
            !AssistedChoicesEqual([choice], ActualHandGainChoices(f).Where(c => c.Id == choice.Id).ToArray()))
            throw new InvalidOperationException("Category gift choice lost its exact private owner and current living targets.");
        ClearPendingDecision();
        if (choice.Parameters["branch"] == "decline" || !_players[f.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) { FinishActualHandGain(f); return; }
        if (!ActualHandGainTurnOpen(f.OwnerSeat) || SameCategoryGiftUsed(f.OwnerSeat, f.SkillId, r.ActualTurn, r.Category!.Value))
            throw new InvalidOperationException("Category gift lost its unpaid actual-turn quota.");
        ReplaceRuntimeTop(f = f with { ActualHandGain = r with { RecipientSeat = choice.Targets.Single() } });
        PayActualHandGain(f);
    }
    private void PayActualHandGain(ProgramSkillFrame f)
    {
        var r = f.ActualHandGain!; var before = _movementSequence;
        var ids = r.Operation == SkillProgramEffectOp.DiscardForeignTurnHandGains ? CurrentForeignHandGainIds(f.OwnerSeat, r.Gains) :
            r.Operation == SkillProgramEffectOp.GiveSameCategoryFromDeck ? _cardZones.CardsAt(CardLocation.DrawPile)
                .Where(c => MatchesSkillProgramCardCategory(c.Kind, r.Category!.Value)).Take(1).Select(c => c.Id).ToArray() : [];
        ReplaceRuntimeTop(f = f with { ActualHandGain = r = r with { Stage = ActualHandGainProgramStage.MovementChildren,
            PaidIds = ids, Before = before, After = before, Issued = true }, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        if (r.Operation == SkillProgramEffectOp.DrawAfterActualOwnHandGain)
        {
            var drawn = DrawCards(_players[f.OwnerSeat], 1, true, new(ActualHandGainDrawReason));
            f = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(f = f with { ActualHandGain = r = f.ActualHandGain! with { PaidIds = drawn.ToArray(), After = _movementSequence } });
            AdvanceEventRulesAndQueueFact(new ActualHandGainDrawPaidEvent(f.Id, r.Source, r.GameplayHash, r.OriginalBatchId,
                r.ActualTurn, r.ActualTurnOwner, before, r.After, drawn.Count));
        }
        else
        {
            void Paid()
            {
                var current = GetActiveProgramFrame(f.Id); var paid = current.ActualHandGain! with { After = _movementSequence };
                ReplaceRuntimeTop(current with { ActualHandGain = paid });
                if (paid.Operation == SkillProgramEffectOp.DiscardForeignTurnHandGains)
                    AdvanceEventRulesAndQueueFact(new ForeignTurnHandGainsCleanupPaidEvent(f.Id, paid.Source, paid.GameplayHash,
                        paid.ActualTurn, paid.ActualTurnOwner, paid.PaidIds, before, paid.After));
                else AdvanceEventRulesAndQueueFact(new SameCategoryDeckGiftIssuedEvent(f.Id, paid.Source, paid.GameplayHash,
                    paid.ActualTurn, paid.ActualTurnOwner, paid.OriginalCardUseFrameId, paid.OriginalActionId, paid.Category!.Value,
                    paid.RecipientSeat, paid.PaidIds.Count, before, paid.After));
            }
            if (ids.Length == 0) Paid();
            else MoveProgramCardsFromMultipleSources(ids, r.Operation == SkillProgramEffectOp.DiscardForeignTurnHandGains ?
                CardLocation.DiscardPile : CardLocation.Hand(r.RecipientSeat), new(r.Operation == SkillProgramEffectOp.DiscardForeignTurnHandGains ?
                    ForeignHandCleanupReason : SameCategoryDeckGiftReason), (_, _) => Paid());
            f = GetActiveProgramFrame(f.Id);
        }
        if (!TryDrainFireTargetMovement(f)) ReturnActualHandGainMovement(f);
    }
    private bool ResumeActualHandGain(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { ActualHandGain: not null } f || f.Id != id) return false;
        AssertActualHandGain(f);
        if (GainGiftHasFinalGameEnd()) return true;
        if (f.PendingMovementContinuation is not null) { if (!TryDrainFireTargetMovement(f)) ReturnActualHandGainMovement(f); return true; }
        if (f.ActualHandGain.Stage != ActualHandGainProgramStage.Choosing) throw new InvalidOperationException("An issued actual gain lost its typed return.");
        if (!_players[f.OwnerSeat].IsAlive || !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
        { ClearPendingDecision(); FinishActualHandGain(f); return true; }
        if (_pendingDecision is null) PublishActualHandGainChoice(f);
        return true;
    }
    private bool ReturnActualHandGainMovement(ProgramSkillFrame f)
    {
        if (f.ActualHandGain is null) return false;
        if (f.PendingMovementContinuation is not { } p || !IsActualHandGainMovement(f, ActualHandGainPausedEffect(f), p))
            throw new InvalidOperationException("Actual gains cannot consume another instruction's return.");
        AssertActualHandGain(f);
        if (GainGiftHasFinalGameEnd() || TryDrainFireTargetMovement(f)) return true;
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null }); FinishActualHandGain(f); return true;
    }
    private void FinishActualHandGain(ProgramSkillFrame f)
    { ReplaceRuntimeTop(f = f with { ActualHandGain = null }); FinishProgramSkill(f, true); }
    private SkillProgramEffect? ActualHandGainPausedEffect(ProgramSkillFrame f) => f.InstructionIndex < 1 ? null :
        ProgramInstructionResolver.Default.Resolve(f, _contentRegistry.GetSkill(f.SkillId).Program!).GetPausedInstruction(f.InstructionIndex).Effect;
    private bool IsActualHandGainMovement(ProgramSkillFrame f, SkillProgramEffect? e, ProgramMovementContinuation p) =>
        f.ActualHandGain is { Stage: ActualHandGainProgramStage.MovementChildren, Issued: true } r && r.InstructionIndex == f.InstructionIndex &&
        e?.Op == r.Operation && e == ActualHandGainPausedEffect(f) && p.SubjectSeat == f.OwnerSeat && p.BeforeCount == 0 && p.CoverageResultBind is null;
    private PromptChoice SelectAiActualHandGain(PendingDecision decision, ProgramSkillFrame f)
    {
        var view = CreateSnapshot(f.OwnerSeat); var hint = new SkillProgramAiHint(0, 0, 0, 1, 0, 0, true, false);
        return decision.Choices.Where(c => c.Targets.Count == 1)
            .Select(c => (Choice: c, Score: _aiBrains[f.OwnerSeat].ScoreProgramTarget(view, c.Targets[0], hint)))
            .Where(x => x.Score > 0).OrderByDescending(x => x.Score).ThenBy(x => x.Choice.Targets[0])
            .Select(x => x.Choice).FirstOrDefault() ?? decision.Choices.Single(c => c.Targets.Count == 0);
    }
    private CardUseFrame NormalizeCompletedCategoryVirtualUse(CardUseFrame use, long? programParentFrameId)
    {
        if (use.Action is not null || !_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.GiveSameCategoryFromDeck)) return use;
        if (use.CardId != 0 || use.PhysicalCardIds is not { Count: 0 } || programParentFrameId is not { } parentId ||
            _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.Id == parentId) is not { } parent ||
            parent.InstructionIndex < 1 || ProgramInstructionResolver.Default.Resolve(parent, _contentRegistry.GetSkill(parent.SkillId).Program!)
                .GetPausedInstruction(parent.InstructionIndex).Effect.Op != SkillProgramEffectOp.UseVirtualCard ||
            CompleteProgramEventHistory().OfType<CompletedCategoryVirtualUseNormalizedEvent>().Any(e => e.CardUseFrameId == use.Id))
            throw new InvalidOperationException("Legacy virtual completion lost its exact Action-null zero-entity producer.");
        var producerSource = new CardConversionSource(parent.SkillId, GetProgramBindingId(parent), parent.OwnerSeat, parent.SkillInstanceId);
        var action = CaptureFactionAction(new CardActionContext(++_cardActionSequence,
            _resolutionStack.OfType<CardUseFrame>().LastOrDefault(f => f.Id != use.Id)?.Action?.ActionId, CardActionType.Use,
            use.SourceSeat, use.SourceSeat, null, null, null, use.CardKind, use.TargetSeats, [], [producerSource], effectiveSuit: Suit.None, effectiveRank: 0));
        ReplaceRuntimeFrame(use.Id, use = use with { Action = action });
        AdvanceEventRulesAndQueueFact(new CompletedCategoryVirtualUseNormalizedEvent(use.Id, use.SourceSeat, use.CardKind, parent.Id,
            producerSource, parent.GameplayHash, parent.InstructionIndex, action));
        AdvanceEventRulesAndQueueFact(new CardActionAcceptedEvent(action)); return use;
    }
    private bool IsNormalizedLegacyCompletedCategoryUse(CardUseFrame use, int actor, CardKind kind, IReadOnlyList<int> completedTargets)
    {
        var facts = CompleteProgramEventHistory().OfType<CompletedCategoryVirtualUseNormalizedEvent>().Where(e => e.CardUseFrameId == use.Id).ToArray();
        if (facts is not [var fact] || use.CardId != 0 || use.PhysicalCardIds is not { Count: 0 } || use.SourceSeat != actor || use.CardKind != kind ||
            fact.ActorSeat != actor || fact.EffectiveKind != kind || use.Action is not { Type: CardActionType.Use } action ||
            action.ActionId != fact.Action.ActionId || action.ActorSeat != actor || action.ProviderSeat != actor || action.EffectiveKind != kind ||
            action.PhysicalCards.Count != 0 || action.ConversionChain is not [var source] || source != fact.ProducerSource ||
            !action.TargetSeats.SequenceEqual(use.TargetSeats) || !fact.Action.TargetSeats.SequenceEqual(use.TargetSeats) ||
            completedTargets.Count == 0 || !use.TargetSeats.SequenceEqual(completedTargets) ||
            _resolutionStack.OfType<ProgramSkillFrame>().LastOrDefault(f => f.Id == fact.ProgramParentFrameId) is not { } parent ||
            parent.GameplayHash != fact.ProducerGameplayHash || parent.InstructionIndex != fact.ProducerInstructionIndex ||
            ProgramInstructionResolver.Default.Resolve(parent, _contentRegistry.GetSkill(parent.SkillId).Program!)
                .GetPausedInstruction(parent.InstructionIndex).Effect.Op != SkillProgramEffectOp.UseVirtualCard ||
            source != new CardConversionSource(parent.SkillId, GetProgramBindingId(parent), parent.OwnerSeat, parent.SkillInstanceId)) return false;
        return CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>().Any(e => e.Action.ActionId == action.ActionId &&
            e.Action.Type == CardActionType.Use && e.Action.ActorSeat == actor && e.Action.ProviderSeat == actor &&
            e.Action.EffectiveKind == kind && e.Action.PhysicalCards.Count == 0 && e.Action.TargetSeats.SequenceEqual(use.TargetSeats));
    }
    private sealed partial class ProgramSkillHost : IActualHandGainAndCategoryGiftHost
    { public SkillProgramStepOutcome ExecuteActualHandGain(ProgramSkillFrame f, SkillProgramEffect e) => engine.ExecuteActualHandGain(f, e); }
}
