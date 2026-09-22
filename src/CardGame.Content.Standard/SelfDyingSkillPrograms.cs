using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class SelfDyingSkillPrograms
{
    private const string BundleResourceName = "self-dying-skills";

    public static ContentSkillDefinition Definition(string id) =>
        EmbeddedSkillProgramCatalog.Definition(BundleResourceName, id) with
    {
        Tags = SkillTag.Limited,
        ExecutionForms = SkillExecutionForm.Trigger
    };
}
