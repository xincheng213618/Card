namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string BeifenObtainReason = "skill-program.le-cai-wen-ji.beifen.obtain";
    private bool HasLeCaiWenJiBeifenCapability => _contentRegistry.ProgramDependencies
        .HasTriggerOperation(SkillProgramEffectOp.ResolveBeifenLostMarkedSuitGains);
    private static readonly IReadOnlyList<Suit> BeifenStandardSuits =
        Array.AsReadOnly(new[] { Suit.Spade, Suit.Heart, Suit.Club, Suit.Diamond });
    private static CardConversionSource BeifenSource(ProgramSkillFrame f) =>
        new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private static bool IsBeifenLossTrigger(SkillProgramTrigger t) => t.Effects is
        [{ Op: SkillProgramEffectOp.ResolveBeifenLostMarkedSuitGains }];

    private IReadOnlyList<BeifenLossSource> CaptureBeifenLossSources(IEnumerable<CardLocation> locations)
    {
        if (!HasLeCaiWenJiBeifenCapability || !_setupComplete) return Array.AsReadOnly(Array.Empty<BeifenLossSource>());
        return Array.AsReadOnly(locations.Where(l => l.Zone is CardZoneKind.Hand or CardZoneKind.Equipment)
            .Where(l => l.OwnerSeat is { } seat && IsValidPlayerSeat(seat)).Select(l => l.OwnerSeat!.Value)
            .Distinct().Where(seat => _players[seat].IsAlive).SelectMany(seat =>
                GetSkillBindingShard(_players[seat]).ProgramInstances.SelectMany(instance =>
                    instance.Program.Triggers.Where(IsBeifenLossTrigger).Select(trigger => new BeifenLossSource(
                        new(instance.SkillId, trigger.Id, seat, instance.SkillInstanceId), instance.Program.GameplayHash)))
                    .OrderBy(s => s.Source.SkillInstanceId, StringComparer.Ordinal).Take(1))
            .OrderBy(s => s.Source.OwnerSeat).ThenBy(s => s.Source.SkillInstanceId, StringComparer.Ordinal).ToArray());
    }
    private bool IsBeifenOwnedLoss(CardMovementRecord m, int owner) =>
        m.From.OwnerSeat == owner && m.From.Zone is CardZoneKind.Hand or CardZoneKind.Equipment &&
        !(m.To.OwnerSeat == owner && m.To.Zone is CardZoneKind.Hand or CardZoneKind.Equipment) &&
        HasShuangjiaEntityMark(m.CardId);

    private void CaptureBeifenLossQualifications(CardMovementBatchContext batch, IReadOnlyList<BeifenLossSource> sources)
    {
        if (!HasLeCaiWenJiBeifenCapability || batch.Movements.Count == 0) return;
        foreach (var source in sources)
        foreach (var m in batch.Movements.Where(m => IsBeifenOwnedLoss(m, source.Source.OwnerSeat)))
            AdvanceEventRulesAndQueueFact(new BeifenLossQualifiedEvent(batch.Id, source.Source, source.GameplayHash,
                m.CardId, m.From, m.To, m.Sequence, batch.TurnNumber));
    }

    private bool ExactBeifenLoss(CardMovementBatchContext batch, CardConversionSource source, string hash)
    {
        if (source.SkillId != "le:beifen" || source.BindingId != "lost-hujia-suits" ||
            !IsValidPlayerSeat(source.OwnerSeat) || _contentRegistry.GetSkill(source.SkillId).Program?.GameplayHash != hash) return false;
        var facts = ProgramEventHistory<BeifenLossQualifiedEvent>().Where(e => e.BatchId == batch.Id && e.Source == source).ToArray();
        var actual = batch.Movements.Where(m => IsBeifenOwnedLoss(m, source.OwnerSeat)).ToArray();
        return facts.Length > 0 && facts.Length == actual.Length &&
            facts.Select(e => e.MovementSequence).Distinct().Count() == facts.Length &&
            actual.All(m => facts.Count(e => e.GameplayHash == hash && e.CardId == m.CardId &&
                e.From == m.From && e.To == m.To && e.MovementSequence == m.Sequence && e.ActualTurn == batch.TurnNumber) == 1 &&
                _cardMovements.Count(history => history == m) == 1);
    }

    private IReadOnlyList<ProgramTriggerCandidate> CollectBeifenLossCandidates(CardMovementBatchContext batch) =>
        !HasLeCaiWenJiBeifenCapability ? [] : Array.AsReadOnly(ProgramEventHistory<BeifenLossQualifiedEvent>()
            .Where(e => e.BatchId == batch.Id).Select(e => new BeifenLossSource(e.Source, e.GameplayHash)).Distinct()
            .Where(s => _players[s.Source.OwnerSeat].IsAlive && ExactBeifenLoss(batch, s.Source, s.GameplayHash) &&
                !ProgramEventHistory<BeifenStartedEvent>().Any(e => e.LossBatchId == batch.Id && e.Source == s.Source))
            .Select(s => new ProgramTriggerCandidate(s.Source.OwnerSeat, s.Source.SkillId, s.Source.BindingId,
                s.Source.SkillInstanceId, s.GameplayHash, GetProgramTrigger(new ProgramTriggerCandidate(s.Source.OwnerSeat,
                    s.Source.SkillId, s.Source.BindingId, s.Source.SkillInstanceId, s.GameplayHash, 0)).Priority)).ToArray());
    private ProgramSkillWindowContext CreateBeifenMovementContext(CardsMovedTriggerWindowFrame window,
        ProgramTriggerCandidate candidate) => new(SkillProgramTriggerWindow.CardsMoved, window.Id, candidate.OwnerSeat,
            SourceSeat: candidate.OwnerSeat, TargetSeat: candidate.OwnerSeat,
            Facts: CaptureProgramTriggerFacts(_players[candidate.OwnerSeat]), MovementBatch: window.Batch,
            OccurrenceIndex: candidate.OccurrenceIndex);

    private bool BeifenMotherMatches(ProgramTriggerCandidate c, ProgramSkillWindowContext context) =>
        c.SkillId == "le:beifen" && c.BindingId == "lost-hujia-suits" && c.OccurrenceIndex == 0 &&
        context.Window == SkillProgramTriggerWindow.CardsMoved && context.OwnerSeat == c.OwnerSeat &&
        context.SourceSeat == c.OwnerSeat && context.TargetSeat == c.OwnerSeat && context.OccurrenceIndex == 0 &&
        context.MovementBatch is { } batch && context.ParentFrameId == batch.Id &&
        _resolutionStack.SingleOrDefault(f => f.Id == batch.Id) is CardsMovedTriggerWindowFrame window &&
        window.Batch == batch && window.CandidateIndex >= 0 && window.CandidateIndex < window.Candidates.Count &&
        window.Candidates[window.CandidateIndex] == c &&
        ExactBeifenLoss(batch, new(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId), c.GameplayHash);
    private bool IsIssuedBeifenCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext context) =>
        HasLeCaiWenJiBeifenCapability && IsValidPlayerSeat(c.OwnerSeat) && _players[c.OwnerSeat].IsAlive &&
        BeifenMotherMatches(c, context) && !ProgramEventHistory<BeifenStartedEvent>()
            .Any(e => e.LossBatchId == context.ParentFrameId && e.Source == new CardConversionSource(c.SkillId,
                c.BindingId, c.OwnerSeat, c.SkillInstanceId));

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 28420,
        SkillProgramEffectOp.ResolveBeifenLostMarkedSuitGains)]
    private bool CanOfferBeifen(ProgramTriggerLegalityContext gate) => IsIssuedBeifenCandidate(gate.Candidate, gate.Context);

    [ProgramCapability]
    private sealed class LeCaiWenJiBeifenCapability(GameEngine engine) : ILeCaiWenJiBeifenHost
    { public SkillProgramStepOutcome Resolve(ProgramSkillFrame frame) => engine.BeginBeifen(frame); }

    private SkillProgramStepOutcome BeginBeifen(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.Beifen is not null) throw new InvalidOperationException("Beifen cannot issue the same loss obtain twice.");
        if (f.WindowContext is not { } context || f.InstructionIndex != 1 || f.SelectedCardIds.Count != 0 ||
            f.SelectedTargetSeats.Count != 0 || f.ActivationId != "lost-hujia-suits" || f.TriggerId != "lost-hujia-suits")
            throw new InvalidOperationException("Beifen requires its actual frozen original loss window.");
        var candidate = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId,
            f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex);
        if (!IsIssuedBeifenCandidate(candidate, context))
            throw new InvalidOperationException("Beifen lost its exact original source and marked HE loss qualification.");
        var hand = GetHand(_players[f.OwnerSeat]).Where(c => HasShuangjiaEntityMark(c.Id)).ToArray();
        var heldSuits = hand.Select(c => EffectiveSuit(_players[f.OwnerSeat], c)).ToHashSet();
        var search = BeifenStandardSuits.Where(s => !heldSuits.Contains(s)).Select(s =>
            new BeifenSearchedEntity(s, _cardZones.CardsAt(CardLocation.DrawPile)
                .FirstOrDefault(c => c.Suit == s && !c.IsGeneralWeapon)?.Id)).ToArray();
        var r = new BeifenReceipt(BeifenSource(f), f.GameplayHash, context.ParentFrameId,
            f.InstructionIndex, hand.Length, _movementSequence)
        { Stage = BeifenStage.ObtainChildren, SequenceAfter = _movementSequence, Search = search };
        ReplaceRuntimeTop(f = f with { Beifen = r, PendingMovementContinuation = new(f.OwnerSeat, 0, null) });
        AdvanceEventRulesAndQueueFact(new BeifenStartedEvent(f.Id, r.Source, r.GameplayHash, r.LossBatchId,
            r.MarkedHandCount, search.Length, r.SequenceBefore));
        foreach (var entity in search) AdvanceEventRulesAndQueueFact(new BeifenSuitSearchedEvent(f.Id, entity.Suit, entity.CardId));
        var ids = search.Where(e => e.CardId is not null).Select(e => e.CardId!.Value).ToArray();
        if (ids.Length > 0 && _players[f.OwnerSeat].IsAlive)
            MoveProgramCardsFromMultipleSources(ids, CardLocation.Hand(f.OwnerSeat), new(BeifenObtainReason), (id, movements) =>
            {
                var current = GetActiveProgramFrame(f.Id);
                ReplaceRuntimeTop(current with { Beifen = current.Beifen! with
                    { ObtainBatchId = id, SequenceAfter = movements[^1].Sequence } });
                foreach (var movement in movements)
                    AdvanceEventRulesAndQueueFact(new BeifenObtainedEvent(f.Id, movement.CardId, movement.CardKind,
                        search.Single(e => e.CardId == movement.CardId).Suit, movement.Sequence, id));
            });
        ContinueBeifen(f.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private void ContinueBeifen(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Beifen: not null } f || f.Id != id ||
            _pendingDecision is not null) return;
        AssertBeifen(f);
        if (new ProgramSkillHost(this).TryStartPostInstructionWindow(id)) return;
        f = GetActiveProgramFrame(id);
        AdvanceEventRulesAndQueueFact(new BeifenCompletedEvent(id, f.Beifen!.LossBatchId,
            ProgramEventHistory<BeifenObtainedEvent>().Count(e => e.FrameId == id)));
        ReplaceRuntimeTop(f = f with { PendingMovementContinuation = null });
        FinishProgramSkill(f, true);
    }

    [ProgramContinuation(ProgramContinuationKind.Resume, 28420)]
    private bool ResumeBeifen(long id)
    {
        if (_resolutionStack.LastOrDefault() is not ProgramSkillFrame { Beifen: not null } f || f.Id != id) return false;
        ContinueBeifen(id); return true;
    }
    [ProgramContinuation(ProgramContinuationKind.Movement, 28420)]
    private bool ReturnBeifenMovement(ProgramSkillFrame f)
    { if (f.Beifen is null) return false; ContinueBeifen(f.Id); return true; }

    [ProgramRuntimeHook(ProgramRuntimePhase.AfterSkillLoss, 28420)]
    [ProgramRuntimeHook(ProgramRuntimePhase.AfterOwnerDeath, 28420)]
    private bool ContinueIssuedBeifen(ProgramSkillFrame f)
    {
        if (f.SkillId != "le:beifen" || f.TriggerId != "lost-hujia-suits" || f.ActivationId != f.TriggerId || f.WindowContext is not { } context ||
            f.InstructionIndex is < 0 or > 1 || f.SelectedCardIds.Count != 0 || f.SelectedTargetSeats.Count != 0) return false;
        var c = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId, f.SkillInstanceId,
            f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex);
        if (!BeifenMotherMatches(c, context)) return false;
        return f.Beifen is not null ? ValidBeifenReceipt(f) : IsIssuedBeifenCandidate(c, context);
    }

    private bool ValidBeifenReceipt(ProgramSkillFrame f)
    {
        if (f.Beifen is not { } r || f.SkillId != "le:beifen" || f.TriggerId != "lost-hujia-suits" ||
            f.ActivationId != f.TriggerId || _contentRegistry.GetSkill(f.SkillId).Program?.GameplayHash != f.GameplayHash ||
            r.Source != BeifenSource(f) || r.GameplayHash != f.GameplayHash ||
            r.InstructionIndex != f.InstructionIndex || f.InstructionIndex != 1 || r.Stage != BeifenStage.ObtainChildren ||
            f.WindowContext?.ParentFrameId != r.LossBatchId || r.SequenceAfter < r.SequenceBefore ||
            r.SequenceAfter > _movementSequence || r.Search is not System.Collections.IList { IsReadOnly: true } ||
            r.Search.Count > 4 || r.Search.Select(e => e.Suit).Distinct().Count() != r.Search.Count ||
            r.Search.Any(e => !BeifenStandardSuits.Contains(e.Suit)) ||
            ProgramEventHistory<BeifenStartedEvent>().Where(e => e.FrameId == f.Id).ToArray() is not [var started] ||
            started.Source != r.Source || started.GameplayHash != r.GameplayHash || started.LossBatchId != r.LossBatchId ||
            started.MarkedHandCount != r.MarkedHandCount || started.MissingSuitCount != r.Search.Count || started.SequenceBefore != r.SequenceBefore ||
            !ProgramEventHistory<BeifenSuitSearchedEvent>().Where(e => e.FrameId == f.Id)
                .Select(e => new BeifenSearchedEntity(e.Suit, e.CardId)).SequenceEqual(r.Search)) return false;
        var obtained = ProgramEventHistory<BeifenObtainedEvent>().Where(e => e.FrameId == f.Id).ToArray();
        if (r.ObtainBatchId == 0) return obtained.Length == 0 && r.SequenceAfter == r.SequenceBefore;
        var chosen = r.Search.Where(e => e.CardId is not null).ToArray();
        return obtained.Length == chosen.Length && obtained.Select(e => e.CardId).Distinct().Count() == chosen.Length &&
            chosen.All(e => obtained.Count(o => o.CardId == e.CardId && o.Suit == e.Suit) == 1) &&
            obtained.All(e => e.BatchId == r.ObtainBatchId && e.MovementSequence > r.SequenceBefore && e.MovementSequence <= r.SequenceAfter &&
                _cardMovements.Count(m => m.Sequence == e.MovementSequence && m.CardId == e.CardId && m.CardKind == e.PrintedKind &&
                    m.From == CardLocation.DrawPile && m.To == CardLocation.Hand(f.OwnerSeat) && m.Reason.Value == BeifenObtainReason) == 1);
    }

    private bool IsBeifenMovement(ProgramSkillFrame f, SkillProgramEffect? effect,
        ProgramMovementContinuation pending)
    {
        if (effect is not { Op: SkillProgramEffectOp.ResolveBeifenLostMarkedSuitGains,
                Target: SkillProgramEffectTarget.Owner, Condition.Kind: SkillProgramConditionKind.Always } ||
            f.Beifen is not { Stage: BeifenStage.ObtainChildren, ObtainBatchId: > 0 } r ||
            f.WindowContext is not { } context || !ValidBeifenReceipt(f) ||
            pending != f.PendingMovementContinuation || pending.SubjectSeat != f.OwnerSeat ||
            pending.BeforeCount != 0 || pending.CoverageResultBind is not null)
            return false;
        var index = _resolutionStack.FindIndex(frame => frame.Id == f.Id);
        var candidate = new ProgramTriggerCandidate(f.OwnerSeat, f.SkillId, f.TriggerId!, f.SkillInstanceId,
            f.GameplayHash, GetProgramTrigger(f).Priority, context.OccurrenceIndex);
        if (index < 1 || _resolutionStack[index - 1].Id != r.LossBatchId ||
            !BeifenMotherMatches(candidate, context)) return false;
        var paid = ProgramEventHistory<BeifenObtainedEvent>().Where(e => e.FrameId == f.Id)
            .OrderBy(e => e.MovementSequence).ToArray();
        if (paid.Length == 0 || paid[0].MovementSequence != r.SequenceBefore + 1 ||
            paid[^1].MovementSequence != r.SequenceAfter ||
            paid.Where((e, i) => e.MovementSequence != r.SequenceBefore + i + 1).Any()) return false;
        bool ExactBatch(CardMovementBatchContext batch) => batch.Id == r.ObtainBatchId &&
            batch.ParentFrameId == f.Id && batch.AwaitingProgramFrameId == f.Id &&
            batch.OriginOwnerSeat == f.OwnerSeat && batch.OriginSkillId == f.SkillId &&
            batch.OriginSkillInstanceId == f.SkillInstanceId && batch.Movements.Count == paid.Length &&
            batch.Movements.Zip(paid).All(pair => pair.First.Sequence == pair.Second.MovementSequence &&
                pair.First.CardId == pair.Second.CardId && pair.First.CardKind == pair.Second.PrintedKind &&
                pair.First.From == CardLocation.DrawPile && pair.First.To == CardLocation.Hand(f.OwnerSeat) &&
                pair.First.Reason.Value == BeifenObtainReason);
        // While the exact child is pending or live, verify its actual producer
        // batch as well. After that child returns, the same scalar payment
        // invoice remains sufficient while later native children drain.
        return _pendingCardsMovedBatches.Where(b => b.Id == r.ObtainBatchId).All(ExactBatch) &&
            _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().Where(w => w.Id == r.ObtainBatchId)
                .All(w => ExactBatch(w.Batch) && ProgramMovementReturnMatches(f, w));
    }

    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 28420)]
    private void AssertBeifen(ProgramSkillFrame f)
    {
        if (f.Beifen is not null && !ValidBeifenReceipt(f))
            throw new InvalidOperationException("Beifen lost its original loss qualification, fresh suit search or once-paid real obtains.");
    }
}
