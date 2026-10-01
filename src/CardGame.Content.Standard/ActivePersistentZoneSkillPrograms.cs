using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class ActivePersistentZoneSkillPrograms
{
    private static SkillProgramCatalog Catalog =>
        EmbeddedSkillProgramCatalog.Catalog("active-persistent-zone-skills");

    public static ContentSkillDefinition Definition(string id)
    {
        var program = Catalog.Programs[id];
        var presentation = Catalog.Presentations[id];
        return new ContentSkillDefinition(id, presentation.Name, presentation.Description)
        {
            Program = program,
            ActionForms = SkillActionForm.Active
        };
    }

}
