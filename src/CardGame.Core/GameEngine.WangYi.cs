namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ZhenlieSkillId = "classic:zhenlie";
    private const string MijiSkillId = "classic:miji";

    private ZhenlieResolution? _pendingZhenlie;
    private MijiResolution? _pendingMiji;

    private bool UsesFormalWangYi =>
        HasClassicGeneralPackage(new Version(1, 85, 0));

    private bool TryBeginZhenlieChoice(AttackResolution attack)
    {
        if (!UsesFormalWangYi || attack.ZhenlieResolved)
        {
            return false;
        }

        var target = _players[attack.TargetSeat];
        var source = _players[attack.SourceSeat];
        if (!target.IsAlive ||
            source.Seat == target.Seat ||
            attack.EffectiveCardKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash) ||
            !HasRuntimeSkill(target, ZhenlieSkillId))
        {
            return false;
        }

        attack.MarkZhenlieResolved();
        BeginZhenlieChoice(new ZhenlieResolution(
            attack.ResolutionId,
            source.Seat,
            target.Seat,
            attack.EffectiveCardKind.Value,
            ZhenlieContinuationKind.Slash,
            attack,
            trick: null,
            nextTargetIndex: 0));
        return true;
    }

    private bool TryBeginZhenlieChoice(JizhiResolution trick, int startIndex = 0)
    {
        if (!UsesFormalWangYi || !IsOrdinaryTrick(trick.EffectiveCardKind))
        {
            return false;
        }

        for (var index = startIndex; index < trick.TargetSeats.Count; index++)
        {
            var targetSeat = trick.TargetSeats[index];
            var target = _players[targetSeat];
            if (!target.IsAlive ||
                targetSeat == trick.PlayerSeat ||
                !HasRuntimeSkill(target, ZhenlieSkillId))
            {
                continue;
            }

            BeginZhenlieChoice(new ZhenlieResolution(
                trick.ResolutionId,
                trick.PlayerSeat,
                targetSeat,
                trick.EffectiveCardKind,
                ZhenlieContinuationKind.Trick,
                attack: null,
                trick,
                nextTargetIndex: index + 1));
            return true;
        }

        return false;
    }

    private void BeginZhenlieChoice(ZhenlieResolution pending)
    {
        if (_pendingZhenlie is not null)
        {
            throw new InvalidOperationException("The engine cannot open two Zhenlie choices at once.");
        }

        _pendingZhenlie = pending;
        var owner = _players[pending.OwnerSeat];
        var source = _players[pending.SourceSeat];
        var cardName = CardCatalog.Get(pending.CardKind).DisplayName;
        _pendingDecision = new PendingDecision(
            DecisionKind.Zhenlie,
            owner.Seat,
            $"{source.Name} 对你使用了【{cardName}】，是否发动【贞烈】失去1点体力，令此牌对你无效并弃置其一张牌？",
            [],
            [],
            pending.SourceSeat,
            pending.CardKind)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = owner.Seat,
            Choices =
            [
                new PromptChoice(
                    new ChoiceId($"zhenlie.use.resolution-{pending.ResolutionId}.seat-{owner.Seat}"),
                    "发动【贞烈】：失去1点体力，令此牌对你无效。",
                    [],
                    [],
                    new Dictionary<string, string> { ["action"] = "zhenlie-use" }),
                new PromptChoice(
                    new ChoiceId($"zhenlie.skip.resolution-{pending.ResolutionId}.seat-{owner.Seat}"),
                    "不发动【贞烈】，继续结算此牌。",
                    [],
                    [],
                    new Dictionary<string, string> { ["action"] = "zhenlie-skip" })
            ]
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private CommandResult SubmitZhenliePromptAnswer(PromptChoice selected)
    {
        var pending = _pendingZhenlie;
        if (pending is null ||
            _pendingDecision is not { Kind: DecisionKind.Zhenlie, PlayerSeat: var playerSeat } ||
            playerSeat != pending.OwnerSeat ||
            !selected.Parameters.TryGetValue("action", out var action))
        {
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的贞烈窗口。");
        }

        if (pending.Stage == ZhenlieStage.Offer)
        {
            if (selected.Cards.Count != 0 || selected.Targets.Count != 0)
            {
                return Reject(CommandErrorCode.InvalidChoice, "贞烈发动选择不能携带牌或目标。");
            }

            return action switch
            {
                "zhenlie-use" => Accept(() => HumanZhenlieCore(
                    useSkill: true,
                    _options.AdvanceAfterHumanCommands)),
                "zhenlie-skip" => Accept(() => HumanZhenlieCore(
                    useSkill: false,
                    _options.AdvanceAfterHumanCommands)),
                _ => Reject(CommandErrorCode.InvalidChoice, "贞烈提示没有可识别的发动选择。")
            };
        }

        if (pending.Stage != ZhenlieStage.SelectDiscard || selected.Targets.Count != 0)
        {
            return Reject(CommandErrorCode.InvalidChoice, "贞烈弃牌选择不符合当前步骤。");
        }

        if (action == "zhenlie-discard-hand" &&
            selected.Cards.Count == 0 &&
            selected.Parameters.TryGetValue("slot-index", out var slotText) &&
            int.TryParse(slotText, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var slot))
        {
            return Accept(() => HumanZhenlieDiscardCore(
                handSlot: slot,
                publicCardId: null,
                _options.AdvanceAfterHumanCommands));
        }

        if (action == "zhenlie-discard-public" && selected.Cards.Count == 1)
        {
            return Accept(() => HumanZhenlieDiscardCore(
                handSlot: null,
                publicCardId: selected.Cards[0],
                _options.AdvanceAfterHumanCommands));
        }

        return Reject(CommandErrorCode.InvalidChoice, "贞烈必须选择使用者的一张暗手牌或公开装备牌。");
    }

    private EngineRunResult HumanZhenlieCore(bool useSkill, bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.Zhenlie);
        ResolveZhenlieChoice(useSkill);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private EngineRunResult HumanZhenlieDiscardCore(
        int? handSlot,
        int? publicCardId,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.Zhenlie);
        ResolveZhenlieDiscard(handSlot, publicCardId);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private void ResolveZhenlieChoice(bool useSkill)
    {
        var pending = _pendingZhenlie ??
            throw new InvalidOperationException("There is no Zhenlie choice to resolve.");
        if (pending.Stage != ZhenlieStage.Offer)
        {
            throw new InvalidOperationException("Zhenlie is no longer awaiting its activation choice.");
        }

        var owner = _players[pending.OwnerSeat];
        ClearPendingDecision();
        if (!useSkill)
        {
            QueueGameEvent(new ZhenlieResolvedEvent(
                pending.ResolutionId,
                owner.Seat,
                pending.SourceSeat,
                pending.CardKind,
                Used: false,
                owner.Hp));
            AddLog("SkillSkipped", $"{owner.Name} 未发动【贞烈】。", owner.Seat, pending.SourceSeat);
            ContinueAfterZhenlie(pending, used: false, discardedCardId: null, discardedFromZone: null);
            return;
        }

        MarkCardEffectIneffective(pending.ResolutionId, owner.Seat);
        owner.Hp = Math.Max(0, owner.Hp - 1);
        QueueGameEvent(new SkillHpLostEvent(
            pending.ResolutionId,
            owner.Seat,
            SkillKind.Zhenlie,
            Amount: 1,
            owner.Hp));
        AddLog(
            "SkillTriggered",
            $"{owner.Name} 发动【贞烈】，失去1点体力，令【{CardCatalog.Get(pending.CardKind).DisplayName}】对自己无效。",
            owner.Seat,
            pending.SourceSeat);

        if (owner.Hp == 0)
        {
            BeginZhenlieDying(pending);
            return;
        }

        BeginZhenlieDiscardOrComplete(pending);
    }

    private void BeginZhenlieDying(ZhenlieResolution pending)
    {
        if (_pendingDying is not null)
        {
            throw new InvalidOperationException("The engine cannot resolve two dying players at once.");
        }

        var owner = _players[pending.OwnerSeat];
        var responderSeats = Array.AsReadOnly(BuildDyingResponderSeats(owner.Seat).ToArray());
        var frameId = ++_resolutionSequence;
        _resolutionStack.Add(new DyingFrame(
            frameId,
            pending.ResolutionId,
            owner.Seat,
            KillerSeat: null,
            responderSeats,
            ResponderIndex: 0));
        _pendingDying = new DyingResolution(
            frameId,
            damageFrameId: null,
            pending.Attack,
            owner.Seat,
            killerSeat: null,
            responderSeats,
            pending.ResolutionId,
            DyingContinuation.CardTargetSkill);
        QueueGameEvent(new PlayerDyingEvent(frameId, owner.Seat, KillerSeat: null));
        _status = EngineStatus.Running;
        if (TryResolveBuqu(_pendingDying))
        {
            return;
        }
        ExposeHumanDyingPrompt();
    }

    private void CompleteZhenlieAfterDying(DyingResolution dying, bool survived)
    {
        var pending = _pendingZhenlie ??
            throw new InvalidOperationException("Zhenlie dying completed without its card-target continuation.");
        if (pending.OwnerSeat != dying.VictimSeat || pending.ResolutionId != dying.ParentFrameId)
        {
            throw new InvalidOperationException("Zhenlie dying returned to the wrong card target.");
        }

        if (survived)
        {
            BeginZhenlieDiscardOrComplete(pending);
            return;
        }

        CompleteUsedZhenlie(pending, discardedCardId: null, discardedFromZone: null);
    }

    private void BeginZhenlieDiscardOrComplete(ZhenlieResolution pending)
    {
        var source = _players[pending.SourceSeat];
        var hand = GetHand(source).ToArray();
        var equipment = GetEquipment(source).OrderBy(card => card.Id).ToArray();
        if (!source.IsAlive || hand.Length + equipment.Length == 0)
        {
            CompleteUsedZhenlie(pending, discardedCardId: null, discardedFromZone: null);
            return;
        }

        pending.Stage = ZhenlieStage.SelectDiscard;
        var choices = hand.Select((_, slot) => new PromptChoice(
                new ChoiceId($"zhenlie.discard-hand.slot-{slot}.resolution-{pending.ResolutionId}"),
                $"弃置 {source.Name} 的一张暗手牌（牌位 {slot + 1}）。",
                [],
                [],
                new Dictionary<string, string>
                {
                    ["action"] = "zhenlie-discard-hand",
                    ["slot-index"] = slot.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }))
            .Concat(equipment.Select(card => new PromptChoice(
                new ChoiceId($"zhenlie.discard-public.card-{card.Id}.resolution-{pending.ResolutionId}"),
                $"弃置 {source.Name} 的装备【{card.DisplayName}】。",
                [card.Id],
                [],
                new Dictionary<string, string> { ["action"] = "zhenlie-discard-public" })))
            .ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.Zhenlie,
            pending.OwnerSeat,
            $"【贞烈】继续结算：请选择弃置 {source.Name} 的一张手牌或装备牌。",
            equipment.Select(card => card.Id).ToArray(),
            [],
            pending.SourceSeat,
            pending.CardKind)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = source.Seat,
            Choices = choices
        };
        _status = _players[pending.OwnerSeat].IsHuman
            ? EngineStatus.AwaitingHumanCardSelection
            : EngineStatus.Running;
    }

    private void ResolveZhenlieDiscard(int? handSlot, int? publicCardId)
    {
        var pending = _pendingZhenlie ??
            throw new InvalidOperationException("There is no Zhenlie discard to resolve.");
        if (pending.Stage != ZhenlieStage.SelectDiscard)
        {
            throw new InvalidOperationException("Zhenlie is not selecting the source card.");
        }

        var source = _players[pending.SourceSeat];
        Card card;
        CardLocation from;
        if (handSlot is { } slot)
        {
            var hand = GetHand(source);
            if (slot < 0 || slot >= hand.Count)
            {
                throw new InvalidOperationException("The selected Zhenlie hand slot is no longer valid.");
            }
            card = hand[slot];
            from = CardLocation.Hand(source.Seat);
        }
        else if (publicCardId is { } cardId)
        {
            card = GetEquipment(source).SingleOrDefault(candidate => candidate.Id == cardId) ??
                throw new InvalidOperationException("The selected Zhenlie equipment is no longer available.");
            from = CardLocation.Equipment(source.Seat);
        }
        else
        {
            throw new InvalidOperationException("Zhenlie must discard exactly one source card.");
        }

        ClearPendingDecision();
        MoveCard(card, from, CardLocation.Processing, CardMoveReasons.ZhenlieDiscard);
        MoveCard(card, CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.ZhenlieDiscard);
        CompleteUsedZhenlie(pending, card.Id, from.Zone);
    }

    private void CompleteUsedZhenlie(
        ZhenlieResolution pending,
        int? discardedCardId,
        CardZoneKind? discardedFromZone)
    {
        var owner = _players[pending.OwnerSeat];
        QueueGameEvent(new CardEffectSkippedEvent(
            pending.ResolutionId,
            pending.SourceSeat,
            pending.OwnerSeat,
            pending.CardKind,
            CardEffectSkipReason.SkillNullified));
        QueueGameEvent(new ZhenlieResolvedEvent(
            pending.ResolutionId,
            owner.Seat,
            pending.SourceSeat,
            pending.CardKind,
            Used: true,
            owner.Hp,
            discardedCardId,
            discardedFromZone));
        AddLog(
            "SkillResolved",
            discardedCardId is null
                ? $"{owner.Name} 的【贞烈】结算完成；牌的使用者已无可弃置牌。"
                : $"{owner.Name} 的【贞烈】弃置了牌的使用者一张牌。",
            owner.Seat,
            pending.SourceSeat);
        ContinueAfterZhenlie(pending, used: true, discardedCardId, discardedFromZone);
    }

    private void ContinueAfterZhenlie(
        ZhenlieResolution pending,
        bool used,
        int? discardedCardId,
        CardZoneKind? discardedFromZone)
    {
        _ = discardedCardId;
        _ = discardedFromZone;
        _pendingZhenlie = null;
        ClearPendingDecision();

        if (pending.Continuation == ZhenlieContinuationKind.Slash)
        {
            var attack = pending.Attack ??
                throw new InvalidOperationException("Zhenlie lost its Slash continuation.");
            if (used)
            {
                SetCardUseStep(attack.ResolutionId, ResolutionFrameStep.ResolvingEffect);
                CompleteAttack(attack);
            }
            else
            {
                ContinueSlashAfterFinalizedTargets(attack);
            }
            return;
        }

        var trick = pending.Trick ??
            throw new InvalidOperationException("Zhenlie lost its ordinary-trick continuation.");
        if (!TryBeginZhenlieChoice(trick, pending.NextTargetIndex))
        {
            ContinueJizhiOrNullificationAfterTargetTriggers(trick);
        }
    }

    private void ContinueJizhiOrNullificationAfterTargetTriggers(JizhiResolution pending)
    {
        if (TryBeginJizhiChoice(pending))
        {
            return;
        }

        BeginNullificationWindow(
            pending.ResolutionId,
            pending.Card,
            pending.PlayerSeat,
            pending.TargetSeats,
            pending.ActionKind,
            pending.TargetCardId,
            pending.RequiredCardKind,
            pending.EffectiveCardKind);
    }

    private void MarkCardEffectIneffective(long resolutionId, int targetSeat)
    {
        var index = _resolutionStack.FindLastIndex(frame => frame.Id == resolutionId);
        if (index < 0 || _resolutionStack[index] is not CardUseFrame cardUse)
        {
            throw new InvalidOperationException("Zhenlie requires an active CardUse frame.");
        }

        var seats = (cardUse.IneffectiveTargetSeats ?? [])
            .Append(targetSeat)
            .Distinct()
            .Order()
            .ToArray();
        _resolutionStack[index] = cardUse with { IneffectiveTargetSeats = seats };
    }

    private bool IsCardEffectIneffective(long resolutionId, int targetSeat) =>
        _resolutionStack.OfType<CardUseFrame>()
            .Single(frame => frame.Id == resolutionId)
            .IneffectiveTargetSeats?.Contains(targetSeat) == true;

    private void CompleteIneffectiveTrickTarget(
        NullificationResolution pending,
        int targetSeat)
    {
        SetCardUseStep(pending.ResolutionId, ResolutionFrameStep.ResolvingEffect);
        foreach (var physicalCard in GetCardUsePhysicalCards(pending.ResolutionId))
        {
            MoveCard(
                physicalCard,
                CardLocation.Processing,
                CardLocation.DiscardPile,
                CardMoveReasons.UseFinished);
        }
        AddLog(
            "CardEffect",
            $"{_players[targetSeat].Name} 的【贞烈】令【{CardCatalog.Get(pending.EffectiveCardKind).DisplayName}】对其无效。",
            targetSeat,
            pending.SourceSeat);
        FinishCardUse(pending.ResolutionId, pending.EffectCard, pending.EffectiveCardKind);
    }

    private bool IsAiZhenliePending() =>
        _pendingZhenlie is { OwnerSeat: var ownerSeat } &&
        _pendingDecision is { Kind: DecisionKind.Zhenlie, PlayerSeat: var decisionSeat } &&
        ownerSeat == decisionSeat &&
        !_players[ownerSeat].IsHuman;

    private void ResolvePendingAiZhenlie()
    {
        var pending = _pendingZhenlie ??
            throw new InvalidOperationException("There is no AI Zhenlie choice to resolve.");
        var owner = _players[pending.OwnerSeat];
        if (pending.Stage == ZhenlieStage.Offer)
        {
            var harmful = pending.CardKind is
                CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or
                CardKind.Duel or CardKind.BarbarianAssault or CardKind.ArrowBarrage or
                CardKind.Dismantlement or CardKind.Snatch or CardKind.FireAttack or
                CardKind.BorrowedSword;
            ResolveZhenlieChoice(harmful && owner.Hp > 1);
            PublishState();
            return;
        }

        var source = _players[pending.SourceSeat];
        var equipment = GetEquipment(source)
            .OrderByDescending(card => GetKeepValue(card, source))
            .ThenBy(card => card.Id)
            .FirstOrDefault();
        if (equipment is not null)
        {
            ResolveZhenlieDiscard(handSlot: null, publicCardId: equipment.Id);
        }
        else
        {
            var hand = GetHand(source);
            ResolveZhenlieDiscard(handSlot: hand.Count == 0 ? null : 0, publicCardId: null);
        }
        PublishState();
    }

    private bool TryBeginMijiChoice(CharacterState owner)
    {
        if (!UsesFormalWangYi ||
            _mijiResolvedThisTurn ||
            !owner.IsAlive ||
            owner.Hp >= owner.MaxHp ||
            !HasRuntimeSkill(owner, MijiSkillId))
        {
            return false;
        }

        var lostHp = owner.MaxHp - owner.Hp;
        _pendingMiji = new MijiResolution(owner.Seat, lostHp);
        _pendingDecision = new PendingDecision(
            DecisionKind.Miji,
            owner.Seat,
            $"结束阶段是否发动【秘计】，按已损失的 {lostHp} 点体力摸 {lostHp} 张牌？",
            [],
            [])
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            Choices =
            [
                new PromptChoice(
                    new ChoiceId($"miji.use.turn-{_turnNumber}.seat-{owner.Seat}"),
                    $"发动【秘计】，摸 {lostHp} 张牌。",
                    [],
                    [],
                    new Dictionary<string, string> { ["action"] = "miji-use" }),
                new PromptChoice(
                    new ChoiceId($"miji.skip.turn-{_turnNumber}.seat-{owner.Seat}"),
                    "不发动【秘计】。",
                    [],
                    [],
                    new Dictionary<string, string> { ["action"] = "miji-skip" })
            ]
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return true;
    }

    private CommandResult SubmitMijiPromptAnswer(PromptChoice selected)
    {
        var pending = _pendingMiji;
        if (pending is null ||
            _pendingDecision is not { Kind: DecisionKind.Miji, PlayerSeat: var playerSeat } ||
            playerSeat != pending.OwnerSeat ||
            !selected.Parameters.TryGetValue("action", out var action))
        {
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的秘计窗口。");
        }

        if (pending.Stage == MijiStage.Offer)
        {
            if (selected.Cards.Count != 0 || selected.Targets.Count != 0)
            {
                return Reject(CommandErrorCode.InvalidChoice, "秘计发动选择不能携带牌或目标。");
            }
            return action switch
            {
                "miji-use" => Accept(() => HumanMijiUseCore(
                    useSkill: true,
                    _options.AdvanceAfterHumanCommands)),
                "miji-skip" => Accept(() => HumanMijiUseCore(
                    useSkill: false,
                    _options.AdvanceAfterHumanCommands)),
                _ => Reject(CommandErrorCode.InvalidChoice, "秘计提示没有可识别的发动选择。")
            };
        }

        if (action == "miji-skip-gift" &&
            !pending.GivingStarted &&
            selected.Cards.Count == 0 &&
            selected.Targets.Count == 0)
        {
            return Accept(() => HumanMijiGiftCore(
                cardId: null,
                targetSeat: null,
                _options.AdvanceAfterHumanCommands));
        }

        if (action == "miji-give" &&
            selected.Cards.Count == 1 &&
            selected.Targets.Count == 1 &&
            selected.Targets[0] != pending.OwnerSeat)
        {
            return Accept(() => HumanMijiGiftCore(
                selected.Cards[0],
                selected.Targets[0],
                _options.AdvanceAfterHumanCommands));
        }

        return Reject(CommandErrorCode.InvalidChoice, "秘计交牌必须选择自己的一张手牌和一名其他存活角色。");
    }

    private EngineRunResult HumanMijiUseCore(bool useSkill, bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.Miji);
        ResolveMijiUse(useSkill);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private EngineRunResult HumanMijiGiftCore(
        int? cardId,
        int? targetSeat,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.Miji);
        ResolveMijiGift(cardId, targetSeat);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private void ResolveMijiUse(bool useSkill)
    {
        var pending = _pendingMiji ??
            throw new InvalidOperationException("There is no Miji choice to resolve.");
        if (pending.Stage != MijiStage.Offer)
        {
            throw new InvalidOperationException("Miji is no longer awaiting activation.");
        }

        var owner = _players[pending.OwnerSeat];
        ClearPendingDecision();
        if (!useSkill)
        {
            CompleteMiji(pending, used: false);
            return;
        }

        var drawn = DrawCards(owner, pending.LostHp, log: true, reason: CardMoveReasons.MijiDraw);
        pending.DrawnCardIds.AddRange(drawn);
        pending.RequiredGiftCount = drawn.Count;
        AddLog(
            "SkillTriggered",
            $"{owner.Name} 发动【秘计】，按已损失体力摸了 {drawn.Count} 张牌。",
            owner.Seat);
        if (pending.RequiredGiftCount == 0 ||
            _players.All(player => !player.IsAlive || player.Seat == owner.Seat))
        {
            CompleteMiji(pending, used: true);
            return;
        }

        pending.Stage = MijiStage.Gift;
        BeginMijiGiftChoice(pending);
    }

    private void BeginMijiGiftChoice(MijiResolution pending)
    {
        var owner = _players[pending.OwnerSeat];
        var hand = GetHand(owner).OrderBy(card => card.Id).ToArray();
        var targets = _players
            .Where(player => player.IsAlive && player.Seat != owner.Seat)
            .OrderBy(player => player.Seat)
            .ToArray();
        if (hand.Length == 0 || targets.Length == 0)
        {
            CompleteMiji(pending, used: true);
            return;
        }

        var remaining = pending.RequiredGiftCount - pending.GivenCardIds.Count;
        var choices = hand.SelectMany(card => targets.Select(target => new PromptChoice(
                new ChoiceId($"miji.give.card-{card.Id}.target-{target.Seat}.index-{pending.GivenCardIds.Count}"),
                $"将【{card.DisplayName}】交给 {target.Name}。",
                [card.Id],
                [target.Seat],
                new Dictionary<string, string> { ["action"] = "miji-give" })))
            .ToList();
        if (!pending.GivingStarted)
        {
            choices.Add(new PromptChoice(
                new ChoiceId($"miji.skip-gift.turn-{_turnNumber}.seat-{owner.Seat}"),
                "不分配手牌，结束【秘计】。",
                [],
                [],
                new Dictionary<string, string> { ["action"] = "miji-skip-gift" }));
        }

        _pendingDecision = new PendingDecision(
            DecisionKind.Miji,
            owner.Seat,
            pending.GivingStarted
                ? $"【秘计】还须交给其他角色 {remaining} 张手牌，请逐张分配。"
                : $"你可以将 {remaining} 张手牌交给其他角色；一旦开始分配，必须交足等量牌。",
            hand.Select(card => card.Id).ToArray(),
            targets.Select(target => target.Seat).ToArray())
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            Choices = choices
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanCardSelection : EngineStatus.Running;
    }

    private void ResolveMijiGift(int? cardId, int? targetSeat)
    {
        var pending = _pendingMiji ??
            throw new InvalidOperationException("There is no Miji gift to resolve.");
        if (pending.Stage != MijiStage.Gift)
        {
            throw new InvalidOperationException("Miji is not distributing cards.");
        }

        if (cardId is null && targetSeat is null && !pending.GivingStarted)
        {
            ClearPendingDecision();
            CompleteMiji(pending, used: true);
            return;
        }

        if (cardId is not { } selectedCardId || targetSeat is not { } selectedTargetSeat ||
            selectedTargetSeat == pending.OwnerSeat || !IsValidPlayerSeat(selectedTargetSeat) ||
            !_players[selectedTargetSeat].IsAlive)
        {
            throw new InvalidOperationException("Miji requires one hand card and one other living target.");
        }

        var owner = _players[pending.OwnerSeat];
        var card = GetHand(owner).SingleOrDefault(candidate => candidate.Id == selectedCardId) ??
            throw new InvalidOperationException("The selected Miji card is no longer in the owner's hand.");
        ClearPendingDecision();
        MoveCard(card, CardLocation.Hand(owner.Seat), CardLocation.Processing, CardMoveReasons.MijiGive);
        MoveCard(card, CardLocation.Processing, CardLocation.Hand(selectedTargetSeat), CardMoveReasons.MijiGive);
        pending.GivingStarted = true;
        pending.GivenCardIds.Add(card.Id);
        pending.TargetSeats.Add(selectedTargetSeat);
        AddLog(
            "SkillCardGiven",
            $"{owner.Name} 通过【秘计】交给 {_players[selectedTargetSeat].Name} 一张手牌。",
            owner.Seat,
            selectedTargetSeat);

        if (pending.GivenCardIds.Count >= pending.RequiredGiftCount)
        {
            CompleteMiji(pending, used: true);
        }
        else
        {
            BeginMijiGiftChoice(pending);
        }
    }

    private void CompleteMiji(MijiResolution pending, bool used)
    {
        var owner = _players[pending.OwnerSeat];
        QueueGameEvent(new MijiResolvedEvent(
            owner.Seat,
            used,
            pending.LostHp,
            Array.AsReadOnly(pending.DrawnCardIds.ToArray()),
            Array.AsReadOnly(pending.GivenCardIds.ToArray()),
            Array.AsReadOnly(pending.TargetSeats.ToArray())));
        AddLog(
            used ? "SkillResolved" : "SkillSkipped",
            used
                ? pending.GivenCardIds.Count == 0
                    ? $"{owner.Name} 完成【秘计】，未分配手牌。"
                    : $"{owner.Name} 完成【秘计】，向其他角色分配了 {pending.GivenCardIds.Count} 张手牌。"
                : $"{owner.Name} 未发动【秘计】。",
            owner.Seat);
        _pendingMiji = null;
        ClearPendingDecision();
        _mijiResolvedThisTurn = true;
        EndTurn();
    }

    private bool IsAiMijiPending() =>
        _pendingMiji is { OwnerSeat: var ownerSeat } &&
        _pendingDecision is { Kind: DecisionKind.Miji, PlayerSeat: var decisionSeat } &&
        ownerSeat == decisionSeat &&
        !_players[ownerSeat].IsHuman;

    private void ResolvePendingAiMiji()
    {
        var pending = _pendingMiji ??
            throw new InvalidOperationException("There is no AI Miji choice to resolve.");
        if (pending.Stage == MijiStage.Offer)
        {
            ResolveMijiUse(useSkill: true);
            PublishState();
            return;
        }

        var owner = _players[pending.OwnerSeat];
        var friendly = _players
            .Where(player => player.IsAlive && player.Seat != owner.Seat && AreMijiAllies(owner, player))
            .OrderBy(player => GetHand(player).Count)
            .ThenBy(player => player.Seat)
            .FirstOrDefault();
        if (friendly is null && !pending.GivingStarted)
        {
            ResolveMijiGift(cardId: null, targetSeat: null);
        }
        else
        {
            var target = friendly ?? _players
                .Where(player => player.IsAlive && player.Seat != owner.Seat)
                .OrderBy(player => player.Seat)
                .First();
            var card = GetHand(owner)
                .OrderBy(candidate => GetKeepValue(candidate, owner))
                .ThenBy(candidate => candidate.Id)
                .First();
            ResolveMijiGift(card.Id, target.Seat);
        }
        PublishState();
    }

    private static bool AreMijiAllies(CharacterState owner, CharacterState target) =>
        owner.TeamId is not null || target.TeamId is not null
            ? owner.TeamId is not null && owner.TeamId == target.TeamId
            : owner.Role switch
            {
                Role.Lord => target.Role is Role.Lord or Role.Loyalist,
                Role.Loyalist => target.Role is Role.Lord or Role.Loyalist,
                Role.Rebel => target.Role == Role.Rebel,
                _ => false
            };

    private void AssertWangYiInvariant()
    {
        if (_pendingZhenlie is { } zhenlie)
        {
            if (!UsesFormalWangYi ||
                !_resolutionStack.OfType<CardUseFrame>().Any(frame => frame.Id == zhenlie.ResolutionId) ||
                (_pendingDecision?.Kind != DecisionKind.Zhenlie &&
                 _pendingDying is not { ResumesCardTargetSkill: true }))
            {
                throw new InvalidOperationException("The active Zhenlie continuation is inconsistent.");
            }
        }
        else if (_pendingDecision?.Kind == DecisionKind.Zhenlie ||
                 _pendingDying is { ResumesCardTargetSkill: true })
        {
            throw new InvalidOperationException("A Zhenlie prompt or dying continuation lost its skill state.");
        }

        if (_pendingMiji is { } miji)
        {
            if (!UsesFormalWangYi ||
                _pendingDecision is not { Kind: DecisionKind.Miji, PlayerSeat: var seat } ||
                seat != miji.OwnerSeat ||
                miji.GivenCardIds.Count > miji.RequiredGiftCount)
            {
                throw new InvalidOperationException("The active Miji continuation is inconsistent.");
            }
        }
        else if (_pendingDecision?.Kind == DecisionKind.Miji)
        {
            throw new InvalidOperationException("A Miji prompt cannot exist without its end-phase continuation.");
        }
    }

    private enum ZhenlieContinuationKind { Slash, Trick }
    private enum ZhenlieStage { Offer, SelectDiscard }

    private sealed class ZhenlieResolution(
        long resolutionId,
        int sourceSeat,
        int ownerSeat,
        CardKind cardKind,
        ZhenlieContinuationKind continuation,
        AttackResolution? attack,
        JizhiResolution? trick,
        int nextTargetIndex)
    {
        public long ResolutionId { get; } = resolutionId;
        public int SourceSeat { get; } = sourceSeat;
        public int OwnerSeat { get; } = ownerSeat;
        public CardKind CardKind { get; } = cardKind;
        public ZhenlieContinuationKind Continuation { get; } = continuation;
        public AttackResolution? Attack { get; } = attack;
        public JizhiResolution? Trick { get; } = trick;
        public int NextTargetIndex { get; } = nextTargetIndex;
        public ZhenlieStage Stage { get; set; }
    }

    private enum MijiStage { Offer, Gift }

    private sealed class MijiResolution(int ownerSeat, int lostHp)
    {
        public int OwnerSeat { get; } = ownerSeat;
        public int LostHp { get; } = lostHp;
        public MijiStage Stage { get; set; }
        public int RequiredGiftCount { get; set; }
        public bool GivingStarted { get; set; }
        public List<int> DrawnCardIds { get; } = [];
        public List<int> GivenCardIds { get; } = [];
        public List<int> TargetSeats { get; } = [];
    }
}
