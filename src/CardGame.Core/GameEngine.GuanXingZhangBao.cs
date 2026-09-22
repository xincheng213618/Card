namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string FuhunSkillId = "classic:fuhun";
    private const string FuhunWushengSkillId = "classic:wusheng";
    private const string FuhunPaoxiaoSkillId = "classic:paoxiao";
    private const string FuhunGrantUsageId = "parent-skills-granted";
    private const string FuhunViewAsBindingId = "two-hand-cards-as-slash";

    private bool UsesFormalGuanXingZhangBao =>
        HasClassicGeneralPackage(new Version(1, 89, 0));

    private CardConversionSource CreateFuhunConversionSource(CharacterState owner) =>
        new(
            FuhunSkillId,
            FuhunViewAsBindingId,
            owner.Seat,
            $"seat-{owner.Seat}:{FuhunSkillId}");

    private bool IsRuntimeAcquiredSkill(CharacterState player, string skillId) =>
        player.AcquiredSkillIds.Contains(skillId) ||
        player.TurnGrantedSkillIds.Contains(skillId, StringComparer.Ordinal);

    private bool CanUseFuhunSlash(CharacterState source)
    {
        if (!UsesFormalGuanXingZhangBao ||
            !source.IsAlive ||
            !HasRuntimeSkill(source, FuhunSkillId) ||
            GetFuhunEligibleHandCards(source).Count < 2 ||
            source.TianyiLostThisTurn ||
            SlashKinds.All(kind => IsCardUseForbidden(source.Seat, kind, CardActionType.Use)))
        {
            return false;
        }

        return _players.Any(target => target.IsAlive && target.Seat != source.Seat &&
            SlashKinds.Any(kind => CanSpendSlashUse(source, target, ignoresCount: false, kind)));
    }

    private bool CanUseFuhunConversion(CharacterState source) =>
        UsesFormalGuanXingZhangBao &&
        source.IsAlive &&
        HasRuntimeSkill(source, FuhunSkillId) &&
        GetFuhunEligibleHandCards(source).Count >= 2 &&
        SlashKinds.Any(kind => !IsCardUseForbidden(source.Seat, kind, CardActionType.Use));

    private IReadOnlyList<Card> GetFuhunEligibleHandCards(CharacterState player) =>
        GetHand(player)
            .Where(card => !IsQianxiHandCardRestricted(player, card))
            .OrderBy(card => card.Id)
            .ToArray();

    private IReadOnlySet<int> GetFuhunTargetSeats(CharacterState source) =>
        !CanUseFuhunSlash(source)
            ? new HashSet<int>()
            : _players
                .Where(target => CanUseVirtualSlashTarget(source, target))
                .Select(target => target.Seat)
                .ToHashSet();

    private IReadOnlyList<IReadOnlyList<Card>> GetFuhunSlashPairs(CharacterState responder)
    {
        if (!CanUseFuhunConversion(responder))
        {
            return [];
        }

        var hand = GetFuhunEligibleHandCards(responder);
        var pairs = new List<IReadOnlyList<Card>>();
        for (var first = 0; first < hand.Count - 1; first++)
        {
            for (var second = first + 1; second < hand.Count; second++)
            {
                pairs.Add(Array.AsReadOnly(new[] { hand[first], hand[second] }));
            }
        }
        return pairs;
    }

    private IReadOnlyList<Card>? FindFuhunSlashPair(
        CharacterState responder,
        IReadOnlyList<int> requestedCardIds)
    {
        if (requestedCardIds.Count != 2 || requestedCardIds.Distinct().Count() != 2)
        {
            return null;
        }

        var requested = requestedCardIds.Order().ToArray();
        return GetFuhunSlashPairs(responder).FirstOrDefault(pair =>
            pair.Select(card => card.Id).Order().SequenceEqual(requested));
    }

    private void ResolveFuhunSlash(
        CharacterState source,
        CharacterState target,
        IReadOnlyList<Card> physicalCards,
        BorrowedSwordResolution? borrowedSword = null,
        JijiangResolution? activeJijiang = null,
        bool enforceOwnTurnSlashLimit = true)
    {
        if (!(enforceOwnTurnSlashLimit ? CanUseFuhunSlash(source) : CanUseFuhunConversion(source)) ||
            physicalCards.Count != 2 ||
            physicalCards.Select(card => card.Id).Distinct().Count() != 2 ||
            physicalCards.Any(card => _cardZones.GetLocation(card.Id) != CardLocation.Hand(source.Seat)) ||
            !(borrowedSword is null
                ? CanUseVirtualSlashTarget(source, target)
                : IsLegalBorrowedSwordSlashTarget(source, target)))
        {
            throw new InvalidOperationException("Fuhun became illegal before resolution.");
        }

        ResolveSlashCore(
            source,
            target,
            physicalCards[0],
            CardKind.Slash,
            source.Seat,
            activeJijiang,
            borrowedSword,
            physicalCards,
            conversionSource: CreateFuhunConversionSource(source));
    }

    private void MoveFuhunResponseCards(
        CharacterState responder,
        IReadOnlyList<Card> pair,
        long resolutionId,
        int responseTargetSeat,
        int? actorSeat = null)
    {
        if (FindFuhunSlashPair(responder, pair.Select(card => card.Id).ToArray()) is null)
        {
            throw new InvalidOperationException("The Fuhun Slash pair is no longer legal.");
        }

        var costs = pair.Select(card => new CardActionCost(
            card.Id,
            card.Kind,
            CardLocation.Hand(responder.Seat))).ToArray();
        foreach (var card in pair)
        {
            MoveCard(
                card,
                CardLocation.Hand(responder.Seat),
                CardLocation.Processing,
                CardMoveReasons.Respond);
            QueueGameEvent(new CardRespondedEvent(
                card.Id,
                responder.Seat,
                responseTargetSeat,
                CardKind.Slash));
        }

        var actionActorSeat = actorSeat ?? responder.Seat;
        var parent = _resolutionStack.OfType<CardUseFrame>()
            .LastOrDefault(frame => frame.Id == resolutionId);
        var action = new CardActionContext(
            ++_cardActionSequence,
            parent?.Action?.ActionId,
            CardActionType.Response,
            actionActorSeat,
            responder.Seat,
            actionActorSeat == responder.Seat ? null : actionActorSeat,
            responder.Seat,
            responseTargetSeat,
            CardKind.Slash,
            [],
            costs,
            [CreateFuhunConversionSource(responder)]);
        QueueGameEvent(new CardActionAcceptedEvent(action));
        QueueGameEvent(new FuhunConvertedEvent(
            resolutionId,
            responder.Seat,
            Array.AsReadOnly(pair.Select(card => card.Id).ToArray()),
            IsUse: false,
            responseTargetSeat));
    }

    private void FinishFuhunResponseCards(IReadOnlyList<Card> pair)
    {
        foreach (var card in pair)
        {
            MoveCard(
                card,
                CardLocation.Processing,
                CardLocation.DiscardPile,
                CardMoveReasons.ResponseFinished);
        }
    }

    private void ResolveDuelFuhunResponse(
        DuelResolution duel,
        CharacterState responder,
        IReadOnlyList<Card> pair)
    {
        if (!ReferenceEquals(_pendingDuel, duel) || responder.Seat != duel.ResponderSeat)
        {
            throw new InvalidOperationException("The Fuhun Duel response is not current.");
        }

        MoveFuhunResponseCards(responder, pair, duel.ResolutionId, duel.OpponentSeat);
        AddLog(
            "CardResponded",
            $"{responder.Name} 发动【父魂】，将两张手牌当【杀】应战【决斗】。",
            responder.Seat,
            duel.OpponentSeat);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(responder.Seat, CardKind.Slash);
        QueueGameEvent(new DuelResponseEvent(
            duel.ResolutionId,
            responder.Seat,
            UsedSlash: true,
            SlashCardId: pair[0].Id,
            ResponseCardKind: CardKind.Slash));
        FinishFuhunResponseCards(pair);
        ContinueDuelAfterSuccessfulSlash(duel, responder.Seat);
    }

    private void ResolveGroupFuhunResponse(
        GroupCardResolution group,
        CharacterState responder,
        IReadOnlyList<Card> pair)
    {
        if (!ReferenceEquals(_pendingGroupCard, group) ||
            group.Effect != GroupCardEffect.ResponseAttack ||
            group.RequiredCardKind != CardKind.Slash ||
            group.CurrentAttack is not { } attack ||
            responder.Seat != attack.TargetSeat)
        {
            throw new InvalidOperationException("The Fuhun group response is not current.");
        }

        MoveFuhunResponseCards(responder, pair, group.ResolutionId, group.SourceSeat);
        AddLog(
            "CardResponded",
            $"{responder.Name} 发动【父魂】，将两张手牌当【杀】响应【{group.Card.DisplayName}】。",
            responder.Seat,
            group.SourceSeat);
        MarkSlashUsedOrPlayedDuringCurrentPlayPhase(responder.Seat, CardKind.Slash);
        QueueGameEvent(new GroupResponseEvent(
            group.ResolutionId,
            group.Card.Kind,
            CardKind.Slash,
            responder.Seat,
            UsedResponse: true,
            ResponseCardId: pair[0].Id,
            ResponseCardKind: CardKind.Slash));
        FinishFuhunResponseCards(pair);
        CompleteAttack(attack);
    }

    private void ApplyFuhunParentSkillGrant(
        AttackResolution attack,
        long damageFrameId,
        int amount)
    {
        if (!UsesFormalGuanXingZhangBao ||
            amount <= 0 ||
            !attack.IsFuhunSlash ||
            _phase != TurnPhase.Play)
        {
            return;
        }

        var owner = _players[attack.SourceSeat];
        if (!owner.IsAlive || !HasRuntimeSkill(owner, FuhunSkillId) ||
            !_skillRuntimeState.TryConsumeUsage(
                owner.Seat,
                FuhunSkillId,
                FuhunGrantUsageId,
                SkillUsageScope.Turn,
                limit: 1))
        {
            return;
        }

        var granted = Array.AsReadOnly(new[] { FuhunWushengSkillId, FuhunPaoxiaoSkillId });
        var sourceId = $"turn:{_turnNumber}:{FuhunSkillId}";
        foreach (var skillId in granted)
        {
            var grantId = $"{sourceId}:{skillId}";
            owner.SkillGrants.Grant(new SkillGrant(grantId, skillId, grantId, sourceId));
        }
        QueueGameEvent(new FuhunSkillsGrantedEvent(damageFrameId, owner.Seat, granted));
        AddLog(
            "SkillTriggered",
            $"{owner.Name} 以【父魂】转化的【杀】造成伤害，本回合获得【武圣】和【咆哮】。",
            owner.Seat,
            attack.TargetSeat);
    }
}
