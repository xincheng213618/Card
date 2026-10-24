namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_ProgramCardUseRoles
{
    // Role changes are normally driven by the card user. Adding a target can also be driven by a
    // character who is already that card's sole designated target, which is how a Slash passes on
    // to another character without moving away from the original target.
    internal static bool IsProgramCardUseRoleOwner(int ownerSeat, CardActionContext action, bool ownerMayBeTarget) =>
        action.ActorSeat == ownerSeat ||
        ownerMayBeTarget && action.EffectiveDesignatedTargetSeats.Contains(ownerSeat) &&
        action.EffectiveKind is CardKind.Slash or CardKind.FireSlash or CardKind.ThunderSlash or CardKind.ColdSlash;
}
