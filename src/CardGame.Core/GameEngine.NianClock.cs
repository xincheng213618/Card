namespace CardGame.Core;

/// <summary>Trusted host elapsed time, recorded as data so a replay never reads a wall clock.</summary>
public sealed record TickNianClockCommand(int ElapsedMilliseconds, long ExpectedRevision) : GameCommand(-1, ExpectedRevision);
public sealed record NianClockAdvancedEvent(int ElapsedMilliseconds, int TotalMilliseconds) : IGameEvent;
public sealed record NianClockExpiredEvent(int TotalMilliseconds) : IGameEvent;
public sealed record NianResolutionCancelledEvent(long FrameId, ResolutionFrameKind Kind) : IGameEvent;

public sealed partial class GameEngine
{
    private const int NianTimeLimitMilliseconds = 30 * 60 * 1000;
    public bool IsNianClockRunning => _modeDefinition.NianActivity is not null && _started && _setupComplete && _winner == Winner.None;
    public int? NianRemainingMilliseconds => _modeDefinition.NianActivity is null ? null :
        Math.Max(0, NianTimeLimitMilliseconds - CompleteProgramEventHistory().OfType<NianClockAdvancedEvent>()
            .Select(entry => entry.TotalMilliseconds).DefaultIfEmpty(0).Last());

    private CommandResult SubmitNianClock(TickNianClockCommand command)
    {
        if (!IsNianClockRunning)
            return Reject(CommandErrorCode.IllegalAction, "The Nian clock runs only during a started, fully selected challenge.");
        if (command.ElapsedMilliseconds is <= 0 or > NianTimeLimitMilliseconds)
            return Reject(CommandErrorCode.IllegalAction, "Elapsed Nian time must be positive and no greater than the entire time limit.");
        return Accept(() =>
        {
            var elapsed = Math.Min(command.ElapsedMilliseconds, NianRemainingMilliseconds!.Value);
            var total = NianTimeLimitMilliseconds - NianRemainingMilliseconds.Value + elapsed;
            AdvanceEventRulesAndQueueFact(new NianClockAdvancedEvent(elapsed, total));
            if (total == NianTimeLimitMilliseconds) ExpireNianChallenge();
            AdvanceRulesAndPublishState();
        });
    }

    private void ExpireNianChallenge()
    {
        // Timeout retires the whole pending challenge. Paid entities stay paid;
        // there is no ordinary child return into an expired outer instruction.
        var revealedJudgments = _resolutionStack.OfType<JudgmentFrame>()
            .Where(frame => frame.CardId is not null)
            .Select(frame => (CardId: frame.CardId!.Value, frame.TargetSeat)).Distinct().ToArray();
        _pendingDecision = null;
        while (_resolutionStack.LastOrDefault() is { } frame)
        {
            _resolutionStack.CompleteTop(frame.Id, frame.Kind);
            AdvanceEventRulesAndQueueFact(new NianResolutionCancelledEvent(frame.Id, frame.Kind));
        }
        _pendingCardsMovedBatches.Clear();
        _pendingHpChanges.Clear();
        _pendingCharacterStateChanges.Clear();
        CancelScheduledProgramPhase();
        ClearActualDiscardRecoveryPhase();
        // Cleanup belongs to the ended challenge, so it must not capture new
        // live-game loss/gain opportunities while retiring paid entities.
        _winnerTeamId = _modeDefinition.BossActivity!.EnemyTeamId;
        _winner = GetWinnerForTeam(_winnerTeamId);
        MoveAllCards(CardLocation.Processing, CardLocation.DiscardPile, new CardMoveReason("activity.nian.timeout"));
        // A judgment's revealed card temporarily shares its owner's delayed
        // zone. Retire only the exact active frame's card, leaving installed
        // delayed tricks and unrelated player regions intact.
        foreach (var judgment in revealedJudgments)
            if (_cardZones.GetLocation(judgment.CardId) == CardLocation.Judgment(judgment.TargetSeat))
                MoveCard(GetAttackCard(judgment.CardId), CardLocation.Judgment(judgment.TargetSeat),
                    CardLocation.DiscardPile, new CardMoveReason("activity.nian.timeout-judgment"));
        _pendingCardsMovedBatches.Clear();
        AdvanceEventRulesAndQueueFact(new NianClockExpiredEvent(NianTimeLimitMilliseconds));
        AdvanceEventRulesAndQueueFact(new WinnerDeterminedEvent(_winner, _winnerTeamId, null));
        CompleteGame();
    }
}
