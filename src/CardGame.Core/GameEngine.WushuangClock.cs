namespace CardGame.Core;

public sealed partial class GameEngine
{
    private CommandResult SubmitWushuangClock(TickWushuangClockCommand command)
    {
        if (command.ActorSeat != -1)
            return Reject(CommandErrorCode.InvalidActor, "无双计时只能由主机（座位-1）推进。");
        if (!IsWushuangClockRunning)
            return Reject(CommandErrorCode.IllegalAction, "无双计时仅在选将完成后的未结束对局中推进。");
        if (command.ElapsedMilliseconds is <= 0 or > WushuangMode.TimeLimitMilliseconds)
            return Reject(CommandErrorCode.IllegalAction, "计时增量必须为正数且不超过15分钟。");
        return Accept(() =>
        {
            var elapsed = Math.Min(command.ElapsedMilliseconds, WushuangRemainingMilliseconds!.Value);
            _wushuangElapsedMilliseconds += elapsed;
            AdvanceEventRulesAndQueueFact(new WushuangClockAdvancedEvent(elapsed, _wushuangElapsedMilliseconds));
            if (_wushuangElapsedMilliseconds == WushuangMode.TimeLimitMilliseconds) ExpireWushuangMatch();
            AdvanceRulesAndPublishState();
        });
    }

    private void ExpireWushuangMatch()
    {
        var left = _players[0];
        var right = _players[1];
        // Local deterministic scoring, deliberately different from OL's opaque
        // MVP formula. Ratios use cross multiplication, without rounding.
        var comparison = (left.Hp * right.MaxHp).CompareTo(right.Hp * left.MaxHp);
        if (comparison == 0) comparison = left.Hp.CompareTo(right.Hp);
        if (comparison == 0) comparison = GetHand(left).Count.CompareTo(GetHand(right).Count);
        _winnerSeat = comparison == 0 ? null : comparison > 0 ? 0 : 1;
        _winnerTeamId = _winnerSeat is { } winner ? _players[winner].TeamId : null;
        _winner = _winnerTeamId is null ? Winner.Draw : GetWinnerForTeam(_winnerTeamId);

        var revealedJudgments = _resolutionStack.OfType<JudgmentFrame>().Where(f => f.CardId is not null)
            .Select(f => (CardId: f.CardId!.Value, f.TargetSeat)).Distinct().ToArray();
        _pendingDecision = null;
        while (_resolutionStack.LastOrDefault() is { } frame)
        {
            _resolutionStack.CompleteTop(frame.Id, frame.Kind);
            AdvanceEventRulesAndQueueFact(new WushuangResolutionCancelledEvent(frame.Id, frame.Kind));
        }
        _pendingCardsMovedBatches.Clear();
        _pendingHpChanges.Clear();
        _pendingCharacterStateChanges.Clear();
        CancelScheduledProgramPhase();
        ClearActualDiscardRecoveryPhase();
        MoveAllCards(CardLocation.Processing, CardLocation.DiscardPile, new("mode.wushuang.timeout"));
        foreach (var judgment in revealedJudgments)
            if (_cardZones.GetLocation(judgment.CardId) == CardLocation.Judgment(judgment.TargetSeat))
                MoveCard(GetAttackCard(judgment.CardId), CardLocation.Judgment(judgment.TargetSeat),
                    CardLocation.DiscardPile, new("mode.wushuang.timeout-judgment"));
        _pendingCardsMovedBatches.Clear();
        AdvanceEventRulesAndQueueFact(new WushuangClockExpiredEvent(_wushuangElapsedMilliseconds, _winnerSeat,
            "本地计分：体力比例、体力值、手牌数依次比较，全部相同则平局。"));
        AdvanceEventRulesAndQueueFact(new WinnerDeterminedEvent(_winner, _winnerTeamId, null) { WinnerSeat = _winnerSeat });
        CompleteGame();
    }
}
