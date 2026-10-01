namespace CardGame.Core;

public sealed record SkillPromptPresentation(string SkillId, string Name, string Title, string Instructions);

public sealed partial class GameEngine
{
    private CommandResult SubmitPindianPromptAnswer(PromptChoice selected)
    {
        if (_pendingDecision?.SkillPrompt is null || _resolutionStack.LastOrDefault() is not PindianFrame)
            return Reject(CommandErrorCode.InvalidPrompt, "There is no current Pindian interaction.");
        return Accept(() =>
        {
            ResolvePindianChoice(selected);
            AdvanceRulesAndPublishState();
            return _options.AdvanceAfterHumanCommands ? AdvanceToHumanBoundary() : BuildResult();
        });
    }

    private bool IsAiPindianPending() =>
        _resolutionStack.LastOrDefault() is PindianFrame &&
        _pendingDecision is { SkillPrompt: not null } decision && !_players[decision.PlayerSeat].IsHuman;

    private int CountCardsUsedByCurrentPlayerThisTurn(int ownerSeat)
    {
        var turnStart = _events.FindLastIndex(envelope => envelope.Payload is TurnStartedEvent started &&
            started.TurnNumber == _turnNumber && started.ActorSeat == ownerSeat);
        return _events.Skip(turnStart + 1).Count(envelope =>
            envelope.Payload is CardUseDeclaredEvent declared && declared.SourceSeat == ownerSeat);
    }

    private int CountCardsUsedOrRespondedByPlayerThisTurn(int ownerSeat)
    {
        // The current turn's start and this command's fresh events may still sit in
        // the pending buffer, so the scan spans both the flushed and pending streams;
        // the boundary is the current turn's start regardless of its owner.
        var turnStart = _events.FindLastIndex(envelope =>
            envelope.Payload is TurnStartedEvent started && started.TurnNumber == _turnNumber);
        IEnumerable<IGameEvent> visible = turnStart >= 0
            ? _events.Skip(turnStart + 1).Select(envelope => envelope.Payload).Concat(_pendingEvents)
            : _pendingEvents;
        return visible.Count(payload =>
            payload is CardUseDeclaredEvent declared && declared.SourceSeat == ownerSeat ||
            payload is CardActionAcceptedEvent accepted &&
            accepted.Action.Type == CardActionType.Response &&
            accepted.Action.ActorSeat == ownerSeat);
    }
}
