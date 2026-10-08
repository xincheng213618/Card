namespace CardGame.Core;

// 观潮 evidence: the committed phase pattern is public, and the phase-start
// event index pins the choice to exactly one play-phase instance.
public sealed record ProgramGuanchaoPatternChosenEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int PhaseEventIndex, bool Ascending) : IGameEvent;
public sealed record ProgramGuanchaoRankDrawEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int TurnNumber, int Rank, bool Drew) : IGameEvent;

// 逊贤 evidence: one scalar per used/played card that finished into the discard
// pile, attributed while the acting use frame is still live; the gift itself is
// public once it moves.
public sealed record SettledActionCardDiscardEvent(long BatchId, int MovementSequence, int CardId,
    int ActorSeat, int TurnNumber) : IGameEvent;
public sealed record ProgramXunxianGiftEvent(long FrameId, string SkillId, string BindingId,
    int OwnerSeat, int RecipientSeat, int CardId, int DiscardMovementSequence) : IGameEvent;

public sealed partial class GameEngine
{
    private bool TracksSettledActionCards =>
        _contentRegistry?.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.XunxianGiftUsedCard) == true ||
        _contentRegistry?.ProgramDependencies.HasTriggerOperation(SkillProgramEffectOp.TongxieFollowUp) == true;

    // One scalar fact per settled use/response card. The attribution has to be
    // captured at batch completion, while the acting use frame is still on the
    // resolution stack; the cards-moved window opens later and reads the facts.
    private void CaptureSettledActionCards(long batchId, int turnNumber, IReadOnlyList<CardMovementRecord> movements)
    {
        if (!TracksSettledActionCards) return;
        foreach (var movement in movements)
        {
            if (movement.To != CardLocation.DiscardPile || movement.From != CardLocation.Processing ||
                movement.Reason.Value is not ("card.use-finished" or "card.response-finished" or "card.respond.nullification-finished"))
                continue;
            var action = SettledActionContext(movement.CardId);
            if (action is null) continue;
            AdvanceEventRulesAndQueueFact(new SettledActionCardDiscardEvent(batchId, movement.Sequence,
                movement.CardId, action.ActorSeat, turnNumber));
            CaptureTongxieSettledSlashUse(batchId, movement);
        }
    }

    private CardActionContext? SettledActionContext(int cardId)
    {
        var use = _resolutionStack.OfType<CardUseFrame>().Select(frame => frame.Action).LastOrDefault(action =>
            action is not null && action.Type == CardActionType.Use && action.PhysicalCards.Any(cost => cost.CardId == cardId));
        if (use is not null) return use;
        var parent = _resolutionStack.OfType<CardUseFrame>().LastOrDefault(frame => frame.Action is not null)?.Action;
        if (parent is null) return null;
        return CompleteProgramEventHistory().OfType<CardActionAcceptedEvent>().Select(accepted => accepted.Action)
            .LastOrDefault(action => action.Type == CardActionType.Response && action.ParentActionId == parent.ActionId &&
                action.PhysicalCards.Any(cost => cost.CardId == cardId));
    }

    private int[] MatchingOwnSettledActionCardIndexes(CardMovementBatchContext batch, ProgramTriggerCandidate candidate)
    {
        if (!TracksSettledActionCards) return [];
        var settled = CompleteProgramEventHistory().OfType<SettledActionCardDiscardEvent>()
            .Where(e => e.BatchId == batch.Id && e.ActorSeat == candidate.OwnerSeat)
            .Select(e => e.MovementSequence).ToHashSet();
        return batch.Movements.Select((movement, index) => (movement, index))
            .Where(item => settled.Contains(item.movement.Sequence) && item.movement.To == CardLocation.DiscardPile &&
                _cardZones.GetLocation(item.movement.CardId) == CardLocation.DiscardPile)
            .Select(item => item.index).Order().ToArray();
    }

    private int CurrentPlayPhaseEventIndex(int ownerSeat)
    {
        var facts = CompleteProgramEventHistory().ToArray();
        return Array.FindLastIndex(facts, e => e is PhaseChangedEvent phase &&
            phase.Phase == TurnPhase.Play && phase.ActorSeat == ownerSeat);
    }

    // The committed pattern is active only while its play-phase boundary is
    // still the owner's latest one; an extra play phase in the same turn never
    // inherits an earlier choice because every entry enqueues its own boundary.
    private (bool Ascending, int PhaseEventIndex)? CurrentGuanchaoPattern(int ownerSeat)
    {
        var facts = CompleteProgramEventHistory().ToArray();
        var phaseIndex = Array.FindLastIndex(facts, e => e is PhaseChangedEvent phase &&
            phase.Phase == TurnPhase.Play && phase.ActorSeat == ownerSeat);
        var choice = facts.OfType<ProgramGuanchaoPatternChosenEvent>().LastOrDefault(e =>
            e.OwnerSeat == ownerSeat && e.PhaseEventIndex == phaseIndex);
        return choice is null ? null : (choice.Ascending, choice.PhaseEventIndex);
    }

    // Prior uses of this phase, in order, deduplicated per accepted action; a
    // use without a rank (virtual declarations, multi-card conversions) stays
    // null and breaks the pattern instead of silently comparing as zero.
    private IReadOnlyList<int?> GuanchaoPhaseUseRanks(int ownerSeat, int phaseEventIndex, long currentActionId)
    {
        var ranks = new List<int?>();
        var seen = new HashSet<long>();
        foreach (var accepted in CompleteProgramEventHistory().Skip(phaseEventIndex + 1).OfType<CardActionAcceptedEvent>())
        {
            var action = accepted.Action;
            if (action.Type != CardActionType.Use || action.ActorSeat != ownerSeat || !seen.Add(action.ActionId) ||
                action.ActionId == currentActionId)
                continue;
            ranks.Add(action.EffectiveRank ?? (action.PhysicalCards.Count == 1 ? GetAdvancedCard(action.PhysicalCards[0].CardId).Rank : null));
        }
        return ranks;
    }

    private static bool MatchesGuanchaoPattern(IReadOnlyList<int?> ranks, bool ascending)
    {
        int? previous = null;
        foreach (var rank in ranks)
        {
            if (rank is not { } value) return false;
            if (previous is { } before && (ascending ? before >= value : before <= value)) return false;
            previous = value;
        }
        return true;
    }

    // 观潮 launch: the owner commits this play phase to one strict pattern.
    private SkillProgramStepOutcome GuanchaoProgramChoosePattern(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (!owner.IsAlive || _winner != Winner.None || _phase != TurnPhase.Play || owner.Seat != _currentSeat)
            return SkillProgramStepOutcome.Continue;
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        PromptChoice Pattern(bool ascending) => new(
            new ChoiceId($"guanchao-pattern.frame-{frame.Id}.{(ascending ? "ascending" : "descending")}"),
            ascending ? "严格递增。" : "严格递减。", [], [],
            new Dictionary<string, string>
            {
                ["program-action"] = "guanchao-pattern",
                ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["ascending"] = ascending ? "true" : "false"
            });
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】请选择本阶段使用牌点数的规律。",
            [], [], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 选择规律",
                "本阶段你使用过的所有牌的点数均符合所选规律时，你每次使用牌摸一张牌。"),
            Choices = new[] { Pattern(true), Pattern(false) }.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveGuanchaoPatternChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Guanchao pattern choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.GuanchaoChoosePattern } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Guanchao pattern choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != active.OwnerSeat)
            throw new InvalidOperationException("The Guanchao pattern chooser changed while suspended.");
        ClearPendingDecision();
        var ascending = selected.Parameters.GetValueOrDefault("ascending") == "true";
        AdvanceEventRulesAndQueueFact(new ProgramGuanchaoPatternChosenEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, CurrentPlayPhaseEventIndex(active.OwnerSeat), ascending));
        AddLog("SkillEffect",
            $"{_players[active.OwnerSeat].Name} 观潮：本阶段使用牌的点数按{(ascending ? "严格递增" : "严格递减")}结算。",
            active.OwnerSeat);
        AdvanceRuntimeProgram(active.Id);
    }

    // 观潮 draw: the whole phase ledger including the current card must match.
    private SkillProgramStepOutcome GuanchaoProgramRankDraw(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (!owner.IsAlive || _winner != Winner.None) return SkillProgramStepOutcome.Continue;
        var pattern = CurrentGuanchaoPattern(owner.Seat);
        if (pattern is null) return SkillProgramStepOutcome.Continue;
        var context = frame.WindowContext?.CardUse;
        var action = _resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault()?.Action ??
            _resolutionStack.OfType<CardUseFrame>().LastOrDefault(item => item.Action?.ActionId == context?.CardActionId)?.Action;
        if (action is null || action.ActorSeat != owner.Seat) return SkillProgramStepOutcome.Continue;
        var currentRank = action.EffectiveRank ?? (action.PhysicalCards.Count == 1 ? GetAdvancedCard(action.PhysicalCards[0].CardId).Rank : (int?)null);
        var ranks = GuanchaoPhaseUseRanks(owner.Seat, pattern.Value.PhaseEventIndex, action.ActionId).Append(currentRank).ToArray();
        var drew = MatchesGuanchaoPattern(ranks, pattern.Value.Ascending);
        if (drew)
            DrawCards(owner, 1, true, new($"skill-program.{active.SkillId}.guanchao-draw"));
        AddLog("SkillEffect", drew
            ? $"{owner.Name} 观潮：本阶段使用过的牌点数均符合所选规律，摸一张牌。"
            : $"{owner.Name} 观潮：本阶段使用过的牌点数不再符合所选规律。", owner.Seat);
        AdvanceEventRulesAndQueueFact(new ProgramGuanchaoRankDrawEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), owner.Seat, _turnNumber, currentRank ?? 0, drew));
        if (drew && !TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
        return drew ? SkillProgramStepOutcome.AwaitChild : SkillProgramStepOutcome.Continue;
    }

    // 逊贤: the settled used/played card is offered to a character with more
    // hand cards or more health; the once-per-turn limit is the trigger's.
    private SkillProgramStepOutcome XunxianProgramGiftUsedCard(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (frame.WindowContext is not { Window: SkillProgramTriggerWindow.DiscardPileReceived,
                MovementBatch: { } batch, MovementIndex: { } index } ||
            index < 0 || index >= batch.Movements.Count ||
            _winner != Winner.None || !owner.IsAlive ||
            !HasRuntimeSkillInstance(owner, active.SkillId, active.SkillInstanceId) ||
            _cardZones.GetLocation(batch.Movements[index].CardId) != CardLocation.DiscardPile)
            return SkillProgramStepOutcome.Continue;
        var recipients = _players.Where(player => player.IsAlive && player.Seat != active.OwnerSeat &&
            (GetHand(player).Count > GetHand(owner).Count || player.Hp > owner.Hp))
            .Select(player => player.Seat).ToArray();
        if (recipients.Length == 0)
        {
            AddLog("SkillEffect", $"{owner.Name} 逊贤：没有手牌数或体力值大于你的角色，牌留在弃牌堆。", active.OwnerSeat);
            AdvanceRuntimeProgram(active.Id);
            return SkillProgramStepOutcome.Continue;
        }
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = recipients.Select(seat => new PromptChoice(
            new ChoiceId($"xunxian-gift.frame-{frame.Id}.seat-{seat}"),
            $"交给 {_players[seat].Name}。",
            [], [seat],
            new Dictionary<string, string>
            {
                ["program-action"] = "xunxian-gift",
                ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["recipient"] = seat.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })).ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, active.OwnerSeat,
            $"【{presentation.Name}】请选择接受牌的角色。",
            [], recipients, active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = false,
            TargetSeat = active.OwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 选择角色", "将置入弃牌堆的牌交给一名手牌数或体力值大于你的角色。"),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveXunxianGiftChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Xunxian gift choice lost its program frame.");
        var paused = ProgramInstructionResolver.Default
            .Resolve(frame, _contentRegistry!.GetSkill(frame.SkillId).Program!)
            .GetPausedInstruction(frame.InstructionIndex);
        if (paused.Effect is not { Op: SkillProgramEffectOp.XunxianGiftUsedCard } ||
            selected.Parameters.GetValueOrDefault("frame-id") !=
                frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture))
            throw new InvalidOperationException("The Xunxian gift choice does not match its suspended instruction.");
        var active = GetActiveProgramFrame(frame.Id);
        if (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger } decision ||
            decision.PlayerSeat != active.OwnerSeat ||
            !int.TryParse(selected.Parameters.GetValueOrDefault("recipient"),
                System.Globalization.CultureInfo.InvariantCulture, out var recipient) ||
            frame.WindowContext is not { Window: SkillProgramTriggerWindow.DiscardPileReceived,
                MovementBatch: { } batch, MovementIndex: { } index } ||
            index < 0 || index >= batch.Movements.Count)
            throw new InvalidOperationException("The Xunxian gift choice lost its chooser or settled card.");
        ClearPendingDecision();
        var movement = batch.Movements[index];
        var owner = _players[active.OwnerSeat];
        if (_winner != Winner.None || !owner.IsAlive || !IsValidPlayerSeat(recipient) ||
            recipient == active.OwnerSeat || !_players[recipient].IsAlive ||
            _cardZones.GetLocation(movement.CardId) != CardLocation.DiscardPile ||
            !(GetHand(_players[recipient]).Count > GetHand(owner).Count || _players[recipient].Hp > owner.Hp))
        {
            CancelProgramBindingAndCleanup(active, "逊贤的目标或弃牌堆中的牌已失效，剩余结算取消。");
            return;
        }
        var card = _cardZones.CardsAt(CardLocation.DiscardPile).Single(item => item.Id == movement.CardId);
        MoveCard(card, CardLocation.DiscardPile, CardLocation.Hand(recipient),
            new($"skill-program.{active.SkillId}.xunxian-gift"));
        AddLog("SkillTriggered",
            $"{owner.Name} 发动【{_contentRegistry.GetSkill(active.SkillId).Name}】，将 {card.DisplayName} 交给 {_players[recipient].Name}。",
            active.OwnerSeat, recipient);
        AdvanceEventRulesAndQueueFact(new ProgramXunxianGiftEvent(active.Id, active.SkillId,
            GetProgramBindingId(active), active.OwnerSeat, recipient, card.Id, movement.Sequence));
        if (!TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
    }

    private PromptChoice SelectAiGuanchaoPatternChoice(PendingDecision decision) =>
        decision.Choices.OrderBy(choice => choice.Id.Value, StringComparer.Ordinal).First();

    private PromptChoice SelectAiXunxianGiftChoice(PendingDecision decision) =>
        decision.Choices.OrderBy(choice => choice.Id.Value, StringComparer.Ordinal).First();

    private sealed partial class ProgramSkillHost : IYanJunProgramHost
    {
        public SkillProgramStepOutcome GuanchaoChoosePattern(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.GuanchaoProgramChoosePattern(frame, effect);
        public SkillProgramStepOutcome GuanchaoRankDraw(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.GuanchaoProgramRankDraw(frame, effect);
        public SkillProgramStepOutcome XunxianGiftUsedCard(ProgramSkillFrame frame, SkillProgramEffect effect) =>
            engine.XunxianProgramGiftUsedCard(frame, effect);
    }
}
