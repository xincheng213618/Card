using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class RuleQuerySkillPrograms
{
    private const string BundleResourceName = "rule-query-skills";

    public static ContentSkillDefinition Definition(string id) =>
        EmbeddedSkillProgramCatalog.Definition(BundleResourceName, id);
}
