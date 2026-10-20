namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_GameDomains
{
    internal static bool IsRuleOutsideGame(CardLocation at)=>at.Zone is CardZoneKind.RoundHold or CardZoneKind.PrivateTurnHold or CardZoneKind.PublicPersistentPile or CardZoneKind.Authority or CardZoneKind.BuquWound or CardZoneKind.Chunlao or CardZoneKind.PrivateReserve or CardZoneKind.PojunHold or CardZoneKind.PublicDeferredPile or CardZoneKind.WoodenOxGrain or CardZoneKind.OutsideGame;
}
