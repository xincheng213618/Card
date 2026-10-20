namespace CardGame.Core;

public sealed partial record ProgramSkillFrame
{
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public PanShuZhirenFieldState? PanShuZhirenField { get; init; }
}

// The two 织纴 field-discarding choices ride the owning program frame: stage one
// asks for one field equipment, stage two for one field delayed trick; each
// stage may be skipped ("至多").
public sealed record PanShuZhirenFieldState(bool AwaitingDelayedTrick, int NameLength);

public sealed partial class GameEngine
{

    // Committed actual uses by the owner since this turn began whose effective
    // kind still is the paid entity's own kind (no conversion declaration).
    private int CountNonConvertedActualUsesThisTurn(int ownerSeat) =>
        EventsSinceLastBoundary(item => item is TurnStartedEvent or TurnEndedEvent)
            .OfType<CardActionAcceptedEvent>()
            .Count(item => item.Action.Type == CardActionType.Use &&
                item.Action.ActorSeat == ownerSeat &&
                item.Action.ConversionChain.Count == 0 &&
                item.Action.PhysicalCards.Count > 0 &&
                item.Action.PhysicalCards.All(cost => cost.CardKind == item.Action.EffectiveKind));

    private static bool IsOtherLastHandLossTrigger(SkillProgramTrigger trigger) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_pan_shu_ol_PanShuPrograms.IsOtherLastHandLossTrigger(trigger);

    // 燕尔 candidates: a living character's last hand card leaving their hand
    // during their own play phase; the observing owner is decoupled from the
    // mover, so the skill holder sees every other character's last-hand loss.
    private IEnumerable<ProgramTriggerCandidate> CollectOtherLastHandLossCandidates(
        CardMovementBatchContext batch)
    {
        foreach (var owner in _players.Where(player => player.IsAlive))
        foreach (var candidate in CollectProgramTriggerCandidates(owner, SkillProgramTriggerWindow.CardsMoved))
        {
            var trigger = GetProgramTrigger(candidate);
            if (!IsOtherLastHandLossTrigger(trigger)) continue;
            var indexes = batch.Movements.Select((movement, index) => (movement, index))
                .Where(item => item.movement.From.Zone == CardZoneKind.Hand &&
                    item.movement.From.OwnerSeat is { } source &&
                    source != candidate.OwnerSeat && source != item.movement.To.OwnerSeat &&
                    IsValidPlayerSeat(source) && _players[source].IsAlive &&
                    _phase == TurnPhase.Play && _currentSeat == source &&
                    LostLastHandCard(batch, source))
                .Select(item => item.index).ToArray();
            foreach (var index in indexes)
            {
                var sourceCount = ProgramMovementSourceCounts(batch, SkillProgramTriggerWindow.CardsMoved)
                    .FirstOrDefault(item => item.Count.Location == batch.Movements[index].From).Count;
                var facts = CaptureCardsMovedTriggerFacts(owner, 1,
                    sourceCount ?? new CardMovementSourceCount(batch.Movements[index].From, 0, 1),
                    SkillProgramTriggerWindow.CardsMoved, batch.MovementTiming);
                if (trigger.Condition.Evaluate(facts, candidate.SkillId, candidate.SkillInstanceId))
                    yield return candidate with { OccurrenceIndex = index };
            }
        }
    }

    private static bool LostLastHandCard(CardMovementBatchContext batch, int sourceSeat) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_pan_shu_ol_PanShuPrograms.LostLastHandCard(batch,sourceSeat);

    private IReadOnlySet<SkillProgramCardCategory> CardIdsCategorySet(IReadOnlyList<int> cardIds)
    {
        var categories = new HashSet<SkillProgramCardCategory>();
        foreach (var cardId in cardIds)
        {
            var location = _cardZones.GetLocation(cardId);
            if (location.OwnerSeat is null) continue;
            var card = _cardZones.CardsAt(location).Single(item => item.Id == cardId);
            categories.Add(GetProgramCardCategory(card.Kind));
        }
        return categories;
    }

    // A cross-skill boolean write: the state stays declared and owned by the
    // paired skill instance; only the value change is applied here.
    private void SetForeignProgramBooleanState(ProgramSkillFrame frame, string targetSkillId, string stateId, bool value)
    {
        var instance = GetSkillBindingShard(_players[frame.OwnerSeat]).ProgramInstances
            .SingleOrDefault(item => item.SkillId == targetSkillId) ??
            throw new InvalidOperationException($"The paired program instance '{targetSkillId}' is not installed.");
        var definition = GetProgramBooleanStateDefinition(targetSkillId, stateId);
        var key = new ProgramBooleanStateKey(frame.OwnerSeat, targetSkillId, instance.SkillInstanceId, stateId);
        if (GetProgramBooleanState(frame.OwnerSeat, targetSkillId, instance.SkillInstanceId, stateId) == value) return;
        _programBooleanStates[key] = value;
        if (definition.Visibility == SkillProgramStateVisibility.Public)
            AdvanceEventRulesAndQueueFact(new ProgramBooleanStateChangedEvent(
                frame.OwnerSeat, targetSkillId, instance.SkillInstanceId, stateId, value, definition.Visibility));
    }

    // Shared one-point recovery used by both skills; keeps the standard
    // replacement queue and committed recovery event shape.
    private void ApplyProgramSkillRecovery(long frameId, int sourceSeat, int targetSeat)
    {
        var target = _players[targetSeat];
        if (!target.IsAlive || !target.HasHpValue) return;
        var recovered = Math.Min(1, target.MaxHp - target.Hp);
        if (recovered <= 0) return;
        if (TryQueueRecoveryReplacement(frameId, sourceSeat, targetSeat, recovered,
                new(RecoveryAttemptProducer.Program, GetActiveProgramFrame(frameId).InstructionIndex))) return;
        var recovery = BeginRecovery(frameId, sourceSeat, targetSeat, recovered);
        target.Hp += recovered;
        AdvanceEventRulesAndQueueFact(new RecoveryAppliedEvent(sourceSeat, targetSeat, recovered, target.Hp));
        PopResolutionFrame(recovery, ResolutionFrameKind.Recovery);
    }


    [ProgramCapability]
    private sealed class PanShuProgramCapability(GameEngine engine) : IPanShuProgramHost
    {
        public SkillProgramStepOutcome ZhirenReorderTopByLength(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ZhirenProgramReorderTopByLength(frame, effect);
        public SkillProgramStepOutcome ZhirenResolveFieldTiers(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ZhirenProgramResolveFieldTiers(frame, effect);
        public SkillProgramStepOutcome YanerResolvePairBenefit(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.YanerProgramResolvePairBenefit(frame, effect);
    }
}
