namespace CardGame.Core;

internal interface ISelectionWoundedQueries : IProgramSelectionQuery
{
    bool IdentityConcealed(int seat);
    bool IsRuleWounded(int seat);
}

internal static class XingzhaoRuntime
{
    // Missing population metadata must not read visibility or wounded counts.
    internal static bool MatchesWoundedGrant(int ownerSeat, SkillProgramHpSkillGrant rule, IProgramSelectionQueries queries)
    {
        if (rule.MinimumLivingWounded is not { } minimum) return true;
        var players = queries.Get<ISelectionParticipantQueries>();
        if (!players.IsAlive(ownerSeat)) return false;
        var wounded = queries.Get<ISelectionWoundedQueries>();
        return !wounded.IdentityConcealed(ownerSeat) &&
            players.Seats.Count(seat => players.IsAlive(seat) && wounded.IsRuleWounded(seat)) >= minimum;
    }
}
