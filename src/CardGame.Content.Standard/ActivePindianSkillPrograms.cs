using CardGame.Core;

namespace CardGame.Content.Standard;

internal static class ActivePindianSkillPrograms
{
    private static SkillProgramCatalog Catalog =>
        EmbeddedSkillProgramCatalog.Catalog("active-pindian-skills");

    public static ContentSkillDefinition Definition(string id)
    {
        var program = Catalog.Programs[id];
        var presentation = Catalog.Presentations[id];
        return new ContentSkillDefinition(id, presentation.Name, presentation.Description)
        {
            Program = program,
            ActionForms = SkillActionForm.Active,
            ExecutionForms = SkillExecutionForm.State
        };
    }

}
