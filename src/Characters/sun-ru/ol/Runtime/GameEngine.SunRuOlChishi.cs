namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string SunRuOlChishiDrawReason = "skill-program.sun-ru.ol.chishi.draw";
    private CardConversionSource SunRuOlSource(ProgramSkillFrame f) => new(f.SkillId, GetProgramBindingId(f), f.OwnerSeat, f.SkillInstanceId);
    [ProgramCapability]
    private sealed class SunRuOlProgramCapability(GameEngine e) : ISunRuOlProgramHost
    {
        public SkillProgramStepOutcome RewardCurrentTurnLastZoneLoss(ProgramSkillFrame f) => e.BeginSunRuOlChishi(f);
        public SkillProgramStepOutcome AbolishEquipmentSlotsForDistinctBenefits(ProgramSkillFrame f) => e.BeginSunRuOlWeimian(f);
    }
    private static bool IsSunRuOlChishiTrigger(SkillProgramTrigger t) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_sun_ru_ol_SunRuOlChishi.IsSunRuOlChishiTrigger(t);
    private bool SunRuOlActualTurn(SunRuOlTurnToken t) => t.TurnNumber > 0 && t.TurnNumber == _turnNumber &&
        t.OwnerSeat == _turnProgression.OwnerSeat && IsValidPlayerSeat(t.OwnerSeat) &&
        ProgramEventHistory<TurnStartedEvent>().Any(e => e.TurnNumber == t.TurnNumber && e.ActorSeat == t.OwnerSeat);
    private static bool SunRuOlLostLastZone(CardMovementBatchContext batch, int seat) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_sun_ru_ol_SunRuOlChishi.SunRuOlLostLastZone(batch,seat);
    private CardMovementBatchContext CaptureSunRuOlChishiBatch(CardMovementBatchContext batch)
    {
        if (!_contentRegistry.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.RewardCurrentTurnLastZoneLoss)) return batch;
        var turn = new SunRuOlTurnToken(batch.TurnNumber, _turnProgression.OwnerSeat);
        return SunRuOlActualTurn(turn) && SunRuOlLostLastZone(batch, turn.OwnerSeat)
            ? batch with { SunRuOlLastZoneLoss = turn } : batch;
    }
    private bool SunRuOlChishiUsed(int owner, string skill, SunRuOlTurnToken turn) =>
        ProgramEventHistory<SunRuOlChishiAppliedEvent>().Any(e => e.Source.OwnerSeat == owner && e.Source.SkillId == skill && e.Turn == turn);
    private IEnumerable<ProgramTriggerCandidate> CollectSunRuOlChishiCandidates(CardMovementBatchContext batch)
    {
        if (batch.SunRuOlLastZoneLoss is not { } turn || !SunRuOlActualTurn(turn) || !_players[turn.OwnerSeat].IsAlive ||
            !SunRuOlLostLastZone(batch, turn.OwnerSeat)) return [];
        return _players.Where(p => p.IsAlive).SelectMany(p => CollectProgramTriggerCandidates(p, SkillProgramTriggerWindow.CardsMoved))
            .Where(c => IsSunRuOlChishiTrigger(GetProgramTrigger(c)) && !SunRuOlChishiUsed(c.OwnerSeat, c.SkillId, turn)).ToArray();
    }
    private ProgramSkillWindowContext CreateSunRuOlChishiContext(CardsMovedTriggerWindowFrame frame, ProgramTriggerCandidate c)
    {
        var actor = frame.Batch.SunRuOlLastZoneLoss?.OwnerSeat ?? throw new InvalidOperationException("Chishi lost its frozen native actual-turn owner.");
        return new(SkillProgramTriggerWindow.CardsMoved, frame.Id, c.OwnerSeat, SourceSeat: actor, TargetSeat: actor,
            Facts: CaptureProgramTriggerFacts(_players[c.OwnerSeat]), MovementBatch: frame.Batch);
    }
    private bool CanRunSunRuOlChishiCandidate(ProgramTriggerCandidate c, ProgramSkillWindowContext context) =>
        context.MovementBatch is { SunRuOlLastZoneLoss: { } turn } batch && SunRuOlActualTurn(turn) &&
        context.Window == SkillProgramTriggerWindow.CardsMoved && context.ParentFrameId == batch.Id && context.OwnerSeat == c.OwnerSeat &&
        context.SourceSeat == turn.OwnerSeat && context.TargetSeat == turn.OwnerSeat && _players[turn.OwnerSeat].IsAlive &&
        SunRuOlLostLastZone(batch, turn.OwnerSeat) && !SunRuOlChishiUsed(c.OwnerSeat, c.SkillId, turn);

    private SkillProgramStepOutcome BeginSunRuOlChishi(ProgramSkillFrame supplied)
    {
        var f = GetActiveProgramFrame(supplied.Id);
        if (f.SunRuOlChishi is not null || f.InstructionIndex != 1 || f.TriggerId is null ||
            f.WindowContext is not { MovementBatch.SunRuOlLastZoneLoss: { } turn } context ||
            _resolutionStack.OfType<CardsMovedTriggerWindowFrame>().SingleOrDefault(native => native.Id == context.ParentFrameId) is not { } w ||
            w.CandidateIndex >= w.Candidates.Count || !MountObserverCandidateMatches(f, w.Candidates[w.CandidateIndex]) ||
            !CanRunSunRuOlChishiCandidate(w.Candidates[w.CandidateIndex], context))
            throw new InvalidOperationException("Chishi requires one real last-zone loss of the actual current-turn actor.");
        var before = _movementSequence;
        ReplaceRuntimeTop(f = f with { SunRuOlChishi = new(SunRuOlSource(f), f.GameplayHash, f.InstructionIndex, w.Id, turn, before, before) });
        AdvanceEventRulesAndQueueFact(new SunRuOlChishiAppliedEvent(f.Id, SunRuOlSource(f), w.Id, turn));
        var granted = _turnCardUseEffects.GrantRuleModifier(turn.TurnNumber, turn.OwnerSeat, f.Id, f.InstructionIndex - 1,
            CreateProgramTurnEffectSource(f), SkillRuleQuery.HandLimit, SkillRuleOperation.Add, 2, affectedSeat: turn.OwnerSeat);
        AdvanceEventRulesAndQueueFact(new TurnRuleModifierGrantedEvent(granted));
        DrawCards(_players[turn.OwnerSeat], 2, true, new(SunRuOlChishiDrawReason));
        f = GetActiveProgramFrame(f.Id);
        ReplaceRuntimeTop(f with { SunRuOlChishi = f.SunRuOlChishi! with { SequenceAfter = _movementSequence } });
        ContinueSunRuOl(f.Id); return SkillProgramStepOutcome.AwaitChild;
    }
}
