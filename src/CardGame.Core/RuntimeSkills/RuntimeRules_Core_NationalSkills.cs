using System.Globalization;

namespace CardGame.Core.RuntimeSkills;

[global::CardGame.Core.ProgramRuntimeModule]
internal static class RuntimeRules_Core_NationalSkills
{
    internal static int DualGeneralMaxHp(GeneralDefinition primary, GeneralDefinition secondary) =>
        (primary.BaseHp + secondary.BaseHp) / 2;

    internal static GeneralSkillDefinition ToRuntimeSkillDefinition(ContentSkillDefinition skill) =>
        new GeneralSkillDefinition(
            skill.Name,
            skill.Description)
        {
            ContentId = skill.Id,
            Tags = skill.Tags,
            ExecutionForms = skill.ExecutionForms,
            ActionForms = skill.ActionForms,
            SelectionWeights = skill.SelectionWeights,
            RevealWeights = skill.RevealWeights,
            ViewAsOpportunities = skill.Program?.ViewAs
                .Where(rule => rule.Condition.Kind == SkillProgramConditionKind.Always &&
                               rule.InputCount == 1 &&
                               rule.SourceZones.Contains(CardZoneKind.Hand) &&
                               rule.InputCategories.Count == 0)
                .Select(rule => new SkillViewAsOpportunity(
                    rule.OutputKind, rule.InputKinds, rule.InputSuits,
                    rule.ForPlay, rule.ForResponse))
                .ToArray()
        };
}
