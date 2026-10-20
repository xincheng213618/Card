namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_ProgramOwnedCardDistributions
{
    internal static string ProgramOwnedDistributionZoneName(CardZoneKind zone) => zone switch
    {
        CardZoneKind.Hand => "手牌",
        CardZoneKind.Equipment => "装备牌",
        _ => throw new InvalidOperationException("Unsupported owned-card distribution zone.")
    };
}
