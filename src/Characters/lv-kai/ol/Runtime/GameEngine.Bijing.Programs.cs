namespace CardGame.Core;

public sealed partial class GameEngine
{


    // 闭境 mark: the chosen hand cards stay registered until their recast.
    private SkillProgramStepOutcome BijingProgramMarkHandCards(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var binding = active.CardSetBindings.SingleOrDefault(item => item.Name == effect.SourceBind!);
        if (binding is null)
        {
            CancelProgramBindingAndCleanup(active, "闭境的选牌绑定未生成，标记结束。");
            return SkillProgramStepOutcome.AwaitChild;
        }
        foreach (var cardId in binding.CardIds)
            if (_cardZones.GetLocation(cardId) == CardLocation.Hand(active.OwnerSeat))
                _bijingMarkedCardIds[cardId] = active.OwnerSeat;
        if (binding.CardIds.Count > 0)
            AdvanceEventRulesAndQueueFact(new ProgramBijingMarkedEvent(active.Id, active.SkillId,
                GetProgramBindingId(active), active.OwnerSeat, binding.CardIds));
        AdvanceRuntimeProgram(active.Id);
        return SkillProgramStepOutcome.Continue;
    }

    // 闭境 recast: marked hand cards are discarded and replaced one-for-one.
    private SkillProgramStepOutcome BijingProgramRecastMarkedCards(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var owner = _players[active.OwnerSeat];
        if (!owner.IsAlive) return SkillProgramStepOutcome.Continue;
        var marked = GetHand(owner).Where(card => _bijingMarkedCardIds.TryGetValue(card.Id, out var seat) &&
            seat == active.OwnerSeat).ToArray();
        foreach (var card in marked)
            _bijingMarkedCardIds.Remove(card.Id);
        if (marked.Length == 0) return SkillProgramStepOutcome.Continue;
        var reason = new CardMoveReason($"skill-program.{active.SkillId}.bijing-recast");
        MoveCards(marked, CardLocation.Hand(active.OwnerSeat), CardLocation.DiscardPile, reason);
        DrawCards(owner, marked.Length, true, new($"skill-program.{active.SkillId}.bijing-recast-draw"));
        AdvanceEventRulesAndQueueFact(new ProgramBijingRecastEvent(active.Id, active.SkillId,
            active.OwnerSeat, Array.AsReadOnly(marked.Select(card => card.Id).ToArray()), marked.Length));
        if (!TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    // 闭境 punish: the turn owner discards two when the owner lost a marked
    // card during this turn; the movement log is the per-turn loss record.
    private SkillProgramStepOutcome BijingProgramPunishDiscardPhase(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var active = GetActiveProgramFrame(frame.Id);
        var ownerSeat = active.OwnerSeat;
        if (frame.WindowContext is not { SourceSeat: { } turnOwner } ||
            turnOwner == ownerSeat || !_players[turnOwner].IsAlive || !_players[ownerSeat].IsAlive ||
            !HasRuntimeSkillInstance(_players[ownerSeat], active.SkillId, active.SkillInstanceId))
            return SkillProgramStepOutcome.Continue;
        var lost = _cardMovements.Where(movement => movement.TurnNumber == _turnNumber &&
                movement.From == CardLocation.Hand(ownerSeat) &&
                _bijingMarkedCardIds.TryGetValue(movement.CardId, out var seat) && seat == ownerSeat)
            .Select(movement => movement.CardId).Distinct().ToArray();
        if (lost.Length == 0) return SkillProgramStepOutcome.Continue;
        var turnOwnerHand = GetHand(_players[turnOwner]);
        if (turnOwnerHand.Count == 0)
        {
            AdvanceEventRulesAndQueueFact(new ProgramBijingPunishEvent(active.Id, active.SkillId,
                ownerSeat, turnOwner, lost, []));
            return SkillProgramStepOutcome.Continue;
        }
        ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with
        {
            BijingPunish = new BijingPunishState(turnOwner, ownerSeat, lost,
                Math.Min(2, turnOwnerHand.Count), [])
        });
        PresentBijingPunishCardPrompt(GetActiveProgramFrame(active.Id));
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void PresentBijingPunishCardPrompt(ProgramSkillFrame frame)
    {
        var state = frame.BijingPunish ?? throw new InvalidOperationException("The Bijing punish lost its state.");
        var active = GetActiveProgramFrame(frame.Id);
        var hand = GetHand(_players[state.TurnOwnerSeat]).OrderBy(card => card.Id).ToArray();
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = hand.Select(card => new PromptChoice(
            new ChoiceId($"bijing-punish.frame-{frame.Id}.card-{card.Id}"),
            $"弃置【{card.DisplayName}】。",
            [card.Id], [],
            new Dictionary<string, string>
            {
                ["program-action"] = "bijing-punish-discard",
                ["card-id"] = card.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)
            })).ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, state.TurnOwnerSeat,
            $"【{presentation.Name}】请弃置 {state.Remaining} 张手牌。",
            hand.Select(card => card.Id).ToArray(), [], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            TargetSeat = state.TurnOwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 弃牌",
                $"{_players[state.OwnerSeat].Name} 本回合失去过“闭境”牌，你弃置 {state.Remaining} 张手牌。"),
            Choices = choices.AsReadOnly()
        };
        _status = _players[state.TurnOwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    [ProgramAnswer("bijing-punish-discard")]
    private void ResolveBijingPunishChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ??
            throw new InvalidOperationException("The Bijing punish choice lost its program frame.");
        var state = frame.BijingPunish ?? throw new InvalidOperationException("The Bijing punish lost its state.");
        if (!int.TryParse(selected.Parameters.GetValueOrDefault("card-id"),
                System.Globalization.CultureInfo.InvariantCulture, out var cardId) ||
            _cardZones.GetLocation(cardId) != CardLocation.Hand(state.TurnOwnerSeat))
            throw new InvalidOperationException("The Bijing punish card is no longer in hand.");
        ClearPendingDecision();
        var active = GetActiveProgramFrame(frame.Id);
        active = AppendFrameNativeLogicalDiscard(active, state.TurnOwnerSeat, [cardId],
            new($"skill-program.{active.SkillId}.bijing-punish"));
        MoveCards([_cardZones.CardsAt(CardLocation.Hand(state.TurnOwnerSeat)).Single(card => card.Id == cardId)],
            CardLocation.Hand(state.TurnOwnerSeat), CardLocation.DiscardPile,
            new($"skill-program.{active.SkillId}.bijing-punish"));
        var discarded = state.DiscardedCardIds.Append(cardId).ToArray();
        if (state.Remaining > 1)
        {
            ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with
            {
                BijingPunish = state with
                {
                    Remaining = state.Remaining - 1,
                    DiscardedCardIds = Array.AsReadOnly(discarded)
                }
            });
            PresentBijingPunishCardPrompt(GetActiveProgramFrame(frame.Id));
            return;
        }
        active = CompleteFrameNativeLogicalDiscard(GetActiveProgramFrame(frame.Id));
        ReplaceRuntimeTop(active with { BijingPunish = null });
        AdvanceEventRulesAndQueueFact(new ProgramBijingPunishEvent(active.Id, active.SkillId,
            state.OwnerSeat, state.TurnOwnerSeat, state.LostCardIds, discarded));
        if (!TryBeginCardsMovedProgramWindow())
            AdvanceRuntimeProgram(active.Id);
    }
}
