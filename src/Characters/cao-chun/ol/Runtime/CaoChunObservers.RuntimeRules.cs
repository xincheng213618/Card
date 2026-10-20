namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_cao_chun_ol_CaoChunObservers
{
    internal static bool ShanjiaHeld(CardLocation location,int owner)=>location.OwnerSeat==owner&&location.Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.WoodenOxGrain;
}
