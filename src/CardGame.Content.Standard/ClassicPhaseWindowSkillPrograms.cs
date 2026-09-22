using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class ClassicPhaseWindowSkillPrograms
{
    private const string BundleResourceName = "classic-phase-window-skills";

    public static ContentSkillDefinition Definition(string id) =>
        EmbeddedSkillProgramCatalog.Definition(BundleResourceName, id);
}
