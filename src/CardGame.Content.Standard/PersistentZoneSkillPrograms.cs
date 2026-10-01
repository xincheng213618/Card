using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class PersistentZoneSkillPrograms
{
    private static SkillProgramCatalog Catalog =>
        EmbeddedSkillProgramCatalog.Catalog("persistent-zone-skills");

    public static ContentSkillDefinition Definition(string id)
    {
        var program = Catalog.Programs[id];
        var presentation = Catalog.Presentations[id];
        return new ContentSkillDefinition(id, presentation.Name, presentation.Description)
        {
            Program = program,
            ExecutionForms = SkillExecutionForm.State | SkillExecutionForm.Trigger
        };
    }

}
