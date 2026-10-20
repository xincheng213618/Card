namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_RuleQueries
{
    internal static void AddFiniteContribution(
        ICollection<RuleQueryContribution> contributions,
        string sourceId,
        int value)
    {
        if (value != 0)
            contributions.Add(new FiniteRuleQueryContribution(sourceId, SkillRuleOperation.Add, value));
    }

    internal static int ConvertRuleValue(RuleQueryEvaluation evaluation) =>
        evaluation.Value is UnlimitedRuleQueryValue
            ? int.MaxValue
            : ((FiniteRuleQueryValue)evaluation.Value).Value;
}
