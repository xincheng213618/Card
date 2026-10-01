using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class AwakeningSkillPrograms
{
    private static SkillProgramCatalog Catalog =>
        EmbeddedSkillProgramCatalog.Catalog("awakening-skills");
    private static SkillProgramCatalog StateCatalog =>
        EmbeddedSkillProgramCatalog.Catalog("state-awakening-skills");

    public static ContentSkillDefinition Definition(string id)
    {
        var catalog = Catalog.Programs.ContainsKey(id) ? Catalog : StateCatalog;
        var program = catalog.Programs[id];
        var presentation = catalog.Presentations[id];
        return new ContentSkillDefinition(id, presentation.Name, presentation.Description)
        {
            Program = program,
            Tags = SkillTag.Awakening,
            ExecutionForms = SkillExecutionForm.Trigger
        };
    }

}
