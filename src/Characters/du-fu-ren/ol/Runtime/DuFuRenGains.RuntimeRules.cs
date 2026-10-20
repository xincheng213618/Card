namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_du_fu_ren_ol_DuFuRenGains
{
    internal static bool IsDuFuRenOwnedZone(CardLocation from) => from.OwnerSeat is not null && from.Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.WoodenOxGrain;
}
