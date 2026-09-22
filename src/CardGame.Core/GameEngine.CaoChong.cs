namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string RenxinSkillId = "classic:renxin";

    private RenxinResolution? _pendingRenxin;

    private bool UsesFormalCaoChong =>
        HasClassicGeneralPackage(new Version(1, 94, 0));

    private bool HasRenxin(CharacterState owner) =>
        UsesFormalCaoChong && HasRuntimeSkill(owner, RenxinSkillId);

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

    private IReadOnlyList<Card> GetRenxinCards(CharacterState owner) =>
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
