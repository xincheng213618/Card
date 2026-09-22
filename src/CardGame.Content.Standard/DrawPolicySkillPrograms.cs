using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class DrawPolicySkillPrograms
{
    private const string BundleResourceName = "draw-policy-skills";

    public static ContentSkillDefinition Definition(string id) =>
        EmbeddedSkillProgramCatalog.Definition(BundleResourceName, id) with
    {
        ExecutionForms = SkillExecutionForm.Trigger
    };
}
