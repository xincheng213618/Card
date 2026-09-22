namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string GongqiSkillId = "classic:gongqi";
    private const string GongqiRangeUsageId = "unlimited-range";
    private const string JiefanSkillId = "classic:jiefan";
    private const string JiefanUsageId = "activation";

    private GongqiResolution? _pendingGongqi;
    private JiefanResolution? _pendingJiefan;

    private bool UsesFormalHanDang =>
        HasClassicGeneralPackage(new Version(1, 93, 0));

    private bool HasGongqi(CharacterState owner) =>
        UsesFormalHanDang && HasRuntimeSkill(owner, GongqiSkillId);

    private bool HasJiefan(CharacterState owner) =>
        UsesFormalHanDang && HasRuntimeSkill(owner, JiefanSkillId);

    private bool HasGongqiUnlimitedRange(CharacterState owner) =>
        HasGongqi(owner) &&
        _skillRuntimeState.GetUsage(
            owner.Seat,
            GongqiSkillId,
            GongqiRangeUsageId,
            SkillUsageScope.Turn) > 0;

    private bool CanUseJiefan(CharacterState owner) =>
        HasJiefan(owner) &&
        owner.IsAlive &&
        owner.Seat == _currentSeat &&
        _phase == TurnPhase.Play &&
        !owner.UsedLimitedSkillKinds.Contains(SkillKind.Jiefan) &&
        _skillRuntimeState.GetUsage(
            owner.Seat,
            JiefanSkillId,
            JiefanUsageId,
            SkillUsageScope.Game) == 0;

    private void BeginGongqi(long frameId, CharacterState owner, int costCardId)
    {
        if (!HasGongqi(owner) || owner.UsedActiveSkillKinds.Contains(SkillKind.Gongqi))
        {
            throw new InvalidOperationException("Gongqi is no longer legal for its owner.");
        }

        var handCard = GetHand(owner).SingleOrDefault(card => card.Id == costCardId);
        var equipmentCard = GetEquipment(owner).SingleOrDefault(card => card.Id == costCardId);
        var cost = handCard ?? equipmentCard ??
            throw new InvalidOperationException("Gongqi must discard one owned hand or equipment card.");
        var from = handCard is not null
            ? CardLocation.Hand(owner.Seat)
            : CardLocation.Equipment(owner.Seat);
        var equipmentCost = EquipmentCatalog.IsEquipment(cost.Kind);

        owner.UsedActiveSkillKinds.Add(SkillKind.Gongqi);
        if (!_skillRuntimeState.TryConsumeUsage(
                owner.Seat,
                GongqiSkillId,
                GongqiRangeUsageId,
                SkillUsageScope.Turn,
                limit: 1))
        {
            throw new InvalidOperationException("Gongqi tried to establish unlimited range twice in one turn.");
        }

        QueueGameEvent(new SkillUsageConsumedEvent(
            owner.Seat,
            GongqiSkillId,
            GongqiRangeUsageId,
            SkillUsageScope.Turn,
            Count: 1));
        MoveCard(cost, from, CardLocation.Processing, CardMoveReasons.GongqiCost);
        MoveCard(cost, CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.GongqiCost);
        AddLog(
            "ActiveSkill",
            $"{owner.Name} 发动【弓骑】，弃置【{cost.DisplayName}】，本回合攻击范围无限。",
            owner.Seat);

        if (!equipmentCost)
        {
            CompleteGongqi(frameId, owner, cost.Id, equipmentCost: false, targetSeat: null, discardedCardId: null);
            return;
        }

        var candidates = _players
            .Where(player => player.IsAlive && player.Seat != owner.Seat &&
                             (GetHand(player).Count > 0 || GetEquipment(player).Count > 0))
            .OrderBy(player => (player.Seat - owner.Seat + _playerCount) % _playerCount)
            .Select(player => new GongqiTargetCards(
                player.Seat,
                GetHand(player).Select(card => card.Id).ToArray(),
                GetEquipment(player).Select(card => card.Id).Order().ToArray()))
            .ToArray();
        if (candidates.Length == 0)
        {
            CompleteGongqi(frameId, owner, cost.Id, equipmentCost: true, targetSeat: null, discardedCardId: null);
            return;
        }

        _pendingGongqi = new GongqiResolution(frameId, owner.Seat, cost.Id, candidates);
        SetActiveSkillFrameStep(frameId, ResolutionFrameStep.AwaitingResponse);
        PublishGongqiDiscardDecision(_pendingGongqi);
    }

    private void PublishGongqiDiscardDecision(GongqiResolution pending)
    {
        var owner = _players[pending.OwnerSeat];
        var choices = new List<PromptChoice>();
        foreach (var targetCards in pending.Targets)
        {
            var target = _players[targetCards.TargetSeat];
            for (var slot = 0; slot < targetCards.HandCardIds.Count; slot++)
            {
                choices.Add(new PromptChoice(
                    new ChoiceId($"gongqi.target-{target.Seat}.hand-slot-{slot}.resolution-{pending.FrameId}"),
                    $"弃置 {target.Name} 的第 {slot + 1} 个暗置手牌牌位。",
                    [],
                    [target.Seat],
                    new Dictionary<string, string>
                    {
                        ["action"] = "gongqi-discard-hand",
                        ["target-seat"] = target.Seat.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        ["slot-index"] = slot.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    }));
            }

            foreach (var cardId in targetCards.EquipmentCardIds)
            {
                var card = GetEquipment(target).Single(candidate => candidate.Id == cardId);
                choices.Add(new PromptChoice(
                    new ChoiceId($"gongqi.target-{target.Seat}.equipment-{card.Id}.resolution-{pending.FrameId}"),
                    $"弃置 {target.Name} 的装备【{card.DisplayName}】。",
                    [card.Id],
                    [target.Seat],
                    new Dictionary<string, string>
                    {
                        ["action"] = "gongqi-discard-equipment",
                        ["target-seat"] = target.Seat.ToString(System.Globalization.CultureInfo.InvariantCulture)
                    }));
            }
        }

        choices.Add(new PromptChoice(
            new ChoiceId($"gongqi.skip.resolution-{pending.FrameId}"),
            "不弃置其他角色的牌。",
            [],
            [],
            new Dictionary<string, string> { ["action"] = "gongqi-skip" }));
        _pendingDecision = new PendingDecision(
            DecisionKind.Gongqi,
            owner.Seat,
            "你以装备牌发动了【弓骑】，可以弃置一名其他角色的一张牌。",
            [],
            pending.Targets.Select(target => target.TargetSeat).ToArray(),
            SourceSeat: owner.Seat)
        {
            PromptId = owner.IsHuman ? CreatePromptId() : default,
            IsPrivate = true,
            Choices = choices
        };
        _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private CommandResult SubmitGongqiPromptAnswer(PromptChoice selected)
    {
        if (_pendingGongqi is not { } pending ||
            _pendingDecision is not { Kind: DecisionKind.Gongqi, PlayerSeat: var ownerSeat } ||
            ownerSeat != pending.OwnerSeat)
        {
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的弓骑弃牌窗口。");
        }

        return Accept(() => HumanGongqiCore(
            pending,
            selected,
            _options.AdvanceAfterHumanCommands));
    }

    private EngineRunResult HumanGongqiCore(
        GongqiResolution pending,
        PromptChoice selected,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.Gongqi);
        ResolveGongqiChoice(pending, selected);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private bool IsAiGongqiPending() =>
        _pendingGongqi is { } pending &&
        _pendingDecision is { Kind: DecisionKind.Gongqi, PlayerSeat: var ownerSeat } &&
        ownerSeat == pending.OwnerSeat &&
        !_players[ownerSeat].IsHuman;

    private void ResolvePendingAiGongqi()
    {
        var pending = _pendingGongqi ??
            throw new InvalidOperationException("There is no AI Gongqi choice to resolve.");
        var decision = _pendingDecision ??
            throw new InvalidOperationException("The AI Gongqi choice has no prompt.");
        var (selected, thought) = _aiBrains[pending.OwnerSeat].ChooseGongqiDiscard(
            CreateSnapshot(pending.OwnerSeat),
            decision.Choices,
            ++_thoughtSequence);
        AddThought(thought);
        ResolveGongqiChoice(pending, selected);
        PublishState();
    }

    private void ResolveGongqiChoice(GongqiResolution pending, PromptChoice selected)
    {
        if (!ReferenceEquals(_pendingGongqi, pending) ||
            _pendingDecision is not { Kind: DecisionKind.Gongqi } ||
            !ReferenceEquals(_pendingDecision.Choices.FirstOrDefault(choice => choice.Id == selected.Id), selected))
        {
            throw new InvalidOperationException("The Gongqi target-card choice is not current.");
        }

        int? targetSeat = null;
        int? discardedCardId = null;
        var action = selected.Parameters.GetValueOrDefault("action");
        if (action != "gongqi-skip")
        {
            if (!selected.Parameters.TryGetValue("target-seat", out var targetText) ||
                !int.TryParse(targetText, out var parsedTarget) ||
                pending.Targets.SingleOrDefault(target => target.TargetSeat == parsedTarget) is not { } targetCards)
            {
                throw new InvalidOperationException("The Gongqi target is malformed.");
            }

            targetSeat = parsedTarget;
            var target = _players[parsedTarget];
            Card card;
            CardLocation from;
            if (action == "gongqi-discard-hand")
            {
                if (!selected.Parameters.TryGetValue("slot-index", out var slotText) ||
                    !int.TryParse(slotText, out var slot) ||
                    slot < 0 || slot >= targetCards.HandCardIds.Count)
                {
                    throw new InvalidOperationException("The Gongqi hand slot is malformed.");
                }
                var cardId = targetCards.HandCardIds[slot];
                card = GetHand(target).SingleOrDefault(candidate => candidate.Id == cardId) ??
                    throw new InvalidOperationException("The selected Gongqi hand card is no longer available.");
                from = CardLocation.Hand(target.Seat);
            }
            else if (action == "gongqi-discard-equipment" && selected.Cards.Count == 1)
            {
                card = GetEquipment(target).SingleOrDefault(candidate => candidate.Id == selected.Cards[0]) ??
                    throw new InvalidOperationException("The selected Gongqi equipment is no longer available.");
                from = CardLocation.Equipment(target.Seat);
            }
            else
            {
                throw new InvalidOperationException("The Gongqi target-card choice is malformed.");
            }

            discardedCardId = card.Id;
            MoveCard(card, from, CardLocation.Processing, CardMoveReasons.GongqiDiscard);
            MoveCard(card, CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.GongqiDiscard);
            AddLog("SkillTriggered", $"{_players[pending.OwnerSeat].Name} 以【弓骑】弃置了 {target.Name} 的一张牌。",
                pending.OwnerSeat, target.Seat);
        }

        ClearPendingDecision();
        _pendingGongqi = null;
        CompleteGongqi(
            pending.FrameId,
            _players[pending.OwnerSeat],
            pending.CostCardId,
            equipmentCost: true,
            targetSeat,
            discardedCardId);
    }

    private void CompleteGongqi(
        long frameId,
        CharacterState owner,
        int costCardId,
        bool equipmentCost,
        int? targetSeat,
        int? discardedCardId)
    {
        QueueGameEvent(new GongqiResolvedEvent(
            frameId,
            owner.Seat,
            costCardId,
            equipmentCost,
            targetSeat,
            discardedCardId));
        SetActiveSkillFrameStep(frameId, ResolutionFrameStep.Completed);
        QueueGameEvent(new ActiveSkillResolvedEvent(
            frameId,
            owner.Seat,
            SkillKind.Gongqi,
            ActiveSkillEffectKind.DiscardForUnlimitedRange));
        PopResolutionFrame(frameId, ResolutionFrameKind.ActiveSkill);
    }

    private void BeginJiefan(long frameId, CharacterState owner, int targetSeat)
    {
        if (!CanUseJiefan(owner) || !_players[targetSeat].IsAlive)
        {
            throw new InvalidOperationException("Jiefan is no longer legal for its owner or target.");
        }

        owner.UsedLimitedSkillKinds.Add(SkillKind.Jiefan);
        if (!_skillRuntimeState.TryConsumeUsage(
                owner.Seat,
                JiefanSkillId,
                JiefanUsageId,
                SkillUsageScope.Game,
                limit: 1))
        {
            throw new InvalidOperationException("Jiefan tried to consume its limited use twice.");
        }
        QueueGameEvent(new SkillUsageConsumedEvent(
            owner.Seat,
            JiefanSkillId,
            JiefanUsageId,
            SkillUsageScope.Game,
            Count: 1));

        var responders = _players
            .Where(player => player.IsAlive && player.Seat != targetSeat &&
                             GetCombatDistance(player.Seat, targetSeat) <= GetAttackRange(player.Seat))
            .OrderBy(player => (player.Seat - owner.Seat + _playerCount) % _playerCount)
            .Select(player => player.Seat)
            .ToArray();
        var pending = new JiefanResolution(frameId, owner.Seat, targetSeat, responders);
        _pendingJiefan = pending;
        QueueGameEvent(new JiefanStartedEvent(
            frameId,
            owner.Seat,
            targetSeat,
            Array.AsReadOnly(responders)));
        AddLog(
            "ActiveSkill",
            $"{owner.Name} 对 {_players[targetSeat].Name} 发动限定技【解烦】，共有 {responders.Length} 名角色依次选择。",
            owner.Seat,
            targetSeat);

        if (responders.Length == 0)
        {
            CompleteJiefan(pending);
            return;
        }

        SetActiveSkillFrameStep(frameId, ResolutionFrameStep.AwaitingResponse);
        PublishNextJiefanDecision(pending);
    }

    private void PublishNextJiefanDecision(JiefanResolution pending)
    {
        while (pending.ResponderIndex < pending.ResponderSeats.Count &&
               !_players[pending.ResponderSeats[pending.ResponderIndex]].IsAlive)
        {
            pending.ResponderIndex++;
        }
        if (pending.ResponderIndex >= pending.ResponderSeats.Count)
        {
            CompleteJiefan(pending);
            return;
        }

        var responder = _players[pending.ResponderSeats[pending.ResponderIndex]];
        var target = _players[pending.TargetSeat];
        var choices = GetEquipment(responder)
            .Where(card => EquipmentCatalog.Get(card.Kind).Slot == EquipmentSlot.Weapon)
            .OrderBy(card => card.Id)
            .Select(card => new PromptChoice(
                new ChoiceId($"jiefan.discard-weapon-{card.Id}.resolution-{pending.FrameId}"),
                $"弃置武器【{card.DisplayName}】。",
                new[] { card.Id },
                Array.Empty<int>(),
                new Dictionary<string, string> { ["action"] = "jiefan-discard-weapon" }))
            .Append(new PromptChoice(
                new ChoiceId($"jiefan.draw.target-{target.Seat}.responder-{responder.Seat}.resolution-{pending.FrameId}"),
                $"令 {target.Name} 摸一张牌。",
                [],
                [target.Seat],
                new Dictionary<string, string> { ["action"] = "jiefan-draw" }))
            .ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.Jiefan,
            responder.Seat,
            $"{_players[pending.OwnerSeat].Name} 发动【解烦】：弃置一张武器牌，或令 {target.Name} 摸一张牌。",
            choices.SelectMany(choice => choice.Cards).ToArray(),
            [target.Seat],
            SourceSeat: pending.OwnerSeat)
        {
            PromptId = responder.IsHuman ? CreatePromptId() : default,
            IsPrivate = true,
            TargetSeat = target.Seat,
            Choices = choices
        };
        _status = responder.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private CommandResult SubmitJiefanPromptAnswer(PromptChoice selected)
    {
        if (_pendingJiefan is not { } pending ||
            _pendingDecision is not { Kind: DecisionKind.Jiefan, PlayerSeat: var responderSeat } ||
            pending.ResponderIndex >= pending.ResponderSeats.Count ||
            responderSeat != pending.ResponderSeats[pending.ResponderIndex])
        {
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的解烦选择窗口。");
        }
        return Accept(() => HumanJiefanCore(
            pending,
            selected,
            _options.AdvanceAfterHumanCommands));
    }

    private EngineRunResult HumanJiefanCore(
        JiefanResolution pending,
        PromptChoice selected,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.Jiefan);
        ResolveJiefanChoice(pending, selected);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private bool IsAiJiefanPending() =>
        _pendingJiefan is { } pending &&
        pending.ResponderIndex < pending.ResponderSeats.Count &&
        _pendingDecision is { Kind: DecisionKind.Jiefan, PlayerSeat: var responderSeat } &&
        responderSeat == pending.ResponderSeats[pending.ResponderIndex] &&
        !_players[responderSeat].IsHuman;

    private void ResolvePendingAiJiefan()
    {
        var pending = _pendingJiefan ??
            throw new InvalidOperationException("There is no AI Jiefan response to resolve.");
        var decision = _pendingDecision ??
            throw new InvalidOperationException("The AI Jiefan response has no prompt.");
        var responderSeat = pending.ResponderSeats[pending.ResponderIndex];
        var (selected, thought) = _aiBrains[responderSeat].ChooseJiefanResponse(
            CreateSnapshot(responderSeat),
            pending.TargetSeat,
            decision.Choices,
            ++_thoughtSequence);
        AddThought(thought);
        ResolveJiefanChoice(pending, selected);
        PublishState();
    }

    private void ResolveJiefanChoice(JiefanResolution pending, PromptChoice selected)
    {
        if (!ReferenceEquals(_pendingJiefan, pending) ||
            pending.ResponderIndex >= pending.ResponderSeats.Count ||
            _pendingDecision is not { Kind: DecisionKind.Jiefan, PlayerSeat: var responderSeat } ||
            responderSeat != pending.ResponderSeats[pending.ResponderIndex] ||
            !ReferenceEquals(_pendingDecision.Choices.FirstOrDefault(choice => choice.Id == selected.Id), selected))
        {
            throw new InvalidOperationException("The Jiefan response is not current.");
        }

        var responder = _players[responderSeat];
        var target = _players[pending.TargetSeat];
        int? discardedWeaponCardId = null;
        IReadOnlyList<int> drawnCardIds = [];
        var action = selected.Parameters.GetValueOrDefault("action");
        if (action == "jiefan-discard-weapon" && selected.Cards.Count == 1)
        {
            var weapon = GetEquipment(responder).SingleOrDefault(card =>
                card.Id == selected.Cards[0] &&
                EquipmentCatalog.Get(card.Kind).Slot == EquipmentSlot.Weapon) ??
                throw new InvalidOperationException("The selected Jiefan weapon is no longer equipped.");
            discardedWeaponCardId = weapon.Id;
            MoveCard(weapon, CardLocation.Equipment(responder.Seat), CardLocation.Processing,
                CardMoveReasons.JiefanWeaponDiscard);
            MoveCard(weapon, CardLocation.Processing, CardLocation.DiscardPile,
                CardMoveReasons.JiefanWeaponDiscard);
            AddLog("SkillTriggered", $"{responder.Name} 响应【解烦】，弃置武器【{weapon.DisplayName}】。",
                responder.Seat, target.Seat);
        }
        else if (action == "jiefan-draw" && selected.Cards.Count == 0 &&
                 selected.Targets.SequenceEqual([target.Seat]))
        {
            var drawn = target.IsAlive
                ? DrawCards(target, 1, log: true, reason: CardMoveReasons.JiefanDraw)
                : [];
            drawnCardIds = Array.AsReadOnly(drawn.ToArray());
            AddLog("SkillTriggered", $"{responder.Name} 响应【解烦】，令 {target.Name} 摸一张牌。",
                responder.Seat, target.Seat);
        }
        else
        {
            throw new InvalidOperationException("The Jiefan choice is malformed.");
        }

        QueueGameEvent(new JiefanChoiceResolvedEvent(
            pending.FrameId,
            responder.Seat,
            target.Seat,
            discardedWeaponCardId,
            drawnCardIds));
        ClearPendingDecision();
        pending.ResponderIndex++;
        PublishNextJiefanDecision(pending);
    }

    private void CompleteJiefan(JiefanResolution pending)
    {
        _pendingJiefan = null;
        ClearPendingDecision();
        SetActiveSkillFrameStep(pending.FrameId, ResolutionFrameStep.Completed);
        QueueGameEvent(new ActiveSkillResolvedEvent(
            pending.FrameId,
            pending.OwnerSeat,
            SkillKind.Jiefan,
            ActiveSkillEffectKind.AidByAttackRange));
        PopResolutionFrame(pending.FrameId, ResolutionFrameKind.ActiveSkill);
    }

    private void AssertHanDangInvariant()
    {
        if (_pendingGongqi is { } gongqi &&
            (!UsesFormalHanDang ||
             _pendingJiefan is not null ||
             _pendingDecision is not { Kind: DecisionKind.Gongqi, PlayerSeat: var gongqiOwner } ||
             gongqiOwner != gongqi.OwnerSeat ||
             _resolutionStack.LastOrDefault() is not ActiveSkillFrame
             {
                 Id: var gongqiFrameId,
                 Skill: SkillKind.Gongqi,
                 Effect: ActiveSkillEffectKind.DiscardForUnlimitedRange,
                 Step: ResolutionFrameStep.AwaitingResponse
             } || gongqiFrameId != gongqi.FrameId))
        {
            throw new InvalidOperationException("A Gongqi continuation lost its owner, prompt or active-skill frame.");
        }

        if (_pendingJiefan is { } jiefan &&
            (!UsesFormalHanDang ||
             _pendingGongqi is not null ||
             jiefan.ResponderIndex < 0 ||
             jiefan.ResponderIndex >= jiefan.ResponderSeats.Count ||
             _pendingDecision is not { Kind: DecisionKind.Jiefan, PlayerSeat: var responderSeat } ||
             responderSeat != jiefan.ResponderSeats[jiefan.ResponderIndex] ||
             _resolutionStack.LastOrDefault() is not ActiveSkillFrame
             {
                 Id: var jiefanFrameId,
                 Skill: SkillKind.Jiefan,
                 Effect: ActiveSkillEffectKind.AidByAttackRange,
                 Step: ResolutionFrameStep.AwaitingResponse
             } || jiefanFrameId != jiefan.FrameId))
        {
            throw new InvalidOperationException("A Jiefan continuation lost its responder, prompt or active-skill frame.");
        }
    }

    private sealed record GongqiTargetCards(
        int TargetSeat,
        IReadOnlyList<int> HandCardIds,
        IReadOnlyList<int> EquipmentCardIds);

    private sealed record GongqiResolution(
        long FrameId,
        int OwnerSeat,
        int CostCardId,
        IReadOnlyList<GongqiTargetCards> Targets);

    private sealed class JiefanResolution(
        long frameId,
        int ownerSeat,
        int targetSeat,
        IReadOnlyList<int> responderSeats)
    {
        public long FrameId { get; } = frameId;
        public int OwnerSeat { get; } = ownerSeat;
        public int TargetSeat { get; } = targetSeat;
        public IReadOnlyList<int> ResponderSeats { get; } = responderSeats;
        public int ResponderIndex { get; set; }
    }
}
