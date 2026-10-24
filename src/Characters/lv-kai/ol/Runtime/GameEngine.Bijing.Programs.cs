namespace CardGame.Core;

public sealed partial class GameEngine
{
    [ProgramFrameInvariant(ProgramFrameInvariantPhase.AfterSelectionState, 7171)]
    private void AssertBijingUnpaidSelection(ProgramSkillFrame frame)
    {
        if (frame.BijingPunish is not { } state) return;
        if (state.OwnerSeat != frame.OwnerSeat || !IsValidPlayerSeat(state.TurnOwnerSeat) || state.Remaining is < 1 or > 2 ||
            (state.Paid ? state.SelectedCardIds.Count != state.Remaining || state.SelectedLocations.Count != state.SelectedCardIds.Count ||
                state.Before < 0 || state.After < state.Before || state.After > _movementSequence ||
                state.SelectedCardIds.Select((id, i) => _cardMovements.Count(m => m.CardId == id && m.Sequence > state.Before && m.Sequence <= state.After &&
                    m.From == state.SelectedLocations[i] && m.Reason.Value == $"skill-program.{frame.SkillId}.bijing-punish") != 1).Any(bad => bad)
                : state.SelectedCardIds.Count >= state.Remaining || state.SelectedCardIds.Any(id => !BijingPunishChoices(frame, state.TurnOwnerSeat).Any(c => c.Cards.Contains(id)))) ||
            state.SelectedCardIds.Distinct().Count() != state.SelectedCardIds.Count ||
            state.DiscardedCardIds.Intersect(state.SelectedCardIds).Any() ||
            GetPausedPrivateOfferEffect(frame)?.Op != SkillProgramEffectOp.BijingPunishDiscardPhase)
            throw new InvalidOperationException("Bijing lost its original unpaid owned-card selection.");
    }
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
        var payable = BijingPunishChoices(active, turnOwner);
        if (payable.Count == 0)
        {
            AdvanceEventRulesAndQueueFact(new ProgramBijingPunishEvent(active.Id, active.SkillId,
                ownerSeat, turnOwner, lost, []));
            return SkillProgramStepOutcome.Continue;
        }
        ReplaceRuntimeTop(GetActiveProgramFrame(active.Id) with
        {
            BijingPunish = new BijingPunishState(turnOwner, ownerSeat, lost,
                Math.Min(2, payable.Count), [])
        });
        PresentBijingPunishCardPrompt(GetActiveProgramFrame(active.Id));
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private IReadOnlyList<PromptChoice> BijingPunishChoices(ProgramSkillFrame frame, int payer) =>
        BuildOwnedCardPaymentChoices(frame.Id, payer, payer, [CardZoneKind.Hand, CardZoneKind.Equipment], OwnedCardMoveIntent.Discard);

    private void PresentBijingPunishCardPrompt(ProgramSkillFrame frame)
    {
        var state = frame.BijingPunish ?? throw new InvalidOperationException("The Bijing punish lost its state.");
        var active = GetActiveProgramFrame(frame.Id);
        var materialChoices = BijingPunishChoices(frame, state.TurnOwnerSeat).Where(choice => !state.SelectedCardIds.Contains(choice.Cards.Single())).ToArray();
        var required = state.Remaining - state.SelectedCardIds.Count;
        var presentation = _contentRegistry!.GetSkill(active.SkillId);
        var choices = materialChoices.Select(choice => new PromptChoice(
            new ChoiceId($"bijing-punish.frame-{frame.Id}.card-{choice.Cards.Single()}"),
            choice.Description,
            choice.Cards, [],
            new Dictionary<string, string>
            {
                ["program-action"] = "bijing-punish-discard",
                ["card-id"] = choice.Cards.Single().ToString(System.Globalization.CultureInfo.InvariantCulture)
            })).ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.ProgramTrigger, state.TurnOwnerSeat,
            $"【{presentation.Name}】请选择另外 {required} 张手牌或装备牌，一并弃置。",
            choices.SelectMany(choice => choice.Cards).ToArray(), [], active.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            TargetSeat = state.TurnOwnerSeat,
            SkillPrompt = new SkillPromptPresentation(active.SkillId, presentation.Name,
                $"{presentation.Name} · 弃牌",
                $"{_players[state.OwnerSeat].Name} 本回合失去过“闭境”牌，你一并弃置 {state.Remaining} 张牌（已选 {state.SelectedCardIds.Count}）。"),
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
            state.Paid || !BijingPunishChoices(frame, state.TurnOwnerSeat).Any(choice => choice.Cards.SequenceEqual([cardId])) || state.SelectedCardIds.Contains(cardId) ||
            state.SelectedCardIds.Any(id => !BijingPunishChoices(frame, state.TurnOwnerSeat).Any(choice => choice.Cards.SequenceEqual([id]))))
            throw new InvalidOperationException("The Bijing punish card is no longer in hand.");
        ClearPendingDecision();
        var chosen = state.SelectedCardIds.Append(cardId).ToArray();
        if (chosen.Length < state.Remaining)
        {
            ReplaceRuntimeTop(GetActiveProgramFrame(frame.Id) with
            {
                BijingPunish = state with { SelectedCardIds = chosen }
            });
            PresentBijingPunishCardPrompt(GetActiveProgramFrame(frame.Id));
            return;
        }
        // The official skill says discard two cards, without an ordered
        // per-card effect: https://www.sanguosha.com/hero/465 . Select the
        // whole unpaid group before moving it or publishing its loss batch.
        if (chosen.Length != state.Remaining) throw new InvalidOperationException("Bijing cannot overpay its original discard group.");
        var active = GetActiveProgramFrame(frame.Id);
        ReplaceRuntimeTop(active = active with { BijingPunish = state with { SelectedCardIds = chosen,
            SelectedLocations = chosen.Select(_cardZones.GetLocation).ToArray(), Paid = true, Before = _movementSequence, After = _movementSequence },
            PendingMovementContinuation = new(state.TurnOwnerSeat, 0, null) });
        active = AppendFrameNativeLogicalDiscard(active, state.TurnOwnerSeat, chosen,
            new($"skill-program.{active.SkillId}.bijing-punish"));
        MoveProgramCardsFromMultipleSources(chosen, CardLocation.DiscardPile, new($"skill-program.{active.SkillId}.bijing-punish"), disclosure: CardMovementDisclosure.FaceUp);
        var discarded = state.DiscardedCardIds.Concat(chosen).ToArray();
        active = CompleteFrameNativeLogicalDiscard(GetActiveProgramFrame(frame.Id));
        ReplaceRuntimeTop(active = active with { BijingPunish = active.BijingPunish! with { After = _movementSequence } });
        AdvanceEventRulesAndQueueFact(new ProgramBijingPunishEvent(active.Id, active.SkillId,
            state.OwnerSeat, state.TurnOwnerSeat, state.LostCardIds, discarded));
        ContinueBijingPaidGroup(active.Id);
    }
}
