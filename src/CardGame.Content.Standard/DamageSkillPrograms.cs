using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class DamageSkillPrograms
{
    private const string BundleResourceName = "damage-skills";

    public static ContentSkillDefinition Definition(string id) =>
        EmbeddedSkillProgramCatalog.Definition(BundleResourceName, id) with
    {
        ExecutionForms = SkillExecutionForm.Trigger
    };
}
