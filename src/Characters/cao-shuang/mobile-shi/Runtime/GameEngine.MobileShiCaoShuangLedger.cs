namespace CardGame.Core;

public sealed partial class GameEngine
{
    private IReadOnlyList<int> ShiCaoShuangMarkedIds =>
        ProgramEventHistory<ShiCaoShuangSheQuanMarkedEvent>().Select(entry => entry.CardId).Distinct().ToArray();
    private bool ShiCaoShuangIsMarkedCard(int cardId) =>
        ProgramEventHistory<ShiCaoShuangSheQuanMarkedEvent>().Any(entry => entry.CardId == cardId);

    // Effective rank projection of 渐专①: a “奢权”牌 counts as rank K (13) while
    // Cao Shuang holds it and as rank A (1) while any other character holds it.
    private int ShiCaoShuangSheQuanRank(int holderSeat, int cardId) =>
        ShiCaoShuangIsMarkedCard(cardId) && ShiCaoShuangHasSkill(holderSeat, ShiCaoShuangComposition.SheQuanSkill)
            ? 13 : 1;

    private int ShiCaoShuangRoundTriggers(bool dianyi, int owner, string windowKind) => dianyi
        ? ProgramEventHistory<ShiCaoShuangDianYiRoundTriggerEvent>()
            .Count(entry => entry.Round == _roundNumber && entry.WindowKind == windowKind && entry.OwnerSeat == owner)
        : ProgramEventHistory<ShiCaoShuangSheQuanRoundTriggerEvent>()
            .Count(entry => entry.Round == _roundNumber && entry.WindowKind == windowKind && entry.OwnerSeat == owner);
    private int ShiCaoShuangDianYiUsesThisRound(int owner) =>
        ProgramEventHistory<ShiCaoShuangDianYiRoundTriggerEvent>()
            .Count(entry => entry.Round == _roundNumber && entry.OwnerSeat == owner);
    private int ShiCaoShuangDianYiGainsThisRound(int owner) =>
        ProgramEventHistory<ShiCaoShuangDianYiGainEvent>().Where(entry => entry.Round == _roundNumber)
            .Sum(entry => entry.Count);
    private int ShiCaoShuangTimings(string skillId, string? windowKind) =>
        ProgramEventHistory<ShiCaoShuangJianZhuanTimingEvent>().Count(entry => entry.SkillId == skillId &&
            (windowKind is null || entry.WindowKind == windowKind));
    private IReadOnlyList<ShiCaoShuangNiZunUsedEvent> ShiCaoShuangNiZunUseEntriesThisRound(int owner) =>
        ProgramEventHistory<ShiCaoShuangNiZunUsedEvent>().Where(entry => entry.Round == _roundNumber).ToArray();
    private int ShiCaoShuangNiZunUsesThisRound(int owner) => ShiCaoShuangNiZunUseEntriesThisRound(owner).Count;

    private IReadOnlyList<int> ShiCaoShuangMarkedInOtherHands(int owner) => ShiCaoShuangMarkedIds
        .Where(cardId => _cardZones.GetLocation(cardId) is { Zone: CardZoneKind.Hand, OwnerSeat: { } seat } &&
            seat != owner && IsValidPlayerSeat(seat) && _players[seat].IsAlive)
        .ToArray();

    private IReadOnlyList<int> ShiCaoShuangStashableIds(int owner) =>
        ProgramEventHistory<ShiCaoShuangMarkedCardDiscardedEvent>().Where(entry => entry.TurnNumber == _turnNumber)
            .Select(entry => entry.CardId).Distinct()
            .Where(cardId => _cardZones.GetLocation(cardId) is { Zone: CardZoneKind.DiscardPile })
            .ToArray();

    private IReadOnlyList<int> ShiCaoShuangStashedIds(int owner)
    {
        if (!_cardZones.HasPrivatePersistentCards(owner)) return [];
        var location = new CardLocation(CardZoneKind.PrivatePersistentPile, owner, ShiCaoShuangComposition.NiZunPileId);
        return _cardZones.CardsAt(location).Select(card => card.Id).ToArray();
    }

    // The “奢权” marker shows how many marked cards each player currently holds in hand.
    private void SyncShiCaoShuangMarker(int seat)
    {
        if (!HasShiCaoShuangCapability || !IsValidPlayerSeat(seat) || !_players[seat].IsAlive) return;
        var count = ShiCaoShuangMarkedIds.Select(cardId => _cardZones.GetLocation(cardId))
            .Count(location => location.Zone == CardZoneKind.Hand && location.OwnerSeat == seat);
        var player = _players[seat];
        var before = player.Markers.GetValueOrDefault(PlayerMarkerKind.ShiCaoShuangSheQuan);
        if (before == count) return;
        var key = (PlayerMarkerKind.ShiCaoShuangSheQuan, seat);
        if (count == 0)
        { player.Markers.Remove(PlayerMarkerKind.ShiCaoShuangSheQuan); player.MarkerSourceCounts.Remove(key); }
        else
        {
            player.Markers[PlayerMarkerKind.ShiCaoShuangSheQuan] = count;
            player.MarkerSourceCounts[key] = count;
        }
        AdvanceEventRulesAndQueueFact(new PlayerMarkerChangedEvent(_resolutionSequence, seat,
            PlayerMarkerKind.ShiCaoShuangSheQuan, count - before, count, seat,
            "skill-program.mobile:shi-shequan.sync"));
    }

    // Observes completed engine facts only: the owner's used cards per turn,
    // marked cards entering the discard pile, and damage consuming the pending
    // NiZun penalty.
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 67900)]
    private void ObserveShiCaoShuangTurnFacts(IGameEvent payload)
    {
        if (!HasShiCaoShuangCapability || _winner != Winner.None) return;
        switch (payload)
        {
            case CardUsedEvent { CardId: var cardId, SourceSeat: var actor } when _turnNumber > 0 &&
                IsValidPlayerSeat(actor) && _players[actor].IsAlive &&
                ShiCaoShuangHasSkill(actor, ShiCaoShuangComposition.NiZunSkill):
                AdvanceEventRulesAndQueueFact(new ShiCaoShuangOwnerCardUsedEvent(_turnNumber, cardId));
                break;
            case CardMovedEvent { CardId: var cardId, From: var from, To: var to } when ShiCaoShuangIsMarkedCard(cardId):
                if (to.Zone == CardZoneKind.DiscardPile && _turnNumber > 0)
                    AdvanceEventRulesAndQueueFact(new ShiCaoShuangMarkedCardDiscardedEvent(_turnNumber, cardId,
                        from.OwnerSeat ?? -1));
                if (from.OwnerSeat is { } lost) SyncShiCaoShuangMarker(lost);
                if (to.OwnerSeat is { } gained) SyncShiCaoShuangMarker(gained);
                break;
            case DamageAppliedEvent { TargetSeat: var target } when _players[target].IsAlive &&
                ShiCaoShuangHasSkill(target, ShiCaoShuangComposition.NiZunSkill) &&
                ShiCaoShuangNiZunUseEntriesThisRound(target).Count > 0 &&
                ShiCaoShuangNiZunDamagePenaltyPending(target):
                AdvanceEventRulesAndQueueFact(new ShiCaoShuangNiZunPenaltyConsumedEvent(_roundNumber,
                    ShiCaoShuangNiZunUsesThisRound(target)));
                break;
        }
    }

    private bool ShiCaoShuangNiZunDamagePenaltyPending(int target)
    {
        var usesTotal = ShiCaoShuangNiZunUsesThisRound(target);
        if (usesTotal == 0) return false;
        var lastConsumed = ProgramEventHistory<ShiCaoShuangNiZunPenaltyConsumedEvent>()
            .LastOrDefault(entry => entry.Round == _roundNumber);
        return lastConsumed is null || usesTotal - lastConsumed.Count > 0;
    }
}
