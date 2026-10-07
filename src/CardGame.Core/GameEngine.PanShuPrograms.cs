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
    // 织纴: X is the character count of the used card's printed Chinese name; the
    // trigger gate guarantees the effective kind is the entity's own kind.
    private int ZhirenNameLength(ProgramSkillFrame frame) =>
        frame.WindowContext?.CardUse is { } use
            ? CardCatalog.Get(use.EffectiveKind).DisplayName.Length
            : throw new InvalidOperationException("The zhiren tiers lost their current card use.");

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
        trigger.Window == SkillProgramTriggerWindow.CardsMoved &&
        trigger.MovementOccurrence == SkillProgramMovementOccurrence.PerOtherLastHandLoss;

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

    private static bool LostLastHandCard(CardMovementBatchContext batch, int sourceSeat)
    {
        var location = CardLocation.Hand(sourceSeat);
        var count = batch.SourceCounts.FirstOrDefault(item => item.Location == location);
        return count is not null && count.CountBefore > 0 && count.CountAfter == 0;
    }

    // 织纴 tier 1: view the top X cards of the draw pile and arrange them back
    // over the top and bottom in any order. The modification granted by 燕尔 may
    // suspend the own-turn gate, so the reorder cannot require the current seat.
    private SkillProgramStepOutcome ZhirenProgramReorderTopByLength(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var length = ZhirenNameLength(active);
        return BeginProgramTopReorder(active, length, null, requireCurrentSeat: false);
    }

    // 织纴 tiers 2-4: field discards are prompted one stage at a time; recovery
    // and the three-card draw settle without prompts.
    private SkillProgramStepOutcome ZhirenProgramResolveFieldTiers(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var length = ZhirenNameLength(active);
        if (length < 2) return ContinueZhirenTiers(active, length, skippedDiscards: true);
        if (ZhirenFieldEquipmentCandidates().Count > 0)
        {
            ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with { PanShuZhirenField = new(false, length) });
            PresentZhirenFieldPrompt(GetActiveProgramFrame(active.Id), delayedTrick: false);
            return SkillProgramStepOutcome.AwaitChoice;
        }
        if (ZhirenFieldDelayedCandidates().Count > 0)
        {
            ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with { PanShuZhirenField = new(true, length) });
            PresentZhirenFieldPrompt(GetActiveProgramFrame(active.Id), delayedTrick: true);
            return SkillProgramStepOutcome.AwaitChoice;
        }
        return ContinueZhirenTiers(active, length, skippedDiscards: true);
    }

    // Resumption path after the first stage: the choice resolver re-enters here
    // through the frame state instead of the paused instruction cursor.
    private void ContinueZhirenFieldAfterEquipment(ProgramSkillFrame frame)
    {
        if (ZhirenFieldDelayedCandidates().Count > 0)
        {
            ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with
            {
                PanShuZhirenField = GetActiveProgramFrame(frame.Id).PanShuZhirenField! with { AwaitingDelayedTrick = true }
            });
            PresentZhirenFieldPrompt(GetActiveProgramFrame(frame.Id), delayedTrick: true);
            return;
        }
        var active = GetActiveProgramFrame(frame.Id);
        var length = active.PanShuZhirenField!.NameLength;
        _ = ContinueZhirenTiers(active, length, skippedDiscards: false);
    }

    private SkillProgramStepOutcome ContinueZhirenTiers(ProgramSkillFrame active, int length, bool skippedDiscards)
    {
        ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with { PanShuZhirenField = null });
        active = GetActiveProgramFrame(active.Id);
        if (length >= 3)
            ApplyProgramSkillRecovery(active.Id, active.OwnerSeat, active.OwnerSeat);
        if (length >= 4)
            DrawProgramCards(active.Id, active.OwnerSeat, 3, null, null,
                SkillProgramCardSetVisibility.Private,
                new($"skill-program.{active.SkillId}.zhiren-tiers"));
        AddLog("SkillTriggered",
            $"{_players[active.OwnerSeat].Name} 的【织纴】按牌名字数 {length} 结算" +
            (skippedDiscards ? "（未弃置场上牌）。" : "。"), active.OwnerSeat);
        AdvanceRuntimeProgram(active.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private IReadOnlyList<Card> ZhirenFieldEquipmentCandidates() => _players
        .Where(player => player.IsAlive)
        .SelectMany(player => GetEquipment(player).Select(card => (Player: player, Card: card)))
        .OrderBy(item => item.Player.Seat)
        .ThenBy(item => item.Card.Id)
        .Select(item => item.Card)
        .ToArray();

    private IReadOnlyList<Card> ZhirenFieldDelayedCandidates() => _players
        .Where(player => player.IsAlive)
        .SelectMany(player => GetJudgment(player).Select(card => (Player: player, Card: card)))
        .OrderBy(item => item.Player.Seat)
        .ThenBy(item => item.Card.Id)
        .Select(item => item.Card)
        .ToArray();

    private void PresentZhirenFieldPrompt(ProgramSkillFrame frame, bool delayedTrick)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var candidates = delayedTrick ? ZhirenFieldDelayedCandidates() : ZhirenFieldEquipmentCandidates();
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = candidates.Select(card =>
        {
            var holder = _cardZones.GetLocation(card.Id);
            var holderName = holder.OwnerSeat is { } seat ? _players[seat].Name : "";
            return new PromptChoice(
                new ChoiceId($"zhiren-field.frame-{frame.Id}.card-{card.Id}"),
                $"弃置 {holderName} 的【{card.DisplayName}】。",
                [card.Id], [],
                new Dictionary<string, string>
                {
                    ["program-action"] = "zhiren-field-discard",
                    ["stage"] = delayedTrick ? "delayed" : "equipment",
                    ["card-id"] = card.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
                });
        }).ToList();
        choices.Add(new PromptChoice(
            new ChoiceId($"zhiren-field.frame-{frame.Id}.skip-{(delayedTrick ? "delayed" : "equipment")}"),
            delayedTrick ? "不弃置延时锦囊牌。" : "不弃置装备牌。",
            [], [],
            new Dictionary<string, string>
            {
                ["program-action"] = "zhiren-field-discard",
                ["stage"] = delayedTrick ? "delayed" : "equipment",
                ["action"] = "skip"
            }));
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            delayedTrick
                ? $"【{presentation.Name}】可弃置场上的一张延时锦囊牌。"
                : $"【{presentation.Name}】可弃置场上的一张装备牌。",
            candidates.Select(card => card.Id).ToArray(), [], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · {(delayedTrick ? "延时锦囊" : "装备")}弃置",
                $"此牌牌名字数为 {active.PanShuZhirenField!.NameLength}，可弃置场上的一张{(delayedTrick ? "延时锦囊牌" : "装备牌")}，也可以放弃。"),
            Choices = choices.AsReadOnly()
        };
        _status = _players[active.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveZhirenFieldChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The zhiren field choice lost its program frame.");
        var state = frame.PanShuZhirenField ??
            throw new InvalidOperationException("The zhiren field choice lost its stage state.");
        var delayedStage = selected.Parameters.GetValueOrDefault("stage") == "delayed";
        if (delayedStage != state.AwaitingDelayedTrick)
            throw new InvalidOperationException("The zhiren field choice does not match its stage.");
        ClearPendingDecision();
        var active = GetActiveProgramFrame(frame.Id);
        if (selected.Parameters.GetValueOrDefault("action") != "skip")
        {
            if (!int.TryParse(selected.Parameters.GetValueOrDefault("card-id"),
                    System.Globalization.CultureInfo.InvariantCulture, out var cardId))
                throw new InvalidOperationException("The zhiren field choice has no card.");
            var location = _cardZones.GetLocation(cardId);
            var expectedZone = delayedStage ? CardZoneKind.Judgment : CardZoneKind.Equipment;
            if (location.Zone != expectedZone || location.OwnerSeat is null)
                throw new InvalidOperationException("The zhiren field card left its field zone.");
            var card = _cardZones.CardsAt(location).Single(item => item.Id == cardId);
            MoveCards([card], location, CardLocation.DiscardPile,
                new($"skill-program.{active.SkillId}.zhiren-field"));
        }
        if (!delayedStage)
        {
            ContinueZhirenFieldAfterEquipment(active);
            return;
        }
        ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with { PanShuZhirenField = null });
        active = GetActiveProgramFrame(active.Id);
        var length = state.NameLength;
        if (length >= 3)
            ApplyProgramSkillRecovery(active.Id, active.OwnerSeat, active.OwnerSeat);
        if (length >= 4)
            DrawProgramCards(active.Id, active.OwnerSeat, 3, null, null,
                SkillProgramCardSetVisibility.Private,
                new($"skill-program.{active.SkillId}.zhiren-tiers"));
        AddLog("SkillTriggered",
            $"{_players[active.OwnerSeat].Name} 的【织纴】按牌名字数 {length} 结算。", active.OwnerSeat);
        AdvanceRuntimeProgram(active.Id);
    }

    // 燕尔: both pair draws settle first (owner first), then the shared-category
    // follow-up grants the paired skill its boolean modification and recovers the
    // last-hand loser by one.
    private SkillProgramStepOutcome YanerProgramResolvePairBenefit(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var loserSeat = active.WindowContext?.SourceSeat ??
            throw new InvalidOperationException("The yaner pair lost its last-hand loser.");
        var owner = _players[active.OwnerSeat];
        if (!owner.IsAlive)
            return SkillProgramStepOutcome.Continue;
        DrawProgramCards(active.Id, active.OwnerSeat, 2, null, "yaner-own",
            SkillProgramCardSetVisibility.Private, new($"skill-program.{active.SkillId}.yaner-draw"));
        IReadOnlyList<int> otherDrawn = [];
        if (IsValidPlayerSeat(loserSeat) && loserSeat != active.OwnerSeat && _players[loserSeat].IsAlive)
        {
            DrawProgramCards(active.Id, loserSeat, 2, null, "yaner-other",
                SkillProgramCardSetVisibility.Private, new($"skill-program.{active.SkillId}.yaner-draw"));
            otherDrawn = GetProgramCardSet(GetActiveProgramFrame(active.Id), "yaner-other").CardIds;
        }
        if (otherDrawn.Count != 2)
        {
            AdvanceRuntimeProgram(active.Id);
            return SkillProgramStepOutcome.AwaitChild;
        }
        var ownDrawn = GetProgramCardSet(GetActiveProgramFrame(active.Id), "yaner-own").CardIds;
        if (ownDrawn.Count != 2)
        {
            AdvanceRuntimeProgram(active.Id);
            return SkillProgramStepOutcome.AwaitChild;
        }
        if (CardIdsCategorySet(ownDrawn).SetEquals(CardIdsCategorySet(otherDrawn)))
        {
            var targetSkillId = effect.SkillIds.Single();
            SetForeignProgramBooleanState(active, targetSkillId, effect.StateId!, value: true);
            AddLog("SkillTriggered",
                $"{owner.Name} 以【燕尔】摸到的牌与 {_players[loserSeat].Name} 摸到的牌类别相同，" +
                $"【{_contentRegistry!.GetSkill(targetSkillId).Name}】直到其下回合开始删去“你的回合内”。",
                active.OwnerSeat, loserSeat);
            ApplyProgramSkillRecovery(active.Id, active.OwnerSeat, loserSeat);
        }
        AdvanceRuntimeProgram(active.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

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
        if (!target.IsAlive) return;
        var recovered = Math.Min(1, target.MaxHp - target.Hp);
        if (recovered <= 0) return;
        if (TryQueueRecoveryReplacement(frameId, sourceSeat, targetSeat, recovered,
                new(RecoveryAttemptProducer.Program, GetActiveProgramFrame(frameId).InstructionIndex))) return;
        var recovery = BeginRecovery(frameId, sourceSeat, targetSeat, recovered);
        target.Hp += recovered;
        AdvanceEventRulesAndQueueFact(new RecoveryAppliedEvent(sourceSeat, targetSeat, recovered, target.Hp));
        PopResolutionFrame(recovery, ResolutionFrameKind.Recovery);
    }

    private PromptChoice SelectAiZhirenFieldChoice(PendingDecision decision) => decision.Choices
        .OrderBy(choice => choice.Parameters.GetValueOrDefault("action") == "skip" ? 1 : 0)
        .ThenBy(choice => choice.Id.Value, StringComparer.Ordinal).First();

    private sealed partial class ProgramSkillHost : IPanShuProgramHost
    {
        public SkillProgramStepOutcome ZhirenReorderTopByLength(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ZhirenProgramReorderTopByLength(frame, effect);
        public SkillProgramStepOutcome ZhirenResolveFieldTiers(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.ZhirenProgramResolveFieldTiers(frame, effect);
        public SkillProgramStepOutcome YanerResolvePairBenefit(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.YanerProgramResolvePairBenefit(frame, effect);
    }
}
