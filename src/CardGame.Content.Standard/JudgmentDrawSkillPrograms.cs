using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class JudgmentDrawSkillPrograms
{
    private const string BundleResourceName = "judgment-draw-skills";

    public static ContentSkillDefinition Definition(string id) =>
        EmbeddedSkillProgramCatalog.Definition(BundleResourceName, id) with
    {
        ExecutionForms = SkillExecutionForm.State | SkillExecutionForm.Trigger
    };
}
