namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string HengyeSkillId = "mou:hengye";
    private const string HengyeGrowthUsageId = "growth";
    private const string YingboSkillId = "mou:yingbo";
    private const string RoundDamageCardHistorySkillId = "system:round-damage-card-history";
    private const string RoundDamageCardUsagePrefix = "card-name";

    private readonly HashSet<int> _roundTurnSeats = [];
    private readonly HashSet<long> _yingboUnrespondableFrames = [];
    private readonly HashSet<long> _yingboRepeatedFrames = [];
    private int _roundNumber;
    private YingboGiftResolution? _pendingYingboGift;

    private bool UsesFormalMouLuMeng =>
        HasClassicGeneralPackage(new Version(1, 80, 0));

    private void BeginRoundForTurn(PlayerRuntime current)
    {
        if (!UsesFormalMouLuMeng) return;

        if (_roundNumber == 0 || _roundTurnSeats.Contains(current.Seat))
        {
            _roundNumber++;
            _roundTurnSeats.Clear();
            _skillRuntimeState.ResetRound();
            QueueGameEvent(new RoundStartedEvent(_roundNumber, current.Seat));
            AddLog("RoundStarted", $"第 {_roundNumber} 轮开始。", current.Seat);
        }

        _roundTurnSeats.Add(current.Seat);
    }

    private static bool IsDamageCardKind(CardKind kind) => kind is
        CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or
        CardKind.Duel or CardKind.BarbarianAssault or CardKind.ArrowBarrage or
        CardKind.FireAttack;

    private static string GetRoundDamageCardUsageId(CardKind kind) =>
        $"{RoundDamageCardUsagePrefix}.{kind}";

    private void RecordYingboCardUse(long resolutionId, int sourceSeat, CardKind cardKind)
    {
        if (!UsesFormalMouLuMeng || !IsDamageCardKind(cardKind)) return;

        var usageId = GetRoundDamageCardUsageId(cardKind);
        var wasUsedEarlier = _players.Any(player =>
            _skillRuntimeState.GetUsage(
                player.Seat,
                RoundDamageCardHistorySkillId,
                usageId,
                SkillUsageScope.Round) > 0);
        var source = _players[sourceSeat];
        if (source.IsAlive && HasRuntimeSkill(source, YingboSkillId))
        {
            if (wasUsedEarlier)
                _yingboRepeatedFrames.Add(resolutionId);
            else
                _yingboUnrespondableFrames.Add(resolutionId);

            QueueGameEvent(new YingboCardModeEvent(
                resolutionId,
                sourceSeat,
                cardKind,
                wasUsedEarlier,
                CannotBeRespondedTo: !wasUsedEarlier,
                ConvertsDamageToFire: wasUsedEarlier,
                DamageBonus: wasUsedEarlier ? 1 : 0));
            AddLog(
                "SkillTriggered",
                wasUsedEarlier
                    ? $"{source.Name} 的【英博】令本轮重复使用的【{CardCatalog.Get(cardKind).DisplayName}】伤害改为火焰且伤害 +1。"
                    : $"{source.Name} 的【英博】令本轮首次使用的【{CardCatalog.Get(cardKind).DisplayName}】不能被响应。",
                sourceSeat);
        }

        if (_skillRuntimeState.GetUsage(
                sourceSeat,
                RoundDamageCardHistorySkillId,
                usageId,
                SkillUsageScope.Round) == 0)
        {
            _skillRuntimeState.TryConsumeUsage(
                sourceSeat,
                RoundDamageCardHistorySkillId,
                usageId,
                SkillUsageScope.Round,
                limit: 1);
        }
    }

    private bool IsYingboUnrespondable(long resolutionId) =>
        UsesFormalMouLuMeng && _yingboUnrespondableFrames.Contains(resolutionId);

    private bool IsYingboRepeated(long resolutionId) =>
        UsesFormalMouLuMeng && _yingboRepeatedFrames.Contains(resolutionId);

    private void ClearYingboCardUse(long resolutionId)
    {
        _yingboUnrespondableFrames.Remove(resolutionId);
        _yingboRepeatedFrames.Remove(resolutionId);
    }

    private int GetHengyeGrowth(PlayerRuntime player) =>
        UsesFormalMouLuMeng && HasRuntimeSkill(player, HengyeSkillId)
            ? _skillRuntimeState.GetUsage(
                player.Seat,
                HengyeSkillId,
                HengyeGrowthUsageId,
                SkillUsageScope.Game)
            : 0;

    private void ApplyHengyeGrowth(
        long damageFrameId,
        PlayerRuntime source,
        PlayerRuntime target,
        int amount)
    {
        if (!UsesFormalMouLuMeng || amount <= 0 || !source.IsAlive ||
            !HasRuntimeSkill(source, HengyeSkillId))
            return;

        var previous = GetHengyeGrowth(source);
        if (!_skillRuntimeState.TryConsumeUsage(
                source.Seat,
                HengyeSkillId,
                HengyeGrowthUsageId,
                SkillUsageScope.Game,
                limit: 3))
            return;

        var current = previous + 1;
        QueueGameEvent(new SkillUsageConsumedEvent(
            source.Seat,
            HengyeSkillId,
            HengyeGrowthUsageId,
            SkillUsageScope.Game,
            current));
        QueueGameEvent(new HengyeGrowthChangedEvent(
            damageFrameId,
            source.Seat,
            previous,
            current));
        AddLog(
            "SkillTriggered",
            $"{source.Name} 的【横野】成长至 {current}/3（摸牌数、出杀次数、攻击范围、手牌上限同步增加）。",
            source.Seat,
            target.Seat);
    }

    private void ResolveHengyeTurnStart(PlayerRuntime player)
    {
        if (GetHengyeGrowth(player) < 3 || player.Hp >= player.MaxHp) return;

        var recoveryFrameId = BeginRecovery(0, player.Seat, player.Seat, 1);
        try
        {
            player.Hp++;
            QueueGameEvent(new RecoveryAppliedEvent(player.Seat, player.Seat, 1, player.Hp));
            AddLog("SkillTriggered", $"{player.Name} 的【横野】已成长至 3，本回合开始时回复 1 点体力。", player.Seat);
        }
        finally
        {
            PopResolutionFrame(recoveryFrameId, ResolutionFrameKind.Recovery);
        }
    }

    private void ResetHengyeAfterKill(PlayerRuntime? killer)
    {
        if (!UsesFormalMouLuMeng || killer is not { IsAlive: true } ||
            !HasRuntimeSkill(killer, HengyeSkillId))
            return;

        var previous = GetHengyeGrowth(killer);
        _skillRuntimeState.ResetSkill(killer.Seat, HengyeSkillId);
        QueueGameEvent(new SkillResetEvent(killer.Seat, HengyeSkillId, previous));
        AddLog("SkillTriggered", $"{killer.Name} 杀死一名角色，重置【横野】至游戏开始时状态。", killer.Seat);
    }

    private bool TryBeginYingboGift(
        long resolutionId,
        int sourceSeat,
        Card card,
        CardKind cardKind,
        IReadOnlyList<Card> physicalCards,
        YingboGiftContinuation continuation,
        AttackResolution? attack = null,
        GroupCardResolution? group = null)
    {
        if (_winner != Winner.None ||
            !IsYingboUnrespondable(resolutionId) ||
            _pendingYingboGift is not null ||
            physicalCards is not [var physicalCard] ||
            physicalCard.Id != card.Id ||
            _cardZones.GetLocation(card.Id) != CardLocation.Processing)
            return false;

        var source = _players[sourceSeat];
        var targets = _players
            .Where(player => player.IsAlive && player.Seat != sourceSeat)
            .OrderBy(player => player.Seat)
            .ToArray();
        if (!source.IsAlive || !HasRuntimeSkill(source, YingboSkillId) || targets.Length == 0)
            return false;

        _pendingYingboGift = new YingboGiftResolution(
            resolutionId,
            sourceSeat,
            card,
            cardKind,
            continuation,
            attack,
            group);
        var choices = targets.Select(target => new PromptChoice(
                new ChoiceId($"yingbo.gift.target-{target.Seat}.resolution-{resolutionId}"),
                $"将【{card.DisplayName}】交给 {target.Name}。",
                [card.Id],
                [target.Seat],
                new Dictionary<string, string> { ["action"] = "yingbo-gift" }))
            .Append(new PromptChoice(
                new ChoiceId($"yingbo.gift.skip.resolution-{resolutionId}"),
                "不交出此牌。",
                [],
                [],
                new Dictionary<string, string> { ["action"] = "yingbo-skip" }))
            .ToArray();
        _pendingDecision = new PendingDecision(
            DecisionKind.Yingbo,
            sourceSeat,
            $"【{card.DisplayName}】结算完毕，是否发动【英博】将此牌交给一名其他角色？",
            [card.Id],
            targets.Select(target => target.Seat).ToArray(),
            SourceSeat: sourceSeat,
            IncomingCard: cardKind)
        {
            PromptId = CreatePromptId(),
            IsPrivate = true,
            Choices = choices
        };
        _status = source.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        return true;
    }

    private CommandResult SubmitYingboPromptAnswer(PromptChoice selected)
    {
        if (_pendingYingboGift is null || _pendingDecision is not { Kind: DecisionKind.Yingbo })
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的英博交牌选择。");

        return Accept(() => HumanYingboCore(
            selected,
            advanceToHumanBoundary: _options.AdvanceAfterHumanCommands));
    }

    private EngineRunResult HumanYingboCore(PromptChoice selected, bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.Yingbo);
        ResolveYingboGiftChoice(selected);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private void ResolveYingboGiftChoice(PromptChoice selected)
    {
        var pending = _pendingYingboGift ??
            throw new InvalidOperationException("There is no Yingbo gift to resolve.");
        if (_pendingDecision is not { Kind: DecisionKind.Yingbo, PlayerSeat: var ownerSeat } ||
            ownerSeat != pending.SourceSeat)
            throw new InvalidOperationException("The Yingbo gift prompt lost its owner.");

        var action = selected.Parameters.GetValueOrDefault("action");
        int? targetSeat = null;
        if (action == "yingbo-gift" && selected.Cards is [var cardId] &&
            cardId == pending.Card.Id && selected.Targets is [var selectedTarget] &&
            IsValidPlayerSeat(selectedTarget) && selectedTarget != pending.SourceSeat &&
            _players[selectedTarget].IsAlive)
        {
            targetSeat = selectedTarget;
            MoveCard(
                pending.Card,
                CardLocation.Processing,
                CardLocation.Hand(selectedTarget),
                new CardMoveReason("skill.yingbo.gift"));
        }
        else if (action == "yingbo-skip" && selected.Cards.Count == 0 && selected.Targets.Count == 0)
        {
            MoveCard(
                pending.Card,
                CardLocation.Processing,
                CardLocation.DiscardPile,
                CardMoveReasons.UseFinished);
        }
        else
        {
            throw new InvalidOperationException("The Yingbo gift choice is malformed or no longer legal.");
        }

        QueueGameEvent(new YingboGiftResolvedEvent(
            pending.ResolutionId,
            pending.SourceSeat,
            pending.Card.Id,
            pending.CardKind,
            targetSeat));
        AddLog(
            targetSeat is null ? "SkillSkipped" : "SkillTriggered",
            targetSeat is { } recipient
                ? $"{_players[pending.SourceSeat].Name} 发动【英博】，将【{pending.Card.DisplayName}】交给 {_players[recipient].Name}。"
                : $"{_players[pending.SourceSeat].Name} 未发动【英博】交牌。",
            pending.SourceSeat,
            targetSeat);

        _pendingYingboGift = null;
        ClearPendingDecision();
        if (pending.Continuation == YingboGiftContinuation.Attack)
        {
            var attack = pending.Attack ??
                throw new InvalidOperationException("The Yingbo attack gift lost its card continuation.");
            if (FinishAttack(attack, allowYingboGift: false))
                throw new InvalidOperationException("A completed Yingbo gift cannot open another Yingbo gift.");
            CompleteAttackAfterCardResolution(attack);
        }
        else
        {
            FinishGroupAttack(
                pending.Group ?? throw new InvalidOperationException("The Yingbo group gift lost its card continuation."),
                allowYingboGift: false);
        }
    }

    private bool IsAiYingboPending() =>
        _pendingYingboGift is { SourceSeat: var sourceSeat } &&
        _pendingDecision is { Kind: DecisionKind.Yingbo, PlayerSeat: var decisionSeat } &&
        sourceSeat == decisionSeat && !_players[sourceSeat].IsHuman;

    private void ResolvePendingAiYingbo()
    {
        if (!IsAiYingboPending())
            throw new InvalidOperationException("There is no AI Yingbo gift to resolve.");

        // Giving a card is optional. The baseline AI avoids leaking role information
        // and deterministically keeps the card in the discard pile.
        var selected = _pendingDecision!.Choices.Single(choice =>
            choice.Parameters.GetValueOrDefault("action") == "yingbo-skip");
        ResolveYingboGiftChoice(selected);
        PublishState();
    }

    private void AssertYingboInvariant()
    {
        if (_pendingYingboGift is null)
        {
            if (_pendingDecision?.Kind == DecisionKind.Yingbo)
                throw new InvalidOperationException("A Yingbo prompt cannot exist without its card continuation.");
            return;
        }

        var pending = _pendingYingboGift;
        if (!UsesFormalMouLuMeng ||
            _pendingDecision is not { Kind: DecisionKind.Yingbo, IsPrivate: true } decision ||
            decision.PlayerSeat != pending.SourceSeat ||
            _cardZones.GetLocation(pending.Card.Id) != CardLocation.Processing ||
            !IsYingboUnrespondable(pending.ResolutionId))
        {
            throw new InvalidOperationException(
                "A Yingbo gift must retain its owner, private prompt and physical card in Processing.");
        }
    }

    private enum YingboGiftContinuation
    {
        Attack,
        GroupAttack
    }

    private sealed record YingboGiftResolution(
        long ResolutionId,
        int SourceSeat,
        Card Card,
        CardKind CardKind,
        YingboGiftContinuation Continuation,
        AttackResolution? Attack,
        GroupCardResolution? Group);
}
