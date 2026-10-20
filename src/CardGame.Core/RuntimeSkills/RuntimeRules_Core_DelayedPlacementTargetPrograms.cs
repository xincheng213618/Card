namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_DelayedPlacementTargetPrograms
{
    internal static LegalActionKind DelayedPlacementActionKind(CardKind kind) => kind switch
    {
        CardKind.Indulgence => LegalActionKind.Indulgence,
        CardKind.SupplyShortage => LegalActionKind.SupplyShortage,
        CardKind.Lightning => LegalActionKind.Lightning,
        _ => throw new InvalidOperationException("A delayed placement continuation requires a delayed trick.")
    };
}
