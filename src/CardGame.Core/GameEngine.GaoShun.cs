namespace CardGame.Core;

public sealed partial class GameEngine
{
    private const string XianzhenSkillId = "classic:xianzhen";
    private const string XianzhenLossUsageId = "loss";
    private const string XianzhenWinUsagePrefix = "win.target-";

    private XianzhenResolution? _pendingXianzhen;

    private bool UsesFormalGaoShun =>
        _rulesVersion >= 105 &&
        IsClassicIdentityMode &&
        _contentRegistry?.Packages.Any(package =>
            package.Id == "standard-classic-generals" &&
            package.Version >= new Version(1, 83, 0)) == true;

    private static string GetXianzhenWinUsageId(int targetSeat) =>
        $"{XianzhenWinUsagePrefix}{targetSeat}";

    private bool HasXianzhenWonAgainst(PlayerRuntime source, int targetSeat) =>
        UsesFormalGaoShun &&
        HasRuntimeSkill(source, XianzhenSkillId) &&
        _skillRuntimeState.GetUsage(
            source.Seat,
            XianzhenSkillId,
            GetXianzhenWinUsageId(targetSeat),
            SkillUsageScope.Turn) > 0;

    private bool HasXianzhenWon(PlayerRuntime source) =>
        _players.Any(target => target.Seat != source.Seat &&
            HasXianzhenWonAgainst(source, target.Seat));

    private bool HasXianzhenLost(PlayerRuntime source) =>
        UsesFormalGaoShun &&
        HasRuntimeSkill(source, XianzhenSkillId) &&
        _skillRuntimeState.GetUsage(
            source.Seat,
            XianzhenSkillId,
            XianzhenLossUsageId,
            SkillUsageScope.Turn) > 0;

    private bool IsXianzhenTarget(PlayerRuntime source, PlayerRuntime target) =>
        HasXianzhenWonAgainst(source, target.Seat);

    private CommandResult SubmitXianzhenPromptAnswer(PromptChoice selected)
    {
        if (_pendingXianzhen is null ||
            _pendingDecision is not { Kind: DecisionKind.XianzhenPindian })
        {
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的陷阵拼点窗口。");
        }

        return Accept(() => HumanXianzhenCore(
            selected,
            _options.AdvanceAfterHumanCommands));
    }

    private void BeginXianzhenPindian(
        PlayerRuntime source,
        int sourceCardId,
        int opponentSeat,
        long frameId)
    {
        var opponent = _players[opponentSeat];
        var sourceCard = GetHand(source).SingleOrDefault(card => card.Id == sourceCardId);
        if (!UsesFormalGaoShun ||
            !HasRuntimeSkill(source, XianzhenSkillId) ||
            sourceCard is null ||
            !opponent.IsAlive ||
            opponent.Seat == source.Seat ||
            GetHand(opponent).Count == 0 ||
            source.UsedActiveSkillKinds.Contains(SkillKind.Xianzhen) ||
            _pendingXianzhen is not null)
        {
            throw new InvalidOperationException("The selected Xianzhen Pindian is no longer legal.");
        }

        source.UsedActiveSkillKinds.Add(SkillKind.Xianzhen);
        _pendingXianzhen = new XianzhenResolution(
            frameId,
            source.Seat,
            opponent.Seat,
            sourceCard.Id);
        SetActiveSkillFrameStep(frameId, ResolutionFrameStep.AwaitingResponse);
        PublishXianzhenPindianChoice(_pendingXianzhen);
    }

    private void PublishXianzhenPindianChoice(XianzhenResolution pending)
    {
        var source = _players[pending.SourceSeat];
        var opponent = _players[pending.OpponentSeat];
        var hand = GetHand(opponent);
        if (!ReferenceEquals(_pendingXianzhen, pending) || hand.Count == 0)
        {
            throw new InvalidOperationException("The Xianzhen opponent has no Pindian card.");
        }

        _pendingDecision = new PendingDecision(
            DecisionKind.XianzhenPindian,
            opponent.Seat,
            $"{source.Name} 对你发动【陷阵】，请选择一张手牌作为拼点牌。",
            hand.Select(card => card.Id).ToArray(),
            [],
            SourceSeat: source.Seat)
        {
            PromptId = opponent.IsHuman ? CreatePromptId() : default,
            TargetSeat = opponent.Seat,
            IsPrivate = true,
            Choices = hand.Select(card => new PromptChoice(
                new ChoiceId($"xianzhen.pindian.card-{card.Id}.resolution-{pending.FrameId}"),
                $"以【{card.DisplayName}】（{card.Rank}）参与拼点",
                [card.Id],
                [],
                new Dictionary<string, string> { ["action"] = "xianzhen-pindian" }))
                .ToArray()
        };
        _status = opponent.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveXianzhenPindianChoice(PromptChoice selected)
    {
        var pending = _pendingXianzhen ??
            throw new InvalidOperationException("There is no Xianzhen Pindian.");
        if (_pendingDecision is not { Kind: DecisionKind.XianzhenPindian } decision ||
            decision.PlayerSeat != pending.OpponentSeat ||
            selected.Parameters.GetValueOrDefault("action") != "xianzhen-pindian" ||
            selected.Cards.Count != 1 ||
            selected.Targets.Count != 0)
        {
            throw new InvalidOperationException("The Xianzhen Pindian choice is malformed.");
        }

        var source = _players[pending.SourceSeat];
        var opponent = _players[pending.OpponentSeat];
        var sourceCard = GetHand(source).Single(card => card.Id == pending.SourceCardId);
        var opponentCard = GetHand(opponent).Single(card => card.Id == selected.Cards[0]);
        ClearPendingDecision();
        SetActiveSkillFrameStep(pending.FrameId, ResolutionFrameStep.ResolvingEffect);
        MoveCard(sourceCard, CardLocation.Hand(source.Seat), CardLocation.Processing,
            CardMoveReasons.PindianReveal);
        MoveCard(opponentCard, CardLocation.Hand(opponent.Seat), CardLocation.Processing,
            CardMoveReasons.PindianReveal);

        var sourceWon = sourceCard.Rank > opponentCard.Rank;
        var usageId = sourceWon
            ? GetXianzhenWinUsageId(opponent.Seat)
            : XianzhenLossUsageId;
        if (!_skillRuntimeState.TryConsumeUsage(
                source.Seat,
                XianzhenSkillId,
                usageId,
                SkillUsageScope.Turn,
                limit: 1))
        {
            throw new InvalidOperationException("Xianzhen tried to record its turn result twice.");
        }

        QueueGameEvent(new SkillUsageConsumedEvent(
            source.Seat,
            XianzhenSkillId,
            usageId,
            SkillUsageScope.Turn,
            Count: 1));
        QueueGameEvent(new PindianResolvedEvent(
            pending.FrameId,
            SkillKind.Xianzhen,
            source.Seat,
            opponent.Seat,
            sourceCard.Id,
            opponentCard.Id,
            sourceCard.Rank,
            opponentCard.Rank,
            sourceWon));
        QueueGameEvent(new XianzhenResolvedEvent(
            pending.FrameId,
            source.Seat,
            opponent.Seat,
            sourceWon));

        MoveCard(sourceCard, CardLocation.Processing, CardLocation.DiscardPile,
            CardMoveReasons.PindianFinish);
        MoveCard(opponentCard, CardLocation.Processing, CardLocation.DiscardPile,
            CardMoveReasons.PindianFinish);
        AddLog(
            "Pindian",
            sourceWon
                ? $"{source.Name} 以 {sourceCard.Rank} 点赢得对 {opponent.Name} 的【陷阵】拼点；本回合对其用牌无距离限制、对其使用【杀】无次数限制且无视其防具。"
                : $"{source.Name} 以 {sourceCard.Rank} 点未赢得对 {opponent.Name} 的【陷阵】拼点；本回合不能使用【杀】。",
            source.Seat,
            opponent.Seat);

        _pendingXianzhen = null;
        SetActiveSkillFrameStep(pending.FrameId, ResolutionFrameStep.Completed);
        QueueGameEvent(new ActiveSkillResolvedEvent(
            pending.FrameId,
            source.Seat,
            SkillKind.Xianzhen,
            ActiveSkillEffectKind.PindianForSlashBonus));
        PopResolutionFrame(pending.FrameId, ResolutionFrameKind.ActiveSkill);
    }

    private EngineRunResult HumanXianzhenCore(
        PromptChoice selected,
        bool advanceToHumanBoundary)
    {
        RequireHumanDecision(DecisionKind.XianzhenPindian);
        ResolveXianzhenPindianChoice(selected);
        PublishState();
        return advanceToHumanBoundary ? AdvanceToHumanBoundary() : BuildResult();
    }

    private bool IsAiXianzhenPending() =>
        _pendingXianzhen is not null &&
        _pendingDecision is { Kind: DecisionKind.XianzhenPindian, PlayerSeat: var seat } &&
        !_players[seat].IsHuman;

    private void ResolvePendingAiXianzhen()
    {
        if (!IsAiXianzhenPending() || _pendingDecision is not { } decision)
        {
            throw new InvalidOperationException("There is no AI Xianzhen choice to resolve.");
        }

        var hand = GetHand(_players[decision.PlayerSeat]);
        var cardId = hand
            .OrderByDescending(card => card.Rank)
            .ThenBy(card => card.Id)
            .First()
            .Id;
        var selected = decision.Choices.Single(choice => choice.Cards.SequenceEqual([cardId]));
        AddThought(new AiThoughtRecord(
            ++_thoughtSequence,
            _turnNumber,
            decision.PlayerSeat,
            selected.Description,
            [],
            "陷阵拼点：使用自己的私有手牌中点数最高的牌。"));
        ResolveXianzhenPindianChoice(selected);
        PublishState();
    }

    private void AssertXianzhenInvariant()
    {
        if (_pendingXianzhen is not { } pending)
        {
            if (_pendingDecision?.Kind == DecisionKind.XianzhenPindian)
            {
                throw new InvalidOperationException(
                    "A Xianzhen prompt cannot exist without its active-skill continuation.");
            }
            return;
        }

        if (!UsesFormalGaoShun ||
            _resolutionStack.OfType<ActiveSkillFrame>().LastOrDefault() is not
            {
                Id: var frameId,
                Skill: SkillKind.Xianzhen,
                Effect: ActiveSkillEffectKind.PindianForSlashBonus,
                Step: ResolutionFrameStep.AwaitingResponse
            } ||
            frameId != pending.FrameId ||
            _pendingDecision is not
            {
                Kind: DecisionKind.XianzhenPindian,
                IsPrivate: true,
                PlayerSeat: var responderSeat
            } ||
            responderSeat != pending.OpponentSeat ||
            _pendingDecision.Choices.Count == 0 ||
            _pendingDecision.Choices.Any(choice =>
                choice.Cards.Count != 1 ||
                choice.Targets.Count != 0 ||
                choice.Parameters.GetValueOrDefault("action") != "xianzhen-pindian"))
        {
            throw new InvalidOperationException(
                "A Xianzhen Pindian must retain its private opponent-card prompt and active-skill frame.");
        }
    }

    private sealed record XianzhenResolution(
        long FrameId,
        int SourceSeat,
        int OpponentSeat,
        int SourceCardId);
}
