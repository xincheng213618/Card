namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string ChengxiangSkillId = "classic:chengxiang";
    private const string RenxinSkillId = "classic:renxin";

    private RenxinResolution? _pendingRenxin;

    private bool UsesFormalCaoChong =>
        HasClassicGeneralPackage(new Version(1, 94, 0));

    private bool HasRenxin(PlayerRuntime owner) =>
        UsesFormalCaoChong && HasRuntimeSkill(owner, RenxinSkillId);

    private PendingDecision CreateChengxiangDecision(DamageSkillResolution pending)
    {
        var owner = _players[pending.OwnerSeat];
        return new PendingDecision(
            DecisionKind.Chengxiang,
            owner.Seat,
            $"{owner.Name} 受到伤害后可以发动【称象】。",
            [],
            [],
            pending.SourceSeat,
            pending.EffectiveCardKind)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = owner.Seat,
            Choices =
            [
                new PromptChoice(
                    new ChoiceId($"chengxiang.use.damage-{pending.DamageFrameId}"),
                    "发动【称象】，亮出牌堆顶四张牌。",
                    [],
                    [],
                    new Dictionary<string, string> { ["action"] = "chengxiang-use" }),
                new PromptChoice(
                    new ChoiceId($"chengxiang.skip.damage-{pending.DamageFrameId}"),
                    "不发动【称象】。",
                    [],
                    [],
                    new Dictionary<string, string> { ["action"] = "chengxiang-skip" })
            ]
        };
    }

    private CommandResult SubmitChengxiangPromptAnswer(PromptChoice selected)
    {
        if (_pendingDamageSkill is not
            {
                Effect: DamageSkillEffectKind.SelectRevealedCardsByRank
            } pending ||
            _pendingDecision is not { Kind: DecisionKind.Chengxiang })
        {
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的称象选择。");
        }

        return Accept(() => HumanChengxiangCore(
            pending,
            selected,
            _options.AdvanceAfterHumanCommands));
    }

    private EngineRunResult HumanChengxiangCore(
        DamageSkillResolution pending,
        PromptChoice selected,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.Chengxiang);
        ResolveChengxiangChoice(pending, selected);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private void ResolvePendingAiChengxiang()
    {
        var pending = _pendingDamageSkill ??
            throw new InvalidOperationException("AI 称象结算不存在。");
        var decision = _pendingDecision ??
            throw new InvalidOperationException("AI 称象结算缺少选择。");
        if (pending.Effect != DamageSkillEffectKind.SelectRevealedCardsByRank ||
            decision.Kind != DecisionKind.Chengxiang ||
            decision.PlayerSeat != pending.OwnerSeat ||
            _players[pending.OwnerSeat].IsHuman)
        {
            throw new InvalidOperationException("AI 称象选择与当前结算不一致。");
        }

        PromptChoice selected;
        if (pending.ChengxiangRevealedCardIds.Count == 0)
        {
            selected = decision.Choices.Single(choice =>
                choice.Parameters.GetValueOrDefault("action") == "chengxiang-use");
        }
        else
        {
            selected = decision.Choices
                .Where(choice => choice.Parameters.GetValueOrDefault("action") == "chengxiang-obtain")
                .OrderByDescending(choice => choice.Cards.Count)
                .ThenByDescending(choice => choice.Cards.Sum(cardId =>
                    _cardZones.CardsAt(CardLocation.Processing)
                        .Single(card => card.Id == cardId).Rank))
                .ThenBy(choice => string.Join(',', choice.Cards))
                .First();
        }

        ResolveChengxiangChoice(pending, selected);
        PublishState();
    }

    private void ResolveChengxiangChoice(DamageSkillResolution pending, PromptChoice selected)
    {
        if (!ReferenceEquals(_pendingDamageSkill, pending) ||
            _pendingDecision is not { Kind: DecisionKind.Chengxiang } decision ||
            !decision.Choices.Any(choice => choice.Id == selected.Id))
        {
            throw new InvalidOperationException("称象选择已失效。");
        }

        var action = selected.Parameters.GetValueOrDefault("action");
        if (pending.ChengxiangRevealedCardIds.Count == 0)
        {
            if (action == "chengxiang-skip" && selected.Cards.Count == 0)
            {
                ClearPendingDecision();
                CompleteChengxiang(pending, used: false, []);
                return;
            }

            if (action != "chengxiang-use" || selected.Cards.Count != 0)
            {
                throw new InvalidOperationException("称象发动选择不合法。");
            }

            ClearPendingDecision();
            var revealed = new List<Card>(4);
            for (var index = 0; index < 4; index++)
            {
                if (DrawOneToProcessing(CardMoveReasons.ChengxiangReveal) is not { } card)
                {
                    break;
                }
                revealed.Add(card);
            }

            pending.ChengxiangRevealedCardIds = Array.AsReadOnly(revealed.Select(card => card.Id).ToArray());
            SetDamageSkillFrameEffectCards(pending.FrameId, pending.ChengxiangRevealedCardIds);
            QueueGameEvent(new CardsRevealedEvent(
                pending.DamageFrameId,
                revealed.Select(ToSnapshot).ToArray()));

            if (revealed.Count == 0)
            {
                CompleteChengxiang(pending, used: true, []);
                return;
            }

            _pendingDecision = CreateChengxiangSubsetDecision(pending, revealed);
            _status = _players[pending.OwnerSeat].IsHuman
                ? EngineStatus.AwaitingHumanResponse
                : EngineStatus.Running;
            return;
        }

        if (action != "chengxiang-obtain" ||
            selected.Cards.Any(cardId => !pending.ChengxiangRevealedCardIds.Contains(cardId)) ||
            selected.Cards.Distinct().Count() != selected.Cards.Count)
        {
            throw new InvalidOperationException("称象取得牌的组合不合法。");
        }

        var rankSum = selected.Cards.Sum(cardId =>
            _cardZones.CardsAt(CardLocation.Processing).Single(card => card.Id == cardId).Rank);
        if (rankSum > 13)
        {
            throw new InvalidOperationException("称象取得牌的点数之和不能超过13。");
        }

        ClearPendingDecision();
        CompleteChengxiang(pending, used: true, selected.Cards);
    }

    private PendingDecision CreateChengxiangSubsetDecision(
        DamageSkillResolution pending,
        IReadOnlyList<Card> revealed)
    {
        var owner = _players[pending.OwnerSeat];
        var choices = new List<PromptChoice>();
        var subsetCount = 1 << revealed.Count;
        for (var mask = 0; mask < subsetCount; mask++)
        {
            var cards = revealed
                .Where((_, index) => (mask & (1 << index)) != 0)
                .ToArray();
            var sum = cards.Sum(card => card.Rank);
            if (sum > 13)
            {
                continue;
            }

            choices.Add(new PromptChoice(
                new ChoiceId($"chengxiang.obtain.damage-{pending.DamageFrameId}.mask-{mask}"),
                cards.Length == 0
                    ? "不获得牌，将四张牌全部置入弃牌堆。"
                    : $"获得 {string.Join("、", cards.Select(card => $"【{card.DisplayName}】{card.RankText}"))}（点数和 {sum}）。",
                cards.Select(card => card.Id).ToArray(),
                [],
                new Dictionary<string, string>
                {
                    ["action"] = "chengxiang-obtain",
                    ["rank-sum"] = sum.ToString(System.Globalization.CultureInfo.InvariantCulture)
                }));
        }

        return new PendingDecision(
            DecisionKind.Chengxiang,
            owner.Seat,
            "选择任意张点数之和不超过13的牌获得，其余牌置入弃牌堆。",
            revealed.Select(card => card.Id).ToArray(),
            [],
            pending.SourceSeat,
            pending.EffectiveCardKind)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            TargetSeat = owner.Seat,
            Choices = choices
        };
    }

    private void CompleteChengxiang(
        DamageSkillResolution pending,
        bool used,
        IReadOnlyList<int> obtainedCardIds)
    {
        if (!ReferenceEquals(_pendingDamageSkill, pending) ||
            _pendingDamageTrigger is not { } window ||
            window.FrameId != pending.TriggerFrameId)
        {
            throw new InvalidOperationException("称象结算不属于当前伤害触发窗口。");
        }

        var owner = _players[pending.OwnerSeat];
        var revealed = pending.ChengxiangRevealedCardIds
            .Select(cardId => _cardZones.CardsAt(CardLocation.Processing)
                .Single(card => card.Id == cardId))
            .ToArray();
        var obtainedSet = obtainedCardIds.ToHashSet();
        var obtained = revealed.Where(card => obtainedSet.Contains(card.Id)).ToArray();
        var discarded = revealed.Where(card => !obtainedSet.Contains(card.Id)).ToArray();
        if (obtained.Length > 0)
        {
            MoveCards(
                obtained,
                CardLocation.Processing,
                CardLocation.Hand(owner.Seat),
                CardMoveReasons.ChengxiangObtain);
        }
        if (discarded.Length > 0)
        {
            MoveCards(
                discarded,
                CardLocation.Processing,
                CardLocation.DiscardPile,
                CardMoveReasons.ChengxiangDiscard);
        }

        QueueGameEvent(new ChengxiangResolvedEvent(
            pending.DamageFrameId,
            owner.Seat,
            used,
            revealed.Select(card => card.Id).ToArray(),
            obtained.Select(card => card.Id).ToArray(),
            discarded.Select(card => card.Id).ToArray(),
            obtained.Sum(card => card.Rank)));
        QueueGameEvent(new DamageSkillResolvedEvent(
            pending.DamageFrameId,
            pending.OwnerSeat,
            pending.SourceSeat,
            pending.Card?.Id,
            pending.EffectiveCardKind,
            pending.Skill,
            used,
            pending.CandidateId,
            pending.Priority));
        AddLog(
            "SkillTriggered",
            !used
                ? $"{owner.Name} 选择不发动【称象】。"
                : $"{owner.Name} 发动【称象】，获得 {obtained.Length} 张牌。",
            owner.Seat,
            pending.SourceSeat);

        SetDamageSkillFrameStep(pending.FrameId, ResolutionFrameStep.ResolvingEffect);
        PopResolutionFrame(pending.FrameId, ResolutionFrameKind.DamageSkill);
        _pendingDamageSkill = null;
        SetDamageTriggerWindowStep(window.FrameId, ResolutionFrameStep.ResolvingEffect);
        AdvanceDamageTriggerCandidate(window);
    }

    private IReadOnlyList<CardSnapshot> GetChengxiangPublicCards() =>
        _pendingDamageSkill is
        {
            Effect: DamageSkillEffectKind.SelectRevealedCardsByRank,
            ChengxiangRevealedCardIds.Count: > 0
        } pending
            ? pending.ChengxiangRevealedCardIds
                .Select(cardId => _cardZones.CardsAt(CardLocation.Processing)
                    .Single(card => card.Id == cardId))
                .Select(ToSnapshot)
                .ToArray()
            : [];

    private bool HasPendingChengxiangReveal =>
        _pendingDamageSkill is
        {
            Effect: DamageSkillEffectKind.SelectRevealedCardsByRank,
            ChengxiangRevealedCardIds.Count: > 0
        };

    private bool IsChengxiangProcessingConsistent(
        AttackResolution attack,
        IReadOnlyList<Card> processing)
    {
        if (_pendingDamageSkill is not
            {
                Effect: DamageSkillEffectKind.SelectRevealedCardsByRank,
                ChengxiangRevealedCardIds.Count: > 0
            } pending)
        {
            return false;
        }

        var revealedIds = pending.ChengxiangRevealedCardIds.ToHashSet();
        if (revealedIds.Any(cardId => processing.All(card => card.Id != cardId)))
        {
            return false;
        }

        var allowedParentIds = attack.PhysicalCards.Select(card => card.Id).ToHashSet();
        if (_pendingLeiji is { } leiji)
        {
            allowedParentIds.UnionWith(leiji.OriginalAttack.PhysicalCards.Select(card => card.Id));
        }
        if (_pendingJudgment?.Attack is { } judgmentAttack)
        {
            allowedParentIds.UnionWith(judgmentAttack.PhysicalCards.Select(card => card.Id));
        }
        if (_pendingBorrowedSword is { } borrowedSword)
        {
            allowedParentIds.Add(borrowedSword.Card.Id);
        }
        if (_resolutionStack.OfType<ProgramCardTriggerWindowFrame>().LastOrDefault() is { } programFrame)
        {
            allowedParentIds.UnionWith(programFrame.Action.PhysicalCards.Select(cost => cost.CardId));
        }

        return processing
            .Where(card => !revealedIds.Contains(card.Id))
            .All(card => allowedParentIds.Contains(card.Id));
    }

    private void SetDamageSkillFrameEffectCards(long frameId, IReadOnlyList<int> cardIds)
    {
        var index = _resolutionStack.FindLastIndex(frame => frame.Id == frameId);
        if (index < 0 || _resolutionStack[index] is not DamageSkillFrame frame)
        {
            throw new InvalidOperationException($"Resolution frame {frameId} is not a DamageSkill frame.");
        }
        _resolutionStack[index] = frame with { EffectCardIds = Array.AsReadOnly(cardIds.ToArray()) };
    }

    private bool TryBeginRenxinChoiceForAttack(AttackResolution attack, int amount)
    {
        if (!UsesFormalCaoChong || attack.RenxinResolved || amount <= 0)
        {
            return false;
        }

        return TryBeginRenxinChoice(
            attack.ResolutionId,
            attack.SourceSeat,
            attack.TargetSeat,
            amount,
            attack,
            ganglie: null);
    }

    private bool TryBeginRenxinChoiceForGanglie(DamageSkillResolution ganglie)
    {
        if (!UsesFormalCaoChong || ganglie.GanglieRenxinResolved)
        {
            return false;
        }

        return TryBeginRenxinChoice(
            ganglie.FrameId,
            ganglie.OwnerSeat,
            ganglie.SourceSeat,
            amount: 1,
            attack: null,
            ganglie);
    }

    private bool TryBeginRenxinChoice(
        long resolutionId,
        int sourceSeat,
        int targetSeat,
        int amount,
        AttackResolution? attack,
        DamageSkillResolution? ganglie)
    {
        var target = _players[targetSeat];
        if (!target.IsAlive || target.Hp != 1)
        {
            return false;
        }

        var candidates = _players
            .Where(player =>
                player.IsAlive &&
                player.Seat != targetSeat &&
                HasRenxin(player) &&
                GetRenxinCards(player).Count > 0)
            .OrderBy(player => (player.Seat - targetSeat + _playerCount) % _playerCount)
            .Select(player => player.Seat)
            .ToArray();
        if (candidates.Length == 0)
        {
            return false;
        }

        _pendingRenxin = new RenxinResolution(
            resolutionId,
            sourceSeat,
            targetSeat,
            amount,
            candidates,
            attack,
            ganglie);
        PublishRenxinDecision(_pendingRenxin);
        return true;
    }

    private IReadOnlyList<Card> GetRenxinCards(PlayerRuntime owner) =>
        GetHand(owner)
            .Concat(GetEquipment(owner))
            .Where(card => EquipmentCatalog.IsEquipment(card.Kind))
            .OrderBy(card => card.Id)
            .ToArray();

    private void PublishRenxinDecision(RenxinResolution pending)
    {
        while (pending.CandidateIndex < pending.CandidateSeats.Count)
        {
            var owner = _players[pending.CandidateSeats[pending.CandidateIndex]];
            var cards = owner.IsAlive ? GetRenxinCards(owner) : [];
            if (cards.Count == 0)
            {
                pending.CandidateIndex++;
                continue;
            }

            var target = _players[pending.TargetSeat];
            var choices = cards.Select(card => new PromptChoice(
                    new ChoiceId($"renxin.use.resolution-{pending.ResolutionId}.owner-{owner.Seat}.card-{card.Id}"),
                    $"弃置【{card.DisplayName}】并翻面，防止 {target.Name} 受到的 {pending.Amount} 点伤害。",
                    [card.Id],
                    [target.Seat],
                    new Dictionary<string, string> { ["action"] = "renxin-use" }))
                .Append(new PromptChoice(
                    new ChoiceId($"renxin.skip.resolution-{pending.ResolutionId}.owner-{owner.Seat}"),
                    "不发动【仁心】。",
                    [],
                    [],
                    new Dictionary<string, string> { ["action"] = "renxin-skip" }))
                .ToArray();
            _pendingDecision = new PendingDecision(
                DecisionKind.Renxin,
                owner.Seat,
                $"{target.Name} 当前体力值为1且将受到伤害，你可以发动【仁心】。",
                cards.Select(card => card.Id).ToArray(),
                [target.Seat],
                pending.SourceSeat)
            {
                PromptId = CreatePromptId(),
                IsPrivate = true,
                TargetSeat = target.Seat,
                Choices = choices
            };
            _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
            return;
        }

        ResumeDamageAfterRenxinSkipped(pending);
    }

    private CommandResult SubmitRenxinPromptAnswer(PromptChoice selected)
    {
        if (_pendingRenxin is not { } pending ||
            _pendingDecision is not { Kind: DecisionKind.Renxin })
        {
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的仁心选择。");
        }
        return Accept(() => HumanRenxinCore(
            pending,
            selected,
            _options.AdvanceAfterHumanCommands));
    }

    private EngineRunResult HumanRenxinCore(
        RenxinResolution pending,
        PromptChoice selected,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.Renxin);
        ResolveRenxinChoice(pending, selected);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private bool IsAiRenxinPending() =>
        _pendingRenxin is not null &&
        _pendingDecision is { Kind: DecisionKind.Renxin, PlayerSeat: var seat } &&
        !_players[seat].IsHuman;

    private void ResolvePendingAiRenxin()
    {
        var pending = _pendingRenxin ??
            throw new InvalidOperationException("AI 仁心结算不存在。");
        var decision = _pendingDecision ??
            throw new InvalidOperationException("AI 仁心结算缺少选择。");
        var selected = decision.Choices.First(choice =>
            choice.Parameters.GetValueOrDefault("action") == "renxin-use");
        ResolveRenxinChoice(pending, selected);
        PublishState();
    }

    private void ResolveRenxinChoice(RenxinResolution pending, PromptChoice selected)
    {
        if (!ReferenceEquals(_pendingRenxin, pending) ||
            _pendingDecision is not { Kind: DecisionKind.Renxin } decision ||
            !decision.Choices.Any(choice => choice.Id == selected.Id))
        {
            throw new InvalidOperationException("仁心选择已失效。");
        }

        var owner = _players[pending.CandidateSeats[pending.CandidateIndex]];
        var target = _players[pending.TargetSeat];
        var action = selected.Parameters.GetValueOrDefault("action");
        if (action == "renxin-skip" && selected.Cards.Count == 0)
        {
            QueueGameEvent(new RenxinResolvedEvent(
                pending.ResolutionId,
                owner.Seat,
                pending.SourceSeat,
                target.Seat,
                Used: false,
                PreventedAmount: 0,
                DiscardedCardId: null,
                owner.IsFaceDown));
            ClearPendingDecision();
            pending.CandidateIndex++;
            PublishRenxinDecision(pending);
            return;
        }

        if (action != "renxin-use" || selected.Cards.Count != 1 ||
            !decision.ValidCardIds.Contains(selected.Cards[0]))
        {
            throw new InvalidOperationException("仁心弃置选择不合法。");
        }

        var card = GetRenxinCards(owner).SingleOrDefault(candidate => candidate.Id == selected.Cards[0]) ??
            throw new InvalidOperationException("仁心所选装备牌已不在技能拥有者区域内。");
        var from = GetHand(owner).Any(candidate => candidate.Id == card.Id)
            ? CardLocation.Hand(owner.Seat)
            : CardLocation.Equipment(owner.Seat);
        ClearPendingDecision();
        MoveCard(card, from, CardLocation.DiscardPile, CardMoveReasons.RenxinDiscard);
        owner.IsFaceDown = !owner.IsFaceDown;
        QueueGameEvent(new RenxinResolvedEvent(
            pending.ResolutionId,
            owner.Seat,
            pending.SourceSeat,
            target.Seat,
            Used: true,
            PreventedAmount: pending.Amount,
            DiscardedCardId: card.Id,
            owner.IsFaceDown));
        AddLog(
            "SkillTriggered",
            $"{owner.Name} 发动【仁心】，弃置【{card.DisplayName}】并翻面，防止了 {target.Name} 受到的 {pending.Amount} 点伤害。",
            owner.Seat,
            target.Seat);

        _pendingRenxin = null;
        if (pending.Attack is { } attack)
        {
            attack.MarkRenxinResolved();
            CompleteAttack(attack);
        }
        else if (pending.Ganglie is { } ganglie)
        {
            ganglie.GanglieRenxinResolved = true;
            CompleteGangliePunishment(ganglie);
        }
        else
        {
            throw new InvalidOperationException("仁心结算没有可恢复的伤害上下文。");
        }
    }

    private void ResumeDamageAfterRenxinSkipped(RenxinResolution pending)
    {
        if (!ReferenceEquals(_pendingRenxin, pending))
        {
            throw new InvalidOperationException("待恢复的仁心结算已失效。");
        }

        _pendingRenxin = null;
        ClearPendingDecision();
        if (pending.Attack is { } attack)
        {
            attack.MarkRenxinResolved();
            if (!ApplyAttackDamage(attack))
            {
                CompleteAttack(attack);
            }
        }
        else if (pending.Ganglie is { } ganglie)
        {
            ganglie.GanglieRenxinResolved = true;
            ApplyGangliePunishmentDamage(ganglie);
        }
        else
        {
            throw new InvalidOperationException("仁心结算没有可恢复的伤害上下文。");
        }
    }

    private sealed class RenxinResolution(
        long resolutionId,
        int sourceSeat,
        int targetSeat,
        int amount,
        IReadOnlyList<int> candidateSeats,
        AttackResolution? attack,
        DamageSkillResolution? ganglie)
    {
        public long ResolutionId { get; } = resolutionId;
        public int SourceSeat { get; } = sourceSeat;
        public int TargetSeat { get; } = targetSeat;
        public int Amount { get; } = amount;
        public IReadOnlyList<int> CandidateSeats { get; } = candidateSeats;
        public AttackResolution? Attack { get; } = attack;
        public DamageSkillResolution? Ganglie { get; } = ganglie;
        public int CandidateIndex { get; set; }
    }
}
