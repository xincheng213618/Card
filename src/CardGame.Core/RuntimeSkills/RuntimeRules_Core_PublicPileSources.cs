namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_PublicPileSources
{
    internal static string PublicPileIdentity(string skill, string instance) => $"{skill.Length}:{skill}{instance.Length}:{instance}";

    internal static CardLocation PublicPileLocation(int seat) => new(CardZoneKind.PublicPersistentPile, seat);
}
