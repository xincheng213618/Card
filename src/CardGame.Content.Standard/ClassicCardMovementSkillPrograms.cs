using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class ClassicCardMovementSkillPrograms
{
    private const string BundleResourceName = "classic-card-movement-skills";

    public static ContentSkillDefinition Definition(string id) =>
        EmbeddedSkillProgramCatalog.Definition(BundleResourceName, id);
}
