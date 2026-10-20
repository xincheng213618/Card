namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_tang_tang_ol_TangTangGains
{
    internal static bool IsTangTangOwnedZone(CardLocation from) => from.OwnerSeat is not null && from.Zone is CardZoneKind.Hand or CardZoneKind.Equipment or CardZoneKind.WoodenOxGrain;
}
