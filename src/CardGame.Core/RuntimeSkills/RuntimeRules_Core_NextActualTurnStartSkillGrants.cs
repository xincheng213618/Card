namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_NextActualTurnStartSkillGrants
{
    internal static void RequireIssuedNextActualTurnStartSkillGrant(int recipientSeat, SkillGrant grant,
        IReadOnlyList<IGameEvent> history)
    {
        var expiry = grant.NextActualTurnStartExpiry!;
        if (grant.GrantId != $"{grant.SourceId}:{grant.SkillId}" ||
            history.OfType<TurnStartedEvent>().Count(fact => fact.TurnNumber == expiry.GrantedTurnNumber &&
                fact.ActorSeat == expiry.GrantedTurnOwnerSeat) != 1 ||
            history.OfType<ProgramTurnSkillsGrantedEvent>().Count(issued =>
                issued.RecipientSeat == recipientSeat && issued.GrantSourceId == grant.SourceId &&
                issued.TurnExpiry is null && issued.NextActualTurnStartExpiry == expiry &&
                issued.GrantedSkillIds.Contains(grant.SkillId, StringComparer.Ordinal)) != 1)
            throw new InvalidOperationException("A next-start skill expiry lost its exact independent issued grant.");
    }
}
