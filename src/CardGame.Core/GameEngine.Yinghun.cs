namespace CardGame.Core;

public sealed partial class GameEngine
{
    private enum YinghunStage
    {
        Offer,
        Discard,
        AwaitingEquipmentLossTriggers
    }

    private sealed class YinghunResolution(int ownerSeat, int lostHp)
    {
        public int OwnerSeat { get; } = ownerSeat;
        public int LostHp { get; } = lostHp;
        public YinghunStage Stage { get; set; }
        public int TargetSeat { get; set; } = -1;
        public int DrawCount { get; set; }
        public int DiscardCount { get; set; }
        public List<int> DiscardedCardIds { get; } = [];
    }

    private void BeginYinghunChoice(CharacterState owner)
    {
        var lostHp = owner.MaxHp - owner.Hp;
        var targets = _players.Where(player => player.IsAlive && player.Seat != owner.Seat)
            .OrderBy(player => player.Seat).ToArray();
        var choices = new List<PromptChoice>();
        foreach (var target in targets)
        {
            choices.Add(CreateYinghunOfferChoice(owner, target, lostHp, drawMany: true));
            choices.Add(CreateYinghunOfferChoice(owner, target, lostHp, drawMany: false));
        }
        choices.Add(new PromptChoice(new ChoiceId($"yinghun.skip.turn-{_turnNumber}"),
            "不发动【英魂】。", [], [],
            new Dictionary<string, string> { ["action"] = "yinghun-skip" }));

        _pendingYinghun = new YinghunResolution(owner.Seat, lostHp) { Stage = YinghunStage.Offer };
        _pendingDecision = new PendingDecision(DecisionKind.Yinghun, owner.Seat,
            $"准备阶段开始：你已损失 {lostHp} 点体力，是否发动【英魂】？", [],
            targets.Select(target => target.Seat).ToArray(), SourceSeat: owner.Seat)
        {
            PromptId = CreatePromptId(),
            Choices = choices.AsReadOnly()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        PublishState();
    }

    private PromptChoice CreateYinghunOfferChoice(
        CharacterState owner, CharacterState target, int lostHp, bool drawMany)
    {
        var drawCount = drawMany ? lostHp : 1;
        var discardCount = drawMany ? 1 : lostHp;
        return new PromptChoice(
            new ChoiceId($"yinghun.target-{target.Seat}.{(drawMany ? "draw-many" : "discard-many")}.turn-{_turnNumber}"),
            $"令 {target.Name} 摸 {drawCount} 张牌，然后弃置 {discardCount} 张牌。",
            [], [target.Seat],
            new Dictionary<string, string>
            {
                ["action"] = "yinghun-use",
                ["mode"] = drawMany ? "draw-many" : "discard-many",
                ["owner-seat"] = owner.Seat.ToString(System.Globalization.CultureInfo.InvariantCulture)
            });
    }

    private CommandResult SubmitYinghunPromptAnswer(PromptChoice selected)
    {
        if (_pendingYinghun is null || _pendingDecision is not { Kind: DecisionKind.Yinghun })
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的英魂窗口。");

        var action = selected.Parameters.GetValueOrDefault("action");
        var valid = _pendingYinghun.Stage switch
        {
            YinghunStage.Offer =>
                action == "yinghun-skip" && selected.Cards.Count == 0 && selected.Targets.Count == 0 ||
                action == "yinghun-use" && selected.Cards.Count == 0 && selected.Targets.Count == 1 &&
                selected.Parameters.GetValueOrDefault("mode") is "draw-many" or "discard-many",
            YinghunStage.Discard => action == "yinghun-discard" && selected.Cards.Count == 1 &&
                                    selected.Targets.Count == 0,
            _ => false
        };
        if (!valid) return Reject(CommandErrorCode.InvalidChoice, "英魂选择与当前结算阶段不符。");

        return Accept(() =>
        {
            ResolveYinghunChoice(selected);
            PublishState();
            return _options.AdvanceAfterHumanCommands ? AdvanceToHumanBoundary() : BuildResult();
        });
    }

    private void ResolveYinghunChoice(PromptChoice selected)
    {
        var pending = _pendingYinghun ?? throw new InvalidOperationException("There is no Yinghun resolution.");
        if (pending.Stage == YinghunStage.Offer)
        {
            if (selected.Parameters.GetValueOrDefault("action") == "yinghun-skip")
            {
                var owner = _players[pending.OwnerSeat];
                ClearPendingDecision();
                _pendingYinghun = null;
                AddLog("SkillSkipped", $"{owner.Name} 未发动【英魂】。", owner.Seat);
                BeginTurnStartAfterYinghun(owner);
                return;
            }

            var target = _players[selected.Targets.Single()];
            if (!target.IsAlive || target.Seat == pending.OwnerSeat)
                throw new InvalidOperationException("The selected Yinghun target is no longer legal.");
            var drawMany = selected.Parameters.GetValueOrDefault("mode") == "draw-many";
            pending.TargetSeat = target.Seat;
            pending.DrawCount = drawMany ? pending.LostHp : 1;
            pending.DiscardCount = drawMany ? 1 : pending.LostHp;
            DrawCards(target, pending.DrawCount, log: true, reason: CardMoveReasons.YinghunDraw);
            AddLog("SkillTriggered",
                $"{_players[pending.OwnerSeat].Name} 对 {target.Name} 发动【英魂】：其摸 {pending.DrawCount} 张牌，然后弃置 {pending.DiscardCount} 张牌。",
                pending.OwnerSeat, target.Seat);
            PublishYinghunDiscardPrompt(pending);
            return;
        }

        var cardId = selected.Cards.Single();
        var targetPlayer = _players[pending.TargetSeat];
        var card = GetHand(targetPlayer).SingleOrDefault(candidate => candidate.Id == cardId);
        var from = CardLocation.Hand(targetPlayer.Seat);
        if (card is null)
        {
            card = GetEquipment(targetPlayer).SingleOrDefault(candidate => candidate.Id == cardId) ??
                throw new InvalidOperationException("The selected Yinghun discard card is no longer available.");
            from = CardLocation.Equipment(targetPlayer.Seat);
        }
        MoveCard(card, from, CardLocation.DiscardPile, CardMoveReasons.YinghunDiscard);
        pending.DiscardedCardIds.Add(card.Id);
        PublishYinghunDiscardPrompt(pending);
    }

    private void PublishYinghunDiscardPrompt(YinghunResolution pending)
    {
        var target = _players[pending.TargetSeat];
        var remaining = pending.DiscardCount - pending.DiscardedCardIds.Count;
        var available = GetHand(target).Concat(GetEquipment(target)).OrderBy(card => card.Id).ToArray();
        if (remaining <= 0 || available.Length == 0)
        {
            ClearPendingDecision();
            pending.Stage = YinghunStage.AwaitingEquipmentLossTriggers;
            _status = EngineStatus.Running;
            return;
        }

        pending.Stage = YinghunStage.Discard;
        _pendingDecision = new PendingDecision(DecisionKind.Yinghun, target.Seat,
            $"【英魂】：请选择一张牌弃置（还需弃置 {remaining} 张）。",
            available.Select(card => card.Id).ToArray(), [], SourceSeat: pending.OwnerSeat)
        {
            PromptId = CreatePromptId(),
            Choices = available.Select(card => new PromptChoice(
                new ChoiceId($"yinghun.discard.card-{card.Id}.remaining-{remaining}"),
                GetHand(target).Any(handCard => handCard.Id == card.Id)
                    ? $"弃置手牌【{card.DisplayName}】（{card.RankText}）。"
                    : $"弃置装备【{card.DisplayName}】。",
                [card.Id], [], new Dictionary<string, string> { ["action"] = "yinghun-discard" }))
                .ToArray()
        };
        _status = target.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private bool IsAiYinghunPending() =>
        _pendingYinghun is not null &&
        _pendingDecision is { Kind: DecisionKind.Yinghun, PlayerSeat: var seat } &&
        !_players[seat].IsHuman;

    private void ResolvePendingAiYinghun()
    {
        var pending = _pendingYinghun ?? throw new InvalidOperationException("There is no AI Yinghun choice.");
        var decision = _pendingDecision ?? throw new InvalidOperationException("Yinghun requires a decision.");
        PromptChoice choice;
        if (pending.Stage == YinghunStage.Offer)
        {
            var target = _players.Where(player => player.IsAlive && player.Seat != pending.OwnerSeat)
                .OrderByDescending(player => GetHand(player).Count + GetEquipment(player).Count)
                .ThenBy(player => player.Seat).First();
            var mode = pending.LostHp > 1 ? "discard-many" : "draw-many";
            choice = decision.Choices.First(candidate => candidate.Targets.SequenceEqual([target.Seat]) &&
                candidate.Parameters.GetValueOrDefault("mode") == mode);
        }
        else
        {
            choice = decision.Choices.OrderBy(candidate => candidate.Cards.Single()).First();
        }
        ResolveYinghunChoice(choice);
        PublishState();
    }

    private bool TryCompleteYinghunAfterLossTriggers()
    {
        if (_pendingYinghun is not { Stage: YinghunStage.AwaitingEquipmentLossTriggers } pending ||
            _pendingDecision is not null || _pendingCardsMovedBatches.Count > 0 ||
            _resolutionStack.Any(frame => frame is CardsMovedTriggerWindowFrame))
            return false;

        var owner = _players[pending.OwnerSeat];
        QueueGameEvent(new YinghunResolvedEvent(owner.Seat, pending.TargetSeat, pending.LostHp,
            pending.DrawCount, pending.DiscardedCardIds.AsReadOnly()));
        _pendingYinghun = null;
        BeginTurnStartAfterYinghun(owner);
        PublishState();
        return true;
    }

    private void AssertYinghunInvariant()
    {
        if (_pendingYinghun is not { } pending) return;
        if (!UsesFormalSunJian || _phase != TurnPhase.Draw || _currentSeat != pending.OwnerSeat ||
            !_players[pending.OwnerSeat].IsAlive ||
            !HasRuntimeSkill(_players[pending.OwnerSeat], SkillKind.Yinghun) || pending.LostHp <= 0)
            throw new InvalidOperationException("Yinghun must remain at its wounded owner's preparation boundary.");
        if (pending.Stage != YinghunStage.AwaitingEquipmentLossTriggers &&
            _pendingDecision is not { Kind: DecisionKind.Yinghun })
            throw new InvalidOperationException("An active Yinghun choice must retain its private prompt.");
    }
}
