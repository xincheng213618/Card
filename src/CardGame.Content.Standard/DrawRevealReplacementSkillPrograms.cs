using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class DrawRevealReplacementSkillPrograms
{
    private const string BundleResourceName = "draw-reveal-replacement-skills";

    public static ContentSkillDefinition Definition(string id) =>
        EmbeddedSkillProgramCatalog.Definition(BundleResourceName, id) with
    {
        ExecutionForms = SkillExecutionForm.Trigger
    };
}
