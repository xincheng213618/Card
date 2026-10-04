namespace CardGame.Core;

public sealed record DeferredPublicPileDeposit(long FrameId, int OwnerSeat, int ProviderSeat,
    string SkillId, string SkillInstanceId, IReadOnlyList<int> CardIds, int CreatedTurn);
public sealed record DeferredPublicPileDepositedEvent(DeferredPublicPileDeposit Deposit) : IGameEvent;
public sealed record DeferredPublicPileObtainedEvent(int OwnerSeat, int ProviderSeat, IReadOnlyList<int> CardIds) : IGameEvent;

public sealed partial class GameEngine
{
    private readonly List<DeferredPublicPileDeposit> _deferredPublicPileDeposits = [];
    private readonly Dictionary<long, (int Seat, IReadOnlyList<int> Ids)> _participantTopViews = [];

    private CardSnapshot[] GetPrivatelyViewedCards(int viewerSeat)
    {
        var topCards = _participantTopViews.Values
            .Where(view => view.Seat == viewerSeat)
            .SelectMany(view => view.Ids)
            .Select(id => _cardZones.CardsAt(CardLocation.Processing).Single(card => card.Id == id))
            .Select(ToSnapshot);
        return topCards.Concat(RequestedDeckBasicPrivateCards(viewerSeat)).Concat(GetOfferedPrivatelyViewedCards(viewerSeat)).Concat(GetQuotaPrivatelyViewedCards(viewerSeat)).Concat(GetConvertingGiftPrivatelyViewedCards(viewerSeat)).Concat(GetExactTopPrivatelyViewedCards(viewerSeat)).Concat(GetPopulationTopPrivatelyViewedCards(viewerSeat)).Concat(GetParticipantPrivatelyViewedCards(viewerSeat)).DistinctBy(card => card.Id).ToArray();
    }

    // Checkpoints replay the complete accepted command prefix. The event stream is never
    // trimmed; include pending movements, so a gain inside an unresolved window counts.
    private IReadOnlyList<Card> GetDiscardEligibleHand(CharacterState owner)
    {
        var hand = GetHand(owner);
        var exactIds = _turnCardUseEffects.GetHandLimitExemptCardIds(_turnNumber, _turnProgression.OwnerSeat, owner.Seat);
        var exemptKinds = _turnCardUseEffects.GetHandLimitExemptCardKinds(_turnNumber, _turnProgression.OwnerSeat, owner.Seat);
        if (!HasCardPolicy(owner, SkillProgramCardPolicyKind.IgnoreTurnObtainedHandCardsForDiscard))
            return exactIds.Count == 0 && exemptKinds.Count == 0 ? hand :
                hand.Where(card => !exactIds.Contains(card.Id) && !IsEffectiveHandKindExempt(owner, card, exemptKinds)).ToArray();
        var obtained = EventsSinceLastBoundary(item => item is TurnStartedEvent)
            .OfType<CardMovedEvent>().Where(item => item.To == CardLocation.Hand(owner.Seat) && item.From != item.To)
            .Select(item => item.CardId).ToHashSet();
        return hand.Where(card => !obtained.Contains(card.Id) && !exactIds.Contains(card.Id) && !IsEffectiveHandKindExempt(owner, card, exemptKinds)).ToArray();
    }

    private void ConsumeSkippedNextTurnDrawBenefits(CharacterState current)
    {
        if (current.IsFaceDown)
            _programNextTurnRuleModifiers.RemoveAll(item => item.TargetSeat == current.Seat && item.Query == SkillRuleQuery.DrawCount);
    }

    private sealed partial class ProgramSkillHost : IDeferredCardProgramHost
    {
        public SkillProgramStepOutcome ViewTopCardsAndObtainMatchingCards(ProgramSkillFrame frame, int seat, int amount, IReadOnlyList<SkillProgramCardCategory> categories) =>
            engine.BeginParticipantTopView(frame, seat, amount);
        public SkillProgramStepOutcome DepositBoundCardsUntilNextTurn(ProgramSkillFrame frame, string bind) => engine.DepositDeferredPile(frame, bind);
        public SkillProgramStepOutcome ObtainDeferredPile(ProgramSkillFrame frame) => engine.ObtainDeferredPile(frame);
        public void RewardDeferredProviders(ProgramSkillFrame frame) => engine.RewardDeferredProviders(frame);
    }

    private SkillProgramStepOutcome BeginParticipantTopView(ProgramSkillFrame frame, int seat, int amount)
    {
        if (!_players[seat].IsAlive || seat == frame.OwnerSeat || _participantTopViews.ContainsKey(frame.Id))
            throw new InvalidOperationException("A private top-card view requires a living other participant and no active view.");
        var cards = new List<Card>();
        for (var index = 0; index < amount; index++)
            if (DrawOneToProcessing(new CardMoveReason("skill-program.private-top.view")) is { } card) cards.Add(card); else break;
        if (cards.Count == 0) return SkillProgramStepOutcome.Continue;
        _participantTopViews.Add(frame.Id, (seat, cards.Select(card => card.Id).ToArray()));
        var skill = _contentRegistry.GetSkill(frame.SkillId);
        var choice = new PromptChoice(new ChoiceId($"participant-top.frame-{frame.Id}.confirm"),
            string.Join("、", cards.Select(card => card.DisplayName + "（" + card.RankText + "）")), cards.Select(card => card.Id).ToArray(), [],
            new Dictionary<string, string> { ["program-action"] = "participant-top-view", ["frame-id"] = frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) });
        _pendingDecision = new(DecisionKind.ProgramTrigger, seat, "观看牌堆顶的牌并确认。", cards.Select(card => card.Id).ToArray(), [], frame.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, TargetSeat = seat, Choices = [choice], SkillPrompt = new(frame.SkillId, skill.Name, skill.Name + " · 观看牌堆顶", skill.Description) };
        _status = _players[seat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return SkillProgramStepOutcome.AwaitChoice;
    }

    private void ResolveParticipantTopView(PromptChoice selected)
    {
        var frame = _resolutionStack.LastOrDefault() as ProgramSkillFrame ?? throw new InvalidOperationException("Private top view lost its frame.");
        var effect = ProgramInstructionResolver.Default.Resolve(frame, _contentRegistry.GetSkill(frame.SkillId).Program!).GetPausedInstruction(frame.InstructionIndex).Effect;
        if (effect.Op != SkillProgramEffectOp.ViewTopCardsAndObtainMatchingCards || !_participantTopViews.TryGetValue(frame.Id, out var view) ||
            frame.SelectedTargetSeats.Single() != view.Seat || !selected.Cards.SequenceEqual(view.Ids) ||
            selected.Parameters.GetValueOrDefault("frame-id") != frame.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) ||
            view.Ids.Any(id => _cardZones.GetLocation(id) != CardLocation.Processing))
            throw new InvalidOperationException("Private top-card confirmation does not match the frozen participant view.");
        var cards = view.Ids.Select(id => _cardZones.CardsAt(CardLocation.Processing).Single(card => card.Id == id)).ToArray();
        var gained = cards.Where(card => MatchesProgramCardCategory(card.Kind, effect.CardCategories)).ToArray();
        var remaining = cards.Except(gained).ToArray();
        ClearPendingDecision(); _participantTopViews.Remove(frame.Id);
        ReplaceRuntimeTop(frame with { PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        // Return non-matching cards first, in their original top-first order. Gain
        // observers therefore cannot draw the unreconciled processing remainder.
        MoveCards(remaining, CardLocation.Processing, CardLocation.DrawPile, new("skill-program.private-top.return"));
        _cardZones.PlaceDrawPileCardsAtTop(remaining.Select(card => card.Id).ToArray());
        MoveCards(gained, CardLocation.Processing, CardLocation.Hand(view.Seat), new("skill-program.private-top.obtain"));
        if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(frame.Id);
    }

    private SkillProgramStepOutcome DepositDeferredPile(ProgramSkillFrame frame, string bind)
    {
        var set = GetProgramCardSet(frame, bind);
        var provider = frame.SelectedTargetSeats.Single();
        if (provider == frame.OwnerSeat || set.CardIds.Count > 3 || set.CardIds.Where((id, index) =>
                set.SourceLocations[index].OwnerSeat != provider || set.SourceLocations[index].Zone is not (CardZoneKind.Hand or CardZoneKind.Equipment) ||
                _cardZones.GetLocation(id) != set.SourceLocations[index]).Any())
            throw new InvalidOperationException("Deferred pile deposit must use the provider's actual selected hand/equipment cards.");
        if (set.CardIds.Count == 0) return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(frame with { PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        MoveProgramCardsFromMultipleSources(set.CardIds, new(CardZoneKind.PublicDeferredPile, frame.OwnerSeat), new("skill-program.deferred-pile.deposit"));
        var realIds = set.CardIds.Where(id => _cardZones.GetLocation(id) == new CardLocation(CardZoneKind.PublicDeferredPile, frame.OwnerSeat)).ToArray();
        if (realIds.Length > 0)
        {
            var deposit = new DeferredPublicPileDeposit(frame.Id, frame.OwnerSeat, provider, frame.SkillId, frame.SkillInstanceId, realIds, _turnNumber);
            _deferredPublicPileDeposits.Add(deposit); AdvanceEventRulesAndQueueFact(new DeferredPublicPileDepositedEvent(deposit));
        }
        if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private SkillProgramStepOutcome ObtainDeferredPile(ProgramSkillFrame frame)
    {
        var deposits = _deferredPublicPileDeposits.Where(item => item.OwnerSeat == frame.OwnerSeat && item.SkillId == frame.SkillId && item.CreatedTurn < _turnNumber).ToArray();
        if (deposits.Length == 0) return SkillProgramStepOutcome.Continue;
        var pile = new CardLocation(CardZoneKind.PublicDeferredPile, frame.OwnerSeat);
        var rewards = new List<ProgramDeferredProviderReward>();
        foreach (var deposit in deposits)
        {
            _deferredPublicPileDeposits.Remove(deposit);
            var ids = deposit.CardIds.Where(id => _cardZones.GetLocation(id) == pile).ToArray();
            rewards.Add(new(deposit.ProviderSeat, ids.Length));
            AdvanceEventRulesAndQueueFact(new DeferredPublicPileObtainedEvent(frame.OwnerSeat, deposit.ProviderSeat, ids));
        }
        var actual = deposits.SelectMany(item => item.CardIds).Distinct().Where(id => _cardZones.GetLocation(id) == pile).ToArray();
        frame = frame with { DeferredProviderRewards = Array.AsReadOnly(rewards.ToArray()) };
        ReplaceRuntimeTop(frame);
        if (actual.Length == 0) return SkillProgramStepOutcome.Continue;
        ReplaceRuntimeTop(frame with { PendingMovementContinuation = new(frame.OwnerSeat, 0, null) });
        MoveCards(actual.Select(id => _cardZones.CardsAt(pile).Single(card => card.Id == id)).ToArray(), pile, CardLocation.Hand(frame.OwnerSeat), new("skill-program.deferred-pile.obtain"));
        if (!TryBeginCardsMovedProgramWindow()) ReturnRuntimeProgramMovement(frame.Id);
        return SkillProgramStepOutcome.AwaitChild;
    }

    private void RewardDeferredProviders(ProgramSkillFrame frame)
    {
        if (frame.DeferredProviderRewards is not { } rewards) return;
        // Consume before drawing: a nested window may suspend, but cannot pay again.
        ReplaceRuntimeTop(frame with { DeferredProviderRewards = null });
        foreach (var reward in rewards)
            if (_players[reward.Seat].IsAlive && reward.Count > 0)
                DrawProgramCards(frame.Id, reward.Seat, reward.Count, null, null, SkillProgramCardSetVisibility.Private, new("skill-program.deferred-pile.provider-reward"));
    }

    private void CleanupLostDeferredPileSources()
    {
        foreach (var deposit in _deferredPublicPileDeposits.Where(item => !_players[item.OwnerSeat].IsAlive ||
                     !_players[item.OwnerSeat].SkillGrants.Grants.Any(grant => grant.SkillId == item.SkillId && grant.SkillInstanceId == item.SkillInstanceId)).ToArray())
        { DiscardDeferredDeposit(deposit); }
    }
    private void DiscardDeferredDeposit(DeferredPublicPileDeposit deposit)
    {
        _deferredPublicPileDeposits.Remove(deposit);
        var pile = new CardLocation(CardZoneKind.PublicDeferredPile, deposit.OwnerSeat);
        var cards = deposit.CardIds.Where(id => _cardZones.GetLocation(id) == pile).Select(id => _cardZones.CardsAt(pile).Single(card => card.Id == id)).ToArray();
        MoveCards(cards, pile, CardLocation.DiscardPile, new("skill-program.deferred-pile.invalid-source"));
    }
}
