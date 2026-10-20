namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_GuanduCards
{
    internal static LegalActionKind GuanduCardAction(CardKind kind) => kind switch
    {
        CardKind.Intoxication => LegalActionKind.Intoxication,
        CardKind.StoreGrain => LegalActionKind.StoreGrain,
        CardKind.Reinforcements => LegalActionKind.Reinforcements,
        CardKind.UnitedEffort => LegalActionKind.UnitedEffort,
        _ => throw new InvalidOperationException("This is not a Guandu card.")
    };
}
