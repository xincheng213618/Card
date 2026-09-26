namespace CardGame.Core;

public sealed partial class GameEngine : IActiveActionEffectHost
{
    private ActiveActionInvocation RequireActiveAction(long frameId)
    {
        var frame = _resolutionStack.OfType<ActiveSkillFrame>()
            .LastOrDefault(candidate => candidate.Id == frameId) ??
            throw new InvalidOperationException("The active-action frame is missing.");
        var rule = ActiveActionCatalog.Find(frame.Skill) ??
            throw new InvalidOperationException("The active-action rule is missing.");
        return new ActiveActionInvocation(frameId, frame.SourceSeat, rule,
            ActiveActionCatalog.Selection(frame.Skill),
            new ActiveSkillEffect(frame.Effect, frame.HpCost, frame.DrawCount,
                RecoveryAmount: frame.RecoveryAmount),
            frame.CardIds ?? [], frame.TargetSeats ?? []);
    }

    ActiveGlobalCardUse IActiveActionEffectHost.UseSelectedCardsAsGlobal(int actorSeat,
        IReadOnlyList<int> cardIds, CardKind outputKind)
    {
        var source = _players[actorSeat];
        if (!CanUseGlobalCard(source, outputKind) ||
            cardIds.Count != 2 || cardIds.Distinct().Count() != 2)
            throw new InvalidOperationException("A global conversion requires two distinct playable cards.");
        var hand = GetHand(source);
        var cards = cardIds.Select(id => hand.SingleOrDefault(card => card.Id == id)).ToArray();
        if (cards.Any(card => card is null) || cards[0]!.Suit != cards[1]!.Suit)
            throw new InvalidOperationException("A same-suit global conversion requires matching hand cards.");
        var physicalCards = cards.Cast<Card>().ToArray();
        var targets = Enumerable.Range(1, _playerCount - 1)
            .Select(offset => _players[(actorSeat + offset) % _playerCount])
            .Where(player => player.IsAlive)
            .Select(player => player.Seat)
            .ToArray();
        var representative = physicalCards[0];
        var resolutionId = BeginCardUse(representative, actorSeat, targets,
            outputKind, physicalCardIds: physicalCards.Select(card => card.Id).ToArray());
        MoveCards(physicalCards, CardLocation.Hand(actorSeat), CardLocation.Processing, CardMoveReasons.Use);
        return new ActiveGlobalCardUse(resolutionId, representative, physicalCards[0].Suit, targets);
    }

    void IActiveActionEffectHost.OpenGlobalCardWindow(ActiveGlobalCardUse use, int actorSeat,
        CardKind outputKind, LegalActionKind actionKind, CardKind responseKind) =>
        BeginJizhiOrNullificationWindow(use.ResolutionId, use.Representative,
            actorSeat, use.Targets, actionKind,
            requiredCardKind: responseKind, playedCardKind: outputKind);

    void IActiveActionEffectHost.PublishEvent(IGameEvent gameEvent) => QueueGameEvent(gameEvent);

    int IActiveActionEffectHost.LoseHp(ActiveActionInvocation action)
    {
        var actor = _players[action.ActorSeat];
        actor.Hp = Math.Max(0, actor.Hp - action.Effect.HpCost);
        QueueGameEvent(new SkillHpLostEvent(action.FrameId, actor.Seat, action.Rule.Kind,
            action.Effect.HpCost, actor.Hp));
        return actor.Hp;
    }

    void IActiveActionEffectHost.BeginDying(ActiveActionInvocation action) =>
        BeginActiveSkillDying(action.FrameId, _players[action.ActorSeat]);

    IReadOnlyList<int> IActiveActionEffectHost.Draw(ActiveActionInvocation action, CardMoveReason reason)
    {
        var drawn = DrawCards(_players[action.ActorSeat], action.Effect.DrawCount,
            log: true, reason: reason);
        QueueGameEvent(new SkillCardsDrawnEvent(action.FrameId, action.ActorSeat,
            action.Rule.Kind, drawn));
        return drawn;
    }

    int IActiveActionEffectHost.Discard(ActiveActionInvocation action, CardMoveReason reason,
        bool handOnly)
    {
        var actor = _players[action.ActorSeat];
        var hand = GetHand(actor);
        var equipment = GetEquipment(actor);
        var selected = action.CardIds.Select(id =>
        {
            var handCard = hand.SingleOrDefault(card => card.Id == id);
            return handCard is not null
                ? (Card: handCard, From: CardLocation.Hand(actor.Seat))
                : handOnly
                    ? throw new InvalidOperationException("The selected active cost is no longer in hand.")
                    : (Card: equipment.Single(card => card.Id == id), From: CardLocation.Equipment(actor.Seat));
        }).ToArray();
        if (handOnly)
            MoveCards(selected.Select(item => item.Card).ToArray(), CardLocation.Hand(actor.Seat),
                CardLocation.Processing, reason);
        else
            foreach (var item in selected)
                MoveCard(item.Card, item.From, CardLocation.Processing, reason);
        QueueGameEvent(new SkillCardsDiscardedEvent(action.FrameId, actor.Seat,
            action.Rule.Kind, Array.AsReadOnly(action.CardIds.ToArray())));
        MoveCards(selected.Select(item => item.Card).ToArray(), CardLocation.Processing,
            CardLocation.DiscardPile, reason);
        return selected.Length;
    }

    int IActiveActionEffectHost.Recover(ActiveActionInvocation action, int targetSeat,
        bool childFrame, int? amount)
    {
        var target = _players[targetSeat];
        if (!target.IsAlive || target.Hp >= target.MaxHp)
            return 0;
        var recovery = Math.Min(amount ?? action.Effect.RecoveryAmount, target.MaxHp - target.Hp);
        var frameId = childFrame
            ? BeginRecovery(action.FrameId, action.ActorSeat, targetSeat, recovery)
            : (long?)null;
        try
        {
            target.Hp += recovery;
            if (action.Effect.Kind == ActiveSkillEffectKind.DiscardAndRecoverTargets)
                AddLog("Recovered",
                    $"{target.Name} 因【{action.Rule.Name}】回复至 {target.Hp}/{target.MaxHp} 点体力。",
                    action.ActorSeat, targetSeat);
            QueueGameEvent(new RecoveryAppliedEvent(action.ActorSeat, targetSeat, recovery, target.Hp));
        }
        finally
        {
            if (frameId is { } id) PopResolutionFrame(id, ResolutionFrameKind.Recovery);
        }
        return recovery;
    }

    int IActiveActionEffectHost.Give(ActiveActionInvocation action, int targetSeat,
        CardMoveReason reason)
    {
        var actor = _players[action.ActorSeat];
        var cards = action.CardIds.Select(id => GetHand(actor).Single(card => card.Id == id)).ToArray();
        MoveCards(cards, CardLocation.Hand(actor.Seat), CardLocation.Processing, reason);
        MoveCards(cards, CardLocation.Processing, CardLocation.Hand(targetSeat), reason);
        QueueGameEvent(new SkillCardsGivenEvent(action.FrameId, actor.Seat, targetSeat,
            action.Rule.Kind, Array.AsReadOnly(action.CardIds.ToArray())));
        return cards.Length;
    }

    int IActiveActionEffectHost.ConsumeGiftLedger(ActiveActionInvocation action, int cardCount)
    {
        if (action.Rule.PhaseGiftLedgerId is not { } usageId ||
            action.Rule.PhaseGiftSkillId is not { } skillId)
            return 0;
        var previous = _skillRuntimeState.GetUsage(action.ActorSeat, skillId, usageId,
            SkillUsageScope.Phase);
        for (var i = 0; i < cardCount; i++)
            if (!_skillRuntimeState.TryConsumeUsage(action.ActorSeat, skillId, usageId,
                    SkillUsageScope.Phase, int.MaxValue))
                throw new InvalidOperationException("The active gift phase ledger overflowed.");
        return previous;
    }

    void IActiveActionEffectHost.BeginVirtualDuel(ActiveActionInvocation action,
        int sourceSeat, int targetSeat)
    {
        var attack = new AttackResolution(action.FrameId, sourceSeat, targetSeat,
            card: null, playedCardKind: CardKind.Duel, sourceSkill: action.Rule.Kind);
        _pendingAttack = attack;
        _pendingDuel = new DuelResolution(attack);
        BeginDuelResponse(_pendingDuel);
    }

    IReadOnlyList<int> IActiveActionEffectHost.PrepareFactionSlashRequest(ActiveActionInvocation action,
        int targetSeat)
    {
        var owner = _players[action.ActorSeat];
        var factionId = action.Selection.ProviderFactionId ??
            throw new InvalidOperationException("A faction request needs a provider faction.");
        if (_pendingJijiang is not null || !CanUseFactionSlashRequest(owner, factionId) ||
            !GetActiveSkillValidTargetSeats(owner, action.Rule.Kind).Contains(targetSeat))
            throw new InvalidOperationException("A faction Slash request cannot target this player.");
        var candidateSeats = GetFactionProviderSeats(owner.Seat, factionId);
        _pendingJijiang = new JijiangResolution(action.FrameId,
            JijiangPurpose.ActiveUse, owner.Seat, candidateSeats,
            targetSeat: targetSeat, activeSkillFrameId: action.FrameId);
        SetActiveSkillFrameStep(action.FrameId, ResolutionFrameStep.AwaitingResponse);
        return candidateSeats;
    }

    void IActiveActionEffectHost.AdvanceFactionSlashRequest() => AdvanceJijiangCandidate();

    private void CompleteActiveFactionRequest(JijiangResolution pending)
    {
        if (!ReferenceEquals(_pendingJijiang, pending) || pending.AwaitingProviders ||
            pending.ActiveSkillFrameId is not { } frameId)
            throw new InvalidOperationException("The completed active faction request is invalid.");
        ActiveActionExecutor.CompleteFactionRequest(RequireActiveAction(frameId), this);
        _pendingJijiang = null;
    }

    void IActiveActionEffectHost.PublishSuitChoice(ActiveActionInvocation action,
        int targetSeat, IReadOnlyList<PromptChoice> choices, string prompt)
    {
        var target = _players[targetSeat];
        _pendingDecision = new PendingDecision(
            action.Rule.SuitPromptKind ??
                throw new InvalidOperationException("A suit-choice effect has no prompt kind."),
            targetSeat, prompt, [], [], SourceSeat: action.ActorSeat)
        {
            PromptId = CreatePromptId(),
            Choices = choices,
            TargetSeat = targetSeat
        };
        SetActiveSkillFrameStep(action.FrameId, ResolutionFrameStep.AwaitingResponse);
        _status = target.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    Card IActiveActionEffectHost.RevealRandomHandGift(ActiveActionInvocation action,
        int targetSeat)
    {
        var source = _players[action.ActorSeat];
        var target = _players[targetSeat];
        var sourceHand = GetHand(source).OrderBy(card => card.Id).ToArray();
        if (sourceHand.Length == 0 || !source.IsAlive || !target.IsAlive)
            throw new InvalidOperationException("A revealed gift needs a living source, target and hand card.");
        ClearPendingDecision();
        SetActiveSkillFrameStep(action.FrameId, ResolutionFrameStep.ResolvingEffect);
        var card = sourceHand[_random.Next(sourceHand.Length)];
        MoveCard(card, CardLocation.Hand(source.Seat), CardLocation.Processing,
            action.Rule.TransferReason ??
            throw new InvalidOperationException("A revealed gift has no transfer reason."));
        MoveCard(card, CardLocation.Processing, CardLocation.Hand(targetSeat),
            action.Rule.TransferReason ??
            throw new InvalidOperationException("A revealed gift has no transfer reason."));
        return card;
    }

    bool IActiveActionEffectHost.BeginSkillDamage(ActiveActionInvocation action,
        int targetSeat, Card? card)
    {
        var source = _players[action.ActorSeat];
        var target = _players[targetSeat];
        if (!source.IsAlive || !target.IsAlive)
            return false;
        SetActiveSkillFrameStep(action.FrameId, ResolutionFrameStep.ResolvingEffect);
        var attack = new AttackResolution(action.FrameId, source.Seat, targetSeat, card,
            damageAmount: 1, sourceSkill: action.Rule.Kind,
            damageNatureOverride: DamageNature.Normal);
        _pendingAttack = attack;
        if (!ApplyAttackDamage(attack))
            CompleteAttack(attack);
        return true;
    }

    void IActiveActionEffectHost.MarkUsed(ActiveActionInvocation action) =>
        _players[action.ActorSeat].UsedActiveSkillKinds.Add(action.Rule.Kind);

    void IActiveActionEffectHost.Complete(ActiveActionInvocation action)
    {
        SetActiveSkillFrameStep(action.FrameId, ResolutionFrameStep.Completed);
        QueueGameEvent(new ActiveSkillResolvedEvent(action.FrameId, action.ActorSeat,
            action.Rule.Kind, action.Effect.Kind));
        PopResolutionFrame(action.FrameId, ResolutionFrameKind.ActiveSkill);
    }

    string IActiveActionEffectHost.Name(int seat) => _players[seat].Name;

    void IActiveActionEffectHost.Log(ActiveActionInvocation action, string message, int? targetSeat) =>
        AddLog("ActiveSkill", message, action.ActorSeat, targetSeat);

    void IActiveActionEffectHost.LogCategory(string category, ActiveActionInvocation action,
        string message, int? targetSeat) =>
        AddLog(category, message, action.ActorSeat, targetSeat);
}
