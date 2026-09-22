using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class DrawPhaseSkillPrograms
{
    private const string BundleResourceName = "draw-phase-skills";

    public static ContentSkillDefinition Definition(string id) =>
        EmbeddedSkillProgramCatalog.Definition(BundleResourceName, id) with
    {
        ExecutionForms = SkillExecutionForm.Trigger
    };
}
