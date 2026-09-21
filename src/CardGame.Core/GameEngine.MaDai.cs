namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string QianxiSkillId = "classic:qianxi";
    private const string QianxiRestrictionUsagePrefix = "restriction";

    private QianxiResolution? _pendingQianxi;

    private bool UsesFormalQianxi =>
        _rulesVersion >= 104 &&
        IsClassicIdentityMode &&
        _contentRegistry?.Packages.Any(package =>
            package.Id == "standard-classic-generals" &&
            package.Version >= new Version(1, 82, 0)) == true;

    private static CardColor GetCardColor(Suit suit) =>
        suit is Suit.Heart or Suit.Diamond ? CardColor.Red : CardColor.Black;

    private static string GetQianxiRestrictionUsageId(int targetSeat, CardColor color) =>
        $"{QianxiRestrictionUsagePrefix}.target-{targetSeat}.{color.ToString().ToLowerInvariant()}";

    private bool IsQianxiHandCardRestricted(PlayerRuntime player, Card card)
    {
        if (!UsesFormalQianxi ||
            _cardZones.GetLocation(card.Id) != CardLocation.Hand(player.Seat))
        {
            return false;
        }

        var color = GetCardColor(EffectiveSuit(player, card));
        return _players.Any(owner =>
            HasRuntimeSkill(owner, QianxiSkillId) &&
            _skillRuntimeState.GetUsage(
                owner.Seat,
                QianxiSkillId,
                GetQianxiRestrictionUsageId(player.Seat, color),
                SkillUsageScope.Turn) > 0);
    }

    private IReadOnlyList<PlayerRuntime> GetQianxiTargets(PlayerRuntime owner) =>
        _players
            .Where(target =>
                target.IsAlive &&
                target.Seat != owner.Seat &&
                GetCombatDistance(owner.Seat, target.Seat) == 1)
            .OrderBy(target => target.Seat)
            .ToArray();

    private bool TryBeginQianxiChoice(PlayerRuntime current)
    {
        if (!UsesFormalQianxi || !HasRuntimeSkill(current, QianxiSkillId))
        {
            return false;
        }

        _pendingQianxi = new QianxiResolution(current.Seat, QianxiStage.Offer);
        _pendingDecision = new PendingDecision(
            DecisionKind.Qianxi,
            current.Seat,
            "准备阶段是否发动【潜袭】，摸一张牌并弃置一张牌？",
            [],
            [])
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            Choices =
            [
                new PromptChoice(
                    new ChoiceId($"qianxi.use.turn-{_turnNumber}.seat-{current.Seat}"),
                    "发动【潜袭】：摸一张牌，然后弃置一张牌。",
                    [],
                    [],
                    new Dictionary<string, string> { ["action"] = "qianxi-use" }),
                new PromptChoice(
                    new ChoiceId($"qianxi.skip.turn-{_turnNumber}.seat-{current.Seat}"),
                    "不发动【潜袭】。",
                    [],
                    [],
                    new Dictionary<string, string> { ["action"] = "qianxi-skip" })
            ]
        };
        _status = current.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        PublishState();
        return true;
    }

    private void ResolveQianxiChoice(PromptChoice selected)
    {
        var pending = _pendingQianxi ??
            throw new InvalidOperationException("There is no Qianxi choice to resolve.");
        var owner = _players[pending.PlayerSeat];
        var action = selected.Parameters.GetValueOrDefault("action");

        switch (pending.Stage)
        {
            case QianxiStage.Offer when action == "qianxi-skip":
                CompleteQianxiSkipped(owner);
                return;
            case QianxiStage.Offer when action == "qianxi-use":
                BeginQianxiDiscard(owner);
                return;
            case QianxiStage.Discard when action == "qianxi-discard":
                ResolveQianxiDiscard(owner, selected);
                return;
            case QianxiStage.Target when action == "qianxi-target":
                ResolveQianxiTarget(owner, selected);
                return;
            default:
                throw new InvalidOperationException("The Qianxi choice does not match its current stage.");
        }
    }

    private void CompleteQianxiSkipped(PlayerRuntime owner)
    {
        QueueGameEvent(new QianxiResolvedEvent(
            owner.Seat,
            Used: false,
            DiscardedCardId: null,
            TargetSeat: null,
            RestrictedColor: null));
        AddLog("SkillSkipped", $"{owner.Name} 未发动【潜袭】。", owner.Seat);
        _pendingQianxi = null;
        ClearPendingDecision();
        BeginTurnStartAfterQianxi(owner);
    }

    private void BeginQianxiDiscard(PlayerRuntime owner)
    {
        ClearPendingDecision();
        DrawCards(owner, 1, log: true, CardMoveReasons.QianxiDraw);
        var cards = GetHand(owner)
            .Concat(GetEquipment(owner))
            .OrderBy(card => card.Id)
            .ToArray();
        if (cards.Length == 0)
        {
            throw new InvalidOperationException("Qianxi must have one card to discard after drawing.");
        }

        _pendingQianxi = new QianxiResolution(owner.Seat, QianxiStage.Discard);
        _pendingDecision = new PendingDecision(
            DecisionKind.Qianxi,
            owner.Seat,
            "【潜袭】已摸一张牌，请弃置一张牌；其颜色将决定本回合的限制。",
            cards.Select(card => card.Id).ToArray(),
            [])
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            Choices = cards.Select(card => new PromptChoice(
                new ChoiceId($"qianxi.discard.card-{card.Id}.turn-{_turnNumber}"),
                $"弃置【{card.DisplayName}】（{GetSuitDisplayName(card.Suit)}）。",
                [card.Id],
                [],
                new Dictionary<string, string> { ["action"] = "qianxi-discard" }))
                .ToArray()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveQianxiDiscard(PlayerRuntime owner, PromptChoice selected)
    {
        if (selected.Cards.Count != 1 || selected.Targets.Count != 0)
        {
            throw new InvalidOperationException("Qianxi must discard exactly one owned card.");
        }

        var cardId = selected.Cards[0];
        var card = GetHand(owner).Concat(GetEquipment(owner))
            .SingleOrDefault(candidate => candidate.Id == cardId) ??
            throw new InvalidOperationException("The selected Qianxi discard is no longer owned.");
        var color = GetCardColor(EffectiveSuit(owner, card));
        MoveCard(
            card,
            FindOwnedCardLocation(owner, card),
            CardLocation.DiscardPile,
            CardMoveReasons.QianxiDiscard);

        var targets = GetQianxiTargets(owner);
        if (targets.Count == 0)
        {
            throw new InvalidOperationException("Qianxi must retain one other character at distance 1.");
        }

        _pendingQianxi = new QianxiResolution(
            owner.Seat,
            QianxiStage.Target,
            card.Id,
            color);
        _pendingDecision = new PendingDecision(
            DecisionKind.Qianxi,
            owner.Seat,
            $"【潜袭】弃置了{GetCardColorDisplayName(color)}牌，请选择一名距离为 1 的其他角色。",
            [],
            targets.Select(target => target.Seat).ToArray())
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            Choices = targets.Select(target => new PromptChoice(
                new ChoiceId($"qianxi.target.seat-{target.Seat}.turn-{_turnNumber}"),
                $"令 {target.Name} 本回合不能使用或打出{GetCardColorDisplayName(color)}手牌。",
                [],
                [target.Seat],
                new Dictionary<string, string> { ["action"] = "qianxi-target" }))
                .ToArray()
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveQianxiTarget(PlayerRuntime owner, PromptChoice selected)
    {
        var pending = _pendingQianxi is { Stage: QianxiStage.Target } current
            ? current
            : throw new InvalidOperationException("Qianxi is not waiting for its target.");
        if (selected.Cards.Count != 0 || selected.Targets.Count != 1)
        {
            throw new InvalidOperationException("Qianxi must select exactly one distance-1 target.");
        }

        var target = GetQianxiTargets(owner)
            .SingleOrDefault(candidate => candidate.Seat == selected.Targets[0]) ??
            throw new InvalidOperationException("The selected Qianxi target is no longer at distance 1.");
        var color = pending.RestrictedColor ??
            throw new InvalidOperationException("Qianxi lost the discarded card color.");
        var usageId = GetQianxiRestrictionUsageId(target.Seat, color);
        if (!_skillRuntimeState.TryConsumeUsage(
                owner.Seat,
                QianxiSkillId,
                usageId,
                SkillUsageScope.Turn,
                limit: 1))
        {
            throw new InvalidOperationException("Qianxi tried to apply the same turn restriction twice.");
        }

        QueueGameEvent(new SkillUsageConsumedEvent(
            owner.Seat,
            QianxiSkillId,
            usageId,
            SkillUsageScope.Turn,
            Count: 1));
        QueueGameEvent(new QianxiResolvedEvent(
            owner.Seat,
            Used: true,
            pending.DiscardedCardId,
            target.Seat,
            color));
        AddLog(
            "SkillTriggered",
            $"{owner.Name} 发动【潜袭】，令 {target.Name} 本回合不能使用或打出{GetCardColorDisplayName(color)}手牌。",
            owner.Seat,
            target.Seat);
        _pendingQianxi = null;
        ClearPendingDecision();
        BeginTurnStartAfterQianxi(owner);
    }

    private static string GetCardColorDisplayName(CardColor color) =>
        color == CardColor.Red ? "红色" : "黑色";

    private CommandResult SubmitQianxiPromptAnswer(PromptChoice selected)
    {
        if (_pendingQianxi is null ||
            _pendingDecision is not { Kind: DecisionKind.Qianxi })
        {
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的潜袭窗口。");
        }

        return Accept(() => HumanQianxiCore(selected, _options.AdvanceAfterHumanCommands));
    }

    private EngineRunResult HumanQianxiCore(
        PromptChoice selected,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.Qianxi);
        ResolveQianxiChoice(selected);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private bool IsAiQianxiPending() =>
        _pendingQianxi is { PlayerSeat: var playerSeat } &&
        _pendingDecision is { Kind: DecisionKind.Qianxi, PlayerSeat: var decisionSeat } &&
        playerSeat == decisionSeat &&
        !_players[playerSeat].IsHuman;

    private void ResolvePendingAiQianxi()
    {
        if (!IsAiQianxiPending())
        {
            throw new InvalidOperationException("There is no AI Qianxi choice to resolve.");
        }

        var pending = _pendingQianxi!;
        var prompt = _pendingDecision!;
        PromptChoice selected;
        if (pending.Stage == QianxiStage.Offer)
        {
            selected = prompt.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") ==
                (GetQianxiTargets(_players[pending.PlayerSeat]).Count > 0
                    ? "qianxi-use"
                    : "qianxi-skip"));
        }
        else if (pending.Stage == QianxiStage.Discard)
        {
            selected = prompt.Choices.OrderBy(choice => choice.Cards[0]).First();
        }
        else
        {
            selected = prompt.Choices.OrderBy(choice => choice.Targets[0]).First();
        }

        ResolveQianxiChoice(selected);
        PublishState();
    }

    private void AssertQianxiInvariant()
    {
        if (_pendingQianxi is not { } pending)
        {
            if (_pendingDecision?.Kind == DecisionKind.Qianxi)
            {
                throw new InvalidOperationException(
                    "A Qianxi prompt cannot exist without its continuation.");
            }
            return;
        }

        var owner = _players[pending.PlayerSeat];
        var decision = _pendingDecision;
        var stageIsValid = pending.Stage switch
        {
            QianxiStage.Offer =>
                pending.DiscardedCardId is null &&
                pending.RestrictedColor is null &&
                decision?.Choices.Count == 2 &&
                decision.Choices.All(choice => choice.Cards.Count == 0 && choice.Targets.Count == 0),
            QianxiStage.Discard =>
                pending.DiscardedCardId is null &&
                pending.RestrictedColor is null &&
                decision is { Choices.Count: > 0 } &&
                decision.Choices.All(choice => choice.Cards.Count == 1 && choice.Targets.Count == 0),
            QianxiStage.Target =>
                pending.DiscardedCardId is not null &&
                pending.RestrictedColor is not null &&
                decision is { Choices.Count: > 0 } &&
                decision.Choices.All(choice => choice.Cards.Count == 0 && choice.Targets.Count == 1),
            _ => false
        };
        if (!UsesFormalQianxi ||
            _phase != TurnPhase.Draw ||
            _currentSeat != pending.PlayerSeat ||
            !owner.IsAlive ||
            !HasRuntimeSkill(owner, QianxiSkillId) ||
            decision is not { Kind: DecisionKind.Qianxi, IsPrivate: true } ||
            decision.PlayerSeat != pending.PlayerSeat ||
            !stageIsValid ||
            _resolutionStack.Count != 0)
        {
            throw new InvalidOperationException(
                "A Qianxi choice must retain its private staged prompt at a clean preparation boundary.");
        }

        var expectedStatus = owner.IsHuman
            ? EngineStatus.AwaitingHumanResponse
            : EngineStatus.Running;
        if (_status != expectedStatus)
        {
            throw new InvalidOperationException("A Qianxi prompt status does not match its owner.");
        }
    }

    private enum QianxiStage
    {
        Offer,
        Discard,
        Target
    }

    private sealed record QianxiResolution(
        int PlayerSeat,
        QianxiStage Stage,
        int? DiscardedCardId = null,
        CardColor? RestrictedColor = null);
}
