using System.Globalization;

namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed record NativeDrawInvocationDraft(long Id, int Owner, int Requested, int Turn, int TurnOwner,
        int PhaseActor, ActualDiscardRecoveryPhaseKey? Phase, long? Parent, CardConversionSource? Producer,
        CardMoveReason Reason, long Before);
    private bool TracksNativeDrawInvocations =>
        _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.DrawAfterActualOutsideDraw);
    private long OutsidePhaseMovementSequence => _cardMovements.LastOrDefault()?.Sequence ?? 0;
    private static bool IsOutsidePhaseOperation(SkillProgramEffectOp op) => OutsidePhaseDrawDiscardComposition.IsOperation(op);
    private static string OutsidePhaseMovementReason(ProgramSkillFrame f, bool draw) =>
        $"skill-program.{f.SkillId}.outside-phase-{(draw ? "draw" : "discard")}";

    private NativeDrawInvocationDraft? CaptureNativeDrawInvocation(int owner, int requested, CardMoveReason reason,
        CardConversionSource? directProducer)
    {
        if (!TracksNativeDrawInvocations || !_setupComplete || requested <= 0 || reason == CardMoveReasons.InitialDeal ||
            _turnNumber < 1 || !IsValidPlayerSeat(owner) || !IsValidPlayerSeat(_turnProgression.OwnerSeat) ||
            _winner != Winner.None || _status == EngineStatus.Completed) return null;
        var phase = CurrentActualDiscardRecoveryPhase();
        return new(++_resolutionSequence, owner, requested, _turnNumber, _turnProgression.OwnerSeat,
            phase?.ActorSeat ?? _currentSeat, phase, _resolutionStack.LastOrDefault()?.Id, directProducer, reason,
            OutsidePhaseMovementSequence);
    }
    private void CompleteNativeDrawInvocation(NativeDrawInvocationDraft? draft, IReadOnlyList<int> actualIds)
    {
        if (draft is null || actualIds.Count == 0) return;
        if (actualIds.Count > draft.Requested || actualIds.Distinct().Count() != actualIds.Count)
            throw new InvalidOperationException("A native draw invocation cannot invent or repeat physical draw materials.");
        var after = OutsidePhaseMovementSequence;
        var materials = _cardMovements.Where(m => m.Sequence > draft.Before && m.Sequence <= after &&
                m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(draft.Owner) && m.Reason == draft.Reason)
            .OrderBy(m => m.Sequence).Select(m => new NativeDrawMaterial(m.CardId, m.CardKind, m.Sequence)).ToArray();
        if (!materials.Select(m => m.CardId).SequenceEqual(actualIds))
            throw new InvalidOperationException("A native draw invocation lost its exact real DrawCards entities.");
        var last = materials[^1];
        var index = _pendingCardsMovedBatches.FindLastIndex(batch => batch.ParentFrameId == draft.Parent &&
            batch.Movements.Any(m => m.Sequence == last.MovementSequence && m.CardId == last.CardId));
        if (index < 0 || _pendingCardsMovedBatches[index].NativeDrawInvocation is not null)
            throw new InvalidOperationException("A native draw invocation lost its last unclaimed physical draw batch.");
        var proof = new NativeDrawInvocationProof { InvocationId = draft.Id, OwnerSeat = draft.Owner,
            RequestedCount = draft.Requested, ActualTurnNumber = draft.Turn, ActualTurnOwnerSeat = draft.TurnOwner,
            PhaseActorSeat = draft.PhaseActor, Phase = draft.Phase, ParentFrameId = draft.Parent,
            DirectProducer = draft.Producer, Reason = draft.Reason, SequenceBefore = draft.Before, SequenceAfter = after,
            LastBatchId = _pendingCardsMovedBatches[index].Id, Materials = materials };
        _pendingCardsMovedBatches[index] = _pendingCardsMovedBatches[index] with { NativeDrawInvocation = proof };
        AdvanceEventRulesAndQueueFact(new NativeDrawInvocationRecordedEvent(proof.InvocationId, proof.OwnerSeat,
            proof.RequestedCount, proof.ActualCount, proof.ActualTurnNumber, proof.ActualTurnOwnerSeat, proof.PhaseActorSeat,
            proof.Phase, proof.ParentFrameId, proof.DirectProducer, proof.Reason, proof.SequenceBefore, proof.SequenceAfter,
            proof.LastBatchId));
    }

    private bool ValidNativeDrawInvocation(NativeDrawInvocationProof p)
    {
        if (p.InvocationId <= 0 || !IsValidPlayerSeat(p.OwnerSeat) || !IsValidPlayerSeat(p.ActualTurnOwnerSeat) ||
            !IsValidPlayerSeat(p.PhaseActorSeat) || p.ActualTurnNumber < 1 || p.ActualCount < 1 ||
            p.RequestedCount < p.ActualCount || p.Reason == CardMoveReasons.InitialDeal || p.LastBatchId <= 0 ||
            p.SequenceBefore < 0 || p.SequenceAfter <= p.SequenceBefore || p.SequenceAfter > OutsidePhaseMovementSequence ||
            p.Materials.Select(m => m.CardId).Distinct().Count() != p.ActualCount ||
            p.Materials.Select(m => m.MovementSequence).Distinct().Count() != p.ActualCount ||
            !p.Materials.Select(m => m.MovementSequence).SequenceEqual(p.Materials.Select(m => m.MovementSequence).Order()) ||
            p.Phase is { } phase && (!IsRecordedActualDiscardRecoveryPhase(phase) ||
                phase.TurnNumber != p.ActualTurnNumber || phase.ActualTurnOwnerSeat != p.ActualTurnOwnerSeat || phase.ActorSeat != p.PhaseActorSeat)) return false;
        var facts = CompleteProgramEventHistory().OfType<NativeDrawInvocationRecordedEvent>()
            .Where(e => e.InvocationId == p.InvocationId).ToArray();
        if (facts is not [var fact] || fact.OwnerSeat != p.OwnerSeat || fact.Requested != p.RequestedCount ||
            fact.ActualCount != p.ActualCount || fact.TurnNumber != p.ActualTurnNumber ||
            fact.ActualTurnOwnerSeat != p.ActualTurnOwnerSeat || fact.PhaseActorSeat != p.PhaseActorSeat || fact.Phase != p.Phase ||
            fact.ParentFrameId != p.ParentFrameId || fact.DirectProducer != p.DirectProducer || fact.Reason != p.Reason ||
            fact.SequenceBefore != p.SequenceBefore || fact.SequenceAfter != p.SequenceAfter || fact.LastBatchId != p.LastBatchId) return false;
        var actual = _cardMovements.Where(m => m.Sequence > p.SequenceBefore && m.Sequence <= p.SequenceAfter &&
            m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(p.OwnerSeat) && m.Reason == p.Reason)
            .OrderBy(m => m.Sequence).Select(m => new NativeDrawMaterial(m.CardId, m.CardKind, m.Sequence));
        return p.Materials.SequenceEqual(actual);
    }
    private int[] MatchingOutsideDrawIndexes(CardMovementBatchContext batch, ProgramTriggerCandidate candidate)
    {
        if (batch.NativeDrawInvocation is not { } proof || proof.LastBatchId != batch.Id || proof.OwnerSeat != candidate.OwnerSeat ||
            proof.ParentFrameId != batch.ParentFrameId || proof.ActualTurnNumber != batch.TurnNumber ||
            proof.Phase?.Kind == ActualDiscardRecoveryPhaseKind.Draw || !ValidNativeDrawInvocation(proof) ||
            proof.DirectProducer is { } source && source.OwnerSeat == candidate.OwnerSeat && source.SkillId == candidate.SkillId) return [];
        var last = proof.Materials[^1];
        return batch.Movements.Select((m, i) => (m, i)).Where(x => x.m.Sequence == last.MovementSequence &&
            x.m.CardId == last.CardId && x.m.CardKind == last.PrintedKind && x.m.From == CardLocation.DrawPile &&
            x.m.To == CardLocation.Hand(candidate.OwnerSeat) && x.m.Reason == proof.Reason).Select(x => x.i).ToArray();
    }
    private int[] MatchingOutsideDiscardIndexes(CardMovementBatchContext batch, ProgramTriggerCandidate candidate)
    {
        if (batch.TurnNumber < 1 || batch.DiscardRecoveryPhase?.Kind == ActualDiscardRecoveryPhaseKind.Discard ||
            batch.DiscardRecoveryPhase is { } phase && (!IsRecordedActualDiscardRecoveryPhase(phase) || phase.TurnNumber != batch.TurnNumber)) return [];
        return batch.Movements.Select((m, i) => (m, i)).Where(x => x.m.To == CardLocation.DiscardPile &&
            GetProgramDiscardSource(x.m) is { OwnerSeat: { } owner, Zone: CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment } &&
            owner == candidate.OwnerSeat && _cardMovements.Contains(x.m)).Select(x => x.i).ToArray();
    }
    private ProgramSkillWindowContext CreateOutsideDiscardContext(CardsMovedTriggerWindowFrame frame, ProgramTriggerCandidate candidate)
    {
        var indexes = MatchingOutsideDiscardIndexes(frame.Batch, candidate);
        if (indexes.Length == 0) throw new InvalidOperationException("An outside-discard context lost its real own discard material.");
        return new(SkillProgramTriggerWindow.DiscardPileReceived, frame.Id, candidate.OwnerSeat,
            SourceSeat: candidate.OwnerSeat, TargetSeat: candidate.OwnerSeat, OccurrenceIndex: candidate.OccurrenceIndex,
            Facts: CaptureCardsMovedTriggerFacts(_players[candidate.OwnerSeat], indexes.Length,
                new(CardLocation.DiscardPile, 0, 0), SkillProgramTriggerWindow.DiscardPileReceived, frame.Batch.MovementTiming),
            MovementBatch: frame.Batch, MovementIndex: indexes[0]);
    }
    private bool ExactOutsidePhaseParent(ProgramTriggerCandidate candidate, ProgramSkillWindowContext context,
        out CardsMovedTriggerWindowFrame parent)
    {
        parent = _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().LastOrDefault(w => w.Id == context.ParentFrameId)!;
        return parent is not null && parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count &&
            parent.Candidates[parent.CandidateIndex] == candidate && context.MovementBatch is { } batch &&
            batch.Id == parent.Batch.Id && batch.Movements.SequenceEqual(parent.Batch.Movements);
    }
    private OutsidePhaseDiscardMaterial[] OutsidePhaseDiscardMaterials(int owner)
    {
        var result = new List<OutsidePhaseDiscardMaterial>();
        foreach (var target in _players.Where(p => p.IsAlive && p.Seat != owner))
        foreach (var zone in new[] { CardZoneKind.Hand, CardZoneKind.Equipment, CardZoneKind.Judgment })
        {
            var from = new CardLocation(zone, target.Seat); var cards = _cardZones.CardsAt(from);
            for (var slot = 0; slot < cards.Count; slot++)
                if (!IsForeignEquipmentDiscardPrevented(owner, cards[slot], from, OwnedCardMoveIntent.Discard))
                    result.Add(new(cards[slot].Id, cards[slot].Kind, from, slot, cards[slot].IsGeneralWeapon));
        }
        return result.ToArray();
    }
    private bool CanRunOutsidePhaseDrawDiscardTrigger(ProgramTriggerCandidate candidate, SkillProgramTrigger trigger,
        ProgramSkillWindowContext context)
    {
        if (!trigger.Effects.Any(e => IsOutsidePhaseOperation(e.Op))) return true;
        if (_winner != Winner.None || _status == EngineStatus.Completed || !_players[candidate.OwnerSeat].IsAlive ||
            !ExactOutsidePhaseParent(candidate, context, out var parent)) return false;
        return trigger.Effects[0].Op == SkillProgramEffectOp.DrawAfterActualOutsideDraw
            ? context.Window == SkillProgramTriggerWindow.CardsGained && MatchingOutsideDrawIndexes(parent.Batch, candidate).Length == 1
            : context.Window == SkillProgramTriggerWindow.DiscardPileReceived && MatchingOutsideDiscardIndexes(parent.Batch, candidate).Length > 0 &&
                OutsidePhaseDiscardMaterials(candidate.OwnerSeat).Length > 0;
    }
    private SkillProgramStepOutcome BeginOutsidePhaseDrawDiscard(ProgramSkillFrame supplied, SkillProgramEffectOp op)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.OutsidePhaseDrawDiscard is not null || !IsOutsidePhaseOperation(op))
            throw new InvalidOperationException("An outside-phase obligation cannot be issued twice.");
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId)) return SkillProgramStepOutcome.Continue;
        var context = f.WindowContext ?? throw new InvalidOperationException("Outside-phase draw/discard needs its real movement parent.");
        var parent = _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().Single(w => w.Id == context.ParentFrameId);
        var candidate = parent.Candidates[parent.CandidateIndex]; var trigger = GetProgramTrigger(f);
        if (!MountObserverCandidateMatches(f, candidate) || trigger.Effects[0].Op != op ||
            !CanRunOutsidePhaseDrawDiscardTrigger(candidate, trigger, context))
            throw new InvalidOperationException("Outside-phase draw/discard lost its exact current movement candidate.");
        var draw = op == SkillProgramEffectOp.DrawAfterActualOutsideDraw;
        var indexes = draw ? MatchingOutsideDrawIndexes(parent.Batch, candidate) : MatchingOutsideDiscardIndexes(parent.Batch, candidate);
        var r = new ProgramOutsidePhaseDrawDiscardReceipt { InstructionIndex = f.InstructionIndex,
            Source = new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId), GameplayHash = f.GameplayHash, Op = op,
            WindowFrameId = parent.Id, OriginalBatchId = parent.Batch.Id, OriginalMovementIndex = indexes[0],
            OriginalPhase = draw ? parent.Batch.NativeDrawInvocation!.Phase : parent.Batch.DiscardRecoveryPhase,
            OriginalDraw = draw ? parent.Batch.NativeDrawInvocation : null,
            OriginalDiscards = draw ? [] : indexes.Select(i => new DiscardRecoveryEntity(parent.Batch.Movements[i].CardId,
                parent.Batch.Movements[i].Sequence, GetProgramDiscardSource(parent.Batch.Movements[i])!.Value)).ToArray(),
            Stage = OutsidePhaseDrawDiscardStage.Choosing,
            EligibleTargets = draw ? _players.Where(p => p.IsAlive).Select(p => p.Seat).ToArray() : [],
            EligibleMaterials = draw ? [] : OutsidePhaseDiscardMaterials(f.OwnerSeat) };
        ReplaceRuntimeTop(f = f with { OutsidePhaseDrawDiscard = r });
        AdvanceEventRulesAndQueueFact(new OutsidePhaseDrawDiscardStartedEvent(f.Id, r.Source, r.GameplayHash, op,
            r.WindowFrameId, r.OriginalBatchId, r.OriginalDraw?.InvocationId));
        PublishOutsidePhaseDrawDiscardChoice(f); return SkillProgramStepOutcome.AwaitChoice;
    }
    private IReadOnlyList<PromptChoice> OutsidePhaseDrawDiscardChoices(ProgramSkillFrame f)
    {
        var r = f.OutsidePhaseDrawDiscard!; var draw = r.Op == SkillProgramEffectOp.DrawAfterActualOutsideDraw;
        Dictionary<string, string> Parameters(string branch) => new() { ["program-action"] = "outside-phase-draw-discard",
            ["frame-id"] = f.Id.ToString(CultureInfo.InvariantCulture), ["branch"] = branch };
        if (draw) return Array.AsReadOnly(r.EligibleTargets.Select(seat => new PromptChoice(
            new($"outside-phase.{f.Id}.draw.{seat}"), $"令 {_players[seat].Name} 摸一张牌。", [], [seat], Parameters("draw-target"))).ToArray());
        return Array.AsReadOnly(r.EligibleMaterials.Select(m =>
        {
            var hidden = m.From.Zone == CardZoneKind.Hand; var parameters = Parameters("discard-card");
            parameters["card-owner-seat"] = m.From.OwnerSeat!.Value.ToString(CultureInfo.InvariantCulture);
            parameters["source-zone"] = m.From.Zone.ToString(); parameters["slot-index"] = m.Slot.ToString(CultureInfo.InvariantCulture);
            return new PromptChoice(new($"outside-phase.{f.Id}.discard.{m.From.OwnerSeat}.{m.From.Zone}.{m.Slot}"),
                hidden ? $"弃置 {_players[m.From.OwnerSeat.Value].Name} 的第 {m.Slot + 1} 个暗置手牌牌位。" :
                    $"弃置 {_players[m.From.OwnerSeat.Value].Name} 的【{GetAdvancedCard(m.CardId).DisplayName}】。",
                hidden ? [] : [m.CardId], [m.From.OwnerSeat.Value], parameters);
        }).ToArray());
    }
    private void PublishOutsidePhaseDrawDiscardChoice(ProgramSkillFrame f)
    {
        var choices = OutsidePhaseDrawDiscardChoices(f); var skill = _contentRegistry.GetSkill(f.SkillId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, f.OwnerSeat,
            f.OutsidePhaseDrawDiscard!.Op == SkillProgramEffectOp.DrawAfterActualOutsideDraw ? "令一名角色摸一张牌。" : "弃置一名其他角色的一张牌。",
            choices.SelectMany(c => c.Cards).Distinct().ToArray(), choices.SelectMany(c => c.Targets).Distinct().ToArray(), f.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, Choices = choices,
            SkillPrompt = new(f.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[f.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private void ResolveOutsidePhaseDrawDiscardChoice(PromptChoice choice)
    {
        var f = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Outside-phase choice lost its owner.");
        AssertOutsidePhaseDrawDiscard(f); var r = f.OutsidePhaseDrawDiscard!;
        if (r.Stage != OutsidePhaseDrawDiscardStage.Choosing || !OutsidePhaseDrawDiscardChoices(f).Any(c =>
                c.Id == choice.Id && c.Cards.SequenceEqual(choice.Cards) && c.Targets.SequenceEqual(choice.Targets) &&
                c.Parameters.OrderBy(x => x.Key).SequenceEqual(choice.Parameters.OrderBy(x => x.Key))))
            throw new InvalidOperationException("Outside-phase choice changed its exact published opaque material or target.");
        ClearPendingDecision();
        if (_winner != Winner.None || !_players[f.OwnerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[f.OwnerSeat], f.SkillId, f.SkillInstanceId))
        { CancelProgramBindingAndCleanup(f, "卫戍在发行摸牌或弃牌前失去原技能资格。"); return; }
        var draw = r.Op == SkillProgramEffectOp.DrawAfterActualOutsideDraw;
        var seat = choice.Targets.Single();
        if (!_players[seat].IsAlive) { CancelProgramBindingAndCleanup(f, "卫戍的原目标已失效，未发行动作。"); return; }
        OutsidePhaseDiscardMaterial? material = null;
        if (!draw)
        {
            material = r.EligibleMaterials.Single(m => m.From.OwnerSeat == seat &&
                m.From.Zone.ToString() == choice.Parameters["source-zone"] && m.Slot.ToString(CultureInfo.InvariantCulture) == choice.Parameters["slot-index"]);
            if (!OutsidePhaseDiscardMaterials(f.OwnerSeat).Contains(material))
                throw new InvalidOperationException("Outside-phase discard must remove its original still-owned legal opaque slot.");
        }
        var before = OutsidePhaseMovementSequence;
        ReplaceRuntimeTop(f = f with { OutsidePhaseDrawDiscard = r with { Stage = OutsidePhaseDrawDiscardStage.MovementChildren,
            MovementIssued = true, ParticipantSeat = seat, PaidMaterial = material, SequenceBefore = before, SequenceAfter = before },
            PendingMovementContinuation = new(seat, 0, null) });
        if (draw)
        {
            var actual = DrawCards(_players[seat], 1, true, new(OutsidePhaseMovementReason(f, true)), r.Source).Count;
            var current = GetActiveProgramFrame(f.Id);
            ReplaceRuntimeTop(current with { OutsidePhaseDrawDiscard = current.OutsidePhaseDrawDiscard! with {
                ActualCount = actual, SequenceAfter = OutsidePhaseMovementSequence } });
            RecordOutsidePhaseIssued(GetActiveProgramFrame(f.Id));
        }
        else
        {
            var paid = material!;
            MoveProgramCardsFromMultipleSources([paid.CardId], CardLocation.DiscardPile, new(OutsidePhaseMovementReason(f, false)), (batch, records) =>
            {
                if (records is not [var move] || move.CardId != paid.CardId || move.CardKind != paid.PrintedKind || move.From != paid.From ||
                    move.To != (paid.IsGeneralWeapon && paid.From.Zone == CardZoneKind.Equipment ? CardLocation.OutsideGame : CardLocation.DiscardPile))
                    throw new InvalidOperationException("Outside-phase discard did not pay its single real frozen foreign card.");
                var current = GetActiveProgramFrame(f.Id);
                ReplaceRuntimeTop(current with { OutsidePhaseDrawDiscard = current.OutsidePhaseDrawDiscard! with {
                    ActualCount = 1, SequenceAfter = move.Sequence, BatchId = batch } });
                RecordOutsidePhaseIssued(GetActiveProgramFrame(f.Id));
            });
        }
        AdvanceRuntimeProgram(f.Id);
    }
    private void RecordOutsidePhaseIssued(ProgramSkillFrame f)
    {
        var r = f.OutsidePhaseDrawDiscard!;
        AdvanceEventRulesAndQueueFact(new OutsidePhaseDrawDiscardMovementIssuedEvent(f.Id, r.Op, r.ParticipantSeat!.Value,
            r.ActualCount, r.SequenceBefore, r.SequenceAfter, r.BatchId, r.PaidMaterial));
    }
    private bool ResumeOutsidePhaseDrawDiscard(long frameId)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame f || f.Id != frameId || f.OutsidePhaseDrawDiscard is not { } r) return false;
        AssertOutsidePhaseDrawDiscard(f);
        if (r.Stage == OutsidePhaseDrawDiscardStage.Choosing)
        { if (_pendingDecision is null) PublishOutsidePhaseDrawDiscardChoice(f); return true; }
        if (r.Stage == OutsidePhaseDrawDiscardStage.MovementChildren)
        {
            if (TryBeginQueuedRecoveryReplacement(frameId, PostEventContinuation.AwaitedProgramMovement) ||
                TryBeginCharacterStateProgramWindow(frameId, CharacterStateContinuation.Program) ||
                TryBeginHpChangedProgramWindow(frameId, PostEventContinuation.AwaitedProgramMovement) ||
                TryBeginCardsMovedProgramWindow(frameId) || TryBeginAdvancedSkillsChanged(frameId)) return true;
            ReplaceRuntimeTop(f = f with { OutsidePhaseDrawDiscard = r with { Stage = OutsidePhaseDrawDiscardStage.Complete }, PendingMovementContinuation = null });
            AdvanceEventRulesAndQueueFact(new OutsidePhaseDrawDiscardCompletedEvent(f.Id, r.ActualCount));
        }
        FinishProgramSkill(f, true); return true;
    }
    private bool ReturnOutsidePhaseDrawDiscardMovement(ProgramSkillFrame f)
    {
        if (f.OutsidePhaseDrawDiscard is not { MovementIssued: true, Stage: OutsidePhaseDrawDiscardStage.MovementChildren }) return false;
        AssertOutsidePhaseDrawDiscard(f);
        if (f.PendingMovementContinuation is null) throw new InvalidOperationException("Outside-phase child lost its owning movement return.");
        AdvanceRuntimeProgram(f.Id); return true;
    }
    private bool IsOutsidePhaseDrawDiscardMovement(ProgramSkillFrame f, SkillProgramEffect? effect, ProgramMovementContinuation pending) =>
        effect is not null && IsOutsidePhaseOperation(effect.Op) && f.OutsidePhaseDrawDiscard is { MovementIssued: true } r &&
        r.Op == effect.Op && f.PendingMovementContinuation == pending && pending.SubjectSeat == r.ParticipantSeat &&
        pending.BeforeCount == 0 && pending.CoverageResultBind is null && ValidOutsidePhaseDrawDiscardReceipt(f);
    private bool CanContinueOutsidePhaseDrawDiscard(ProgramSkillFrame f) =>
        f.OutsidePhaseDrawDiscard is { MovementIssued: true } && ValidOutsidePhaseDrawDiscardReceipt(f);
    private PromptChoice SelectAiOutsidePhaseDrawDiscard(PendingDecision decision, ProgramSkillFrame f)
    {
        if (f.OutsidePhaseDrawDiscard!.Op == SkillProgramEffectOp.DrawAfterActualOutsideDraw)
            return decision.Choices.OrderByDescending(c => c.Targets.Single() == f.OwnerSeat).ThenBy(c => c.Targets.Single()).First();
        var (choice, thought) = _aiBrains[decision.PlayerSeat].ChooseOtherOwnedCardDiscard(CreateSnapshot(decision.PlayerSeat),
            decision.Choices, _contentRegistry.GetSkill(f.SkillId).Name, ++_thoughtSequence);
        AddThought(thought); return choice;
    }
    private sealed partial class ProgramSkillHost : IOutsidePhaseDrawDiscardProgramHost
    {
        public SkillProgramStepOutcome OutsidePhaseDrawDiscard(ProgramSkillFrame f, SkillProgramEffectOp op) => engine.BeginOutsidePhaseDrawDiscard(f, op);
        public bool CanContinueOutsidePhaseDrawDiscard(ProgramSkillFrame f) => engine.CanContinueOutsidePhaseDrawDiscard(f);
    }
}
