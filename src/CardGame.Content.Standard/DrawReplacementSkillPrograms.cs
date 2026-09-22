using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class DrawReplacementSkillPrograms
{
    private const string BundleResourceName = "draw-replacement-skills";

    public static ContentSkillDefinition Definition(string id) =>
        EmbeddedSkillProgramCatalog.Definition(BundleResourceName, id) with
    {
        ExecutionForms = SkillExecutionForm.Trigger
    };
}
