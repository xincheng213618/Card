namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_DyingWindowSkillPolicies
{
    // Some historical zero-entity Duel paths stored false as a placeholder.
    // New color policies must interpret an absent suit and physical cost as
    // colorless without rewriting that legacy action or its replay invariants.
    internal static bool? ActualTargetPolicyColor(CardActionContext action) =>
        action.PhysicalCards.Count == 0 && action.EffectiveSuit is null or Suit.None ? null : action.EffectiveIsRed;
}
