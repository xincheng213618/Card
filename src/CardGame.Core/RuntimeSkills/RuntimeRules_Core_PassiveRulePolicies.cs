namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_PassiveRulePolicies
{
    // This predicate receives the current attempt's participants. In particular,
    // it never treats the placeholder seat of source-less damage as a source.
    internal static bool MatchesPerOccurrenceSourceDamageModifier(SkillProgramDamageModifier modifier,
        bool sourceHasHp, int sourceHp, int sourceHandCount, int sourceToTargetDistance, int roundNumber) => modifier.Condition switch
    {
        SkillProgramDamageModifierCondition.SourceHandBelowHp => sourceHasHp && sourceHandCount < sourceHp,
        SkillProgramDamageModifierCondition.SourceToTargetDistanceGreaterThan =>
            modifier.DistanceThreshold is { } threshold && sourceToTargetDistance > threshold,
        SkillProgramDamageModifierCondition.SourceRoundIsOdd => roundNumber > 0 && roundNumber % 2 == 1,
        SkillProgramDamageModifierCondition.SourceRoundIsEven => roundNumber > 0 && roundNumber % 2 == 0,
        _ => false
    };
}
