namespace CardGame.Core;

public sealed partial class GameEngine
{
    // A temporarily removed grant must not be revived after its boundary by a
    // later native restoration. The recipient's real start, not global time,
    // proves that this independent source's lifetime has elapsed.
    private bool HasNextActualTurnStartSkillGrantExpired(SkillGrant grant) =>
        grant.NextActualTurnStartExpiry is { } expiry &&
        ProgramEventHistory<TurnStartedEvent>().Any(started =>
            started.ActorSeat == expiry.RecipientSeat && started.TurnNumber > expiry.GrantedTurnNumber);

    private (CharacterState Recipient, SkillGrant Grant)[] NextActualTurnStartSkillGrantsDue(TurnStartedEvent started) =>
        _players.OrderBy(player => player.Seat).SelectMany(recipient => recipient.SkillGrants.Grants
            .Where(grant => grant.NextActualTurnStartExpiry is { } expiry &&
                expiry.RecipientSeat == recipient.Seat && recipient.Seat == started.ActorSeat &&
                expiry.GrantedTurnNumber < started.TurnNumber)
            .Select(grant => (Recipient: recipient, Grant: grant))).ToArray();

    private static void RequireIssuedNextActualTurnStartSkillGrant(int recipientSeat, SkillGrant grant,
        IReadOnlyList<IGameEvent> history) =>
        global::CardGame.Core.RuntimeSkills.RuntimeRules_Core_NextActualTurnStartSkillGrants.RequireIssuedNextActualTurnStartSkillGrant(recipientSeat,grant,history);

    // No binding-backed state may carry an expired skill into this new turn.
    // Keep the receipt on the disabled owning grant until TurnStarted is queued;
    // then the post-fact observer removes it and issues the exact expiry fact.
    [ProgramRuleObserver(ProgramRulePhase.BeforeEvent, 1)]
    private void DisableNextActualTurnStartSkillGrants(TurnStartedEvent started)
    {
        var due = NextActualTurnStartSkillGrantsDue(started);
        if (due.Length == 0) return;
        var history = CompleteProgramEventHistory().ToArray();
        if (started.TurnNumber <= 0 || !IsValidPlayerSeat(started.ActorSeat) ||
            !_players[started.ActorSeat].IsAlive || _resolutionStack.Count != 0 || _pendingDecision is not null ||
            _phase != TurnPhase.Draw || _turnNumber != started.TurnNumber || _currentSeat != started.ActorSeat ||
            _turnProgression.TurnNumber != started.TurnNumber || _turnProgression.OwnerSeat != started.ActorSeat ||
            history.OfType<TurnStartedEvent>().Any(fact => fact.TurnNumber == started.TurnNumber))
            throw new InvalidOperationException("A next-start skill disable requires the exact incoming real turn-start boundary.");
        foreach (var (recipient, grant) in due)
            RequireIssuedNextActualTurnStartSkillGrant(recipient.Seat, grant, history);
        // SetEnabled changes the binding-shard revision before projection and
        // derived-grant observers run. Already disabled leases still expire.
        foreach (var (recipient, grant) in due) recipient.SkillGrants.SetEnabled(grant.GrantId, false);
    }

    // Real extra turns and face-down skipped turns issue TurnStarted. Inserted
    // phases do not. The receipt belongs to each grant, independent of its donor.
    [ProgramRuleObserver(ProgramRulePhase.AfterEvent, 35)]
    private void ExpireNextActualTurnStartSkillGrants(TurnStartedEvent started)
    {
        var due = NextActualTurnStartSkillGrantsDue(started);
        if (due.Length == 0) return;
        var history = CompleteProgramEventHistory().ToArray();
        if (started.TurnNumber <= 0 || !IsValidPlayerSeat(started.ActorSeat) ||
            _turnNumber != started.TurnNumber || _currentSeat != started.ActorSeat ||
            _turnProgression.TurnNumber != started.TurnNumber || _turnProgression.OwnerSeat != started.ActorSeat ||
            history.OfType<TurnStartedEvent>().Count(fact => fact == started) != 1)
            throw new InvalidOperationException("A next-start skill expiry requires its exact real TurnStarted fact.");
        foreach (var (recipient, grant) in due)
        {
            RequireIssuedNextActualTurnStartSkillGrant(recipient.Seat, grant, history);
            if (grant.IsEnabled || !recipient.SkillGrants.RemoveGrant(grant.GrantId))
                throw new InvalidOperationException("A next-start skill expiry requires its disabled exact owning grant.");
            AdvanceEventRulesAndQueueFact(new ProgramNextActualTurnStartSkillGrantExpiredEvent(
                recipient.Seat, grant, started.TurnNumber, started.ActorSeat));
        }
    }
}
