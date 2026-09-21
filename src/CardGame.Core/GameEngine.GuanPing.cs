namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string LongyinSkillId = "classic:longyin";

    private LongyinResolution? _pendingLongyin;

    private bool UsesFormalGuanPing =>
        HasClassicGeneralPackage(new Version(1, 97, 0));

    private bool TryBeginLongyinWindow(
        AttackResolution attack,
        bool countedTowardSlashLimit,
        FangtianHalberdResolution? fangtian = null)
    {
        if (!UsesFormalGuanPing ||
            _phase != TurnPhase.Play ||
            attack.SourceSeat != _currentSeat ||
            attack.EffectiveCardKind is not (CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash))
        {
            return false;
        }

        var candidates = Enumerable.Range(0, _playerCount)
            .Select(offset => (_currentSeat + offset) % _playerCount)
            .Where(seat =>
            {
                var owner = _players[seat];
                return owner.IsAlive &&
                       HasRuntimeSkill(owner, LongyinSkillId) &&
                       GetLongyinCostCards(owner).Count > 0;
            })
            .ToArray();
        if (candidates.Length == 0)
        {
            return false;
        }

        if (_pendingLongyin is not null)
        {
            throw new InvalidOperationException("The engine cannot open two Longyin windows at once.");
        }

        var slashWasRed = attack.PhysicalCards.Count > 0 &&
                          attack.PhysicalCards.All(card => card.Suit is Suit.Heart or Suit.Diamond);
        var pending = new LongyinResolution(
            attack,
            candidates,
            countedTowardSlashLimit,
            slashWasRed,
            fangtian);
        _pendingLongyin = pending;
        BeginNextLongyinCandidate(pending);
        return true;
    }

    private void BeginNextLongyinCandidate(LongyinResolution pending)
    {
        if (!ReferenceEquals(_pendingLongyin, pending))
        {
            throw new InvalidOperationException("The Longyin continuation is no longer current.");
        }

        while (pending.CandidateIndex < pending.CandidateSeats.Count)
        {
            var owner = _players[pending.CandidateSeats[pending.CandidateIndex]];
            var cards = GetLongyinCostCards(owner);
            if (!owner.IsAlive || !HasRuntimeSkill(owner, LongyinSkillId) || cards.Count == 0)
            {
                pending.CandidateIndex++;
                continue;
            }

            var slashName = CardCatalog.Get(pending.Attack.EffectiveCardKind ?? CardKind.Slash).DisplayName;
            var choices = cards
                .Select(card =>
                {
                    var location = FindOwnedCardLocation(owner, card);
                    return new PromptChoice(
                        new ChoiceId($"longyin.card-{card.Id}.resolution-{pending.Attack.ResolutionId}.owner-{owner.Seat}"),
                        $"弃置{(location.Zone == CardZoneKind.Equipment ? "装备" : "手牌")}【{card.DisplayName}】，令此【{slashName}】不计入次数{(pending.SlashWasRed ? "，然后摸一张牌" : "")}。",
                        [card.Id],
                        [pending.Attack.SourceSeat],
                        new Dictionary<string, string>
                        {
                            ["action"] = "longyin-use",
                            ["source-zone"] = location.Zone.ToString()
                        });
                })
                .Append(new PromptChoice(
                    new ChoiceId($"longyin.skip.resolution-{pending.Attack.ResolutionId}.owner-{owner.Seat}"),
                    $"不发动【龙吟】，继续结算此【{slashName}】。",
                    [],
                    [pending.Attack.SourceSeat],
                    new Dictionary<string, string> { ["action"] = "longyin-skip" }))
                .ToArray();
            _pendingDecision = new PendingDecision(
                DecisionKind.Longyin,
                owner.Seat,
                $"{_players[pending.Attack.SourceSeat].Name} 在出牌阶段使用了【{slashName}】，是否发动【龙吟】？",
                cards.Select(card => card.Id).ToArray(),
                [pending.Attack.SourceSeat],
                SourceSeat: pending.Attack.SourceSeat,
                IncomingCard: pending.Attack.EffectiveCardKind)
            {
                PromptId = CreatePromptId(),
                IsPrivate = true,
                TargetSeat = pending.Attack.TargetSeat,
                Choices = Array.AsReadOnly(choices)
            };
            _status = owner.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
            return;
        }

        CompleteLongyinWindow(pending);
    }

    private IReadOnlyList<Card> GetLongyinCostCards(PlayerRuntime owner) =>
        GetHand(owner).Concat(GetEquipment(owner)).ToArray();

    private CommandResult SubmitLongyinPromptAnswer(PromptChoice selected)
    {
        if (_pendingLongyin is not { } pending ||
            _pendingDecision is not { Kind: DecisionKind.Longyin, PlayerSeat: var ownerSeat } ||
            pending.CurrentCandidateSeat != ownerSeat)
        {
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的龙吟窗口。");
        }

        return Accept(() => HumanLongyinCore(
            pending,
            selected,
            _options.AdvanceAfterHumanCommands));
    }

    private EngineRunResult HumanLongyinCore(
        LongyinResolution pending,
        PromptChoice selected,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.Longyin);
        ResolveLongyinChoice(pending, selected);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private bool IsAiLongyinPending() =>
        _pendingLongyin is { } pending &&
        _pendingDecision is { Kind: DecisionKind.Longyin, PlayerSeat: var ownerSeat } &&
        pending.CurrentCandidateSeat == ownerSeat &&
        !_players[ownerSeat].IsHuman;

    private void ResolvePendingAiLongyin()
    {
        var pending = _pendingLongyin ??
            throw new InvalidOperationException("There is no AI Longyin window to resolve.");
        var decision = _pendingDecision ??
            throw new InvalidOperationException("The AI Longyin window has no prompt.");
        var (selected, thought) = _aiBrains[decision.PlayerSeat].ChooseLongyinChoice(
            CreateSnapshot(decision.PlayerSeat),
            pending.Attack.SourceSeat,
            pending.SlashWasRed,
            pending.SlashCountRemoved || !pending.CountedTowardSlashLimit,
            decision.Choices,
            ++_thoughtSequence);
        _aiThoughts.Add(thought);
        ResolveLongyinChoice(pending, selected);
        PublishState();
    }

    private void ResolveLongyinChoice(LongyinResolution pending, PromptChoice selected)
    {
        if (!ReferenceEquals(_pendingLongyin, pending) ||
            _pendingDecision is not { Kind: DecisionKind.Longyin, PlayerSeat: var ownerSeat } ||
            pending.CurrentCandidateSeat != ownerSeat ||
            !selected.Parameters.TryGetValue("action", out var action))
        {
            throw new InvalidOperationException("The Longyin choice is no longer current.");
        }

        var owner = _players[ownerSeat];
        ClearPendingDecision();
        if (action == "longyin-skip")
        {
            if (selected.Cards.Count != 0)
            {
                throw new InvalidOperationException("Skipping Longyin cannot select a card.");
            }
            QueueGameEvent(new LongyinResolvedEvent(
                pending.Attack.ResolutionId,
                owner.Seat,
                pending.Attack.SourceSeat,
                Used: false,
                DiscardedCardId: null,
                DiscardedCardKind: null,
                pending.Attack.EffectiveCardKind ?? CardKind.Slash,
                pending.SlashWasRed,
                SlashCountRemoved: false,
                DrawnCardIds: []));
            AddLog("SkillSkipped", $"{owner.Name} 未发动【龙吟】。", owner.Seat, pending.Attack.SourceSeat);
        }
        else if (action == "longyin-use" && selected.Cards.Count == 1)
        {
            var card = GetLongyinCostCards(owner).SingleOrDefault(candidate => candidate.Id == selected.Cards[0]) ??
                       throw new InvalidOperationException("The selected Longyin cost is no longer owned by its user.");
            var source = FindOwnedCardLocation(owner, card);
            MoveCard(card, source, CardLocation.DiscardPile, CardMoveReasons.LongyinDiscard);
            var removedCount = false;
            if (pending.CountedTowardSlashLimit && !pending.SlashCountRemoved)
            {
                if (_slashCountThisTurn <= 0)
                {
                    throw new InvalidOperationException("Longyin cannot remove a missing Slash count.");
                }
                _slashCountThisTurn--;
                pending.SlashCountRemoved = true;
                removedCount = true;
            }
            var drawn = pending.SlashWasRed
                ? DrawCards(owner, 1, log: false, CardMoveReasons.LongyinDraw)
                : [];
            QueueGameEvent(new LongyinResolvedEvent(
                pending.Attack.ResolutionId,
                owner.Seat,
                pending.Attack.SourceSeat,
                Used: true,
                card.Id,
                card.Kind,
                pending.Attack.EffectiveCardKind ?? CardKind.Slash,
                pending.SlashWasRed,
                removedCount,
                drawn));
            AddLog(
                "SkillTriggered",
                $"{owner.Name} 发动【龙吟】，弃置【{card.DisplayName}】并令此【{CardCatalog.Get(pending.Attack.EffectiveCardKind ?? CardKind.Slash).DisplayName}】不计入次数{(pending.SlashWasRed ? "，然后摸一张牌" : "")}。",
                owner.Seat,
                pending.Attack.SourceSeat);
        }
        else
        {
            throw new InvalidOperationException("The selected Longyin branch is unsupported.");
        }

        pending.CandidateIndex++;
        BeginNextLongyinCandidate(pending);
    }

    private void CompleteLongyinWindow(LongyinResolution pending)
    {
        if (!ReferenceEquals(_pendingLongyin, pending))
        {
            throw new InvalidOperationException("The Longyin window is no longer current.");
        }
        _pendingLongyin = null;
        if (pending.Fangtian is { } fangtian)
        {
            fangtian.LongyinResolved = true;
            BeginNextFangtianHalberdTarget(fangtian);
            return;
        }
        BeginSlashTargetResolution(pending.Attack);
    }

    private sealed class LongyinResolution(
        AttackResolution attack,
        IReadOnlyList<int> candidateSeats,
        bool countedTowardSlashLimit,
        bool slashWasRed,
        FangtianHalberdResolution? fangtian)
    {
        public AttackResolution Attack { get; } = attack;
        public IReadOnlyList<int> CandidateSeats { get; } = Array.AsReadOnly(candidateSeats.ToArray());
        public bool CountedTowardSlashLimit { get; } = countedTowardSlashLimit;
        public bool SlashWasRed { get; } = slashWasRed;
        public FangtianHalberdResolution? Fangtian { get; } = fangtian;
        public int CandidateIndex { get; set; }
        public bool SlashCountRemoved { get; set; }
        public int CurrentCandidateSeat =>
            CandidateIndex < CandidateSeats.Count ? CandidateSeats[CandidateIndex] : -1;
    }
}
