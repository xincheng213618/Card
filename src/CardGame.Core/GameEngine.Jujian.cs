namespace CardGame.Core;

public sealed partial class GameEngine
{
    private enum JujianStage
    {
        OwnerChoice,
        TargetBenefit
    }

    private sealed record JujianResolution(
        long ResolutionId,
        int OwnerSeat,
        JujianStage Stage,
        int? DiscardedCardId = null,
        CardKind? DiscardedCardKind = null,
        int? TargetSeat = null);

    private JujianResolution? _pendingJujian;

    private bool TryBeginJujianChoice(CharacterState owner)
    {
        var cards = GetHand(owner)
            .Concat(GetEquipment(owner))
            .Where(card => CardCatalog.Get(card.Kind).CategoryName != "基本牌")
            .OrderBy(card => card.Id)
            .ToArray();
        var targets = _players
            .Where(player => player.IsAlive && player.Seat != owner.Seat)
            .OrderBy(player => player.Seat)
            .ToArray();
        if (cards.Length == 0 || targets.Length == 0)
        {
            _jujianResolvedThisTurn = true;
            return false;
        }

        var resolutionId = ++_resolutionSequence;
        _pendingJujian = new JujianResolution(
            resolutionId,
            owner.Seat,
            JujianStage.OwnerChoice);
        var choices = cards
            .SelectMany(card => targets.Select(target => new PromptChoice(
                new ChoiceId($"jujian.use.{card.Id}.{target.Seat}"),
                $"弃置【{card.DisplayName}】，令 {target.Name} 选择一项。",
                [card.Id],
                [target.Seat],
                new Dictionary<string, string> { ["action"] = "jujian-use" })))
            .Append(new PromptChoice(
                new ChoiceId("jujian.skip"),
                "不发动【举荐】。",
                [],
                [],
                new Dictionary<string, string> { ["action"] = "jujian-skip" }))
            .ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.Jujian,
            owner.Seat,
            $"{owner.Name} 的结束阶段：是否弃置一张非基本牌发动【举荐】？",
            cards.Select(card => card.Id).ToArray(),
            targets.Select(target => target.Seat).ToArray())
        {
            PromptId = CreatePromptId(),
            Choices = choices
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return true;
    }

    private CommandResult SubmitJujianPromptAnswer(PromptChoice selected)
    {
        if (_pendingDecision is not { Kind: DecisionKind.Jujian })
        {
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的举荐选择。");
        }

        return Accept(() => HumanJujianCore(
            selected,
            advanceToHumanBoundary: _options.AdvanceAfterHumanCommands));
    }

    private EngineRunResult HumanJujianCore(PromptChoice selected, bool advanceToHumanBoundary)
    {
        ResolveJujianChoice(selected);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private void ResolveJujianChoice(PromptChoice selected)
    {
        if (_pendingJujian is not { } pending ||
            _pendingDecision is not { Kind: DecisionKind.Jujian, PlayerSeat: var responderSeat })
        {
            throw new InvalidOperationException("There is no Jujian choice to resolve.");
        }

        var action = selected.Parameters.GetValueOrDefault("action");
        if (pending.Stage == JujianStage.OwnerChoice)
        {
            ResolveJujianOwnerChoice(pending, responderSeat, selected, action);
            return;
        }

        ResolveJujianTargetBenefit(pending, responderSeat, selected, action);
    }

    private void ResolveJujianOwnerChoice(
        JujianResolution pending,
        int responderSeat,
        PromptChoice selected,
        string? action)
    {
        if (responderSeat != pending.OwnerSeat)
        {
            throw new InvalidOperationException("Only the Jujian owner may pay its cost.");
        }

        var owner = _players[pending.OwnerSeat];
        if (action == "jujian-skip" && selected.Cards.Count == 0 && selected.Targets.Count == 0)
        {
            ClearPendingDecision();
            _pendingJujian = null;
            _jujianResolvedThisTurn = true;
            AddLog("SkillSkipped", $"{owner.Name} 未发动【举荐】。", owner.Seat);
            QueueGameEvent(new JujianResolvedEvent(
                pending.ResolutionId, owner.Seat, false, null, null, null, null, 0, 0, null, null));
            EndTurn();
            return;
        }

        if (action != "jujian-use" || selected.Cards.Count != 1 || selected.Targets.Count != 1)
        {
            throw new InvalidOperationException("The Jujian owner choice is malformed.");
        }

        var targetSeat = selected.Targets[0];
        if (!IsValidPlayerSeat(targetSeat) || targetSeat == owner.Seat || !_players[targetSeat].IsAlive)
        {
            throw new InvalidOperationException("The Jujian target is no longer legal.");
        }

        var card = FindOwnedPlayableCard(owner, selected.Cards[0]);
        if (card is null || CardCatalog.Get(card.Kind).CategoryName == "基本牌" ||
            GetWoodenOxGrain(owner).Any(candidate => candidate.Id == card.Id))
        {
            throw new InvalidOperationException("The Jujian cost is no longer a non-basic hand or equipment card.");
        }

        MoveCard(
            card,
            FindOwnedCardLocation(owner, card),
            CardLocation.DiscardPile,
            CardMoveReasons.JujianDiscard);
        var target = _players[targetSeat];
        _pendingJujian = pending with
        {
            Stage = JujianStage.TargetBenefit,
            DiscardedCardId = card.Id,
            DiscardedCardKind = card.Kind,
            TargetSeat = targetSeat
        };
        var choices = new List<PromptChoice>
        {
            new(
                new ChoiceId("jujian.draw-two"),
                "摸两张牌。",
                [],
                [],
                new Dictionary<string, string> { ["action"] = "jujian-draw" })
        };
        if (target.Hp < target.MaxHp)
        {
            choices.Add(new PromptChoice(
                new ChoiceId("jujian.recover-one"),
                "回复1点体力。",
                [],
                [],
                new Dictionary<string, string> { ["action"] = "jujian-recover" }));
        }
        if (target.IsFaceDown || target.IsChained)
        {
            choices.Add(new PromptChoice(
                new ChoiceId("jujian.restore-general"),
                "复原武将牌（翻至正面并解除横置）。",
                [],
                [],
                new Dictionary<string, string> { ["action"] = "jujian-restore" }));
        }

        _pendingDecision = new PendingDecision(
            DecisionKind.Jujian,
            target.Seat,
            $"{owner.Name} 对你发动【举荐】：请选择一项。",
            [],
            [],
            SourceSeat: owner.Seat)
        {
            PromptId = CreatePromptId(),
            Choices = choices
        };
        _status = target.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AddLog("SkillTriggered", $"{owner.Name} 弃置【{card.DisplayName}】，对 {target.Name} 发动【举荐】。", owner.Seat);
    }

    private void ResolveJujianTargetBenefit(
        JujianResolution pending,
        int responderSeat,
        PromptChoice selected,
        string? action)
    {
        if (pending.TargetSeat is not { } targetSeat || responderSeat != targetSeat ||
            selected.Cards.Count != 0 || selected.Targets.Count != 0)
        {
            throw new InvalidOperationException("Only the selected Jujian target may choose its benefit.");
        }

        var owner = _players[pending.OwnerSeat];
        var target = _players[targetSeat];
        var benefit = action switch
        {
            "jujian-draw" => JujianBenefitKind.DrawTwo,
            "jujian-recover" when target.Hp < target.MaxHp => JujianBenefitKind.RecoverOne,
            "jujian-restore" when target.IsFaceDown || target.IsChained => JujianBenefitKind.RestoreGeneral,
            _ => throw new InvalidOperationException("The selected Jujian benefit is no longer legal.")
        };

        ClearPendingDecision();
        _pendingJujian = null;
        _jujianResolvedThisTurn = true;
        var drawnCards = 0;
        var recoveredHp = 0;
        switch (benefit)
        {
            case JujianBenefitKind.DrawTwo:
                drawnCards = DrawCards(target, 2, log: true, reason: CardMoveReasons.JujianDraw).Count;
                break;
            case JujianBenefitKind.RecoverOne:
                var recoveryFrameId = BeginRecovery(
                    pending.ResolutionId,
                    owner.Seat,
                    target.Seat,
                    1);
                try
                {
                    recoveredHp = Math.Min(1, target.MaxHp - target.Hp);
                    target.Hp += recoveredHp;
                    QueueGameEvent(new RecoveryAppliedEvent(
                        owner.Seat,
                        target.Seat,
                        recoveredHp,
                        target.Hp));
                }
                finally
                {
                    PopResolutionFrame(recoveryFrameId, ResolutionFrameKind.Recovery);
                }
                break;
            case JujianBenefitKind.RestoreGeneral:
                target.IsFaceDown = false;
                if (target.IsChained)
                {
                    target.IsChained = false;
                    QueueGameEvent(new IronChainStateChangedEvent(
                        pending.ResolutionId,
                        owner.Seat,
                        target.Seat,
                        IsChained: false));
                }
                break;
        }

        AddLog("SkillResolved", benefit switch
        {
            JujianBenefitKind.DrawTwo => $"{target.Name} 选择摸两张牌。",
            JujianBenefitKind.RecoverOne => $"{target.Name} 选择回复1点体力。",
            _ => $"{target.Name} 选择复原武将牌。"
        }, target.Seat);
        QueueGameEvent(new JujianResolvedEvent(
            pending.ResolutionId,
            owner.Seat,
            true,
            pending.DiscardedCardId,
            pending.DiscardedCardKind,
            target.Seat,
            benefit,
            drawnCards,
            recoveredHp,
            target.IsFaceDown,
            target.IsChained));
        EndTurn();
    }

    private bool IsAiJujianPending() =>
        _pendingDecision is { Kind: DecisionKind.Jujian, PlayerSeat: var playerSeat } &&
        !_players[playerSeat].IsHuman;

    private void ResolvePendingAiJujian()
    {
        if (!IsAiJujianPending() || _pendingJujian is not { } pending)
        {
            throw new InvalidOperationException("There is no AI Jujian choice to resolve.");
        }

        var responderSeat = _pendingDecision!.PlayerSeat;
        var brain = _aiBrains[responderSeat];
        var view = CreateSnapshot(responderSeat, revealAll: false);
        var selected = pending.Stage == JujianStage.OwnerChoice
            ? brain.ChooseJujianOwnerChoice(view, _pendingDecision.Choices)
            : brain.ChooseJujianBenefit(view, _pendingDecision.Choices);
        ResolveJujianChoice(selected);
        PublishState();
    }
}
