using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class DrawAdjustmentSkillPrograms
{
    private const string BundleResourceName = "draw-adjustment-skills";

    public static ContentSkillDefinition Definition(string id) =>
        EmbeddedSkillProgramCatalog.Definition(BundleResourceName, id) with
    {
        ExecutionForms = SkillExecutionForm.Trigger
    };
}
