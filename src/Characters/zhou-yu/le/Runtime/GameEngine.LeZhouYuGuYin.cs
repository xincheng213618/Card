namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string GuYinDrawReason = "skill-program.le-zhou-yu.guyin.draw";
    private bool HasGuYinCapability => _contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.InitializeGuYinInitialEntities);
    private static CardConversionSource GuYinSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    private static bool IsGuYinDiscardTrigger(SkillProgramTrigger t) => t.Effects is [{ Op: SkillProgramEffectOp.ResolveGuYinInitialEntityDiscardDraw }];

    private int ApplyGuYinInitialHandSize(CharacterState player, int ordinaryCount)
    {
        if (!HasGuYinCapability) return ordinaryCount;
        var sources = _players.SelectMany(p => GetSkillBindingShard(p).ProgramInstances
            .Where(i => i.SkillId == "le:guyin").Select(i => p.Seat)).ToArray();
        return sources.Contains(player.Seat) ? 0 : (int)Math.Min(int.MaxValue, (long)ordinaryCount + sources.Length);
    }

    private void CaptureGuYinOpeningOrigins()
    {
        if (!HasGuYinCapability) return;
        if (ProgramEventHistory<GuYinOpeningCapturedEvent>().Any())
            throw new InvalidOperationException("GuYin global opening identities are captured only once.");
        var entities = _players.SelectMany(p => GetHand(p).Select(c =>
        {
            var entry = _cardMovements.LastOrDefault(m => m.CardId == c.Id && m.To == CardLocation.Hand(p.Seat));
            if (entry is null || entry.CardKind != c.Kind) throw new InvalidOperationException("GuYin opening entity needs its actual final opening-Hand entry.");
            return new GuYinInitialEntity(p.Seat, c.Id, c.Kind, entry.Sequence);
        })).ToArray();
        var id = ++_resolutionSequence;
        AdvanceEventRulesAndQueueFact(new GuYinOpeningCapturedEvent(id, entities.Length));
        foreach (var e in entities) AdvanceEventRulesAndQueueFact(new GuYinOpeningEntityEvent(id,
            e.OriginalOwnerSeat, e.CardId, e.PrintedKind, e.EntrySequence));
    }

    private ProgramLifecycleTriggerWindowFrame CaptureGuYinInitialEntities(ProgramLifecycleTriggerWindowFrame window)
    {
        if (!HasGuYinCapability || window.Window != SkillProgramTriggerWindow.GameStarting ||
            window.Continuation != ProgramLifecycleContinuation.CompleteGameStarting) return window;
        if (window.GuYinInitialEntities is not null) throw new InvalidOperationException("GuYin opening entities are frozen once.");
        return window with { GuYinInitialEntities = GuYinOriginalEntities() };
    }

    private bool IsIssuedGuYinInitialCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext context) =>
        HasGuYinCapability && c.SkillId == "le:guyin" && c.BindingId == "mark-initial-entities" &&
        IsValidPlayerSeat(c.OwnerSeat) && _contentRegistry.GetSkill(c.SkillId).Program?.GameplayHash == c.GameplayHash &&
        GetProgramTrigger(c).Effects is [{ Op: SkillProgramEffectOp.InitializeGuYinInitialEntities }] &&
        context.Window == SkillProgramTriggerWindow.GameStarting && context.OwnerSeat == c.OwnerSeat &&
        _resolutionStack.SingleOrDefault(f => f.Id == context.ParentFrameId) is ProgramLifecycleTriggerWindowFrame
            { Window: SkillProgramTriggerWindow.GameStarting, Continuation: ProgramLifecycleContinuation.CompleteGameStarting, GuYinInitialEntities: not null } parent &&
        parent.CandidateIndex >= 0 && parent.CandidateIndex < parent.Candidates.Count && parent.Candidates[parent.CandidateIndex] == c &&
        context.SourceSeat == parent.OwnerSeat && context.TargetSeat == parent.OwnerSeat &&
        !ProgramEventHistory<GuYinInitializedEvent>().Any(e => e.Source.OwnerSeat == c.OwnerSeat);

    [ProgramCapability]
    private sealed class LeZhouYuGuYinCapability(GameEngine engine) : ILeZhouYuGuYinHost
    {
        public SkillProgramStepOutcome Initialize(ProgramSkillFrame f) => engine.InitializeGuYin(f);
        public SkillProgramStepOutcome Draw(ProgramSkillFrame f) => engine.BeginGuYinDraw(f);
    }

    private SkillProgramStepOutcome InitializeGuYin(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.InstructionIndex != 1 || !CanContinueIssuedGuYin(f) ||
            ProgramEventHistory<GuYinInitializedEvent>().Any(e => e.Source.OwnerSeat == f.OwnerSeat))
            throw new InvalidOperationException("GuYin initialization needs its exact frozen native opening mother.");
        var parent = (ProgramLifecycleTriggerWindowFrame)_resolutionStack.Single(x => x.Id == f.WindowContext!.ParentFrameId);
        var entities = parent.GuYinInitialEntities!.Where(e => e.OriginalOwnerSeat != f.OwnerSeat).ToArray();
        if (entities.Select(e => e.CardId).Distinct().Count() != entities.Length || entities.Any(e => !ValidGuYinInitialEntry(e)))
            throw new InvalidOperationException("GuYin opening entities lost their genuine entry invoices.");
        AdvanceEventRulesAndQueueFact(new GuYinInitializedEvent(f.Id, GuYinSource(f), f.GameplayHash, entities.Length));
        foreach (var e in entities) AdvanceEventRulesAndQueueFact(new GuYinInitialEntityRegisteredEvent(f.Id,
            f.OwnerSeat, e.OriginalOwnerSeat, e.CardId, e.PrintedKind, e.EntrySequence));
        return SkillProgramStepOutcome.Continue;
    }

    private bool ValidGuYinInitialEntry(GuYinInitialEntity e) => IsValidPlayerSeat(e.OriginalOwnerSeat) &&
        _cardMovements.Count(m => m.Sequence == e.EntrySequence && m.CardId == e.CardId &&
            m.CardKind == e.PrintedKind && m.To == CardLocation.Hand(e.OriginalOwnerSeat)) == 1;

    private IReadOnlyList<GuYinInitialEntity> GuYinOriginalEntities()
    {
        if (ProgramEventHistory<GuYinOpeningCapturedEvent>().ToArray() is not [var captured]) return [];
        var entities = ProgramEventHistory<GuYinOpeningEntityEvent>().Where(e => e.SnapshotId == captured.SnapshotId)
            .Select(e => new GuYinInitialEntity(e.OriginalOwnerSeat, e.CardId, e.PrintedKind, e.EntrySequence)).ToArray();
        if (entities.Length != captured.EntityCount || entities.Select(e => e.CardId).Distinct().Count() != entities.Length ||
            entities.Any(e => !ValidGuYinInitialEntry(e))) throw new InvalidOperationException("GuYin opening registry lost its original immutable entry invoices.");
        return Array.AsReadOnly(entities);
    }

    private IReadOnlyList<GuYinSourceSnapshot> CaptureGuYinSources() => !HasGuYinCapability || !_setupComplete ? [] :
        Array.AsReadOnly(_players.Where(p => p.IsAlive).SelectMany(p => GetSkillBindingShard(p).ProgramInstances
            .Where(i => i.SkillId == "le:guyin").SelectMany(i => i.Program.Triggers.Where(IsGuYinDiscardTrigger)
                .Select(t => new GuYinSourceSnapshot(new(i.SkillId, t.Id, p.Seat, i.SkillInstanceId), i.Program.GameplayHash)))
            .OrderBy(s => s.Source.SkillInstanceId, StringComparer.Ordinal).Take(1))
            .OrderBy(s => (s.Source.OwnerSeat - _currentSeat + _playerCount) % _playerCount).ToArray());

    private void CaptureGuYinDiscardBatch(CardMovementBatchContext batch, IReadOnlyList<GuYinSourceSnapshot> sources)
    {
        if (!HasGuYinCapability || sources.Count == 0) return;
        var initial = GuYinOriginalEntities();
        foreach (var m in batch.Movements.Where(m => m.To == CardLocation.DiscardPile &&
            (GetProgramDiscardSource(m) is not null || IsNativeTrueUseDiscardMovement(batch, m))))
        {
            var origin = initial.SingleOrDefault(e => e.CardId == m.CardId && e.PrintedKind == m.CardKind);
            if (origin is null || !ValidGuYinInitialEntry(origin)) continue;
            foreach (var s in sources.Where(s => s.Source.OwnerSeat != origin.OriginalOwnerSeat))
                AdvanceEventRulesAndQueueFact(new GuYinDiscardQualifiedEvent(batch.Id, s.Source, s.GameplayHash,
                    origin.OriginalOwnerSeat, m.CardId, m.CardKind, m.Sequence, batch.TurnNumber,
                    batch.MovementTiming?.ActualTurnOwnerSeat ?? _turnProgression.OwnerSeat));
        }
    }

    private GuYinDiscardQualifiedEvent[] ExactGuYinQualifications(CardMovementBatchContext batch, CardConversionSource source, string hash)
    {
        if (source.SkillId != "le:guyin" || source.BindingId != "initial-entity-discard-draw" ||
            !IsValidPlayerSeat(source.OwnerSeat) || _contentRegistry.GetSkill(source.SkillId).Program?.GameplayHash != hash) return [];
        var facts = ProgramEventHistory<GuYinDiscardQualifiedEvent>().Where(e => e.BatchId == batch.Id && e.Source == source)
            .OrderBy(e => e.MovementSequence).ToArray();
        if (facts.Length == 0 || facts.Select(e => e.MovementSequence).Distinct().Count() != facts.Length ||
            facts.Any(e => e.GameplayHash != hash || e.ActualTurn != batch.TurnNumber || e.OriginalOwnerSeat == source.OwnerSeat ||
                !GuYinOriginalEntities().Any(o => o.OriginalOwnerSeat == e.OriginalOwnerSeat && o.CardId == e.CardId &&
                    o.PrintedKind == e.PrintedKind && ValidGuYinInitialEntry(o)) ||
                batch.Movements.Count(m => m.Sequence == e.MovementSequence && m.CardId == e.CardId &&
                    m.CardKind == e.PrintedKind && m.To == CardLocation.DiscardPile && _cardMovements.Count(x => x == m) == 1) != 1)) return [];
        return facts;
    }

    private IReadOnlyList<ProgramTriggerCandidate> CollectGuYinDiscardCandidates(CardMovementBatchContext batch) =>
        !HasGuYinCapability ? [] : Array.AsReadOnly(ProgramEventHistory<GuYinDiscardQualifiedEvent>()
            .Where(e => e.BatchId == batch.Id).Select(e => new GuYinSourceSnapshot(e.Source, e.GameplayHash)).Distinct()
            .Where(s => _players[s.Source.OwnerSeat].IsAlive && ExactGuYinQualifications(batch, s.Source, s.GameplayHash).Length > 0 &&
                !ProgramEventHistory<GuYinDrawStartedEvent>().Any(e => e.BatchId == batch.Id && e.Source == s.Source))
            .Select(s => new ProgramTriggerCandidate(s.Source.OwnerSeat, s.Source.SkillId, s.Source.BindingId,
                s.Source.SkillInstanceId, s.GameplayHash, GetProgramTrigger(new ProgramTriggerCandidate(s.Source.OwnerSeat,
                    s.Source.SkillId, s.Source.BindingId, s.Source.SkillInstanceId, s.GameplayHash, 0)).Priority)).ToArray());

    private ProgramSkillWindowContext CreateGuYinMovementContext(CardsMovedTriggerWindowFrame window, ProgramTriggerCandidate c) =>
        new(SkillProgramTriggerWindow.CardsMoved, window.Id, c.OwnerSeat, SourceSeat: c.OwnerSeat, TargetSeat: c.OwnerSeat,
            Facts: CaptureProgramTriggerFacts(_players[c.OwnerSeat]), MovementBatch: window.Batch, OccurrenceIndex: c.OccurrenceIndex);

    private bool GuYinMotherMatches(ProgramTriggerCandidate c, ProgramSkillWindowContext context) =>
        c.SkillId == "le:guyin" && c.BindingId == "initial-entity-discard-draw" && c.OccurrenceIndex == 0 &&
        context.Window == SkillProgramTriggerWindow.CardsMoved && context.OwnerSeat == c.OwnerSeat &&
        context.SourceSeat == c.OwnerSeat && context.TargetSeat == c.OwnerSeat && context.OccurrenceIndex == 0 &&
        context.MovementBatch is { } batch && context.ParentFrameId == batch.Id &&
        _resolutionStack.SingleOrDefault(f => f.Id == batch.Id) is CardsMovedTriggerWindowFrame window && window.Batch == batch &&
        window.CandidateIndex >= 0 && window.CandidateIndex < window.Candidates.Count && window.Candidates[window.CandidateIndex] == c &&
        ExactGuYinQualifications(batch, new(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId), c.GameplayHash).Length > 0;

    private bool IsIssuedGuYinCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext context) =>
        IsIssuedGuYinInitialCandidate(c, context) || HasGuYinCapability && IsValidPlayerSeat(c.OwnerSeat) &&
        _players[c.OwnerSeat].IsAlive && GuYinMotherMatches(c, context) && !ProgramEventHistory<GuYinDrawStartedEvent>()
            .Any(e => e.BatchId == context.ParentFrameId && e.Source == new CardConversionSource(c.SkillId, c.BindingId, c.OwnerSeat, c.SkillInstanceId));

    [ProgramTriggerLegality(ProgramTriggerLegalityPhase.BeforeGainQualification, 28810,
        SkillProgramEffectOp.InitializeGuYinInitialEntities, SkillProgramEffectOp.ResolveGuYinInitialEntityDiscardDraw)]
    private bool CanOfferGuYin(ProgramTriggerLegalityContext g) => IsIssuedGuYinCandidate(g.Candidate, g.Context);
}
