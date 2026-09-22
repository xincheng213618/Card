namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string JunxingSkillId = "classic:junxing";
    private const string YuceSkillId = "classic:yuce";

    private JunxingResolution? _pendingJunxing;
    private YuceResolution? _pendingYuce;

    private bool UsesFormalManChong =>
        HasClassicGeneralPackage(new Version(1, 96, 0));

    private void BeginJunxing(
        long frameId,
        CharacterState owner,
        IReadOnlyList<int> costCardIds,
        int targetSeat)
    {
        if (!UsesFormalManChong ||
            !HasRuntimeSkill(owner, JunxingSkillId) ||
            owner.UsedActiveSkillKinds.Contains(SkillKind.Junxing) ||
            costCardIds.Count == 0 ||
            !IsValidPlayerSeat(targetSeat) ||
            targetSeat == owner.Seat ||
            !_players[targetSeat].IsAlive ||
            _pendingJunxing is not null)
        {
            throw new InvalidOperationException("The selected Junxing activation is no longer legal.");
        }

        var hand = GetHand(owner);
        var costs = costCardIds
            .Select(cardId => hand.SingleOrDefault(card => card.Id == cardId) ??
                throw new InvalidOperationException("Junxing must discard exact cards from its owner's hand."))
            .ToArray();
        if (costs.Select(card => card.Id).Distinct().Count() != costs.Length)
        {
            throw new InvalidOperationException("Junxing cost cards must be distinct.");
        }

        owner.UsedActiveSkillKinds.Add(SkillKind.Junxing);
        MoveCards(costs, CardLocation.Hand(owner.Seat), CardLocation.Processing, CardMoveReasons.JunxingCost);
        QueueGameEvent(new SkillCardsDiscardedEvent(
            frameId,
            owner.Seat,
            SkillKind.Junxing,
            Array.AsReadOnly(costCardIds.ToArray())));
        MoveCards(costs, CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.JunxingCost);

        var categories = costs
            .Select(card => CardCatalog.Get(card.Kind).CategoryName)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var target = _players[targetSeat];
        var eligible = GetHand(target)
            .Where(card => !categories.Contains(CardCatalog.Get(card.Kind).CategoryName, StringComparer.Ordinal))
            .OrderBy(card => card.Id)
            .ToArray();
        var pending = new JunxingResolution(
            frameId,
            owner.Seat,
            target.Seat,
            Array.AsReadOnly(costCardIds.ToArray()),
            Array.AsReadOnly(categories));
        _pendingJunxing = pending;

        var choices = eligible.Select(card => new PromptChoice(
            new ChoiceId($"junxing-discard.card-{card.Id}.resolution-{frameId}"),
            $"弃置【{card.DisplayName}】（{CardCatalog.Get(card.Kind).CategoryName}）。",
            [card.Id],
            [],
            new Dictionary<string, string> { ["action"] = "junxing-discard" })).ToList();
        choices.Add(new PromptChoice(
            new ChoiceId($"junxing-turn-draw.resolution-{frameId}"),
            $"翻面，然后摸 {costCardIds.Count} 张牌。",
            [],
            [],
            new Dictionary<string, string> { ["action"] = "junxing-turn-draw" }));

        _pendingDecision = new PendingDecision(
            DecisionKind.Junxing,
            target.Seat,
            $"{owner.Name} 对你发动【峻刑】，弃置牌类别为 {string.Join("、", categories)}。请选择弃置一张其他类别的手牌，或翻面并摸 {costCardIds.Count} 张牌。",
            eligible.Select(card => card.Id).ToArray(),
            [],
            SourceSeat: owner.Seat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = target.Seat,
            Choices = choices.AsReadOnly()
        };
        SetActiveSkillFrameStep(frameId, ResolutionFrameStep.AwaitingResponse);
        _status = target.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AddLog(
            "ActiveSkill",
            $"{owner.Name} 发动【峻刑】，弃置 {costCardIds.Count} 张手牌并等待 {target.Name} 选择。",
            owner.Seat,
            target.Seat);
    }

    private CommandResult SubmitJunxingPromptAnswer(PromptChoice selected)
    {
        if (_pendingJunxing is not { } pending ||
            _pendingDecision is not { Kind: DecisionKind.Junxing, PlayerSeat: var responderSeat } ||
            responderSeat != pending.TargetSeat)
        {
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的峻刑选择。");
        }

        var action = selected.Parameters.GetValueOrDefault("action");
        if (action is not ("junxing-discard" or "junxing-turn-draw"))
        {
            return Reject(CommandErrorCode.InvalidChoice, "峻刑选择不属于当前结算。");
        }

        return Accept(() => HumanJunxingCore(
            pending,
            selected,
            _options.AdvanceAfterHumanCommands));
    }

    private EngineRunResult HumanJunxingCore(
        JunxingResolution pending,
        PromptChoice selected,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.Junxing);
        ResolveJunxingChoice(pending, selected);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private bool IsAiJunxingPending() =>
        _pendingJunxing is { } pending &&
        _pendingDecision is { Kind: DecisionKind.Junxing, PlayerSeat: var responderSeat } &&
        responderSeat == pending.TargetSeat &&
        !_players[responderSeat].IsHuman;

    private void ResolvePendingAiJunxing()
    {
        var pending = _pendingJunxing ??
            throw new InvalidOperationException("There is no AI Junxing choice to resolve.");
        var decision = _pendingDecision ??
            throw new InvalidOperationException("The AI Junxing choice has no prompt.");
        var target = _players[pending.TargetSeat];
        var discard = decision.Choices
            .Where(choice => choice.Parameters.GetValueOrDefault("action") == "junxing-discard")
            .OrderBy(choice => CardCatalog.Get(
                GetHand(target).Single(card => card.Id == choice.Cards.Single()).Kind).HandKeepValue)
            .ThenBy(choice => choice.Cards.Single())
            .FirstOrDefault();
        var selected = target.IsFaceDown
            ? decision.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "junxing-turn-draw")
            : discard ?? decision.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "junxing-turn-draw");
        ResolveJunxingChoice(pending, selected);
        PublishState();
    }

    private void ResolveJunxingChoice(JunxingResolution pending, PromptChoice selected)
    {
        if (!ReferenceEquals(_pendingJunxing, pending) ||
            _pendingDecision is not { Kind: DecisionKind.Junxing, PlayerSeat: var responderSeat } ||
            responderSeat != pending.TargetSeat)
        {
            throw new InvalidOperationException("The Junxing choice is no longer current.");
        }

        var owner = _players[pending.OwnerSeat];
        var target = _players[pending.TargetSeat];
        var action = selected.Parameters.GetValueOrDefault("action");
        int? discardedCardId = null;
        var turnedOver = false;
        IReadOnlyList<int> drawnCardIds = [];
        ClearPendingDecision();
        SetActiveSkillFrameStep(pending.FrameId, ResolutionFrameStep.ResolvingEffect);

        if (action == "junxing-discard")
        {
            var cardId = selected.Cards.SingleOrDefault(-1);
            var card = GetHand(target).SingleOrDefault(candidate => candidate.Id == cardId) ??
                throw new InvalidOperationException("The selected Junxing response card is no longer in hand.");
            if (pending.CostCategories.Contains(CardCatalog.Get(card.Kind).CategoryName, StringComparer.Ordinal))
            {
                throw new InvalidOperationException("Junxing cannot discard a card matching any cost category.");
            }
            MoveCard(card, CardLocation.Hand(target.Seat), CardLocation.DiscardPile, CardMoveReasons.JunxingDiscard);
            discardedCardId = card.Id;
        }
        else if (action == "junxing-turn-draw")
        {
            if (selected.Cards.Count != 0)
            {
                throw new InvalidOperationException("The Junxing turn-over branch cannot select a card.");
            }
            target.IsFaceDown = !target.IsFaceDown;
            turnedOver = true;
            drawnCardIds = DrawCards(
                target,
                pending.CostCardIds.Count,
                log: true,
                reason: CardMoveReasons.JunxingDraw);
        }
        else
        {
            throw new InvalidOperationException("The selected Junxing branch is unsupported.");
        }

        QueueGameEvent(new JunxingResolvedEvent(
            pending.FrameId,
            owner.Seat,
            target.Seat,
            pending.CostCardIds,
            pending.CostCategories,
            discardedCardId,
            turnedOver,
            drawnCardIds,
            target.IsFaceDown));
        AddLog(
            "SkillTriggered",
            discardedCardId is not null
                ? $"{target.Name} 响应【峻刑】并弃置一张不同类别的手牌。"
                : $"{target.Name} 响应【峻刑】并翻面，摸了 {drawnCardIds.Count} 张牌。",
            owner.Seat,
            target.Seat);

        _pendingJunxing = null;
        SetActiveSkillFrameStep(pending.FrameId, ResolutionFrameStep.Completed);
        QueueGameEvent(new ActiveSkillResolvedEvent(
            pending.FrameId,
            owner.Seat,
            SkillKind.Junxing,
            ActiveSkillEffectKind.DiscardHandForCategoryChoice));
        PopResolutionFrame(pending.FrameId, ResolutionFrameKind.ActiveSkill);
    }

    private PendingDecision CreateYuceDecision(DamageSkillResolution pending)
    {
        var owner = _players[pending.OwnerSeat];
        var choices = GetHand(owner)
            .OrderBy(card => card.Id)
            .Select(card => new PromptChoice(
                new ChoiceId($"yuce-use.card-{card.Id}.damage-{pending.DamageFrameId}"),
                $"发动【御策】并展示【{card.DisplayName}】（{CardCatalog.Get(card.Kind).CategoryName}）。",
                [card.Id],
                [],
                new Dictionary<string, string> { ["action"] = "yuce-use" }))
            .ToList();
        choices.Add(new PromptChoice(
            new ChoiceId($"yuce-skip.damage-{pending.DamageFrameId}"),
            "不发动【御策】。",
            [],
            [],
            new Dictionary<string, string> { ["action"] = "yuce-skip" }));
        return new PendingDecision(
            DecisionKind.Yuce,
            owner.Seat,
            "你受到伤害，是否发动【御策】展示一张手牌？",
            GetHand(owner).Select(card => card.Id).ToArray(),
            [],
            SourceSeat: pending.SourceSeat,
            IncomingCard: pending.EffectiveCardKind)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            Choices = choices.AsReadOnly()
        };
    }

    private CommandResult SubmitYucePromptAnswer(PromptChoice selected)
    {
        if (_pendingDamageSkill is not { Effect: DamageSkillEffectKind.RevealCardAndChallengeSource } ||
            _pendingDecision is not { Kind: DecisionKind.Yuce })
        {
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的御策选择。");
        }
        return Accept(() => HumanYuceCore(selected, _options.AdvanceAfterHumanCommands));
    }

    private EngineRunResult HumanYuceCore(PromptChoice selected, bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.Yuce);
        ResolveYucePromptChoice(selected);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private void ResolvePendingAiYuce()
    {
        var pending = _pendingDamageSkill ??
            throw new InvalidOperationException("AI Yuce damage skill is missing.");
        var decision = _pendingDecision ??
            throw new InvalidOperationException("AI Yuce prompt is missing.");
        PromptChoice selected;
        if (_pendingYuce is null)
        {
            var owner = _players[pending.OwnerSeat];
            selected = decision.Choices
                .Where(choice => choice.Parameters.GetValueOrDefault("action") == "yuce-use")
                .OrderBy(choice => CardCatalog.Get(
                    GetHand(owner).Single(card => card.Id == choice.Cards.Single()).Kind).HandKeepValue)
                .ThenBy(choice => choice.Cards.Single())
                .First();
        }
        else
        {
            var source = _players[pending.SourceSeat];
            selected = decision.Choices
                .Where(choice => choice.Parameters.GetValueOrDefault("action") == "yuce-discard")
                .OrderBy(choice => CardCatalog.Get(
                    GetHand(source).Single(card => card.Id == choice.Cards.Single()).Kind).HandKeepValue)
                .ThenBy(choice => choice.Cards.Single())
                .FirstOrDefault() ?? decision.Choices.Single(choice =>
                    choice.Parameters.GetValueOrDefault("action") == "yuce-decline");
        }
        ResolveYucePromptChoice(selected);
    }

    private void ResolveYucePromptChoice(PromptChoice selected)
    {
        var pending = _pendingDamageSkill ??
            throw new InvalidOperationException("Yuce damage skill is missing.");
        if (pending.Effect != DamageSkillEffectKind.RevealCardAndChallengeSource)
        {
            throw new InvalidOperationException("The current damage skill is not Yuce.");
        }

        if (_pendingYuce is { } challenge)
        {
            ResolveYuceSourceChoice(pending, challenge, selected);
            return;
        }

        var action = selected.Parameters.GetValueOrDefault("action");
        if (action == "yuce-skip")
        {
            ClearPendingDecision();
            CompleteYuce(pending, used: false, null, null, null, recoveredHp: 0);
            return;
        }
        if (action != "yuce-use")
        {
            throw new InvalidOperationException("The selected Yuce owner choice is unsupported.");
        }

        var owner = _players[pending.OwnerSeat];
        var revealedCardId = selected.Cards.SingleOrDefault(-1);
        var revealed = GetHand(owner).SingleOrDefault(card => card.Id == revealedCardId) ??
            throw new InvalidOperationException("The selected Yuce reveal card is no longer in hand.");
        var category = CardCatalog.Get(revealed.Kind).CategoryName;
        ClearPendingDecision();
        QueueGameEvent(new CardsRevealedEvent(pending.DamageFrameId, [ToSnapshot(revealed)]));

        var source = _players[pending.SourceSeat];
        var eligible = source.IsAlive
            ? GetHand(source)
                .Where(card => !string.Equals(
                    CardCatalog.Get(card.Kind).CategoryName,
                    category,
                    StringComparison.Ordinal))
                .OrderBy(card => card.Id)
                .ToArray()
            : [];
        var yuce = new YuceResolution(revealed.Id, category);
        _pendingYuce = yuce;
        if (eligible.Length == 0)
        {
            CompleteYuceWithRecovery(pending, yuce, discardedCardId: null);
            return;
        }

        var choices = eligible.Select(card => new PromptChoice(
            new ChoiceId($"yuce-discard.card-{card.Id}.damage-{pending.DamageFrameId}"),
            $"弃置【{card.DisplayName}】（{CardCatalog.Get(card.Kind).CategoryName}），阻止回复。",
            [card.Id],
            [],
            new Dictionary<string, string> { ["action"] = "yuce-discard" })).ToList();
        choices.Add(new PromptChoice(
            new ChoiceId($"yuce-decline.damage-{pending.DamageFrameId}"),
            $"不弃置牌，令 {owner.Name} 回复 1 点体力。",
            [],
            [],
            new Dictionary<string, string> { ["action"] = "yuce-decline" }));
        _pendingDecision = new PendingDecision(
            DecisionKind.Yuce,
            source.Seat,
            $"{owner.Name} 发动【御策】并展示【{revealed.DisplayName}】（{category}）。你可弃置一张不同类别的手牌，否则其回复1点体力。",
            eligible.Select(card => card.Id).ToArray(),
            [],
            SourceSeat: owner.Seat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = owner.Seat,
            Choices = choices.AsReadOnly()
        };
        _status = source.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveYuceSourceChoice(
        DamageSkillResolution pending,
        YuceResolution challenge,
        PromptChoice selected)
    {
        var source = _players[pending.SourceSeat];
        var action = selected.Parameters.GetValueOrDefault("action");
        if (action == "yuce-discard")
        {
            var cardId = selected.Cards.SingleOrDefault(-1);
            var card = GetHand(source).SingleOrDefault(candidate => candidate.Id == cardId) ??
                throw new InvalidOperationException("The selected Yuce response card is no longer in hand.");
            if (string.Equals(
                    CardCatalog.Get(card.Kind).CategoryName,
                    challenge.RevealedCategory,
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Yuce requires a response card of a different category.");
            }
            ClearPendingDecision();
            MoveCard(card, CardLocation.Hand(source.Seat), CardLocation.DiscardPile, CardMoveReasons.YuceDiscard);
            CompleteYuce(
                pending,
                used: true,
                challenge.RevealedCardId,
                challenge.RevealedCategory,
                card.Id,
                recoveredHp: 0);
            return;
        }
        if (action != "yuce-decline" || selected.Cards.Count != 0)
        {
            throw new InvalidOperationException("The selected Yuce source choice is unsupported.");
        }
        ClearPendingDecision();
        CompleteYuceWithRecovery(pending, challenge, discardedCardId: null);
    }

    private void CompleteYuceWithRecovery(
        DamageSkillResolution pending,
        YuceResolution challenge,
        int? discardedCardId)
    {
        var owner = _players[pending.OwnerSeat];
        var recoveredHp = Math.Min(1, Math.Max(0, owner.MaxHp - owner.Hp));
        if (recoveredHp > 0)
        {
            var recoveryFrameId = BeginRecovery(
                pending.DamageFrameId,
                owner.Seat,
                owner.Seat,
                recoveredHp);
            owner.Hp += recoveredHp;
            QueueGameEvent(new RecoveryAppliedEvent(owner.Seat, owner.Seat, recoveredHp, owner.Hp));
            PopResolutionFrame(recoveryFrameId, ResolutionFrameKind.Recovery);
        }
        CompleteYuce(
            pending,
            used: true,
            challenge.RevealedCardId,
            challenge.RevealedCategory,
            discardedCardId,
            recoveredHp);
    }

    private void CompleteYuce(
        DamageSkillResolution pending,
        bool used,
        int? revealedCardId,
        string? revealedCategory,
        int? discardedCardId,
        int recoveredHp)
    {
        if (!ReferenceEquals(_pendingDamageSkill, pending))
        {
            throw new InvalidOperationException("Yuce is no longer the current damage skill.");
        }
        var window = _pendingDamageTrigger ??
            throw new InvalidOperationException("Yuce has no damage trigger window.");
        var owner = _players[pending.OwnerSeat];
        var source = _players[pending.SourceSeat];

        _pendingYuce = null;
        ClearPendingDecision();
        SetDamageSkillFrameStep(pending.FrameId, ResolutionFrameStep.ResolvingEffect);
        QueueGameEvent(new YuceResolvedEvent(
            pending.DamageFrameId,
            owner.Seat,
            source.Seat,
            used,
            revealedCardId,
            revealedCategory,
            discardedCardId,
            recoveredHp));
        QueueGameEvent(new DamageSkillResolvedEvent(
            pending.DamageFrameId,
            owner.Seat,
            source.Seat,
            pending.Card?.Id,
            pending.EffectiveCardKind,
            pending.Skill,
            used,
            pending.CandidateId,
            pending.Priority,
            source.Seat));
        AddLog(
            used ? "SkillTriggered" : "SkillSkipped",
            !used
                ? $"{owner.Name} 选择不发动【御策】。"
                : discardedCardId is not null
                    ? $"{owner.Name} 发动【御策】，{source.Name} 弃置一张不同类别的手牌。"
                    : $"{owner.Name} 发动【御策】，{source.Name} 未弃置牌，{owner.Name} 回复 {recoveredHp} 点体力。",
            owner.Seat,
            source.Seat);
        PopResolutionFrame(pending.FrameId, ResolutionFrameKind.DamageSkill);
        _pendingDamageSkill = null;
        SetDamageTriggerWindowStep(window.FrameId, ResolutionFrameStep.ResolvingEffect);
        AdvanceDamageTriggerCandidate(window);
    }

    private IReadOnlyList<CardSnapshot> GetYucePublicCards()
    {
        if (_pendingYuce is not { } pending ||
            _pendingDamageSkill is not { Effect: DamageSkillEffectKind.RevealCardAndChallengeSource } damage)
        {
            return [];
        }
        var card = GetHand(_players[damage.OwnerSeat])
            .SingleOrDefault(candidate => candidate.Id == pending.RevealedCardId);
        return card is null ? [] : [ToSnapshot(card)];
    }

    private sealed record JunxingResolution(
        long FrameId,
        int OwnerSeat,
        int TargetSeat,
        IReadOnlyList<int> CostCardIds,
        IReadOnlyList<string> CostCategories);

    private sealed record YuceResolution(
        int RevealedCardId,
        string RevealedCategory);
}
