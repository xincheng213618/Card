namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_ProgramCardPayments
{
    internal static bool IsProgramAttackRangeCoverageDecreased(ProgramSkillFrame frame, string bind)
    {
        var result = frame.AttackRangeCoverageBindings.Single(item => item.Name == bind);
        return result.AfterCount < result.BeforeCount;
    }

    internal static bool MatchesProgramCardCategory(
        CardKind kind,
        IReadOnlyList<SkillProgramCardCategory> categories)
    {
        return categories.Any(category => CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramCardPayments.MatchesSkillProgramCardCategory(kind, category));
    }

    // "Trick" keeps matching every trick; "InstantTrick" excludes delayed tricks (Indulgence, SupplyShortage, Lightning).
    internal static bool MatchesSkillProgramCardCategory(CardKind kind, SkillProgramCardCategory category) =>
        category != SkillProgramCardCategory.InstantTrick
            ? CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramCardPayments.GetProgramCardCategory(kind) == category
            : CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramCardPayments.GetProgramCardCategory(kind) == SkillProgramCardCategory.Trick &&
              CardUseCategoryCatalog.Get(kind) is not CardUseCategories.DelayedTrick;



    internal static bool MatchesProgramCardFilter(
        CardKind kind,
        IReadOnlyList<SkillProgramCardCategory> categories,
        IReadOnlyList<CardKind> kinds) =>
        (categories.Count == 0 || CardGame.Core.RuntimeSkills.RuntimeRules_Core_ProgramCardPayments.MatchesProgramCardCategory(kind, categories)) &&
        (kinds.Count == 0 || kinds.Contains(kind));

    internal static SkillProgramCardCategory GetProgramCardCategory(CardKind kind)
    {
        return EquipmentCatalog.IsEquipment(kind)
            ? SkillProgramCardCategory.Equipment
            : CardCatalog.Get(kind).CategoryName switch
            {
                "基本牌" => SkillProgramCardCategory.Basic,
                "锦囊牌" => SkillProgramCardCategory.Trick,
                _ => throw new InvalidOperationException($"Card kind '{kind}' has no supported skill-program category.")
            };
    }
}
