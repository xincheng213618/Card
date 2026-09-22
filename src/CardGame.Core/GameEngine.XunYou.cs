namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string QiceSkillId = "classic:qice";
    private const string ZhiyuSkillId = "classic:zhiyu";

    private QiceResolution? _pendingQice;
    private ZhiyuDiscardResolution? _pendingZhiyuDiscard;

    private bool UsesFormalXunYou =>
        HasClassicGeneralPackage(new Version(1, 87, 0));

    private bool IsAiQicePending() =>
        _pendingQice is not null &&
        _pendingDecision is { Kind: DecisionKind.Qice } decision &&
        !_players[decision.PlayerSeat].IsHuman;

    private bool CanUseFormalQice(CharacterState source)
    {
        var hand = GetHand(source);
        return UsesFormalXunYou &&
               HasRuntimeSkill(source, QiceSkillId) &&
               _phase == TurnPhase.Play &&
               _currentSeat == source.Seat &&
               hand.Count > 0 &&
               !source.UsedActiveSkillKinds.Contains(SkillKind.Qice) &&
               hand.All(card => !IsQianxiHandCardRestricted(source, card));
    }

    private void BeginQiceChoice(CharacterState source, IReadOnlyList<int> selectedCardIds)
    {
        var hand = GetHand(source).OrderBy(card => card.Id).ToArray();
        var selected = selectedCardIds.Order().ToArray();
        if (!CanUseFormalQice(source) ||
            selected.Length != hand.Length ||
            !selected.SequenceEqual(hand.Select(card => card.Id)))
        {
            throw new InvalidOperationException("奇策必须选择当前全部手牌，且至少选择一张。");
        }

        var options = BuildQiceOptions(source);
        if (options.Count == 0)
        {
            throw new InvalidOperationException("当前没有可由奇策使用的普通锦囊牌。");
        }

        source.UsedActiveSkillKinds.Add(SkillKind.Qice);
        _pendingQice = new QiceResolution(source.Seat, Array.AsReadOnly(selected), options);
        _pendingDecision = new PendingDecision(
            DecisionKind.Qice,
            source.Seat,
            $"【奇策】已选择全部 {selected.Length} 张手牌，请选择要视为使用的普通锦囊及其合法目标。",
            selected,
            options.SelectMany(option => option.TargetSeats).Distinct().Order().ToArray(),
            SourceSeat: source.Seat)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            Choices = options.Select(CreateQicePromptChoice).ToArray()
        };
        _status = source.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        AddLog("SkillTriggered", $"{source.Name} 发动【奇策】，准备将全部 {selected.Length} 张手牌当一张普通锦囊牌使用。", source.Seat);
    }

    private CommandResult SubmitQicePromptAnswer(PromptChoice selected)
    {
        if (_pendingQice is null || _pendingDecision is not { Kind: DecisionKind.Qice })
        {
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的奇策牌名选择。");
        }

        return Accept(() => HumanQiceCore(selected, _options.AdvanceAfterHumanCommands));
    }

    private EngineRunResult HumanQiceCore(PromptChoice selected, bool advanceToHumanBoundary)
    {
        ResolveQiceChoice(selected.Id);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private void ResolvePendingAiQice()
    {
        var pending = _pendingQice ?? throw new InvalidOperationException("AI 奇策结算不存在。");
        var option = pending.Options.FirstOrDefault(candidate => candidate.EffectiveCardKind == CardKind.DrawTwo) ??
                     pending.Options.First();
        ResolveQiceChoice(option.Id);
        PublishState();
    }

    private void ResolveQiceChoice(ChoiceId choiceId)
    {
        var pending = _pendingQice ?? throw new InvalidOperationException("没有等待响应的奇策结算。");
        var source = _players[pending.SourceSeat];
        var option = pending.Options.SingleOrDefault(candidate => candidate.Id == choiceId) ??
                     throw new InvalidOperationException("所选奇策锦囊不在已发布选项中。");
        var hand = GetHand(source).OrderBy(card => card.Id).ToArray();
        if (!source.IsAlive ||
            !pending.CardIds.SequenceEqual(hand.Select(card => card.Id)) ||
            !source.UsedActiveSkillKinds.Contains(SkillKind.Qice))
        {
            throw new InvalidOperationException("奇策等待选择期间手牌或技能状态发生了变化。");
        }

        var representative = hand[0];
        var physicalCardIds = hand.Select(card => card.Id).ToArray();
        _pendingQice = null;
        ClearPendingDecision();

        var resolutionId = BeginCardUse(
            representative,
            source.Seat,
            option.TargetSeats,
            option.EffectiveCardKind,
            physicalCardIds: physicalCardIds);
        MoveCards(hand, CardLocation.Hand(source.Seat), CardLocation.Processing, CardMoveReasons.Use);
        QueueGameEvent(new QiceConvertedEvent(
            resolutionId,
            source.Seat,
            Array.AsReadOnly(physicalCardIds),
            option.EffectiveCardKind,
            option.TargetSeats));
        AddLog(
            "SkillTriggered",
            $"{source.Name} 发动【奇策】，将全部 {physicalCardIds.Length} 张手牌当【{CardCatalog.Get(option.EffectiveCardKind).DisplayName}】使用。",
            source.Seat,
            option.TargetSeats.FirstOrDefault(-1));
        BeginJizhiOrNullificationWindow(
            resolutionId,
            representative,
            source.Seat,
            option.TargetSeats,
            option.ActionKind,
            option.TargetCardId,
            option.RequiredCardKind,
            option.EffectiveCardKind);
    }

    private IReadOnlyList<QiceOption> BuildQiceOptions(CharacterState source)
    {
        var options = new List<QiceOption>();
        void Add(
            CardKind cardKind,
            LegalActionKind actionKind,
            string description,
            IReadOnlyList<int>? targetSeats = null,
            int? targetCardId = null,
            CardKind? requiredCardKind = null)
        {
            var targets = targetSeats?.ToArray() ?? [];
            var targetPart = targets.Length == 0 ? "none" : string.Join('-', targets);
            var cardPart = targetCardId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none";
            options.Add(new QiceOption(
                new ChoiceId($"qice.{cardKind}.targets-{targetPart}.card-{cardPart}"),
                cardKind,
                actionKind,
                Array.AsReadOnly(targets),
                targetCardId,
                requiredCardKind,
                description));
        }

        Add(CardKind.DrawTwo, LegalActionKind.DrawTwo, "当【无中生有】使用：摸两张牌");

        if (CanUseGlobalCard(source, CardKind.BarbarianAssault) ||
            CanUseGlobalCard(source, CardKind.ArrowBarrage))
        {
            var otherSeats = Enumerable.Range(1, _playerCount - 1)
                .Select(offset => _players[(source.Seat + offset) % _playerCount])
                .Where(player => player.IsAlive)
                .Select(player => player.Seat)
                .ToArray();
            var barbarianTargets = otherSeats.Where(seat =>
                    !(UsesFormalMengHuo && HasRuntimeSkill(_players[seat], SkillKind.Huoshou)) &&
                    !(UsesFormalZhuRong && HasRuntimeSkill(_players[seat], SkillKind.Juxiang)))
                .ToArray();
            if (CanUseGlobalCard(source, CardKind.BarbarianAssault))
                Add(CardKind.BarbarianAssault, LegalActionKind.BarbarianAssault,
                    "当【南蛮入侵】使用：其他角色依次响应【杀】", barbarianTargets,
                    requiredCardKind: CardKind.Slash);
            if (CanUseGlobalCard(source, CardKind.ArrowBarrage))
                Add(CardKind.ArrowBarrage, LegalActionKind.ArrowBarrage,
                    "当【万箭齐发】使用：其他角色依次响应【闪】", otherSeats,
                    requiredCardKind: CardKind.Dodge);
        }

        var allAliveSeats = Enumerable.Range(0, _playerCount)
            .Select(offset => _players[(source.Seat + offset) % _playerCount])
            .Where(player => player.IsAlive)
            .Select(player => player.Seat)
            .ToArray();
        if (CanUseGlobalCard(source, CardKind.PeachGarden))
            Add(CardKind.PeachGarden, LegalActionKind.PeachGarden,
                "当【桃园结义】使用：所有存活角色依次回复体力", allAliveSeats);
        var availableCards = _cardZones.Count(CardLocation.DrawPile) + _cardZones.Count(CardLocation.DiscardPile);
        if (CanUseGlobalCard(source, CardKind.FiveGrains))
            Add(CardKind.FiveGrains, LegalActionKind.FiveGrains,
                "当【五谷丰登】使用：所有存活角色依次选牌", allAliveSeats.Take(availableCards).ToArray());

        foreach (var target in _players.Where(player =>
                     player.IsAlive &&
                     player.Seat != source.Seat &&
                     !IsCardTargetProhibited(player, CardKind.Duel) &&
                     !IsDirectedCardTargetProhibited(source.Seat, player.Seat, CardKind.Duel)))
        {
            Add(CardKind.Duel, LegalActionKind.Duel,
                $"当【决斗】对 {target.Name} 使用", [target.Seat]);
        }

        foreach (var target in _players.Where(player =>
                     player.IsAlive &&
                     player.Seat != source.Seat &&
                     HasTargetCard(player) &&
                     !IsDirectedCardTargetProhibited(source.Seat, player.Seat, CardKind.Dismantlement)))
        {
            AddQiceTargetCardOptions(options, source, target, CardKind.Dismantlement,
                LegalActionKind.Dismantlement, "过河拆桥");
        }

        foreach (var target in _players.Where(player =>
                     player.IsAlive &&
                     player.Seat != source.Seat &&
                     (HasCardDistanceExemption(source, player, CardKind.Snatch) ||
                      GetCombatDistance(source.Seat, player.Seat) == 1) &&
                     HasTargetCard(player) &&
                     !IsCardTargetProhibited(player, CardKind.Snatch) &&
                     !IsDirectedCardTargetProhibited(source.Seat, player.Seat, CardKind.Snatch)))
        {
            AddQiceTargetCardOptions(options, source, target, CardKind.Snatch,
                LegalActionKind.Snatch, "顺手牵羊");
        }

        foreach (var target in _players.Where(player =>
                     player.IsAlive &&
                     GetHand(player).Count > 0 &&
                     !IsDirectedCardTargetProhibited(source.Seat, player.Seat, CardKind.FireAttack)))
        {
            Add(CardKind.FireAttack, LegalActionKind.FireAttack,
                $"当【火攻】对 {target.Name} 使用", [target.Seat]);
        }

        var chainTargets = _players.Where(player =>
                player.IsAlive && !IsDirectedCardTargetProhibited(source.Seat, player.Seat, CardKind.IronChain))
            .OrderBy(player => player.Seat).ToArray();
        foreach (var target in chainTargets)
        {
            Add(CardKind.IronChain, LegalActionKind.IronChain,
                $"当【铁索连环】对 {target.Name} 使用", [target.Seat]);
        }
        for (var first = 0; first < chainTargets.Length - 1; first++)
        for (var second = first + 1; second < chainTargets.Length; second++)
        {
            Add(CardKind.IronChain, LegalActionKind.IronChain,
                $"当【铁索连环】对 {chainTargets[first].Name}、{chainTargets[second].Name} 使用",
                [chainTargets[first].Seat, chainTargets[second].Seat]);
        }

        foreach (var weaponOwner in _players.Where(player =>
                     player.IsAlive &&
                     player.Seat != source.Seat &&
                     GetWeapon(player) is not null &&
                     !IsDirectedCardTargetProhibited(source.Seat, player.Seat, CardKind.BorrowedSword)))
        foreach (var slashTarget in _players.Where(player => IsLegalBorrowedSwordSlashTarget(weaponOwner, player)))
        {
            Add(CardKind.BorrowedSword, LegalActionKind.BorrowedSword,
                $"当【借刀杀人】使用：令 {weaponOwner.Name} 对 {slashTarget.Name} 使用【杀】，否则获得其武器",
                [weaponOwner.Seat, slashTarget.Seat]);
        }

        return Array.AsReadOnly(options.ToArray());
    }

    private void AddQiceTargetCardOptions(
        ICollection<QiceOption> options,
        CharacterState source,
        CharacterState target,
        CardKind cardKind,
        LegalActionKind actionKind,
        string cardName)
    {
        void AddTarget(int? targetCardId, string suffix)
        {
            var cardPart = targetCardId?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none";
            options.Add(new QiceOption(
                new ChoiceId($"qice.{cardKind}.targets-{target.Seat}.card-{cardPart}"),
                cardKind,
                actionKind,
                Array.AsReadOnly(new[] { target.Seat }),
                targetCardId,
                RequiredCardKind: null,
                $"当【{cardName}】对 {target.Name} 使用{suffix}"));
        }

        if (GetHand(target).Count > 0) AddTarget(null, "，选择其一张暗置手牌");
        foreach (var equipment in GetEquipment(target)) AddTarget(equipment.Id, $"，选择其装备【{equipment.DisplayName}】");
        foreach (var judgment in GetJudgment(target)) AddTarget(judgment.Id, $"，选择其判定区【{judgment.DisplayName}】");
    }

    private static PromptChoice CreateQicePromptChoice(QiceOption option) =>
        new(
            option.Id,
            option.Description,
            [],
            option.TargetSeats,
            new Dictionary<string, string>
            {
                ["action"] = "qice-use",
                ["card-kind"] = option.EffectiveCardKind.ToString(),
                ["action-kind"] = option.ActionKind.ToString(),
                ["target-card-id"] = option.TargetCardId?.ToString(
                    System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty
            });

    private PendingDecision CreateZhiyuDecision(DamageSkillResolution pending)
    {
        var owner = _players[pending.OwnerSeat];
        return new PendingDecision(
            DecisionKind.Zhiyu,
            owner.Seat,
            $"你受到伤害，是否发动【智愚】摸一张牌并展示所有手牌？",
            [],
            [],
            SourceSeat: pending.SourceSeat,
            IncomingCard: pending.EffectiveCardKind)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            Choices =
            [
                new PromptChoice(new ChoiceId("zhiyu-use"), "发动【智愚】：摸一张牌并展示所有手牌。", [], [],
                    new Dictionary<string, string> { ["action"] = "zhiyu-use" }),
                new PromptChoice(new ChoiceId("zhiyu-skip"), "不发动【智愚】。", [], [],
                    new Dictionary<string, string> { ["action"] = "zhiyu-skip" })
            ]
        };
    }

    private CommandResult SubmitZhiyuPromptAnswer(PromptChoice selected)
    {
        if (_pendingDamageSkill is not { Effect: DamageSkillEffectKind.RevealHandAndPunishSource } ||
            _pendingDecision is not { Kind: DecisionKind.Zhiyu })
        {
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的智愚结算。");
        }
        return Accept(() => HumanZhiyuCore(selected, _options.AdvanceAfterHumanCommands));
    }

    private EngineRunResult HumanZhiyuCore(PromptChoice selected, bool advanceToHumanBoundary)
    {
        ResolveZhiyuPromptChoice(selected);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private void ResolvePendingAiZhiyu()
    {
        var decision = _pendingDecision ?? throw new InvalidOperationException("AI 智愚提示不存在。");
        var choice = _pendingZhiyuDiscard is not null
            ? decision.Choices.OrderBy(candidate => candidate.Cards.Single()).First()
            : decision.Choices.Single(candidate => candidate.Parameters.GetValueOrDefault("action") == "zhiyu-use");
        ResolveZhiyuPromptChoice(choice);
    }

    private void ResolveZhiyuPromptChoice(PromptChoice selected)
    {
        var pending = _pendingDamageSkill ?? throw new InvalidOperationException("智愚伤害技能帧不存在。");
        if (pending.Effect != DamageSkillEffectKind.RevealHandAndPunishSource)
            throw new InvalidOperationException("当前伤害技能不是智愚。");

        if (_pendingZhiyuDiscard is { } discard)
        {
            var cardId = selected.Cards.SingleOrDefault(-1);
            var source = _players[pending.SourceSeat];
            var card = GetHand(source).SingleOrDefault(candidate => candidate.Id == cardId) ??
                       throw new InvalidOperationException("智愚来源弃置牌已不在其手牌中。");
            ClearPendingDecision();
            MoveCard(card, CardLocation.Hand(source.Seat), CardLocation.DiscardPile, CardMoveReasons.ZhiyuDiscard);
            CompleteZhiyu(pending, true, discard.DrawnCardId, discard.RevealedCards,
                discard.AllSameColor, card.Id);
            return;
        }

        var action = selected.Parameters.GetValueOrDefault("action");
        if (action == "zhiyu-skip")
        {
            ClearPendingDecision();
            CompleteZhiyu(pending, false, null, [], false, null);
            return;
        }
        if (action != "zhiyu-use") throw new InvalidOperationException("智愚选择无效。");

        ClearPendingDecision();
        var owner = _players[pending.OwnerSeat];
        var drawn = DrawOne(owner, CardMoveReasons.ZhiyuDraw);
        var revealed = GetHand(owner).Select(ToSnapshot).ToArray();
        var allSameColor = revealed.Length > 0 &&
                           GetHand(owner).Select(card => IsRedSuit(card.Suit)).Distinct().Count() == 1;
        QueueGameEvent(new CardsRevealedEvent(pending.DamageFrameId, revealed));

        var sourcePlayer = _players[pending.SourceSeat];
        var sourceHand = sourcePlayer.IsAlive ? GetHand(sourcePlayer).OrderBy(card => card.Id).ToArray() : [];
        if (allSameColor && sourceHand.Length > 0)
        {
            _pendingZhiyuDiscard = new ZhiyuDiscardResolution(
                pending,
                drawn?.Id,
                Array.AsReadOnly(revealed),
                allSameColor);
            _pendingDecision = new PendingDecision(
                DecisionKind.Zhiyu,
                sourcePlayer.Seat,
                $"{owner.Name} 的【智愚】展示的手牌颜色均相同，请弃置一张手牌。",
                sourceHand.Select(card => card.Id).ToArray(),
                [],
                SourceSeat: owner.Seat)
            {
                PromptId = CreatePromptId(),
                IsPrivate = true,
                Choices = sourceHand.Select(card => new PromptChoice(
                    new ChoiceId($"zhiyu-discard.card-{card.Id}"),
                    $"弃置【{card.DisplayName}】。",
                    [card.Id],
                    [],
                    new Dictionary<string, string> { ["action"] = "zhiyu-discard" })).ToArray()
            };
            _status = sourcePlayer.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
            return;
        }

        CompleteZhiyu(pending, true, drawn?.Id, revealed, allSameColor, null);
    }

    private void CompleteZhiyu(
        DamageSkillResolution pending,
        bool used,
        int? drawnCardId,
        IReadOnlyList<CardSnapshot> revealedCards,
        bool allSameColor,
        int? discardedCardId)
    {
        if (!ReferenceEquals(_pendingDamageSkill, pending))
            throw new InvalidOperationException("智愚结算已不再是当前伤害技能。");
        var window = _pendingDamageTrigger ?? throw new InvalidOperationException("智愚缺少伤害触发窗口。");
        var owner = _players[pending.OwnerSeat];
        var source = _players[pending.SourceSeat];

        _pendingZhiyuDiscard = null;
        ClearPendingDecision();
        SetDamageSkillFrameStep(pending.FrameId, ResolutionFrameStep.ResolvingEffect);
        QueueGameEvent(new ZhiyuResolvedEvent(
            pending.DamageFrameId,
            owner.Seat,
            source.Seat,
            used,
            drawnCardId,
            revealedCards,
            allSameColor,
            discardedCardId));
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
                ? $"{owner.Name} 选择不发动【智愚】。"
                : allSameColor && discardedCardId is not null
                    ? $"{owner.Name} 发动【智愚】摸一张牌并展示同色手牌，{source.Name} 弃置一张手牌。"
                    : $"{owner.Name} 发动【智愚】摸一张牌并展示所有手牌。",
            owner.Seat,
            source.Seat);
        PopResolutionFrame(pending.FrameId, ResolutionFrameKind.DamageSkill);
        _pendingDamageSkill = null;
        SetDamageTriggerWindowStep(window.FrameId, ResolutionFrameStep.ResolvingEffect);
        AdvanceDamageTriggerCandidate(window);
    }

    private sealed record QiceOption(
        ChoiceId Id,
        CardKind EffectiveCardKind,
        LegalActionKind ActionKind,
        IReadOnlyList<int> TargetSeats,
        int? TargetCardId,
        CardKind? RequiredCardKind,
        string Description);

    private sealed record QiceResolution(
        int SourceSeat,
        IReadOnlyList<int> CardIds,
        IReadOnlyList<QiceOption> Options);

    private sealed record ZhiyuDiscardResolution(
        DamageSkillResolution DamageSkill,
        int? DrawnCardId,
        IReadOnlyList<CardSnapshot> RevealedCards,
        bool AllSameColor);
}
