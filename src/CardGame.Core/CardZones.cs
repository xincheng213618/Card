namespace CardGame.Core;

/// <summary>
/// A physical card is always in exactly one zone. Owned zones require a seat;
/// shared zones must not carry one.
/// </summary>
public enum CardZoneKind
{
    DrawPile,
    Hand,
    Processing,
    DiscardPile,
    Equipment,
    Judgment,
    WoodenOxGrain,
    BuquWound,
    Authority,
    OutsideGame,
    Chunlao
}

public readonly record struct CardLocation
{
    [System.Text.Json.Serialization.JsonConstructor]
    public CardLocation(CardZoneKind zone, int? ownerSeat = null)
    {
        var owned = zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.Judgment or CardZoneKind.WoodenOxGrain or CardZoneKind.BuquWound or CardZoneKind.Authority or CardZoneKind.Chunlao;
        if (owned && ownerSeat is null or < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(ownerSeat), $"Zone {zone} requires a non-negative owner seat.");
        }

        if (!owned && ownerSeat is not null)
        {
            throw new ArgumentException($"Shared zone {zone} cannot have an owner seat.", nameof(ownerSeat));
        }

        Zone = zone;
        OwnerSeat = ownerSeat;
    }

    public CardZoneKind Zone { get; }

    public int? OwnerSeat { get; }

    public static CardLocation DrawPile => new(CardZoneKind.DrawPile);

    public static CardLocation Processing => new(CardZoneKind.Processing);

    public static CardLocation DiscardPile => new(CardZoneKind.DiscardPile);

    public static CardLocation OutsideGame => new(CardZoneKind.OutsideGame);

    public static CardLocation Hand(int seat) => new(CardZoneKind.Hand, seat);

    public static CardLocation Equipment(int seat) => new(CardZoneKind.Equipment, seat);

    public static CardLocation Judgment(int seat) => new(CardZoneKind.Judgment, seat);

    public static CardLocation WoodenOxGrain(int seat) => new(CardZoneKind.WoodenOxGrain, seat);

    public static CardLocation BuquWound(int seat) => new(CardZoneKind.BuquWound, seat);

    public static CardLocation Authority(int seat) => new(CardZoneKind.Authority, seat);

    public static CardLocation Chunlao(int seat) => new(CardZoneKind.Chunlao, seat);

    public override string ToString() => OwnerSeat is { } seat ? $"{Zone}[{seat}]" : Zone.ToString();
}

/// <summary>
/// Trusted-host diagnostic projection. It is deliberately separate from GameSnapshot
/// because deck order and hidden card locations must not be sent to a player client.
/// </summary>
public sealed record CardZoneDiagnostic(
    int CardId,
    CardKind CardKind,
    CardLocation Location,
    int ZoneIndex);

/// <summary>A stable, namespaced reason identifier suitable for movement rules and replay.</summary>
public readonly record struct CardMoveReason
{
    [System.Text.Json.Serialization.JsonConstructor]
    public CardMoveReason(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Contains('.'))
        {
            throw new ArgumentException("A card move reason must be a non-empty namespaced id.", nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

public static class CardMoveReasons
{
    public static CardMoveReason InitialDeal { get; } = new("setup.initial-deal");
    public static CardMoveReason Draw { get; } = new("rule.draw");
    public static CardMoveReason Use { get; } = new("card.use");
    public static CardMoveReason Respond { get; } = new("card.respond");
    public static CardMoveReason ResponseFinished { get; } = new("card.response-finished");
    public static CardMoveReason Nullification { get; } = new("card.respond.nullification");
    public static CardMoveReason NullificationFinished { get; } = new("card.respond.nullification-finished");
    public static CardMoveReason IronChainUse { get; } = new("card.effect.iron-chain");
    public static CardMoveReason IronChainFinished { get; } = new("card.effect.iron-chain-finished");
    public static CardMoveReason RecastDiscard { get; } = new("card.recast.discard");
    public static CardMoveReason RecastDraw { get; } = new("card.recast.draw");
    public static CardMoveReason DelayedCardPlace { get; } = new("card.effect.delayed-place");
    public static CardMoveReason DelayedCardFinish { get; } = new("card.effect.delayed-finish");
    public static CardMoveReason DelayedCardTransfer { get; } = new("card.effect.delayed-transfer");
    public static CardMoveReason UseFinished { get; } = new("card.use-finished");
    public static CardMoveReason Reveal { get; } = new("card.public-reveal");
    public static CardMoveReason HarvestPick { get; } = new("card.harvest-pick");
    public static CardMoveReason HarvestDiscard { get; } = new("card.harvest-discard");
    public static CardMoveReason Dismantlement { get; } = new("card.effect.dismantlement");
    public static CardMoveReason DismantlementFinished { get; } = new("card.effect.dismantlement-finished");
    public static CardMoveReason DismantlementJudgment { get; } = new("card.effect.dismantlement-judgment");
    public static CardMoveReason DismantlementJudgmentFinished { get; } = new("card.effect.dismantlement-judgment-finished");
    public static CardMoveReason Snatch { get; } = new("card.effect.snatch");
    public static CardMoveReason SnatchFinished { get; } = new("card.effect.snatch-finished");
    public static CardMoveReason SnatchJudgment { get; } = new("card.effect.snatch-judgment");
    public static CardMoveReason SnatchJudgmentFinished { get; } = new("card.effect.snatch-judgment-finished");
    public static CardMoveReason FireAttackReveal { get; } = new("card.effect.fire-attack-reveal");
    public static CardMoveReason FireAttackDiscard { get; } = new("card.effect.fire-attack-discard");
    public static CardMoveReason FireAttackFinished { get; } = new("card.effect.fire-attack-finished");
    public static CardMoveReason FireAttackDiscardFinished { get; } = new("card.effect.fire-attack-discard-finished");
    public static CardMoveReason BorrowedSwordGive { get; } = new("card.effect.borrowed-sword-give-weapon");
    public static CardMoveReason StoneAxeDiscard { get; } = new("equipment.stone-axe.discard");
    public static CardMoveReason CixiongDiscard { get; } = new("equipment.cixiong-double-swords.discard");
    public static CardMoveReason CixiongDraw { get; } = new("equipment.cixiong-double-swords.draw");
    public static CardMoveReason IceSwordDiscard { get; } = new("equipment.ice-sword.discard");
    public static CardMoveReason TianxiangDiscard { get; } = new("skill.tianxiang.discard");
    public static CardMoveReason TianxiangDraw { get; } = new("skill.tianxiang.draw");
    public static CardMoveReason BuquReveal { get; } = new("skill.buqu.reveal");
    public static CardMoveReason BuquDuplicate { get; } = new("skill.buqu.duplicate");
    public static CardMoveReason BuquDeathDiscard { get; } = new("skill.buqu.death-discard");
    public static CardMoveReason QilinBowDiscard { get; } = new("equipment.qilin-bow.discard-mount");
    public static CardMoveReason MengjinDiscard { get; } = new("skill.mengjin.discard");
    public static CardMoveReason PindianReveal { get; } = new("skill.pindian.reveal");
    public static CardMoveReason PindianFinish { get; } = new("skill.pindian.finish");
    public static CardMoveReason JudgmentReveal { get; } = new("judgment.reveal");
    public static CardMoveReason JudgmentFinish { get; } = new("judgment.finish");
    public static CardMoveReason EquipmentUse { get; } = new("equipment.use");
    public static CardMoveReason EquipmentEnter { get; } = new("equipment.enter");
    public static CardMoveReason EquipmentReplace { get; } = new("equipment.replace");
    public static CardMoveReason WoodenOxStore { get; } = new("equipment.wooden-ox.store");
    public static CardMoveReason WoodenOxTransfer { get; } = new("equipment.wooden-ox.transfer");
    public static CardMoveReason WoodenOxGrainDiscard { get; } = new("equipment.wooden-ox.grain-discard");
    public static CardMoveReason DeathEquipmentDiscard { get; } = new("rule.death-equipment-discard");
    public static CardMoveReason JianxiongClaim { get; } = new("skill.jianxiong.claim-damage-card");
    public static CardMoveReason FeedbackClaim { get; } = new("skill.feedback.claim-damage-card");
    public static CardMoveReason FeedbackTakeSourceCard { get; } = new("skill.feedback.take-source-card");
    public static CardMoveReason YijiDraw { get; } = new("skill.yiji.draw");
    public static CardMoveReason YijiGive { get; } = new("skill.yiji.give-card");
    public static CardMoveReason JiemingDraw { get; } = new("skill.jieming.draw");
    public static CardMoveReason YaowuDraw { get; } = new("skill.yaowu.draw");
    public static CardMoveReason YuanhuDiscard { get; } = new("skill.yuanhu.discard");
    public static CardMoveReason GanglieDiscard { get; } = new("skill.ganglie.discard");
    public static CardMoveReason GuicaiReplace { get; } = new("skill.guicai.replace");
    public static CardMoveReason GuidaoReplace { get; } = new("skill.guidao.replace");
    public static CardMoveReason ProgramJudgmentReplace { get; } = new("skill-program.judgment.replace");
    public static CardMoveReason ProgramJudgmentOldCard { get; } = new("skill-program.judgment.old-card");
    public static CardMoveReason HuangtianGive { get; } = new("skill.huangtian.give-card");
    public static CardMoveReason ZaiqiReveal { get; } = new("skill.zaiqi.reveal");
    public static CardMoveReason ZaiqiGain { get; } = new("skill.zaiqi.gain");
    public static CardMoveReason ZaiqiDiscard { get; } = new("skill.zaiqi.discard");
    public static CardMoveReason JuxiangGain { get; } = new("skill.juxiang.gain");
    public static CardMoveReason LierenGain { get; } = new("skill.lieren.gain");
    public static CardMoveReason TianduClaim { get; } = new("skill.tiandu.claim-judgment");
    public static CardMoveReason LuoshenClaim { get; } = new("skill.luoshen.claim-judgment");
    public static CardMoveReason JizhiDraw { get; } = new("skill.jizhi.draw");
    public static CardMoveReason FanjianGive { get; } = new("skill.fanjian.give-card");
    public static CardMoveReason TuxiGain { get; } = new("skill.tuxi.gain-card");
    public static CardMoveReason QiangxiDiscard { get; } = new("skill.qiangxi.discard-weapon");
    public static CardMoveReason LiuliDiscard { get; } = new("skill.liuli.discard");
    public static CardMoveReason LijianDiscard { get; } = new("skill.lijian.discard");
    public static CardMoveReason ZhenlieDiscard { get; } = new("skill.zhenlie.discard");
    public static CardMoveReason MijiDraw { get; } = new("skill.miji.draw");
    public static CardMoveReason MijiGive { get; } = new("skill.miji.give-card");
    public static CardMoveReason AuthorityDeathDiscard { get; } = new("skill.quanji.death-discard");
    public static CardMoveReason ChunlaoStore { get; } = new("skill.chunlao.store-chun");
    public static CardMoveReason ChunlaoRescue { get; } = new("skill.chunlao.rescue");
    public static CardMoveReason ChunlaoDeathDiscard { get; } = new("skill.chunlao.death-discard");
    public static CardMoveReason GongqiCost { get; } = new("skill.gongqi.cost");
    public static CardMoveReason GongqiDiscard { get; } = new("skill.gongqi.discard-target-card");
    public static CardMoveReason LongyinDiscard { get; } = new("skill.longyin.discard");
    public static CardMoveReason LongyinDraw { get; } = new("skill.longyin.draw");
    public static CardMoveReason JiefanWeaponDiscard { get; } = new("skill.jiefan.discard-weapon");
    public static CardMoveReason JiefanDraw { get; } = new("skill.jiefan.draw");
    public static CardMoveReason ShensuDiscard { get; } = new("skill.shensu.discard-equipment");
    public static CardMoveReason JieyinDiscard { get; } = new("skill.jieyin.discard");
    public static CardMoveReason KujinDraw { get; } = new("skill.kujin.draw");
    public static CardMoveReason ZhihengDiscard { get; } = new("skill.zhiheng.discard");
    public static CardMoveReason ZhihengDraw { get; } = new("skill.zhiheng.draw");
    public static CardMoveReason RendeGive { get; } = new("skill.rende.give-card");
    public static CardMoveReason QingnangDiscard { get; } = new("skill.qingnang.discard");
    public static CardMoveReason HuichunDiscard { get; } = new("skill.huichun.discard");
    public static CardMoveReason HandLimitDiscard { get; } = new("rule.hand-limit-discard");
    public static CardMoveReason DeathDiscard { get; } = new("rule.death-discard");
    public static CardMoveReason LordPenalty { get; } = new("mode.identity.lord-killed-loyalist");
    public static CardMoveReason Reshuffle { get; } = new("deck.reshuffle");
}

public sealed record CardMovementRecord(
    int Sequence,
    int TurnNumber,
    int CardId,
    CardKind CardKind,
    CardLocation From,
    CardLocation To,
    CardMoveReason Reason);

internal readonly record struct CardTransfer(
    int CardId,
    CardLocation From,
    CardLocation To);

internal sealed class CardZoneStore
{
    private readonly Dictionary<CardLocation, List<Card>> _zones = [];
    private readonly Dictionary<CardLocation, IReadOnlyList<Card>> _zoneViews = [];
    private readonly Dictionary<int, CardLocation> _locations = [];

    public CardZoneStore(int playerCount)
    {
        if (playerCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(playerCount));
        }

        AddZone(CardLocation.DrawPile);
        AddZone(CardLocation.Processing);
        AddZone(CardLocation.DiscardPile);
        AddZone(CardLocation.OutsideGame);

        for (var seat = 0; seat < playerCount; seat++)
        {
            AddZone(CardLocation.Hand(seat));
            AddZone(CardLocation.Equipment(seat));
            AddZone(CardLocation.Judgment(seat));
            AddZone(CardLocation.WoodenOxGrain(seat));
            AddZone(CardLocation.BuquWound(seat));
            AddZone(CardLocation.Authority(seat));
            AddZone(CardLocation.Chunlao(seat));
        }
    }

    public int TotalCards => _locations.Count;

    public IReadOnlyList<Card> CardsAt(CardLocation location) =>
        _zoneViews.TryGetValue(location, out var cards)
            ? cards
            : throw new ArgumentOutOfRangeException(nameof(location), location, "The card zone is not registered for this game.");

    public int Count(CardLocation location) => GetZone(location).Count;

    public CardLocation GetLocation(int cardId) =>
        _locations.TryGetValue(cardId, out var location)
            ? location
            : throw new InvalidOperationException($"Card {cardId} is not registered in any zone.");

    public void LoadInitialDeck(IEnumerable<Card> cards)
    {
        ArgumentNullException.ThrowIfNull(cards);
        if (_locations.Count != 0)
        {
            throw new InvalidOperationException("The initial deck has already been loaded.");
        }

        var initialCards = cards.ToArray();
        var duplicate = initialCards
            .GroupBy(card => card.Id)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Duplicate physical card id {duplicate.Key}.");
        }

        var drawPile = GetZone(CardLocation.DrawPile);
        foreach (var card in initialCards)
        {
            _locations.Add(card.Id, CardLocation.DrawPile);
            drawPile.Add(card);
        }
    }

    public Card Move(int cardId, CardLocation from, CardLocation to)
    {
        return MoveBatch([new CardTransfer(cardId, from, to)])[0];
    }

    public IReadOnlyList<Card> MoveMany(
        IEnumerable<int> cardIds,
        CardLocation from,
        CardLocation to)
    {
        ArgumentNullException.ThrowIfNull(cardIds);
        _ = GetZone(from);
        _ = GetZone(to);
        return MoveBatch(cardIds.Select(cardId => new CardTransfer(cardId, from, to)));
    }

    public IReadOnlyList<Card> MoveBatch(IEnumerable<CardTransfer> transfers)
    {
        ArgumentNullException.ThrowIfNull(transfers);
        var batch = transfers.ToArray();
        if (batch.Select(transfer => transfer.CardId).Distinct().Count() != batch.Length)
        {
            throw new InvalidOperationException("A card movement batch cannot contain duplicate ids.");
        }

        var cards = new Card[batch.Length];
        for (var index = 0; index < batch.Length; index++)
        {
            var transfer = batch[index];
            if (transfer.From == transfer.To)
            {
                throw new InvalidOperationException(
                    $"Card {transfer.CardId} cannot move from {transfer.From} to the same zone.");
            }

            var source = GetZone(transfer.From);
            _ = GetZone(transfer.To);
            if (!_locations.TryGetValue(transfer.CardId, out var actual) || actual != transfer.From)
            {
                throw new InvalidOperationException(
                    $"Card {transfer.CardId} is in {actual}, not expected zone {transfer.From}.");
            }

            cards[index] = source.FirstOrDefault(card => card.Id == transfer.CardId) ??
                throw new InvalidOperationException(
                    $"Card {transfer.CardId} location index is inconsistent with zone {transfer.From}.");
        }

        var movedIdsBySource = batch
            .GroupBy(transfer => transfer.From)
            .ToDictionary(
                group => group.Key,
                group => group.Select(transfer => transfer.CardId).ToHashSet());
        foreach (var (sourceLocation, movedIds) in movedIdsBySource)
        {
            GetZone(sourceLocation).RemoveAll(card => movedIds.Contains(card.Id));
        }

        for (var index = 0; index < batch.Length; index++)
        {
            var transfer = batch[index];
            GetZone(transfer.To).Add(cards[index]);
            _locations[transfer.CardId] = transfer.To;
        }

        return cards;
    }

    public Card? MoveTop(CardLocation from, CardLocation to)
    {
        var source = GetZone(from);
        if (source.Count == 0)
        {
            return null;
        }

        return Move(source[^1].Id, from, to);
    }

    public IReadOnlyList<Card> MoveAll(CardLocation from, CardLocation to)
    {
        var cards = GetZone(from).ToArray();
        return MoveMany(cards.Select(card => card.Id), from, to);
    }

    public void Shuffle(CardLocation location, DeterministicRandom random)
    {
        ArgumentNullException.ThrowIfNull(random);
        random.Shuffle(GetZone(location));
    }

    public void ShuffleKeepingSingleKindAtBottom(
        CardLocation location,
        DeterministicRandom random,
        CardKind deferredKind)
    {
        ArgumentNullException.ThrowIfNull(random);
        var zone = GetZone(location);
        var deferred = zone.Where(card => card.Kind == deferredKind).ToArray();
        if (deferred.Length != 1)
        {
            random.Shuffle(zone);
            return;
        }

        zone.Remove(deferred[0]);
        random.Shuffle(zone);
        zone.Insert(0, deferred[0]);
    }

    /// <summary>
    /// Reorders an exact private view of the current draw-pile top. The first
    /// top id becomes the next card drawn; the first bottom id becomes the
    /// deepest card in the pile. No card changes zone, so this operation does
    /// not create a movement record.
    /// </summary>
    public void ReorderDrawPileTop(
        IReadOnlyList<int> viewedTopFirst,
        IReadOnlyList<int> newTopFirst,
        IReadOnlyList<int> newBottomFirst)
    {
        ArgumentNullException.ThrowIfNull(viewedTopFirst);
        ArgumentNullException.ThrowIfNull(newTopFirst);
        ArgumentNullException.ThrowIfNull(newBottomFirst);

        var drawPile = GetZone(CardLocation.DrawPile);
        if (viewedTopFirst.Count > drawPile.Count)
        {
            throw new InvalidOperationException("The viewed draw-pile slice is larger than the draw pile.");
        }

        var actualTopFirst = drawPile
            .TakeLast(viewedTopFirst.Count)
            .Reverse()
            .Select(card => card.Id)
            .ToArray();
        if (!actualTopFirst.SequenceEqual(viewedTopFirst))
        {
            throw new InvalidOperationException("The viewed draw-pile slice is no longer current.");
        }

        var rearranged = newTopFirst.Concat(newBottomFirst).ToArray();
        if (rearranged.Length != viewedTopFirst.Count ||
            rearranged.Distinct().Count() != rearranged.Length ||
            !rearranged.OrderBy(id => id).SequenceEqual(viewedTopFirst.OrderBy(id => id)))
        {
            throw new InvalidOperationException("The reordered draw-pile cards must be an exact partition of the viewed slice.");
        }

        var viewedCards = drawPile
            .TakeLast(viewedTopFirst.Count)
            .ToDictionary(card => card.Id);
        drawPile.RemoveRange(drawPile.Count - viewedTopFirst.Count, viewedTopFirst.Count);
        drawPile.InsertRange(0, newBottomFirst.Select(id => viewedCards[id]));
        drawPile.AddRange(newTopFirst.Reverse().Select(id => viewedCards[id]));
    }

    /// <summary>Places cards just moved to the draw pile at its bottom, in supplied bottom-first order.</summary>
    public void PlaceDrawPileCardsAtBottom(IReadOnlyList<int> cardIds)
    {
        ArgumentNullException.ThrowIfNull(cardIds);
        var drawPile = GetZone(CardLocation.DrawPile);
        if (cardIds.Count == 0) return;
        if (!drawPile.TakeLast(cardIds.Count).Select(card => card.Id).SequenceEqual(cardIds))
            throw new InvalidOperationException("The bottom placement must use the exact most recently moved cards.");
        var moved = drawPile.TakeLast(cardIds.Count).ToArray();
        drawPile.RemoveRange(drawPile.Count - cardIds.Count, cardIds.Count);
        drawPile.InsertRange(0, moved);
    }

    public IReadOnlyList<CardZoneDiagnostic> CreateDiagnostics() =>
        _zones
            .SelectMany(pair => pair.Value.Select((card, index) =>
                new CardZoneDiagnostic(card.Id, card.Kind, pair.Key, index)))
            .OrderBy(card => card.CardId)
            .ToArray();

    public void AssertInvariants(int expectedCardCount)
    {
        var observed = new HashSet<int>();
        var observedCount = 0;
        foreach (var (location, cards) in _zones)
        {
            foreach (var card in cards)
            {
                observedCount++;
                if (!observed.Add(card.Id))
                {
                    throw new InvalidOperationException($"Physical card {card.Id} appears in more than one zone.");
                }

                if (!_locations.TryGetValue(card.Id, out var indexedLocation) || indexedLocation != location)
                {
                    throw new InvalidOperationException($"Physical card {card.Id} has an inconsistent location index.");
                }
            }
        }

        if (observedCount != _locations.Count || observedCount != expectedCardCount)
        {
            throw new InvalidOperationException(
                $"Card conservation failed: zones={observedCount}, index={_locations.Count}, expected={expectedCardCount}.");
        }
    }

    private void AddZone(CardLocation location)
    {
        var cards = new List<Card>();
        _zones.Add(location, cards);
        _zoneViews.Add(location, cards.AsReadOnly());
    }

    private List<Card> GetZone(CardLocation location) =>
        _zones.TryGetValue(location, out var cards)
            ? cards
            : throw new ArgumentOutOfRangeException(nameof(location), location, "The card zone is not registered for this game.");
}
