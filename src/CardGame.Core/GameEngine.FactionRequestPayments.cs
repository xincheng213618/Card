namespace CardGame.Core;

public sealed partial class GameEngine
{
    private bool IsFactionRequestCostPrompt(FactionCardRequestHandle pending) =>
        pending.PolicySource?.DiscardCost == 1 && !pending.CostPaid && !pending.ProviderRewarded &&
        pending.AwaitingProviders && pending.CandidateIndex == 0 &&
        _pendingDecision is { Kind: DecisionKind.RespondSlash, IsPrivate: true } prompt &&
        prompt.PlayerSeat == pending.OwnerSeat && AssistedChoicesEqual(prompt.Choices, FactionRequestCostChoices(pending));

    private IReadOnlyList<PromptChoice> FactionRequestCostChoices(FactionCardRequestHandle pending)
    {
        var owner = _players[pending.OwnerSeat];
        return GetHand(owner).Concat(GetEquipment(owner)).Select(card => new PromptChoice(
            new ChoiceId("faction-request-cost." + card.Id), "弃置" + card.DisplayName + "，请求同势力角色提供杀。",
            [card.Id], [], new Dictionary<string, string> { ["response"] = "faction-request-cost", ["skill"] = GetFactionRequestSkillId(pending) })).ToArray();
    }

    private bool TryPublishFactionRequestCost(FactionCardRequestHandle pending)
    {
        if (pending.PolicySource?.DiscardCost is not > 0 || pending.CostPaid) return false;
        if (pending.PolicySource.DiscardCost != 1 || pending.CandidateSeats.Count == 0)
            throw new InvalidOperationException("A paid faction request requires one physical cost and another eligible provider.");
        var choices = FactionRequestCostChoices(pending);
        if (choices.Count == 0) throw new InvalidOperationException("The requester has no physical hand or equipment card to pay.");
        _pendingDecision = new(DecisionKind.RespondSlash, pending.OwnerSeat, "发动主公技：先弃置一张牌。", [], [], pending.OwnerSeat)
        { PromptId = CreatePromptId(), IsPrivate = true, RequiredCardKind = CardKind.Slash, Choices = choices };
        _status = _players[pending.OwnerSeat].IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return true;
    }

    private EngineRunResult ResolveFactionRequestCostChoice(PromptChoice selected, bool advanceToHumanBoundary)
    {
        var pending = ActiveFactionCardRequest ?? throw new InvalidOperationException("Faction request payment lost its pending request.");
        var canonical = FactionRequestCostChoices(pending).SingleOrDefault(choice => choice.Id == selected.Id);
        if (pending.CostPaid || pending.PolicySource?.DiscardCost != 1 ||
            _pendingDecision is not { Kind: DecisionKind.RespondSlash } prompt || prompt.PlayerSeat != pending.OwnerSeat ||
            canonical is null || !AssistedChoicesEqual([canonical], [selected]))
            throw new InvalidOperationException("Faction request payment changed its physical owner cost.");
        var cardId = canonical.Cards.Single(); var from = _cardZones.GetLocation(cardId);
        var card = _cardZones.CardsAt(from).Single(item => item.Id == cardId);
        ClearPendingDecision();
        pending.CostPaid = true;
        MoveCard(card, from, CardLocation.DiscardPile, new CardMoveReason("program.faction-request.cost"));
        AdvanceFactionSlashCandidate();
        AdvanceRulesAndPublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private sealed record FactionProviderRewardReceipt(long OwnerFrameId, int DrawCount);
    private FactionProviderRewardReceipt CaptureFactionProviderReward(FactionCardRequestHandle pending) =>
        new(pending.OwnerFrameId, pending.ProviderRewarded ? 0 : pending.PolicySource?.ProviderDrawCount ?? 0);

    private void RewardFactionRequestProvider(FactionProviderRewardReceipt receipt, int providerSeat)
    {
        if (receipt.DrawCount <= 0) return;
        if (_resolutionStack.Any(frame => frame.Id == receipt.OwnerFrameId))
            UpdateCardContinuations(receipt.OwnerFrameId, state => state with
                { FactionCardRequest = state.FactionCardRequest is { } request ? request with { ProviderRewarded = true } : null });
        if (_players[providerSeat].IsAlive)
            DrawCards(_players[providerSeat], receipt.DrawCount, true,
                new CardMoveReason("program.faction-request.provider-reward"));
    }
}
