namespace CardGame.Core;

public sealed partial class GameEngine
{
    private CommandResult SubmitTianyiPromptAnswer(PromptChoice selected)
    {
        if (_pendingTianyi is null || _pendingDecision is not { Kind: DecisionKind.TianyiPindian })
            return Reject(CommandErrorCode.InvalidPrompt, "There is no Tianyi Pindian awaiting a response.");
        return Accept(() => HumanTianyiCore(selected, _options.AdvanceAfterHumanCommands));
    }

    private void BeginTianyiPindian(PlayerRuntime source, int sourceCardId, int opponentSeat, long frameId)
    {
        var opponent = _players[opponentSeat];
        var sourceCard = GetHand(source).SingleOrDefault(card => card.Id == sourceCardId);
        if (!UsesFormalTaishiCi || !source.General.HasSkill(SkillKind.Tianyi) || sourceCard is null ||
            !opponent.IsAlive || opponent.Seat == source.Seat || GetHand(opponent).Count == 0 ||
            source.UsedActiveSkillKinds.Contains(SkillKind.Tianyi) || _pendingTianyi is not null)
            throw new InvalidOperationException("The selected Tianyi Pindian is no longer legal.");

        source.UsedActiveSkillKinds.Add(SkillKind.Tianyi);
        _pendingTianyi = new TianyiResolution(frameId, source.Seat, opponent.Seat, sourceCard.Id);
        SetActiveSkillFrameStep(frameId, ResolutionFrameStep.AwaitingResponse);
        PublishTianyiPindianChoice(_pendingTianyi);
    }

    private void PublishTianyiPindianChoice(TianyiResolution pending)
    {
        var source = _players[pending.SourceSeat];
        var opponent = _players[pending.OpponentSeat];
        var hand = GetHand(opponent);
        if (!ReferenceEquals(_pendingTianyi, pending) || hand.Count == 0)
            throw new InvalidOperationException("The Tianyi opponent has no Pindian card.");

        _pendingDecision = new PendingDecision(
            DecisionKind.TianyiPindian,
            opponent.Seat,
            $"{source.Name} 对你发动【天义】，请选择一张手牌作为拼点牌。",
            hand.Select(card => card.Id).ToArray(),
            [],
            SourceSeat: source.Seat)
        {
            PromptId = opponent.IsHuman ? CreatePromptId() : default,
            TargetSeat = opponent.Seat,
            IsPrivate = true,
            Choices = hand.Select(card => new PromptChoice(
                new ChoiceId($"tianyi-pindian-{card.Id}.resolution-{pending.FrameId}"),
                $"以【{card.DisplayName}】（{card.Rank}）参与拼点",
                [card.Id],
                [],
                new Dictionary<string, string> { ["action"] = "tianyi-pindian" })).ToArray()
        };
        _status = opponent.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
    }

    private void ResolveTianyiPindianChoice(PromptChoice selected)
    {
        var pending = _pendingTianyi ?? throw new InvalidOperationException("There is no Tianyi Pindian.");
        if (_pendingDecision is not { Kind: DecisionKind.TianyiPindian } decision ||
            decision.PlayerSeat != pending.OpponentSeat ||
            selected.Parameters.GetValueOrDefault("action") != "tianyi-pindian" ||
            selected.Cards.Count != 1)
            throw new InvalidOperationException("The Tianyi Pindian choice is malformed.");

        var source = _players[pending.SourceSeat];
        var opponent = _players[pending.OpponentSeat];
        var sourceCard = GetHand(source).Single(card => card.Id == pending.SourceCardId);
        var opponentCard = GetHand(opponent).Single(card => card.Id == selected.Cards[0]);
        ClearPendingDecision();
        SetActiveSkillFrameStep(pending.FrameId, ResolutionFrameStep.ResolvingEffect);
        MoveCard(sourceCard, CardLocation.Hand(source.Seat), CardLocation.Processing, CardMoveReasons.PindianReveal);
        MoveCard(opponentCard, CardLocation.Hand(opponent.Seat), CardLocation.Processing, CardMoveReasons.PindianReveal);
        var sourceWon = sourceCard.Rank > opponentCard.Rank;
        source.TianyiWonThisTurn = sourceWon;
        source.TianyiLostThisTurn = !sourceWon;
        QueueGameEvent(new PindianResolvedEvent(
            pending.FrameId, SkillKind.Tianyi, source.Seat, opponent.Seat,
            sourceCard.Id, opponentCard.Id, sourceCard.Rank, opponentCard.Rank, sourceWon));
        MoveCard(sourceCard, CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.PindianFinish);
        MoveCard(opponentCard, CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.PindianFinish);
        AddLog("Pindian", $"{source.Name} 以 {sourceCard.Rank} 点与 {opponent.Name} 的 {opponentCard.Rank} 点拼点，天义{(sourceWon ? "获胜" : "未赢")}。", source.Seat, opponent.Seat);

        _pendingTianyi = null;
        SetActiveSkillFrameStep(pending.FrameId, ResolutionFrameStep.Completed);
        QueueGameEvent(new ActiveSkillResolvedEvent(
            pending.FrameId, source.Seat, SkillKind.Tianyi, ActiveSkillEffectKind.PindianForSlashBonus));
        PopResolutionFrame(pending.FrameId, ResolutionFrameKind.ActiveSkill);
    }

    private bool IsAiTianyiPending() =>
        _pendingTianyi is not null &&
        _pendingDecision is { Kind: DecisionKind.TianyiPindian, PlayerSeat: var seat } &&
        !_players[seat].IsHuman;

    private void ResolvePendingAiTianyi()
    {
        if (!IsAiTianyiPending() || _pendingDecision is not { } decision)
            throw new InvalidOperationException("There is no AI Tianyi choice to resolve.");
        var hand = GetHand(_players[decision.PlayerSeat]);
        var cardId = hand.OrderByDescending(card => card.Rank).ThenBy(card => card.Id).First().Id;
        var selected = decision.Choices.Single(choice => choice.Cards.SequenceEqual([cardId]));
        AddThought(new AiThoughtRecord(++_thoughtSequence, _turnNumber, decision.PlayerSeat,
            selected.Description, [], "天义拼点：使用自己的私有手牌中点数最高的牌。"));
        ResolveTianyiPindianChoice(selected);
        PublishState();
    }

    private sealed class TianyiResolution(long frameId, int sourceSeat, int opponentSeat, int sourceCardId)
    {
        public long FrameId { get; } = frameId;
        public int SourceSeat { get; } = sourceSeat;
        public int OpponentSeat { get; } = opponentSeat;
        public int SourceCardId { get; } = sourceCardId;
    }
}
