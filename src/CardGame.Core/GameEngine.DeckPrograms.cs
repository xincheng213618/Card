namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed partial class ProgramSkillHost : IDeckProgramHost
    {
        public SkillProgramStepOutcome ExecuteDeckProgram(SkillProgramEffect effect, ProgramSkillFrame frame) => engine.ExecuteDeckProgram(effect, frame);
    }

    private IEnumerable<LegalAction> BuildDeckEndExchangeActions(CharacterState provider)
    {
        foreach (var owner in _players.Where(player => player.IsAlive && player.Seat != provider.Seat))
        foreach (var program in EnabledActivationPrograms(owner))
        foreach (var activation in program.Activations.Where(item => ProgramInstructionResolver.Default.Features(item).HasOperation(SkillProgramEffectOp.ExchangeOwnedCardThroughDeckEnd)))
        {
            if (_programContributionUses.GetValueOrDefault((provider.Seat, owner.Seat, program.Id, activation.Id)) >= 1) continue;
            var cards = GetHand(provider).Concat(GetEquipment(provider)).Select(card => card.Id).ToArray();
            if (cards.Length == 0) continue;
            yield return new(LegalActionKind.UseProgramSkill, null, owner.Seat, $"将一张手牌或装备牌交给 {owner.Name}，由其选择是否交换牌堆另一端的牌。",
                MinCardCount: 1, MaxCardCount: 1, MinTargetCount: 1, MaxTargetCount: 1)
            {
                ProgramSkillId = program.Id, ProgramActivationId = activation.Id, ProgramSkillOwnerSeat = owner.Seat,
                SelectableCardIds = cards, SelectableTargetSeats = [owner.Seat],
                ProgramAiHint = new(1, 0, 0, 0, 0, 0, true, false)
            };
        }
    }

    private bool TryExecuteDeckEndContribution(CharacterState provider, CharacterState owner, LegalAction action, int cardId)
    {
        var program = GetEnabledSkillProgram(owner, action.ProgramSkillId!);
        var plan = ProgramInstructionResolver.Default.Find(program,
            ProgramInstructionSourceKind.Activation, action.ProgramActivationId);
        var activation = plan?.Activation;
        if (activation is null || !plan!.Features.HasOperation(SkillProgramEffectOp.ExchangeOwnedCardThroughDeckEnd)) return false;
        var key = (provider.Seat, owner.Seat, program.Id, activation.Id);
        if (_programContributionUses.GetValueOrDefault(key) >= 1 || provider.Seat == owner.Seat)
            throw new InvalidOperationException("Deck exchange contribution exhausted its own phase entry.");
        var source = _cardZones.GetLocation(cardId);
        if (source.OwnerSeat != provider.Seat || source.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment))
            throw new InvalidOperationException("The contribution requires one real provider hand or equipment card.");
        _programContributionUses[key] = 1;
        var frame = new ProgramSkillFrame(++_resolutionSequence, owner.Seat, program.Id, activation.Id,
            program.GameplayHash, 0, [cardId], [])
        {
            SkillInstanceId = GetRuntimeSkillInstanceId(owner, program.Id),
            DeckEndExchange = new(provider.Seat, cardId, source, "gift")
        };
        PushRuntimeFrame(frame);
        AdvanceEventRulesAndQueueFact(new ProgramSkillStartedEvent(frame.Id, owner.Seat, program.Id, activation.Id));
        AdvanceRuntimeProgram(frame.Id);
        return true;
    }

    private SkillProgramStepOutcome ExecuteDeckProgram(SkillProgramEffect effect, ProgramSkillFrame frame)
    {
        if (effect.Op == SkillProgramEffectOp.ExchangeOwnedCardThroughDeckEnd)
        {
            if (frame.DeckEndExchange is null)
            {
                var id = frame.SelectedCardIds.Single(); var location = _cardZones.GetLocation(id);
                if (location.OwnerSeat != frame.OwnerSeat || location.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment))
                    throw new InvalidOperationException("A self deck exchange requires one owned HE entity.");
                ReplaceRuntimeTop(frame = frame with { DeckEndExchange = new(frame.OwnerSeat, id, location, "choice") });
            }
            if (frame.DeckEndExchange!.Stage == "gift")
            {
                var d = frame.DeckEndExchange;
                var card = _cardZones.CardsAt(d.OriginalLocation).Single(c => c.Id == d.CardId);
                ReplaceRuntimeTop(frame with { DeckEndExchange = d with { Stage = "choice" } });
                MoveCard(card, d.OriginalLocation, CardLocation.Hand(frame.OwnerSeat), new("program.deck-end.gift"));
                AwaitDeckProgramMovement(frame.Id); return SkillProgramStepOutcome.AwaitChild;
            }
            ResumeDeckEndExchange(frame);
            return SkillProgramStepOutcome.AwaitChoice;
        }
        var target = frame.WindowContext?.TargetSeat ?? throw new InvalidOperationException("Deck Slash sequence requires its ending-turn target.");
        if (_players[target].Gender != GeneralGender.Male || !_players[target].IsAlive || target == frame.OwnerSeat ||
            _players[frame.OwnerSeat].Hp <= 0 || _cardZones.Count(CardLocation.DrawPile) > _players[frame.OwnerSeat].Hp * 10)
            return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(frame = frame with { DeckSlashSequence = new(target, [], _playerCount) });
        ResumeDeckSlashSequence(frame, effect);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private bool TryResumeDeckPrograms(long frameId)
    {
        var frame = GetActiveProgramFrame(frameId);
        if (frame.InstructionIndex == 0 || frame.DeckEndExchange is null && frame.DeckSlashSequence is null) return false;
        if (!DeckProgramSourceValid(frame) || _winner != Winner.None)
        { CancelProgramBindingAndCleanup(frame, "牌堆操作的技能实例或拥有者已失效。"); return true; }
        if (frame.DeckEndExchange is not null) ResumeDeckEndExchange(frame);
        else ResumeDeckSlashSequence(frame, ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect);
        return true;
    }
    private bool DeckProgramSourceValid(ProgramSkillFrame frame) => _players[frame.OwnerSeat].IsAlive &&
        HasRuntimeSkillInstance(_players[frame.OwnerSeat], frame.SkillId, frame.SkillInstanceId) &&
        EnabledSkillPrograms(_players[frame.OwnerSeat]).Any(program => program.Id == frame.SkillId);

    private void ResumeDeckEndExchange(ProgramSkillFrame frame)
    {
        var d = frame.DeckEndExchange!;
        if (!DeckProgramSourceValid(frame) || !_players[d.ProviderSeat].IsAlive)
        { CancelProgramBindingAndCleanup(frame, "牌堆交换参与者或技能已失效。"); return; }
        if (d.Stage == "choice")
        {
            var expected = d.ProviderSeat == frame.OwnerSeat ? d.OriginalLocation : CardLocation.Hand(frame.OwnerSeat);
            if (_cardZones.GetLocation(d.CardId) != expected) { FinishDeckEndExchange(frame); return; }
            PublishDeckEndChoice(frame); return;
        }
        if (d.Stage != "draw" || d.PlacedOnTop is null) throw new InvalidOperationException("Deck exchange lost its ordered draw stage.");
        var seats = new[] { frame.OwnerSeat, d.ProviderSeat }.Distinct().ToArray();
        if (d.DrawIndex >= seats.Length) { FinishDeckEndExchange(frame); return; }
        var seat = seats[d.DrawIndex];
        ReplaceRuntimeTop(frame with { DeckEndExchange = d with { DrawIndex = d.DrawIndex + 1 } });
        if (_players[seat].IsAlive && EnsureDrawPile())
        {
            var pile = _cardZones.CardsAt(CardLocation.DrawPile);
            var card = d.PlacedOnTop.Value ? pile[0] : pile[^1];
            MoveCard(card, CardLocation.DrawPile, CardLocation.Hand(seat), new("program.deck-end.draw"));
        }
        AwaitDeckProgramMovement(frame.Id);
    }
    private void FinishDeckEndExchange(ProgramSkillFrame frame)
    { ReplaceRuntimeTop(frame with { DeckEndExchange = null }); AdvanceRuntimeProgram(frame.Id); }
    private IReadOnlyList<PromptChoice> DeckEndChoices(ProgramSkillFrame frame)
    {
        var d = frame.DeckEndExchange!;
        PromptChoice Choice(string end, string label) => new(new($"deck-end.{frame.Id}.{end}"), label, [], [],
            new Dictionary<string,string> { ["program-action"] = "deck-end-exchange", ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), ["end"] = end });
        var choices = new List<PromptChoice> { Choice("top", "置于牌堆顶，从牌堆底摸牌"), Choice("bottom", "置于牌堆底，从牌堆顶摸牌") };
        if (d.ProviderSeat != frame.OwnerSeat) choices.Add(Choice("decline", "保留获得的牌，不交换牌堆牌"));
        return choices;
    }
    private void PublishDeckEndChoice(ProgramSkillFrame frame)
    {
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        var held = _cardZones.CardsAt(_cardZones.GetLocation(frame.DeckEndExchange!.CardId)).Single(card => card.Id == frame.DeckEndExchange.CardId);
        _pendingDecision = new(DecisionKind.ProgramTrigger, frame.OwnerSeat, "选择将【" + held.DisplayName + " " + GetSuitDisplayName(held.Suit) + held.RankText + "】置于哪一端，再从另一端摸牌。", [], [], frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = frame.OwnerSeat, Choices = DeckEndChoices(frame),
          SkillPrompt = new(frame.SkillId, skill.Name, skill.Name, skill.Description) };
        _status = _players[frame.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }
    private void ResolveDeckEndChoice(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Missing deck exchange frame.");
        var d = frame.DeckEndExchange ?? throw new InvalidOperationException("Missing deck exchange draft.");
        if (d.Stage != "choice" || !AssistedChoicesEqual([selected], DeckEndChoices(frame).Where(choice => choice.Id == selected.Id).ToArray()))
            throw new InvalidOperationException("Deck exchange choice changed while suspended.");
        ClearPendingDecision();
        if (!DeckProgramSourceValid(frame) || !_players[d.ProviderSeat].IsAlive)
        { CancelProgramBindingAndCleanup(frame, "牌堆交换的参与者或技能已失效。"); return; }
        var location = d.ProviderSeat == frame.OwnerSeat ? d.OriginalLocation : CardLocation.Hand(frame.OwnerSeat);
        if (_cardZones.GetLocation(d.CardId) != location) { FinishDeckEndExchange(frame); return; }
        if (selected.Parameters["end"] == "decline") { FinishDeckEndExchange(frame); return; }
        var onTop = selected.Parameters["end"] == "top";
        var card = _cardZones.CardsAt(location).Single(card => card.Id == d.CardId);
        ReplaceRuntimeTop(frame with { DeckEndExchange = d with { Stage = "draw", PlacedOnTop = onTop } });
        MoveCard(card, location, CardLocation.DrawPile, new("program.deck-end.place"));
        if (!onTop) _cardZones.PlaceDrawPileCardsAtBottom([card.Id]);
        AwaitDeckProgramMovement(frame.Id);
    }
    private void AwaitDeckProgramMovement(long id)
    {
        var frame = GetActiveProgramFrame(id);
        ReplaceRuntimeTop(frame with { PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(id);
    }

    private void ResumeDeckSlashSequence(ProgramSkillFrame frame, SkillProgramEffect effect)
    {
        var d = frame.DeckSlashSequence!;
        if (d.Shuffled) { ReplaceRuntimeTop(frame with { DeckSlashSequence = null }); AdvanceRuntimeProgram(frame.Id); return; }
        var target = _players[d.TargetSeat]; var owner = _players[frame.OwnerSeat];
        // Query again after every real child; new deck cards participate, previously
        // declared physical entities never do, even if a child returned them.
        var declared = d.DeclaredIds.ToHashSet();
        var next = target.IsAlive && owner.IsAlive && d.UsedCount < d.Limit && DeckProgramSourceValid(frame) && _winner == Winner.None
            ? _cardZones.CardsAt(CardLocation.DrawPile).Reverse().FirstOrDefault(card => IsSlashCard(card.Kind) &&
                !declared.Contains(card.Id) && CanUseSlashTarget(owner, target, card, effectiveKind: card.Kind,
                    ignoreDistance: effect.TargetRestriction == SkillProgramCardTargetRestriction.DistanceUnlimitedAgainstTarget))
            : null;
        if (next is null)
        {
            ReplaceRuntimeTop(frame with { DeckSlashSequence = d with { ActiveCardId = null, Shuffled = true } });
            ShuffleDeckSequence(frame); AwaitDeckProgramMovement(frame.Id); return;
        }
        ReplaceRuntimeTop(frame with { DeckSlashSequence = d with { DeclaredIds = d.DeclaredIds.Append(next.Id).ToArray(), ActiveCardId = next.Id } });
        ResolveSlashCore(owner, target, next, next.Kind, owner.Seat, countsTowardSlashLimit: false,
            programSkillCardUseFrameId: frame.Id, physicalSourceLocation: CardLocation.DrawPile);
    }
    private void ShuffleDeckSequence(ProgramSkillFrame frame)
    {
        MoveAllCards(CardLocation.DiscardPile, CardLocation.DrawPile, CardMoveReasons.Reshuffle);
        _cardZones.Shuffle(CardLocation.DrawPile, _random);
        AdvanceEventRulesAndQueueFact(new DeckSlashSequenceShuffledEvent(frame.Id, frame.OwnerSeat, frame.DeckSlashSequence!.UsedCount, _cardZones.Count(CardLocation.DrawPile)));
    }
    private void CleanupDeckSlashSequence(ProgramSkillFrame frame)
    {
        if (frame.DeckSlashSequence is { Shuffled: false }) ShuffleDeckSequence(frame);
    }
    private void AssertDeckProgramDrafts(ProgramSkillFrame frame, SkillProgramEffect paused)
    {
        if (frame.DeckEndExchange is { } d)
        {
            if (paused.Op != SkillProgramEffectOp.ExchangeOwnedCardThroughDeckEnd || !IsValidPlayerSeat(d.ProviderSeat) ||
                frame.SelectedCardIds is not [var id] || id != d.CardId || d.OriginalLocation.OwnerSeat != d.ProviderSeat ||
                d.OriginalLocation.Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
                d.Stage is not ("choice" or "draw") || d.DrawIndex < 0 || d.DrawIndex > (d.ProviderSeat == frame.OwnerSeat ? 1 : 2) ||
                (d.Stage == "choice" ? d.PlacedOnTop is not null || d.DrawIndex != 0 : d.PlacedOnTop is null))
                throw new InvalidOperationException("Deck exchange draft lost its frozen entity, owner or ordered stage.");
            if (ReferenceEquals(frame, _resolutionStack.LastOrDefault()) && d.Stage == "choice" && frame.PendingMovementContinuation is null &&
                (_pendingDecision is not { Kind: DecisionKind.ProgramTrigger, IsPrivate: true } p || p.PlayerSeat != frame.OwnerSeat || !AssistedChoicesEqual(p.Choices, DeckEndChoices(frame))))
                throw new InvalidOperationException("Deck exchange lost its private matching prompt.");
        }
        if (frame.DeckSlashSequence is { } s && (paused.Op != SkillProgramEffectOp.UseDeckSlashesThenShuffle ||
            frame.WindowContext?.TargetSeat != s.TargetSeat || s.TargetSeat == frame.OwnerSeat || s.Limit != _playerCount ||
            s.DeclaredIds.Distinct().Count() != s.DeclaredIds.Count || s.UsedCount > s.Limit ||
            s.ActiveCardId is { } active && s.DeclaredIds.LastOrDefault() != active))
            throw new InvalidOperationException("Deck Slash sequence lost its physical membership or declaration cap.");
    }
}
