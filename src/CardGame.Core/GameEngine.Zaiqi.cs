namespace CardGame.Core;

public sealed partial class GameEngine
{
    private sealed record ZaiqiResolution(int PlayerSeat, DelayedTurnEffects DelayedEffects);

    private void BeginZaiqiChoice(PlayerRuntime current, DelayedTurnEffects delayedEffects)
    {
        _pendingZaiqi = new ZaiqiResolution(current.Seat, delayedEffects);
        _pendingDecision = new PendingDecision(DecisionKind.Zaiqi, current.Seat,
            $"是否发动【再起】，放弃摸牌并展示牌堆顶 {current.MaxHp - current.Hp} 张牌？", [], [])
        {
            PromptId = CreatePromptId(), IsPrivate = true,
            Choices =
            [
                new PromptChoice(new ChoiceId("zaiqi.use"), "发动【再起】。", [], [],
                    new Dictionary<string, string> { ["action"] = "zaiqi-use" }),
                new PromptChoice(new ChoiceId("zaiqi.skip"), "不发动【再起】，正常摸牌。", [], [],
                    new Dictionary<string, string> { ["action"] = "zaiqi-skip" })
            ]
        };
        _status = current.IsHuman ? EngineStatus.AwaitingHumanResponse : EngineStatus.Running;
        PublishState();
    }

    private CommandResult SubmitZaiqiPromptAnswer(PromptChoice selected)
    {
        if (_pendingZaiqi is null || _pendingDecision is not { Kind: DecisionKind.Zaiqi })
            return Reject(CommandErrorCode.InvalidPrompt, "没有等待响应的再起窗口。");
        if (selected.Cards.Count != 0 || selected.Targets.Count != 0 ||
            !selected.Parameters.TryGetValue("action", out var action) ||
            action is not ("zaiqi-use" or "zaiqi-skip"))
            return Reject(CommandErrorCode.InvalidChoice, "再起选择不符合当前窗口。");
        return Accept(() =>
        {
            ResolveZaiqiChoice(action == "zaiqi-use");
            PublishState();
            return _options.AdvanceAfterHumanCommands ? AdvanceToHumanBoundary() : BuildResult();
        });
    }

    private void ResolveZaiqiChoice(bool useSkill)
    {
        var pending = _pendingZaiqi ?? throw new InvalidOperationException("There is no Zaiqi choice.");
        var owner = _players[pending.PlayerSeat];
        ClearPendingDecision();
        _pendingZaiqi = null;
        if (!useSkill)
        {
            DrawCards(owner, GetTurnDrawCount(owner), log: true);
            AddLog("SkillSkipped", $"{owner.Name} 未发动【再起】。", owner.Seat);
            CompleteTurnStartAfterDraw(owner, pending.DelayedEffects);
            return;
        }

        var revealed = new List<Card>();
        for (var i = 0; i < owner.MaxHp - owner.Hp; i++)
        {
            var card = DrawOneToProcessing(CardMoveReasons.ZaiqiReveal);
            if (card is null) break;
            revealed.Add(card);
        }
        var hearts = revealed.Where(card => card.Suit == Suit.Heart).ToArray();
        var gained = revealed.Where(card => card.Suit != Suit.Heart).ToArray();
        foreach (var card in hearts)
            MoveCard(card, CardLocation.Processing, CardLocation.DiscardPile, CardMoveReasons.ZaiqiDiscard);
        foreach (var card in gained)
            MoveCard(card, CardLocation.Processing, CardLocation.Hand(owner.Seat), CardMoveReasons.ZaiqiGain);
        var recovered = Math.Min(hearts.Length, owner.MaxHp - owner.Hp);
        if (recovered > 0)
        {
            var frameId = BeginRecovery(0, owner.Seat, owner.Seat, recovered);
            owner.Hp += recovered;
            QueueGameEvent(new RecoveryAppliedEvent(owner.Seat, owner.Seat, recovered, owner.Hp));
            PopResolutionFrame(frameId, ResolutionFrameKind.Recovery);
        }
        QueueGameEvent(new CardsRevealedEvent(0, revealed.Select(ToSnapshot).ToArray()));
        QueueGameEvent(new ZaiqiResolvedEvent(owner.Seat, revealed.Select(c => c.Id).ToArray(),
            hearts.Select(c => c.Id).ToArray(), gained.Select(c => c.Id).ToArray(), recovered));
        AddLog("SkillTriggered", $"{owner.Name} 发动【再起】，展示 {revealed.Count} 张牌，回复 {recovered} 点体力并获得 {gained.Length} 张牌。", owner.Seat);
        CompleteTurnStartAfterDraw(owner, pending.DelayedEffects);
    }

    private bool IsAiZaiqiPending() =>
        _pendingZaiqi is { PlayerSeat: var seat } &&
        _pendingDecision is { Kind: DecisionKind.Zaiqi, PlayerSeat: var decisionSeat } &&
        seat == decisionSeat && !_players[seat].IsHuman;

    private void ResolvePendingAiZaiqi()
    {
        var pending = _pendingZaiqi ?? throw new InvalidOperationException("There is no AI Zaiqi choice.");
        var owner = _players[pending.PlayerSeat];
        ResolveZaiqiChoice(owner.MaxHp - owner.Hp >= 2 || owner.Hp == 1);
        PublishState();
    }
}
